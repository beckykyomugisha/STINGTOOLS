// FolderConsolidateCommand.cs — ISO 19650 consolidation (WP9, migration wizard).
//
// The ONLY path that relocates a project's legacy STING folders. It shows a dry-run of
// every legacy sibling that would be folded into the unified <root>/_data (file counts +
// destinations), writes that dry-run to CSV, and moves nothing until the user confirms.
//
// This header used to claim "It NEVER runs automatically" while StingToolsApp called
// MigrateFromLegacy unprompted on every DocumentOpened — the claim is now true, and the
// engine enforces it: MigrateFromLegacy refuses to act unless the caller passes
// consented: true, and refuses a second time once the breadcrumb exists.
//
// Recovery: files are MOVED, but no source folder is ever deleted — a drained folder is
// renamed *.migrated_yyyyMMdd — and .sting_consolidation.json records every relocation
// source-to-destination, so the operation can be undone by hand.
//
// RunWithConsent is the shared entry point; the folder-setup dialog and the FolderMigrate
// command tag call it too, so consent is implemented once rather than three times.

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
                return RunWithConsent(doc) ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("FolderConsolidateCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Preview → confirm → consolidate → report. The single consent gate in front of
        /// <see cref="ProjectFolderEngine.MigrateFromLegacy"/>; every entry point routes
        /// here so the user gets the same dry-run and the same warning wherever they start.
        /// Returns true only when files were actually moved.
        /// </summary>
        public static bool RunWithConsent(Document doc)
        {
            if (doc == null) return false;

            if (string.IsNullOrEmpty(doc.PathName))
            {
                TaskDialog.Show("STING — Consolidate Folders", "Save the project to disk first.");
                return false;
            }

            if (ProjectFolderEngine.HasConsolidated(doc))
            {
                TaskDialog.Show("STING — Consolidate Folders",
                    "This project has already been consolidated — it runs once only.\n\n" +
                    "What moved is recorded in:\n" + ProjectFolderEngine.ConsolidationBreadcrumbPath(doc));
                return false;
            }

            var scan = ProjectFolderEngine.ScanLegacy(doc);
            if (scan.TotalFiles == 0)
            {
                TaskDialog.Show("STING — Consolidate Folders",
                    "Nothing to consolidate — no legacy STING folders found next to the model.");
                return false;
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
                    "Files are MOVED, not copied. No source folder is deleted — once drained it is " +
                    "renamed *.migrated_" + DateTime.Now.ToString("yyyyMMdd") + " — and every relocation is " +
                    "recorded in .sting_consolidation.json so this can be undone by hand.\n\n" +
                    "This runs ONCE per project. Continue?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes)
            {
                TaskDialog.Show("STING — Consolidate Folders",
                    $"Dry-run only. Nothing was moved.\nReport: {reportPath}");
                return false;
            }

            // consented: true is legitimate here and ONLY here — the user has just seen
            // exactly what would move and said yes to this specific run.
            var rep = ProjectFolderEngine.MigrateFromLegacy(doc, consented: true);
            ProjectFolderEngine.InvalidateFolderStatsCache();

            if (!rep.DidRun)
            {
                TaskDialog.Show("STING — Consolidate Folders", $"Nothing was moved: {rep.SkippedReason}");
                return false;
            }

            string warn = rep.Warnings.Count > 0
                ? $"\n\n{rep.Warnings.Count} warning(s):\n" + string.Join("\n", rep.Warnings.Take(8))
                : "";
            TaskDialog.Show("STING — Consolidate Folders",
                $"Consolidation complete:\n\n  {rep.FilesMoved} file(s) moved\n" +
                $"  {rep.FoldersRemoved} legacy folder(s) renamed aside (not deleted)\n\n" +
                $"Record of every move:\n{rep.BreadcrumbPath}{warn}");
            return true;
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
