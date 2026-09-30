// CloudProjectRootCommands.cs — view / set the shared project root of a cloud model (ACC-HARD-3).
//
// Command tag: Cloud_SetProjectRoot (BIM tab, next to "Consolidate Folders").
//
// A Revit cloud model (Autodesk Docs / BIM 360 / Cloud Worksharing) has no folder on disk,
// so STING cannot put its project files "next to the model". The root is instead recorded
// per ACC project in %APPDATA%\Planscape\cloud_project_roots.json. This file holds the
// one-time prompt (raised by CloudProjectRootResolver when a cloud model has no root) and
// the command that shows or changes the mapping.
//
// Choosing the folder — the trade-offs, stated in the prompt too:
//   * A network share (\\server\projects\KUT) — one copy, live, every user writes the same
//     files. Best for state that several people update (issue register, SEQ counters).
//     Needs the share reachable; offline, STING refuses rather than forks.
//   * A synced folder (Desktop Connector, OneDrive/SharePoint) — works offline, but two
//     users editing the same JSON before a sync produce a conflict copy; last writer wins.
//     Acceptable for a small team working mostly in different areas.
//   * A local folder — only correct when one person runs STING for the project.
// Every user on the team must point at the SAME location (drive letters may differ; the
// mapping is per machine).

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Cloud
{
    /// <summary>The interactive prompt registered as CloudProjectRootResolver.PromptHandler.</summary>
    public static class CloudProjectRootPrompt
    {
        private const string TradeOffs =
            "Pick a folder EVERY team member can reach — the same folder for everyone:\n" +
            "  • Network share (e.g. \\\\server\\Projects\\KUT): one live copy; best for shared state.\n" +
            "  • Synced folder (Desktop Connector / OneDrive): works offline, but simultaneous edits " +
            "can produce conflict copies.\n" +
            "  • Local folder: only if you are the sole STING user on this project.";

        /// <summary>
        /// Explain, then show a folder picker. Returns the picked folder, or null when the user
        /// declines. The caller turns the pick into the root and records it.
        /// </summary>
        public static string Ask(CloudRootPromptRequest req)
        {
            bool ws = !string.IsNullOrEmpty(req?.CentralPath);
            string why = ws
                ? $"This is a local copy of the central model\n{req.CentralPath}\nand STING keeps a new project's " +
                  "files beside the CENTRAL model so every user shares them — but that folder cannot be used from " +
                  "this machine right now. STING will not fall back to your local-copy folder: that would give you " +
                  "a private issue register, counters and audit log.\n\n" +
                  "Best: reconnect the share and choose \"Not now\" (STING asks again next session). " +
                  "Otherwise choose the folder the team uses.\n\n"
                : "This model lives in Autodesk Construction Cloud, so it has no folder on disk. STING's " +
                  "project files — issue register, escalation record, ACC settings, sequence counters, " +
                  "audit log, exports — need one shared folder, or each user would get a private copy.\n\n";
            var td = new TaskDialog(ws ? "STING — workshared project root" : "STING — cloud project root")
            {
                MainInstruction = ws ? "The central model's folder is not reachable. Where should STING keep this project's files?"
                                     : "Where should STING keep this cloud project's files?",
                MainContent =
                    $"Model: {req?.ModelDisplayPath}\n\n" +
                    why +
                    (string.IsNullOrEmpty(req?.CurrentFolder) ? "" : $"Recorded folder (not usable now): {req.CurrentFolder}\n\n") +
                    TradeOffs + "\n\n" +
                    $"STING will use <folder>\\{req?.ProjectCode} unless the folder you pick is already the " +
                    "project root. The choice is saved on this machine (per ACC project, or per central model).",
                FooterText = $"Why asked: {req?.Reason}",
                MainIcon = TaskDialogIcon.TaskDialogIconInformation,
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Choose the shared folder…");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Not now",
                "STING features that store project files are unavailable for this model until a folder is set " +
                "(BIM tab → Cloud Project Root).");
            if (td.Show() != TaskDialogResult.CommandLink1)
            {
                StingLog.Warn($"CLOUD ROOT: user declined to choose a root for {req?.Key}.");
                return null;
            }
            return PickFolder(req?.CurrentFolder);
        }

        internal static string PickFolder(string initial)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "STING — choose the shared project folder",
                    Multiselect = false,
                };
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
                return dlg.ShowDialog() == true ? dlg.FolderName : null;
            }
            catch (Exception ex)
            {
                StingLog.Error("CloudProjectRootPrompt.PickFolder", ex);
                TaskDialog.Show("STING — cloud project root", $"The folder picker failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>View, set, change or clear the cloud project root. Tag: <c>Cloud_SetProjectRoot</c>.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CloudSetProjectRootCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = ParameterHelpers.GetDoc(data);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var info = CloudProjectRootResolver.Describe(doc);
                if (!info.IsCloud)
                {
                    if (CloudProjectRootResolver.IsWorksharedLocalCopy(doc))
                        return WorksharedRoot(doc);
                    string local = ProjectFolderEngine.GetRootPath(doc);
                    TaskDialog.Show("STING — project root",
                        "This is neither a cloud model nor a workshared local copy, so no shared-root rule applies.\n\n" +
                        $"Its STING project root is next to the model:\n{local}");
                    return Result.Succeeded;
                }

                string key = info.Key;
                string text = CloudProjectRootResolver.ReadMappingText(out string readError);
                var map = readError == null ? CloudProjectRoot.ParseMapping(text, out string parseError) : null;
                string current = null;
                map?.TryGetValue(key ?? "", out current);
                string resolved = CloudProjectRootResolver.TryResolve(doc, out var decision, allowPrompt: false);

                var sb = new StringBuilder();
                sb.AppendLine($"Model: {info.UserVisiblePath}");
                sb.AppendLine($"ACC project GUID: {info.ProjectGuid ?? "(unavailable)"}");
                sb.AppendLine($"Model GUID: {info.ModelGuid ?? "(unavailable)"}");
                sb.AppendLine($"Mapping key: {key ?? "(none — cannot record a root)"}");
                sb.AppendLine($"Mapping file: {CloudProjectRootResolver.MappingPath}");
                sb.AppendLine();
                sb.AppendLine($"Recorded root: {current ?? "(none)"}");
                sb.AppendLine($"In use now: {resolved ?? "NONE — " + decision?.Reason}");

                var td = new TaskDialog("STING — cloud project root")
                {
                    MainInstruction = resolved != null ? "This cloud model has a shared project root."
                                                       : "This cloud model has NO usable project root.",
                    MainContent = sb.ToString(),
                    FooterText = "Changing the folder does not move files already in the old one — copy them across first.",
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                if (key != null && (decision == null || !decision.MappingMalformed))
                {
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        current == null ? "Choose the shared folder…" : "Change the folder…");
                    if (current != null)
                        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Forget this mapping",
                            "The model will ask again next time a project folder is needed.");
                }
                if (resolved != null)
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Open the folder");

                var res = td.Show();
                if (res == TaskDialogResult.CommandLink1)
                {
                    string picked = CloudProjectRootPrompt.PickFolder(current);
                    if (string.IsNullOrEmpty(picked)) return Result.Cancelled;
                    string root = CloudProjectRoot.RootForPickedFolder(picked, ProjectFolderEngine.DetectProjectCode(doc));
                    if (!CloudProjectRootResolver.SetMapping(key, root, out string err))
                    {
                        TaskDialog.Show("STING — cloud project root", $"Could not record the folder:\n{err}");
                        return Result.Failed;
                    }
                    TaskDialog.Show("STING — cloud project root",
                        $"Recorded for every model in this ACC project on this machine:\n{root}\n\n" +
                        "Other team members must choose the same folder on their machines.");
                    return Result.Succeeded;
                }
                if (res == TaskDialogResult.CommandLink2)
                {
                    if (!CloudProjectRootResolver.RemoveMapping(key, out string err))
                    {
                        TaskDialog.Show("STING — cloud project root", $"Could not update the mapping:\n{err}");
                        return Result.Failed;
                    }
                    return Result.Succeeded;
                }
                if (res == TaskDialogResult.CommandLink3 && resolved != null)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", resolved) { UseShellExecute = true })?.Dispose();
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CloudSetProjectRootCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// File-based workshared local copy (ACC-HARD-3b). Shows where the root is and, when it
        /// is still beside this user's local copy, offers the consented move to the shared root
        /// beside the central model. Moves go through ProjectFolderEngine.RelocateProjectRoot
        /// (move-never-delete, *.migrated_yyyyMMdd, recorded in .sting_consolidation.json).
        /// </summary>
        private static Result WorksharedRoot(Document doc)
        {
            const string title = "STING — workshared project root";
            string central = CloudProjectRootResolver.CentralPathOf(doc);
            string centralDir = WorksharedProjectRoot.CentralDirOf(central);
            string code = ProjectFolderEngine.DetectProjectCode(doc);
            string current = ProjectFolderEngine.GetRootPath(doc);
            // path-discipline: model-dir -- deliberately the LOCAL COPY's folder: that is where the per-user root to move lives
            string localDir = Path.GetDirectoryName(doc.PathName);
            string perUser = string.IsNullOrEmpty(localDir) ? null : Path.Combine(localDir, code);

            string stampText = Core.Storage.StingProjectRootSchema.Read(doc)?.RootRelativePath;
            var stampKind = WorksharedProjectRoot.ClassifyStamp(stampText, out string stampRel);
            bool centralReachable = !string.IsNullOrEmpty(centralDir) && Directory.Exists(centralDir);
            string shared = centralReachable
                ? WorksharedProjectRoot.CentralRoot(centralDir, code, stampKind == RootStampKind.Central ? stampRel : null)
                : null;

            bool currentIsPerUser = !string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(localDir)
                && Path.GetFullPath(current).StartsWith(Path.GetFullPath(localDir), StringComparison.OrdinalIgnoreCase);
            // A per-user tree left behind after the switch (another user moved first, or the
            // stamp synced before this user's tree was moved).
            bool leftover = !currentIsPerUser && perUser != null && Directory.Exists(perUser)
                && Directory.EnumerateFiles(perUser, "*.*", SearchOption.AllDirectories).Any();
            string moveFrom = currentIsPerUser ? current : (leftover ? perUser : null);

            var sb = new StringBuilder();
            sb.AppendLine($"Local copy: {doc.PathName}");
            sb.AppendLine($"Central model: {central}");
            sb.AppendLine($"Stamp: {(string.IsNullOrEmpty(stampText) ? "(none)" : stampText)}");
            sb.AppendLine();
            sb.AppendLine($"Root in use: {current ?? "NONE"}");
            sb.AppendLine($"Shared root beside the central: {shared ?? (centralDir == null ? "(central has no folder)" : "(central folder unreachable from this machine)")}");
            if (currentIsPerUser)
                sb.AppendLine("\nThis project's root is under YOUR local-copy folder, so other users have their own. " +
                              "Move it to the shared root so everyone reads and writes one register?");
            else if (leftover)
                sb.AppendLine($"\nA per-user STING folder with files is still beside your local copy:\n{perUser}\n" +
                              "Merge it into the shared root? (Files with the same name are both kept.)");

            var td = new TaskDialog(title)
            {
                MainInstruction = currentIsPerUser ? "This workshared project's root is per user."
                                                   : "Workshared project root",
                MainContent = sb.ToString(),
                FooterText = "Nothing is deleted: moved-out folders are renamed *.migrated_yyyyMMdd and every move is " +
                             "recorded in <shared root>\\_data\\.sting_consolidation.json. Sync with Central afterwards.",
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            bool canMove = moveFrom != null && shared != null;
            if (canMove)
            {
                int n = Directory.EnumerateFiles(moveFrom, "*.*", SearchOption.AllDirectories).Count();
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Move to the shared root",
                    $"{n} file(s): {moveFrom}  →  {shared}");
            }
            if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Open the folder in use");

            var res = td.Show();
            if (res == TaskDialogResult.CommandLink3)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", current) { UseShellExecute = true })?.Dispose();
                return Result.Succeeded;
            }
            if (res != TaskDialogResult.CommandLink1 || !canMove) return Result.Cancelled;

            // Switch the root identity first, inside a transaction, so a failure leaves the
            // files exactly where they were.
            using (var t = new Transaction(doc, "STING: Move project root beside the central model"))
            {
                if (t.Start() != TransactionStatus.Started)
                {
                    TaskDialog.Show(title, "Could not start a transaction. Nothing was moved.");
                    return Result.Failed;
                }
                if (!Core.Storage.StingProjectRootSchema.WriteCentralStamp(doc, centralDir, shared))
                {
                    t.RollBack();
                    TaskDialog.Show(title, "Could not stamp the shared root into the model (is Project Information " +
                                           "borrowed by another user?). Nothing was moved.");
                    return Result.Failed;
                }
                t.Commit();
            }

            var rep = ProjectFolderEngine.RelocateProjectRoot(doc, moveFrom, shared, consented: true);
            ProjectFolderEngine.InvalidateSetupCache(doc.PathName);
            string after = ProjectFolderEngine.GetRootPath(doc);
            StingLog.Info($"Workshared root move: {moveFrom} → {shared}; now resolving {after}");

            var msg = new StringBuilder();
            msg.AppendLine(rep.DidRun ? $"Moved {rep.FilesMoved} file(s); {rep.FilesFailed} failed." : $"Not moved: {rep.SkippedReason}");
            msg.AppendLine($"Root now in use: {after}");
            if (!string.IsNullOrEmpty(rep.BreadcrumbPath)) msg.AppendLine($"Record: {rep.BreadcrumbPath}");
            foreach (var w in rep.Warnings.Take(10)) msg.AppendLine("• " + w);
            msg.AppendLine("\nSynchronize with Central so every user's local copy picks up the shared root.");
            TaskDialog.Show(title, msg.ToString());
            return rep.FilesFailed > 0 ? Result.Failed : Result.Succeeded;
        }
    }
}
