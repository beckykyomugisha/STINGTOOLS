// StingTools v4 MVP — Fabrication PDF exporter for isometric sheets.
//
// Uses Revit 2022+ PDFExportOptions.Combine + Document.Export(PDF)
// overload to emit every SP-... sheet as a single combined PDF.
// Falls back to the classic PrintManager pipeline on older Revit
// versions where that API isn't available. Output lands alongside
// the CSV sidecars in the project output directory.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    public static class FabricationPdfExporter
    {
        /// <summary>
        /// Export selected SP-... sheets to a single combined PDF.
        /// Returns the output path, or empty on failure.
        /// </summary>
        public static string ExportSheetsToPdf(Document doc, IEnumerable<IsoSheetRow> rows)
        {
            try
            {
                var sheetIds = rows.Where(r => r.Include)
                                   .Select(r => new ElementId(r.SheetId))
                                   .ToList();
                if (sheetIds.Count == 0) return "";

                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                Directory.CreateDirectory(outDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"STING_v4_isometrics_{stamp}";

                // Revit 2022+ — PDFExportOptions combines multiple sheets.
                // TODO-VERIFY-API: Combine flag availability on target Revit.
                try
                {
                    var opts = new PDFExportOptions
                    {
                        Combine = true,
                        FileName = fileName,
                        PaperFormat = ExportPaperFormat.Default,
                        PaperOrientation = PageOrientationType.Landscape,
                        PaperPlacement = PaperPlacementType.Center,
                        ZoomType = ZoomType.Zoom,
                        ZoomPercentage = 100,
                        HideReferencePlane = true,
                        HideScopeBoxes = true,
                        HideCropBoundaries = true,
                        MaskCoincidentLines = true,
                    };
                    doc.Export(outDir, sheetIds, opts);
                    return Path.Combine(outDir, fileName + ".pdf");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PDFExportOptions.Combine path failed: {ex.Message}; falling back to PrintManager.");
                }

                // Fallback — PrintManager. Kept short; the Combine path is preferred.
                string fallbackPath = Path.Combine(outDir, fileName + ".pdf");
                var pm = doc.PrintManager;
                pm.PrintRange = PrintRange.Select;
                pm.Apply();
                var vss = pm.ViewSheetSetting;
                var set = new ViewSet();
                foreach (var id in sheetIds)
                    if (doc.GetElement(id) is ViewSheet s) set.Insert(s);
                vss.CurrentViewSheetSet.Views = set;
                vss.SaveAs("STING v4 PDF sheets");
                pm.PrintToFile = true;
                pm.PrintToFileName = fallbackPath;
                pm.CombinedFile = true;
                pm.SubmitPrint();
                return fallbackPath;
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationPdfExporter.ExportSheetsToPdf", ex);
                return "";
            }
        }
    }
}
