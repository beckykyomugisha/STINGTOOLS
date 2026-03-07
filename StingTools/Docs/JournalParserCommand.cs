using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Docs
{
    /// <summary>
    /// Parse Revit journal files (.txt) to extract diagnostics:
    /// addin load status, errors, crashes, command timeline, memory usage.
    /// Presents a summary TaskDialog and optionally exports CSV report.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class JournalParserCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(UIApplication app)
        {
            return RunParser(app);
        }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            return RunParser(commandData.SafeApp());
        }

        private static Result RunParser(UIApplication app)
        {
            // Locate the Revit journals folder
            string journalDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Autodesk", "Revit", "Autodesk Revit 2025", "Journals");

            if (!Directory.Exists(journalDir))
            {
                // Try 2026/2027
                foreach (string year in new[] { "2026", "2027" })
                {
                    string alt = journalDir.Replace("2025", year);
                    if (Directory.Exists(alt)) { journalDir = alt; break; }
                }
            }

            if (!Directory.Exists(journalDir))
            {
                TaskDialog.Show("Journal Parser",
                    "Cannot find Revit Journals directory.\n" +
                    "Expected: " + journalDir);
                return Result.Failed;
            }

            // Find the most recent journal files
            var journalFiles = Directory.GetFiles(journalDir, "journal.*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(10)
                .ToList();

            if (journalFiles.Count == 0)
            {
                TaskDialog.Show("Journal Parser", "No journal files found in:\n" + journalDir);
                return Result.Failed;
            }

            // Let user pick which journal to parse
            var td = new TaskDialog("STING Journal Parser");
            td.MainInstruction = "Select journal file to analyze";
            td.MainContent = "Most recent journals:";

            var links = new List<string>();
            for (int i = 0; i < Math.Min(journalFiles.Count, 4); i++)
            {
                string name = Path.GetFileName(journalFiles[i]);
                var fi = new FileInfo(journalFiles[i]);
                string when = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                string size = fi.Length > 1024 * 1024
                    ? $"{fi.Length / (1024 * 1024.0):F1} MB"
                    : $"{fi.Length / 1024.0:F0} KB";
                links.Add($"{name}  ({when}, {size})");
            }

            if (links.Count > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, links[0]);
            if (links.Count > 1) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, links[1]);
            if (links.Count > 2) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, links[2]);
            if (links.Count > 3) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, links[3]);
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();
            string selectedFile = result switch
            {
                TaskDialogResult.CommandLink1 => journalFiles[0],
                TaskDialogResult.CommandLink2 => journalFiles[1],
                TaskDialogResult.CommandLink3 => journalFiles[2],
                TaskDialogResult.CommandLink4 => journalFiles[3],
                _ => null
            };

            if (selectedFile == null) return Result.Cancelled;

            // Parse the journal
            var report = JournalParser.Parse(selectedFile);

            // Show summary
            var summary = new TaskDialog("Journal Analysis");
            summary.MainInstruction = $"Journal: {Path.GetFileName(selectedFile)}";
            summary.MainContent = report.BuildSummary();
            summary.ExpandedContent = report.BuildDetails();
            summary.FooterText = "Export to CSV for full analysis.";
            summary.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Export CSV Report");
            summary.CommonButtons = TaskDialogCommonButtons.Close;

            if (summary.Show() == TaskDialogResult.CommandLink1)
            {
                string csvPath = Path.Combine(
                    StingToolsApp.DataPath ?? Path.GetTempPath(),
                    $"JOURNAL_ANALYSIS_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(csvPath, report.BuildCsv());
                TaskDialog.Show("Journal Parser", $"Exported to:\n{csvPath}");
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Parses Revit journal (.txt) files extracting addin status, errors,
    /// commands, memory snapshots, and crash indicators.
    /// </summary>
    internal static class JournalParser
    {
        internal static JournalReport Parse(string filePath)
        {
            var report = new JournalReport
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileSize = new FileInfo(filePath).Length
            };

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (IOException ex)
            {
                // Journal may be locked by Revit — read with sharing
                using (var fs = new FileStream(filePath, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    lines = sr.ReadToEnd().Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
                }
            }

            report.TotalLines = lines.Length;

            // Regex patterns for journal entries
            var timestampRx = new Regex(@"'[HCE]\s+(\d{2}-\w{3}-\d{4}\s+\d{2}:\d{2}:\d{2}\.\d+)");
            var addinLoadRx = new Regex(@"API_SUCCESS\s*\{\s*Starting External Application:\s*(.+?),\s*Class:\s*(.+?),.*?Assembly:\s*(.+?\.dll)", RegexOptions.IgnoreCase);
            var addinBtnRx = new Regex(@"API_SUCCESS\s*\{\s*Added pushbutton.*?name:\s*(.+?),.*?class:\s*(.+?),.*?assembly:\s*(.+?\.dll)", RegexOptions.IgnoreCase);
            var addinManifestRx = new Regex(@"\[Jrn\.AddInManifest\].*?AddInName:\s*(.+?)\s+.*?AddInVersion:\s*(\S+).*?AddInLoadFailureMessage:\s*(\S+)", RegexOptions.IgnoreCase);
            var errorRx = new Regex(@"API_ERROR|FATAL|Exception|StackOverflow|AccessViolation|NullReference|OutOfMemory|assembly.*version.*conflict", RegexOptions.IgnoreCase);
            var ribbonEventRx = new Regex(@"Jrn\.RibbonEvent\s+""Execute external command:(.+?)""");
            var memoryRx = new Regex(@"RAM.*?Avail\s+(\d+).*?Used\s+(\d+).*?Peak\s+(\d+)");
            var taskDialogRx = new Regex(@"TaskDialog\s+""(.+?)""");
            var versionRx = new Regex(@"Build:\s*(\S+).*?Branch:\s*(\S+)");
            var userRx = new Regex(@"""Username""\s*,\s*""(.+?)""");
            var crashRx = new Regex(@"JRNABC|Jrn\.Abort|fatal|unhandled|crash|access violation", RegexOptions.IgnoreCase);

            DateTime? firstTimestamp = null;
            DateTime? lastTimestamp = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Timestamps
                var tsMatch = timestampRx.Match(line);
                if (tsMatch.Success)
                {
                    if (DateTime.TryParse(tsMatch.Groups[1].Value, out DateTime ts))
                    {
                        firstTimestamp ??= ts;
                        lastTimestamp = ts;
                    }
                }

                // Revit version
                if (line.Contains("Build:") && report.RevitVersion == null)
                {
                    var vm = versionRx.Match(line);
                    if (vm.Success)
                        report.RevitVersion = $"{vm.Groups[2].Value} (Build {vm.Groups[1].Value})";
                }

                // Username
                var um = userRx.Match(line);
                if (um.Success && report.Username == null)
                    report.Username = um.Groups[1].Value;

                // Addin loading
                var addinMatch = addinLoadRx.Match(line);
                if (addinMatch.Success)
                {
                    report.AddinsLoaded.Add(new AddinInfo
                    {
                        Name = addinMatch.Groups[1].Value.Trim(),
                        ClassName = addinMatch.Groups[2].Value.Trim(),
                        AssemblyPath = addinMatch.Groups[3].Value.Trim(),
                        LineNumber = i + 1
                    });
                }

                // Addin manifest entries (load time, failure status)
                var manifestMatch = addinManifestRx.Match(line);
                if (manifestMatch.Success)
                {
                    string name = manifestMatch.Groups[1].Value.Trim();
                    string version = manifestMatch.Groups[2].Value.Trim();
                    string failMsg = manifestMatch.Groups[3].Value.Trim();

                    // Find matching addin and update
                    var existing = report.AddinsLoaded
                        .FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.Version = version;
                        existing.LoadFailure = failMsg;
                    }
                    else
                    {
                        report.AddinsLoaded.Add(new AddinInfo
                        {
                            Name = name,
                            Version = version,
                            LoadFailure = failMsg,
                            LineNumber = i + 1
                        });
                    }
                }

                // Errors and warnings
                if (errorRx.IsMatch(line))
                {
                    report.Errors.Add(new JournalEntry
                    {
                        LineNumber = i + 1,
                        Text = line.Trim().Substring(0, Math.Min(line.Trim().Length, 200))
                    });
                }

                // Commands executed
                var cmdMatch = ribbonEventRx.Match(line);
                if (cmdMatch.Success)
                {
                    string cmdText = cmdMatch.Groups[1].Value;
                    // Extract timestamp if available
                    DateTime? cmdTime = null;
                    if (tsMatch.Success && DateTime.TryParse(tsMatch.Groups[1].Value, out DateTime ct))
                        cmdTime = ct;

                    report.CommandsExecuted.Add(new CommandEntry
                    {
                        CommandText = cmdText,
                        LineNumber = i + 1,
                        Timestamp = cmdTime
                    });
                }

                // TaskDialogs
                var tdMatch = taskDialogRx.Match(line);
                if (tdMatch.Success)
                {
                    report.TaskDialogs.Add(new JournalEntry
                    {
                        LineNumber = i + 1,
                        Text = tdMatch.Groups[1].Value
                    });
                }

                // Memory snapshots
                var memMatch = memoryRx.Match(line);
                if (memMatch.Success)
                {
                    if (int.TryParse(memMatch.Groups[2].Value, out int used))
                    {
                        if (used > report.PeakRAM_MB)
                            report.PeakRAM_MB = used;
                        report.LastRAM_MB = used;
                    }
                }

                // Crash indicators
                if (crashRx.IsMatch(line))
                {
                    report.CrashIndicators.Add(new JournalEntry
                    {
                        LineNumber = i + 1,
                        Text = line.Trim().Substring(0, Math.Min(line.Trim().Length, 200))
                    });
                }
            }

            report.SessionStart = firstTimestamp;
            report.SessionEnd = lastTimestamp;
            if (firstTimestamp.HasValue && lastTimestamp.HasValue)
                report.SessionDuration = lastTimestamp.Value - firstTimestamp.Value;

            // STING-specific analysis
            report.StingAddins = report.AddinsLoaded
                .Where(a => a.Name.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.ClassName?.IndexOf("StingTools", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.ClassName?.IndexOf("StingBIM", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.AssemblyPath?.IndexOf("StingTools", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.AssemblyPath?.IndexOf("StingBIM", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            report.StingCommands = report.CommandsExecuted
                .Where(c => c.CommandText.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0
                    || c.CommandText.IndexOf("StingTools", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            return report;
        }
    }

    internal class JournalReport
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public int TotalLines { get; set; }
        public string RevitVersion { get; set; }
        public string Username { get; set; }
        public DateTime? SessionStart { get; set; }
        public DateTime? SessionEnd { get; set; }
        public TimeSpan? SessionDuration { get; set; }
        public int PeakRAM_MB { get; set; }
        public int LastRAM_MB { get; set; }
        public List<AddinInfo> AddinsLoaded { get; set; } = new List<AddinInfo>();
        public List<AddinInfo> StingAddins { get; set; } = new List<AddinInfo>();
        public List<JournalEntry> Errors { get; set; } = new List<JournalEntry>();
        public List<CommandEntry> CommandsExecuted { get; set; } = new List<CommandEntry>();
        public List<CommandEntry> StingCommands { get; set; } = new List<CommandEntry>();
        public List<JournalEntry> TaskDialogs { get; set; } = new List<JournalEntry>();
        public List<JournalEntry> CrashIndicators { get; set; } = new List<JournalEntry>();

        public string BuildSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Revit: {RevitVersion ?? "Unknown"}");
            sb.AppendLine($"User: {Username ?? "Unknown"}");
            if (SessionDuration.HasValue)
                sb.AppendLine($"Session: {SessionDuration.Value.TotalMinutes:F1} minutes");
            sb.AppendLine($"Peak RAM: {PeakRAM_MB} MB");
            sb.AppendLine();
            sb.AppendLine($"Addins loaded: {AddinsLoaded.Count}");
            sb.AppendLine($"STING addins: {StingAddins.Count}");
            sb.AppendLine($"Commands run: {CommandsExecuted.Count}");
            sb.AppendLine($"STING commands: {StingCommands.Count}");
            sb.AppendLine($"Errors/warnings: {Errors.Count}");
            sb.AppendLine($"Crash indicators: {CrashIndicators.Count}");

            if (CrashIndicators.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("⚠ CRASH DETECTED:");
                foreach (var c in CrashIndicators.Take(3))
                    sb.AppendLine($"  Line {c.LineNumber}: {c.Text}");
            }

            return sb.ToString();
        }

        public string BuildDetails()
        {
            var sb = new StringBuilder();

            // STING addin status
            sb.AppendLine("─── STING Plugin Status ───");
            if (StingAddins.Count == 0)
                sb.AppendLine("  No STING addins found in this session.");
            else
            {
                foreach (var a in StingAddins)
                {
                    sb.AppendLine($"  {a.Name} v{a.Version ?? "?"} — {a.LoadFailure ?? "OK"}");
                    sb.AppendLine($"    {a.AssemblyPath ?? a.ClassName}");
                }
            }

            // STING commands
            if (StingCommands.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("─── STING Commands ───");
                foreach (var c in StingCommands)
                    sb.AppendLine($"  Line {c.LineNumber}: {c.CommandText}");
            }

            // Errors
            if (Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"─── Errors ({Errors.Count}) ───");
                foreach (var e in Errors.Take(10))
                    sb.AppendLine($"  Line {e.LineNumber}: {e.Text}");
                if (Errors.Count > 10)
                    sb.AppendLine($"  ... and {Errors.Count - 10} more");
            }

            // TaskDialogs
            if (TaskDialogs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"─── TaskDialogs ({TaskDialogs.Count}) ───");
                foreach (var t in TaskDialogs.Take(10))
                    sb.AppendLine($"  Line {t.LineNumber}: {t.Text}");
            }

            // All commands timeline
            if (CommandsExecuted.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"─── Command Timeline ({CommandsExecuted.Count}) ───");
                foreach (var c in CommandsExecuted)
                    sb.AppendLine($"  Line {c.LineNumber}: {c.CommandText}");
            }

            return sb.ToString();
        }

        public string BuildCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Section,LineNumber,Type,Name,Detail");
            sb.AppendLine($"Info,,Revit,\"{RevitVersion}\",\"{Username}\"");
            sb.AppendLine($"Info,,Session,\"{SessionStart}\",\"{SessionEnd}\"");
            sb.AppendLine($"Info,,Memory,PeakRAM={PeakRAM_MB}MB,LastRAM={LastRAM_MB}MB");

            foreach (var a in AddinsLoaded)
                sb.AppendLine($"Addin,{a.LineNumber},Load,\"{a.Name}\",\"{a.AssemblyPath}\"");

            foreach (var e in Errors)
                sb.AppendLine($"Error,{e.LineNumber},Warning,\"{Escape(e.Text)}\",");

            foreach (var c in CommandsExecuted)
                sb.AppendLine($"Command,{c.LineNumber},Execute,\"{Escape(c.CommandText)}\",\"{c.Timestamp}\"");

            foreach (var t in TaskDialogs)
                sb.AppendLine($"Dialog,{t.LineNumber},TaskDialog,\"{Escape(t.Text)}\",");

            foreach (var cr in CrashIndicators)
                sb.AppendLine($"Crash,{cr.LineNumber},Indicator,\"{Escape(cr.Text)}\",");

            return sb.ToString();
        }

        private static string Escape(string s)
            => s?.Replace("\"", "\"\"") ?? "";
    }

    internal class AddinInfo
    {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string AssemblyPath { get; set; }
        public string Version { get; set; }
        public string LoadFailure { get; set; }
        public int LineNumber { get; set; }
    }

    internal class JournalEntry
    {
        public int LineNumber { get; set; }
        public string Text { get; set; }
    }

    internal class CommandEntry
    {
        public string CommandText { get; set; }
        public int LineNumber { get; set; }
        public DateTime? Timestamp { get; set; }
    }
}
