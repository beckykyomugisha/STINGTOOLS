// StingTools v4 MVP — Fabrication PDF exporter for isometric sheets.
//
// Uses PDFExportOptions.Combine + Document.Export(folder, viewIds,
// options) — the same pattern the rest of the codebase already
// exercises (see Docs/PrintManagerCommands.cs, ExLink/
// AutomationEngine.cs, Temp/OperationsCommands.cs). Target is Revit
// 2025 / 2026 / 2027 per CLAUDE.md so we keep the option set minimal
// — only the properties confirmed across every existing call site
// (FileName, Combine, AlwaysUseRaster, ColorDepth). Rarely-used
// properties (PaperFormat / PaperOrientation / ZoomType / …) were
// dropped after the verification pass so we don't introduce API
// surface this codebase isn't already relying on.

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

                var opts = new PDFExportOptions
                {
                    Combine = true,
                    FileName = fileName,
                    AlwaysUseRaster = false,
                    ColorDepth = ColorDepthType.Color,
                };
                bool ok = doc.Export(outDir, sheetIds, opts);
                return ok ? Path.Combine(outDir, fileName + ".pdf") : "";
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationPdfExporter.ExportSheetsToPdf", ex);
                return "";
            }
        }
    }
}
