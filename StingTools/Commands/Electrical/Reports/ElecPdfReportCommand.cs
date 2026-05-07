using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Exports every PanelScheduleView to PDF via Revit's PrintManager —
    /// one file per schedule, batch mode. Wired from the RPRT tab's "PDF
    /// Report" button (tag <c>Rprt_PDF</c>). Replaces the Phase 177 stub
    /// that surfaced "queued for Phase 178" to the user.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecPdfReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>().ToList();
            if (schedules.Count == 0)
            {
                TaskDialog.Show("STING Electrical PDF",
                    "No panel schedules found. Run PNLS → Batch Schedules first.");
                return Result.Cancelled;
            }

            var td = new TaskDialog("STING Electrical PDF Report")
            {
                MainInstruction = $"Export {schedules.Count} panel schedule(s) to PDF",
                MainContent =
                    "Each schedule exports to a separate file using Revit's active print driver. " +
                    "Ensure a PDF driver (Microsoft Print to PDF, Bluebeam, Adobe, etc.) is selected " +
                    "in Revit's Print Settings before running.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };
            if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            string outputDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc), "ElecPDF");
            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex) { StingLog.Warn($"ElecPDF mkdir: {ex.Message}"); }

            int exported = 0;
            var errors = new List<string>();
            var pm = doc.PrintManager;
            try
            {
                pm.PrintRange = PrintRange.Select;
                pm.PrintToFile = true;
                pm.CombinedFile = false;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ElecPDF PrintManager setup: {ex.Message}");
                TaskDialog.Show("STING Electrical PDF",
                    $"PrintManager configuration failed: {ex.Message}\n\n" +
                    "Open File → Print Setup in Revit, choose a PDF driver, then re-run this command.");
                return Result.Failed;
            }

            foreach (var psv in schedules)
            {
                string safeName = SanitiseName(psv.Name);
                try
                {
                    pm.PrintToFileName = Path.Combine(outputDir, safeName + ".pdf");
                    pm.SubmitPrint(psv);
                    exported++;
                    StingLog.Info($"ElecPDF exported: {psv.Name}");
                }
                catch (Exception ex)
                {
                    errors.Add($"{psv.Name}: {ex.Message}");
                    StingLog.Warn($"ElecPDF {psv.Name}: {ex.Message}");
                }
            }

            string summary = $"Exported {exported}/{schedules.Count} panel schedule(s) to:\n{outputDir}";
            if (errors.Count > 0)
                summary += "\n\nFailures:\n" + string.Join("\n", errors.Take(5)) +
                    (errors.Count > 5 ? $"\n…and {errors.Count - 5} more (see StingTools.log)" : "");
            TaskDialog.Show("STING Electrical PDF", summary);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outputDir)
                { UseShellExecute = true });
            }
            catch { /* non-fatal */ }
            return Result.Succeeded;
        }

        private static string SanitiseName(string name)
            => string.Concat((name ?? "Schedule").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
