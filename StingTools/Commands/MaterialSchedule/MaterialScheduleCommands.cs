// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleCommands.cs — MAT-SCHED entry points.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ.MaterialSchedule;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.Commands.MaterialSchedule
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaterialScheduleExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                // Prices in or out.
                var priceDlg = new TaskDialog("Material Schedule")
                {
                    MainInstruction = "Include prices?",
                    MainContent = "A priced schedule carries Rate, Amount, contingency and a grand total. "
                                + "A quantities-only schedule is a buy-list for the site team.",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                priceDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Priced schedule",
                    "Quantities plus rates, amounts, contingency and grand total.");
                priceDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Quantities only",
                    "Commodities, units and order quantities. No money.");
                var choice = priceDlg.Show();
                if (choice == TaskDialogResult.Cancel) return Result.Cancelled;

                var options = new MaterialScheduleOptions
                {
                    ShowPrices = choice == TaskDialogResult.CommandLink1,
                    ContingencyPct = 5.0
                };

                var built = MaterialScheduleBuilder.Build(doc, options);
                var msDoc = built.Document;

                if (msDoc.Stages.Count == 0)
                {
                    TaskDialog.Show("Material Schedule",
                        "No material commodities were produced.\n\n"
                        + string.Join("\n\n", built.Warnings));
                    return Result.Cancelled;
                }

                // Reconciliation gate — skippable, mirroring the BOQ coverage gate.
                if (!msDoc.Reconciliation.IsClean)
                {
                    var issues = msDoc.Reconciliation.Issues;
                    var gate = new TaskDialog("Material Schedule — reconciliation")
                    {
                        MainInstruction = $"{issues.Count} reconciliation issue(s) found",
                        MainContent = string.Join("\n", issues.Take(6).Select(i => $"• [{i.Code}] {i.Message}"))
                                    + (issues.Count > 6 ? $"\n… and {issues.Count - 6} more." : "")
                                    + "\n\nAll issues are listed on the Validation sheet.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                        DefaultButton = TaskDialogResult.Cancel
                    };
                    gate.VerificationText = "Export anyway";
                    if (gate.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                }

                string path = StingPaths.ExportFile(doc, "MaterialSchedule",
                    $"MaterialSchedule_{msDoc.ProjectCode}", ".xlsx");
                MaterialScheduleXlsxWriter.Write(msDoc, path);

                string warn = built.Warnings.Count > 0
                    ? "\n\nWarnings:\n" + string.Join("\n", built.Warnings.Select(w => "• " + w))
                    : "";
                TaskDialog.Show("Material Schedule",
                    $"{msDoc.Stages.Count} stage(s), "
                  + $"{msDoc.Stages.Sum(s => s.Commodities.Count)} commodity row(s).\n"
                  + (options.ShowPrices ? $"Grand total: UGX {msDoc.GrandTotalUGX:N0}\n" : "")
                  + $"\n{path}{warn}");

                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                }
                catch (Exception ex) { StingLog.Warn($"Open material schedule xlsx: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MaterialScheduleExportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
