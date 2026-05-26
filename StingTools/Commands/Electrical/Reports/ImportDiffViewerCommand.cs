using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Panels;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Surfaces the diff captured by the most recent
    /// <see cref="ImportPanelSchedulesFromExcelCommand"/> run. Avoids the
    /// "go look at StingTools.log" advice the Phase 177 stub gave the user.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportDiffViewerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var diff = ImportPanelSchedulesFromExcelCommand.LastImportDiff;
            if (diff == null || diff.Count == 0)
            {
                TaskDialog.Show("STING Import Diff",
                    "No import diff available yet. Run RPRT → Import from Excel first.\n\n" +
                    "Detailed run logs are also appended to StingTools.log alongside the Revit project file.");
                return Result.Cancelled;
            }

            const int CapLines = 50;
            string body = string.Join("\n", diff.Take(CapLines));
            if (diff.Count > CapLines)
                body += $"\n\n… and {diff.Count - CapLines} more line(s) — see StingTools.log for the full transcript.";

            string when = ImportPanelSchedulesFromExcelCommand.LastImportTime.ToString("yyyy-MM-dd HH:mm:ss");
            string src  = ImportPanelSchedulesFromExcelCommand.LastImportSource ?? "";
            string instruction = string.IsNullOrEmpty(src)
                ? $"Last import: {when}"
                : $"Last import: {when}\nSource: {src}";

            new TaskDialog("STING Last Import Diff")
            {
                MainInstruction = instruction,
                MainContent = body,
                CommonButtons = TaskDialogCommonButtons.Ok
            }.Show();
            return Result.Succeeded;
        }
    }
}
