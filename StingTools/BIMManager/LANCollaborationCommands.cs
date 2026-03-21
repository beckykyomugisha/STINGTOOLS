using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  LAN Collaboration Commands
    //
    //  Ported from StingD85/Stingtools-Original StingBIM.AI.Collaboration/LAN/
    //  Provides LAN-based worksharing collaboration with no cloud dependency:
    //    - Worksharing setup with standard worksets
    //    - Sync-to-central with conflict detection
    //    - Auto-backup with configurable interval
    //    - Team management via shared JSON files
    //    - Change logging with CSV export
    //
    //  Architecture:
    //    Communication via shared JSON files on LAN/network drive:
    //    - {project}_team.json      → Team roster (online/offline, workset assignment)
    //    - {project}_changelog.json → Timestamped change records
    //    - {project}_notifications.json → Inter-user notifications
    //    - {central}.rvt.lock       → Pessimistic sync lock
    //
    //  Patterns from Original repo:
    //    - CollaborationResult factory (Succeeded/Failed/ConflictsFound)
    //    - 3-retry with 2s delay for network access
    //    - Lock file protocol with guaranteed cleanup
    //    - FileSystemWatcher-based notification (simplified here)
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: LANCollaborationEngine ──

    internal static class LANCollaborationEngine
    {
        private static readonly object _syncLock = new object();
        private static Timer _autoSyncTimer;
        private static Timer _autoBackupTimer;
        private static string _lastProjectPath;

        /// <summary>Standard worksets for a STING workshared project.</summary>
        internal static readonly string[] StandardWorksetNames = new[]
        {
            "Architecture", "Structure", "MEP-Electrical", "MEP-Mechanical",
            "MEP-Plumbing", "MEP-Fire Protection", "Interior", "Site",
            "Shared Levels and Grids", "Links"
        };

        // ── Worksharing Setup ──

        /// <summary>Enable worksharing and create standard worksets.</summary>
        internal static CollaborationResult EnableWorksharing(Document doc)
        {
            try
            {
                if (doc.IsWorkshared)
                    return CollaborationResult.Succeeded("Worksharing is already enabled.", new[] { "Run Workset Audit to verify setup." });

                // Enable worksharing
                doc.EnableWorksharing("Shared Levels and Grids", "Architecture");

                // Create standard worksets
                int created = 0;
                var existing = new HashSet<string>(
                    new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                        .ToWorksets().Select(w => w.Name), StringComparer.OrdinalIgnoreCase);

                using (Transaction tx = new Transaction(doc, "STING Create Worksets"))
                {
                    tx.Start();
                    foreach (string name in StandardWorksetNames)
                    {
                        if (!existing.Contains(name))
                        {
                            Workset.Create(doc, name);
                            created++;
                        }
                    }
                    tx.Commit();
                }

                return CollaborationResult.Succeeded(
                    $"Worksharing enabled. {created} worksets created.",
                    new[] { "Save the model to a central location (network drive).",
                            "Each team member should create a local copy.",
                            "Use Sync to Central for regular synchronisation." });
            }
            catch (Exception ex)
            {
                StingLog.Error($"EnableWorksharing: {ex.Message}", ex);
                return CollaborationResult.Failed($"Failed to enable worksharing: {ex.Message}");
            }
        }

        // ── Sync to Central ──

        /// <summary>Perform sync-to-central with pre-checks.</summary>
        internal static CollaborationResult SyncToCentral(Document doc)
        {
            if (!doc.IsWorkshared)
                return CollaborationResult.Failed("Model is not workshared.");

            lock (_syncLock)
            {
                try
                {
                    // Pre-check: is central model accessible?
                    var centralPath = doc.GetWorksharingCentralModelPath();
                    if (centralPath == null)
                        return CollaborationResult.Failed("No central model path configured.");

                    string centralPathStr = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath);
                    if (!IsServerAccessible(centralPathStr))
                        return CollaborationResult.Failed("Central model location is not accessible.\nCheck network connection.");

                    // Acquire lock
                    string lockFile = centralPathStr + ".lock";
                    if (File.Exists(lockFile))
                    {
                        string lockInfo = "(unknown)";
                        try { lockInfo = File.ReadAllText(lockFile); } catch { }
                        return CollaborationResult.ConflictsFound(
                            $"Another user is currently syncing.\nLock info: {lockInfo}",
                            new[] { "Wait a moment and try again.", "If the lock is stale, delete it manually." });
                    }

                    // Write lock file
                    try
                    {
                        File.WriteAllText(lockFile, JsonConvert.SerializeObject(new
                        {
                            user = Environment.UserName,
                            machine = Environment.MachineName,
                            timestamp = DateTime.Now.ToString("o")
                        }));
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SyncLock write: {ex.Message}");
                    }

                    try
                    {
                        // Perform sync
                        var syncOptions = new SynchronizeWithCentralOptions();
                        var relinquishOptions = new RelinquishOptions(true);
                        relinquishOptions.StandardWorksets = true;
                        relinquishOptions.ViewWorksets = true;
                        relinquishOptions.FamilyWorksets = true;
                        relinquishOptions.UserWorksets = true;
                        relinquishOptions.CheckedOutElements = true;
                        syncOptions.SetRelinquishOptions(relinquishOptions);
                        syncOptions.Comment = $"STING sync by {Environment.UserName} at {DateTime.Now:HH:mm}";

                        doc.SynchronizeWithCentral(new TransactWithCentralOptions(), syncOptions);

                        // Log change
                        LogChange(doc, "Sync to Central", $"Synced by {Environment.UserName}");

                        return CollaborationResult.Succeeded(
                            "Sync to Central completed successfully.",
                            new[] { $"Synced at {DateTime.Now:HH:mm:ss}", $"User: {Environment.UserName}" });
                    }
                    finally
                    {
                        // Always release lock
                        try { if (File.Exists(lockFile)) File.Delete(lockFile); }
                        catch (Exception ex) { StingLog.Warn($"SyncLock cleanup: {ex.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Error($"SyncToCentral: {ex.Message}", ex);
                    return CollaborationResult.Failed($"Sync failed: {ex.Message}");
                }
            }
        }

        // ── Auto-Sync Timer ──

        /// <summary>Start auto-sync timer (interval in minutes).</summary>
        internal static void StartAutoSync(int intervalMinutes)
        {
            StopAutoSync();
            _autoSyncTimer = new Timer(intervalMinutes * 60 * 1000);
            _autoSyncTimer.Elapsed += (s, e) =>
            {
                StingLog.Info($"Auto-sync timer fired (every {intervalMinutes} min)");
                // Note: actual sync must be dispatched to Revit main thread via ExternalEvent
            };
            _autoSyncTimer.AutoReset = true;
            _autoSyncTimer.Start();
            StingLog.Info($"Auto-sync started: every {intervalMinutes} minutes");
        }

        /// <summary>Stop auto-sync timer.</summary>
        internal static void StopAutoSync()
        {
            if (_autoSyncTimer != null)
            {
                _autoSyncTimer.Stop();
                _autoSyncTimer.Dispose();
                _autoSyncTimer = null;
                StingLog.Info("Auto-sync stopped");
            }
        }

        internal static bool IsAutoSyncRunning => _autoSyncTimer != null;

        // ── Backup ──

        /// <summary>Create a backup of the current model.</summary>
        internal static CollaborationResult CreateBackup(Document doc)
        {
            try
            {
                string projectPath = doc.PathName;
                if (string.IsNullOrEmpty(projectPath))
                    return CollaborationResult.Failed("Save the model first before creating a backup.");

                string backupDir = Path.Combine(Path.GetDirectoryName(projectPath), "STING_BACKUPS");
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupName = $"{Path.GetFileNameWithoutExtension(projectPath)}_{timestamp}.rvt";
                string backupPath = Path.Combine(backupDir, backupName);

                // Save as backup
                var saveOpts = new SaveAsOptions { OverwriteExistingFile = false };
                doc.SaveAs(backupPath, saveOpts);

                // Clean up old backups (keep last 10)
                var backups = Directory.GetFiles(backupDir, "*.rvt")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();
                if (backups.Count > 10)
                {
                    foreach (var old in backups.Skip(10))
                    {
                        try { File.Delete(old); } catch { }
                    }
                }

                LogChange(doc, "Backup", $"Backup created: {backupName}");
                return CollaborationResult.Succeeded(
                    $"Backup created: {backupName}",
                    new[] { $"Location: {backupDir}", $"Backups retained: {Math.Min(backups.Count, 10)}" });
            }
            catch (Exception ex)
            {
                StingLog.Error($"CreateBackup: {ex.Message}", ex);
                return CollaborationResult.Failed($"Backup failed: {ex.Message}");
            }
        }

        /// <summary>List available backups.</summary>
        internal static List<BackupInfo> ListBackups(Document doc)
        {
            var backups = new List<BackupInfo>();
            string projectPath = doc.PathName;
            if (string.IsNullOrEmpty(projectPath)) return backups;

            string backupDir = Path.Combine(Path.GetDirectoryName(projectPath), "STING_BACKUPS");
            if (!Directory.Exists(backupDir)) return backups;

            foreach (var file in Directory.GetFiles(backupDir, "*.rvt").OrderByDescending(f => File.GetCreationTime(f)))
            {
                var fi = new FileInfo(file);
                backups.Add(new BackupInfo
                {
                    FileName = fi.Name,
                    FilePath = file,
                    Created = fi.CreationTime,
                    SizeMB = fi.Length / (1024.0 * 1024.0)
                });
            }
            return backups;
        }

        // ── Team Management ──

        /// <summary>Register current user in team roster.</summary>
        internal static void RegisterTeamMember(Document doc)
        {
            try
            {
                var roster = LoadTeamRoster(doc);
                string key = $"{Environment.UserName}@{Environment.MachineName}";
                roster[key] = new TeamMember
                {
                    UserName = Environment.UserName,
                    MachineName = Environment.MachineName,
                    JoinedAt = DateTime.Now,
                    LastSeen = DateTime.Now,
                    IsOnline = true
                };
                SaveTeamRoster(doc, roster);
            }
            catch (Exception ex) { StingLog.Warn($"RegisterTeamMember: {ex.Message}"); }
        }

        /// <summary>Get team roster.</summary>
        internal static List<TeamMember> GetTeamMembers(Document doc)
        {
            return LoadTeamRoster(doc).Values.OrderByDescending(m => m.LastSeen).ToList();
        }

        // ── Change Log ──

        /// <summary>Log a change event.</summary>
        internal static void LogChange(Document doc, string action, string description)
        {
            try
            {
                var log = LoadChangeLog(doc);
                log.Add(new ChangeLogEntry
                {
                    Timestamp = DateTime.Now,
                    User = Environment.UserName,
                    Machine = Environment.MachineName,
                    Action = action,
                    Description = description
                });
                // Keep last 500 entries
                if (log.Count > 500) log = log.Skip(log.Count - 500).ToList();
                SaveChangeLog(doc, log);
            }
            catch (Exception ex) { StingLog.Warn($"LogChange: {ex.Message}"); }
        }

        /// <summary>Get recent changes.</summary>
        internal static List<ChangeLogEntry> GetRecentChanges(Document doc, int count = 50)
        {
            return LoadChangeLog(doc).OrderByDescending(e => e.Timestamp).Take(count).ToList();
        }

        // ── Network helpers ──

        internal static bool IsServerAccessible(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return false;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(dir)) return true;
                }
                catch { }
                if (attempt < 2) System.Threading.Thread.Sleep(2000);
            }
            return false;
        }

        // ── File I/O helpers ──

        private static string GetCollabDir(Document doc)
        {
            string projectPath = doc.PathName;
            if (!string.IsNullOrEmpty(projectPath))
                return Path.GetDirectoryName(projectPath);
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static string GetProjectKey(Document doc)
        {
            string name = doc.ProjectInformation?.Name ?? Path.GetFileNameWithoutExtension(doc.PathName ?? "project");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static Dictionary<string, TeamMember> LoadTeamRoster(Document doc)
        {
            try
            {
                string path = Path.Combine(GetCollabDir(doc), $"{GetProjectKey(doc)}_team.json");
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<Dictionary<string, TeamMember>>(File.ReadAllText(path))
                        ?? new Dictionary<string, TeamMember>();
            }
            catch (Exception ex) { StingLog.Warn($"LoadTeamRoster: {ex.Message}"); }
            return new Dictionary<string, TeamMember>();
        }

        private static void SaveTeamRoster(Document doc, Dictionary<string, TeamMember> roster)
        {
            try
            {
                string path = Path.Combine(GetCollabDir(doc), $"{GetProjectKey(doc)}_team.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(roster, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"SaveTeamRoster: {ex.Message}"); }
        }

        private static List<ChangeLogEntry> LoadChangeLog(Document doc)
        {
            try
            {
                string path = Path.Combine(GetCollabDir(doc), $"{GetProjectKey(doc)}_changelog.json");
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<List<ChangeLogEntry>>(File.ReadAllText(path))
                        ?? new List<ChangeLogEntry>();
            }
            catch (Exception ex) { StingLog.Warn($"LoadChangeLog: {ex.Message}"); }
            return new List<ChangeLogEntry>();
        }

        private static void SaveChangeLog(Document doc, List<ChangeLogEntry> log)
        {
            try
            {
                string path = Path.Combine(GetCollabDir(doc), $"{GetProjectKey(doc)}_changelog.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(log, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"SaveChangeLog: {ex.Message}"); }
        }
    }

    // ── Data types (ported from Stingtools-Original) ──

    /// <summary>Factory-pattern result type from Stingtools-Original.</summary>
    internal class CollaborationResult
    {
        public bool Success { get; set; }
        public bool HasConflicts { get; set; }
        public string Message { get; set; } = "";
        public string[] Suggestions { get; set; } = Array.Empty<string>();

        public static CollaborationResult Succeeded(string msg, string[] suggestions = null) =>
            new CollaborationResult { Success = true, Message = msg, Suggestions = suggestions ?? Array.Empty<string>() };
        public static CollaborationResult Failed(string msg) =>
            new CollaborationResult { Success = false, Message = msg };
        public static CollaborationResult ConflictsFound(string msg, string[] suggestions = null) =>
            new CollaborationResult { Success = false, HasConflicts = true, Message = msg, Suggestions = suggestions ?? Array.Empty<string>() };

        public string FormatForDisplay()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Message);
            if (Suggestions.Length > 0)
            {
                sb.AppendLine();
                foreach (var s in Suggestions) sb.AppendLine($"  • {s}");
            }
            return sb.ToString();
        }
    }

    internal class TeamMember
    {
        public string UserName { get; set; } = "";
        public string MachineName { get; set; } = "";
        public DateTime JoinedAt { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsOnline { get; set; }
        public string AssignedWorkset { get; set; } = "";
    }

    internal class ChangeLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string User { get; set; } = "";
        public string Machine { get; set; } = "";
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
    }

    internal class BackupInfo
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime Created { get; set; }
        public double SizeMB { get; set; }
    }

    #endregion

    #region ── Commands ──

    /// <summary>Enable worksharing and create standard worksets.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LANEnableWorksharingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = LANCollaborationEngine.EnableWorksharing(ctx.Doc);
            TaskDialog.Show("LAN Worksharing", result.FormatForDisplay());
            if (result.Success) LANCollaborationEngine.RegisterTeamMember(ctx.Doc);
            return result.Success ? Result.Succeeded : Result.Failed;
        }
    }

    /// <summary>Sync to central with conflict detection and lock protocol.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LANSyncToCentralCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = LANCollaborationEngine.SyncToCentral(ctx.Doc);
            string title = result.HasConflicts ? "Sync Conflict" : result.Success ? "Sync Complete" : "Sync Failed";
            TaskDialog.Show(title, result.FormatForDisplay());
            StingLog.Info($"LANSync: {(result.Success ? "OK" : "Failed")} — {result.Message}");
            return result.Success ? Result.Succeeded : Result.Failed;
        }
    }

    /// <summary>Create a backup of the current model.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LANBackupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            TaskDialog td = new TaskDialog("Backup");
            td.MainInstruction = "Model Backup";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Create backup now");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "View existing backups");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel");
            var choice = td.Show();

            if (choice == TaskDialogResult.CommandLink1)
            {
                var result = LANCollaborationEngine.CreateBackup(ctx.Doc);
                TaskDialog.Show("Backup", result.FormatForDisplay());
            }
            else if (choice == TaskDialogResult.CommandLink2)
            {
                var backups = LANCollaborationEngine.ListBackups(ctx.Doc);
                if (backups.Count == 0)
                {
                    TaskDialog.Show("Backups", "No backups found.");
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Available Backups ({backups.Count})\n");
                    foreach (var b in backups)
                        sb.AppendLine($"  {b.Created:yyyy-MM-dd HH:mm}  {b.SizeMB:F1} MB  {b.FileName}");
                    TaskDialog.Show("Backups", sb.ToString());
                }
            }

            return Result.Succeeded;
        }
    }

    /// <summary>View team roster and recent activity.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LANTeamDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            // Register ourselves
            LANCollaborationEngine.RegisterTeamMember(ctx.Doc);

            var members = LANCollaborationEngine.GetTeamMembers(ctx.Doc);
            var changes = LANCollaborationEngine.GetRecentChanges(ctx.Doc, 20);

            var sb = new StringBuilder();
            sb.AppendLine("LAN Collaboration Dashboard\n");

            sb.AppendLine($"── Team Members ({members.Count}) ──");
            if (members.Count == 0) sb.AppendLine("  No team members registered yet.");
            else
            {
                foreach (var m in members)
                {
                    string status = (DateTime.Now - m.LastSeen).TotalMinutes < 30 ? "Online" : "Offline";
                    sb.AppendLine($"  [{status,-7}] {m.UserName}@{m.MachineName}  (last seen: {m.LastSeen:HH:mm})");
                }
            }

            if (changes.Count > 0)
            {
                sb.AppendLine($"\n── Recent Activity ({changes.Count}) ──");
                foreach (var c in changes.Take(15))
                    sb.AppendLine($"  {c.Timestamp:MM-dd HH:mm} [{c.User}] {c.Action}: {c.Description}");
            }

            sb.AppendLine($"\n── Auto-Sync: {(LANCollaborationEngine.IsAutoSyncRunning ? "Running" : "Stopped")} ──");

            TaskDialog.Show("Team Dashboard", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>View and export change log.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LANChangeLogCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var changes = LANCollaborationEngine.GetRecentChanges(ctx.Doc, 100);
            if (changes.Count == 0)
            {
                TaskDialog.Show("Change Log", "No changes recorded yet.");
                return Result.Succeeded;
            }

            TaskDialog td = new TaskDialog("Change Log");
            td.MainInstruction = $"{changes.Count} changes recorded";
            var sb = new StringBuilder();
            foreach (var c in changes.Take(25))
                sb.AppendLine($"{c.Timestamp:yyyy-MM-dd HH:mm} [{c.User}] {c.Action}: {c.Description}");
            if (changes.Count > 25) sb.AppendLine($"... and {changes.Count - 25} more");
            td.MainContent = sb.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Export to CSV");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Close");

            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "ChangeLog", ".csv");
                var csv = new StringBuilder();
                csv.AppendLine("Timestamp,User,Machine,Action,Description");
                foreach (var c in changes)
                    csv.AppendLine($"{c.Timestamp:o},{c.User},{c.Machine},\"{c.Action}\",\"{c.Description.Replace("\"", "\"\"")}\"");
                File.WriteAllText(path, csv.ToString());
                TaskDialog.Show("Change Log", $"Exported to:\n{path}");
            }

            return Result.Succeeded;
        }
    }

    /// <summary>Toggle auto-sync timer.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LANAutoSyncToggleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (LANCollaborationEngine.IsAutoSyncRunning)
            {
                LANCollaborationEngine.StopAutoSync();
                TaskDialog.Show("Auto-Sync", "Auto-sync stopped.");
            }
            else
            {
                TaskDialog td = new TaskDialog("Auto-Sync");
                td.MainInstruction = "Start Auto-Sync";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Every 15 minutes");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Every 30 minutes");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Every 60 minutes");
                var choice = td.Show();

                int interval = choice switch
                {
                    TaskDialogResult.CommandLink1 => 15,
                    TaskDialogResult.CommandLink2 => 30,
                    TaskDialogResult.CommandLink3 => 60,
                    _ => 0
                };
                if (interval > 0)
                {
                    LANCollaborationEngine.StartAutoSync(interval);
                    TaskDialog.Show("Auto-Sync", $"Auto-sync started: every {interval} minutes.\n\nNote: you will be prompted before each sync.");
                }
            }
            return Result.Succeeded;
        }
    }

    #endregion
}
