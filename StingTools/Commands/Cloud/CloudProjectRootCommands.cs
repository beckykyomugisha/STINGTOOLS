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
            var td = new TaskDialog("STING — cloud project root")
            {
                MainInstruction = "Where should STING keep this cloud project's files?",
                MainContent =
                    $"Model: {req?.ModelDisplayPath}\n\n" +
                    "This model lives in Autodesk Construction Cloud, so it has no folder on disk. STING's " +
                    "project files — issue register, escalation record, ACC settings, sequence counters, " +
                    "audit log, exports — need one shared folder, or each user would get a private copy.\n\n" +
                    (string.IsNullOrEmpty(req?.CurrentFolder) ? "" : $"Recorded folder (not usable now): {req.CurrentFolder}\n\n") +
                    TradeOffs + "\n\n" +
                    $"STING will use <folder>\\{req?.ProjectCode} unless the folder you pick is already the " +
                    "project root. The choice is saved on this machine for every model in the same ACC project.",
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
    [Transaction(TransactionMode.ReadOnly)]
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
                    string local = ProjectFolderEngine.GetRootPath(doc);
                    TaskDialog.Show("STING — cloud project root",
                        "This is not a cloud model, so no mapping applies.\n\n" +
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
    }
}
