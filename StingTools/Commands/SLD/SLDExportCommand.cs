using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.SLD
{
    /// <summary>
    /// Exports every STING-authored SLD drafting view to PDF (via the Revit
    /// 2022+ native PDF exporter — no system print driver required), DWG,
    /// or a CSV summary. Wired from the SLD tab's "Export" button
    /// (tag <c>SLD_Export</c>).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SLDExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;

            var sldViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>()
                .Where(v => (v.Name ?? "").IndexOf("SLD", StringComparison.OrdinalIgnoreCase) >= 0
                         && (v.Name ?? "").StartsWith("STING", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sldViews.Count == 0)
            {
                TaskDialog.Show("STING SLD Export",
                    "No STING SLD views found. Run SLD → Generate first.");
                return Result.Cancelled;
            }

            var td = new TaskDialog("STING SLD Export")
            {
                MainInstruction = $"Export {sldViews.Count} SLD view(s)",
                MainContent = "Choose export format:",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Export to PDF (Revit native exporter)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Export to DWG");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Export view names to CSV summary");
            var choice = td.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;

            string outputDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc), "SLD_Export");
            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex) { StingLog.Warn($"SLDExport mkdir {outputDir}: {ex.Message}"); }

            var errors = new List<string>();
            int exported = 0;
            switch (choice)
            {
                case TaskDialogResult.CommandLink1: exported = ExportToPdf(doc, sldViews, outputDir, errors); break;
                case TaskDialogResult.CommandLink2: exported = ExportToDwg(doc, sldViews, outputDir, errors); break;
                case TaskDialogResult.CommandLink3: exported = ExportToCsvSummary(doc, sldViews, outputDir, errors); break;
            }

            string summary = $"Exported {exported}/{sldViews.Count} SLD view(s) to:\n{outputDir}";
            if (errors.Count > 0)
                summary += $"\n\nWarnings ({errors.Count}):\n" + string.Join("\n", errors.Take(5));
            TaskDialog.Show("STING SLD Export", summary);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outputDir)
                { UseShellExecute = true });
            }
            catch { /* non-fatal */ }
            return Result.Succeeded;
        }

        private static int ExportToPdf(Document doc, List<ViewDrafting> views, string dir, List<string> errors)
        {
            int count = 0;
            var opts = new PDFExportOptions
            {
                Combine = false,
                StopOnError = false,
                HideScopeBoxes = true,
                HideCropBoundaries = true
            };
            foreach (var v in views)
            {
                try
                {
                    opts.FileName = SanitiseName(v.Name);
                    if (doc.Export(dir, new List<ElementId> { v.Id }, opts)) count++;
                    else errors.Add($"{v.Name}: Export returned false");
                }
                catch (Exception ex)
                {
                    errors.Add($"{v.Name}: {ex.Message}");
                    StingLog.Warn($"SLDExport PDF {v.Name}: {ex.Message}");
                }
            }
            return count;
        }

        private static int ExportToDwg(Document doc, List<ViewDrafting> views, string dir, List<string> errors)
        {
            try
            {
                var opts = new DWGExportOptions { MergedViews = false, FileVersion = ACADVersion.R2013 };
                var viewIds = views.Select(v => v.Id).ToList();
                string fileName = $"STING_SLD_{DateTime.Now:yyyyMMdd_HHmm}";
                doc.Export(dir, fileName, viewIds, opts);
                return views.Count;
            }
            catch (Exception ex)
            {
                errors.Add($"DWG export: {ex.Message}");
                StingLog.Warn($"SLDExport DWG: {ex.Message}");
                return 0;
            }
        }

        private static int ExportToCsvSummary(Document doc, List<ViewDrafting> views, string dir, List<string> errors)
        {
            try
            {
                string path = Path.Combine(dir, $"SLD_Summary_{DateTime.Now:yyyyMMdd_HHmm}.csv");
                var lines = new List<string> { "View Name,Element Count,Scale,Phase Id" };
                foreach (var v in views)
                {
                    int count = new FilteredElementCollector(doc, v.Id)
                        .WhereElementIsNotElementType().GetElementCount();
                    long phaseId = 0;
                    try { phaseId = v.CreatedPhaseId?.Value ?? 0; } catch { }
                    lines.Add($"\"{v.Name?.Replace("\"", "\"\"")}\",{count},{v.Scale},{phaseId}");
                }
                File.WriteAllLines(path, lines);
                return views.Count;
            }
            catch (Exception ex)
            {
                errors.Add($"CSV summary: {ex.Message}");
                StingLog.Warn($"SLDExport CSV: {ex.Message}");
                return 0;
            }
        }

        private static string SanitiseName(string name)
            => string.Concat((name ?? "SLD").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
