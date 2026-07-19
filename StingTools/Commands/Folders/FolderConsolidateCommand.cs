// FolderConsolidateCommand.cs — ISO 19650 consolidation (WP9, migration wizard).
//
// The explicit, user-driven folder migration. It NEVER runs automatically: it shows a
// dry-run of every legacy sibling folder that would be folded into the unified <root>/_data
// (with file counts + destinations) and only moves anything after the user confirms. The
// underlying MigrateFromLegacy moves files (it never deletes a non-empty folder), so a
// mis-click is recoverable; and a dry-run report CSV is written before any move.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Folders
{
    /// <summary>
    /// Consolidate all legacy STING folders next to the .rvt into the one unified project
    /// root. Dry-run first, applies only on confirmation. Command tag: <c>Folders_ConsolidateAll</c>.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class FolderConsolidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                if (string.IsNullOrEmpty(doc.PathName))
                {
                    TaskDialog.Show("STING — Consolidate Folders", "Save the project to disk first.");
                    return Result.Cancelled;
                }

                var scan = ProjectFolderEngine.ScanLegacy(doc);
                if (scan.TotalFiles == 0)
                {
                    TaskDialog.Show("STING — Consolidate Folders",
                        "Nothing to consolidate — no legacy STING folders found next to the model.");
                    return Result.Succeeded;
                }

                // Write the dry-run report before touching anything.
                string reportPath = WriteDryRunReport(doc, scan);

                var sb = new StringBuilder();
                foreach (var it in scan.Items)
                    sb.AppendLine($"  • {Path.GetFileName(it.Source)}  ({it.FileCount} file(s))  →  {Shorten(it.Destination)}");

                var td = new TaskDialog("STING — Consolidate Folders")
                {
                    MainInstruction = $"Move {scan.TotalFiles} file(s) from {scan.Items.Count} legacy folder(s) into the unified project root?",
                    MainContent =
                        sb.ToString() +
                        $"\nDry-run report written to:\n{reportPath}\n\n" +
                        "Files are MOVED (not copied); a non-empty legacy folder is never deleted. " +
                        "The operation is logged to the activity log. Continue?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (td.Show() != TaskDialogResult.Yes)
                {
                    TaskDialog.Show("STING — Consolidate Folders",
                        $"Dry-run only. Nothing was moved.\nReport: {reportPath}");
                    return Result.Cancelled;
                }

                var rep = ProjectFolderEngine.MigrateFromLegacy(doc);
                ProjectFolderEngine.InvalidateFolderStatsCache();

                string warn = rep.Warnings.Count > 0
                    ? $"\n\n{rep.Warnings.Count} warning(s):\n" + string.Join("\n", rep.Warnings.Take(8))
                    : "";
                TaskDialog.Show("STING — Consolidate Folders",
                    $"Consolidation complete:\n\n  {rep.FilesMoved} file(s) moved\n  {rep.FoldersRemoved} empty legacy folder(s) removed{warn}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FolderConsolidateCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string WriteDryRunReport(Document doc, ProjectFolderEngine.LegacyScanReport scan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Source,FileCount,Destination");
            foreach (var it in scan.Items)
                sb.AppendLine($"\"{it.Source}\",{it.FileCount},\"{it.Destination}\"");
            string path = ProjectFolderEngine.GetExportPath(doc, "Compliance", "STING_Consolidate_DryRun", ".csv");
            OutputLocationHelper.WriteAllTextAtomic(path, sb.ToString());
            return path;
        }

        private static string Shorten(string dest)
        {
            if (string.IsNullOrEmpty(dest)) return dest;
            try { return dest.Length > 60 ? "…" + dest.Substring(dest.Length - 58) : dest; }
            catch { return dest; }
        }
    }
}
