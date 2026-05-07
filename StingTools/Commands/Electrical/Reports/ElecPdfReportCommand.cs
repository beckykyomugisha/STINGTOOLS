using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Exports every PanelScheduleView to PDF via the Revit 2022+
    /// <c>Document.Export(folder, name, viewIds, PDFExportOptions)</c> API —
    /// one file per schedule, batch mode. Wired from the RPRT tab's "PDF
    /// Report" button (tag <c>Rprt_PDF</c>). The earlier PrintManager.SubmitPrint
    /// path required a system PDF printer to be pre-selected and silently
    /// failed when none was; the native Export path has no driver dependency.
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
                    "Uses Revit's native PDF exporter — one file per schedule, " +
                    "no system PDF driver required.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };
            if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            string outputDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc), "ElecPDF");
            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex) { StingLog.Warn($"ElecPDF mkdir: {ex.Message}"); }

            int exported = 0;
            var errors = new List<string>();
            var pdfOpts = new PDFExportOptions
            {
                Combine = false,
                StopOnError = false,
                HideScopeBoxes = true,
                HideCropBoundaries = true
            };

            foreach (var psv in schedules)
            {
                string safeName = SanitiseName(psv.Name);
                try
                {
                    pdfOpts.FileName = safeName;
                    bool ok = doc.Export(outputDir, new List<ElementId> { psv.Id }, pdfOpts);
                    if (ok) { exported++; StingLog.Info($"ElecPDF exported: {psv.Name}"); }
                    else    { errors.Add($"{psv.Name}: Export returned false"); }
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
