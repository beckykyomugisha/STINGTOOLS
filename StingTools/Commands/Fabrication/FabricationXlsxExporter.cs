// StingTools v4 MVP — Fabrication XLSX exporter.
//
// ClosedXML-backed multi-sheet workbook writer. Where the CSV path
// drops one file per action (cut list, weld map, iso index), this
// bundles them into one workbook with coloured headers and auto-
// filter so foremen can sort/filter without re-opening external
// tooling. Also emits the consolidated BOQ roll-up (#10) from the
// accumulated package rows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    public static class FabricationXlsxExporter
    {
        private static readonly XLColor HeaderFill = XLColor.FromArgb(0x2D, 0x2D, 0x30);
        private static readonly XLColor HeaderFont = XLColor.White;
        private static readonly XLColor AltRowFill = XLColor.FromArgb(0xF2, 0xF2, 0xF4);

        public static string ExportCutListXlsx(Autodesk.Revit.DB.Document doc, IEnumerable<CutListRow> rows)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_pipe_cut_list.xlsx");
            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Cut list");
                    string[] hdr = { "Include", "Element ID", "System", "Size (mm)", "Length (mm)", "Material", "Mitre°" };
                    for (int c = 0; c < hdr.Length; c++) WriteHeader(ws.Cell(1, c + 1), hdr[c]);
                    int r = 2;
                    foreach (var row in rows.Where(x => x.Include))
                    {
                        ws.Cell(r, 1).Value = "Y";
                        ws.Cell(r, 2).Value = row.ElementId;
                        ws.Cell(r, 3).Value = row.System;
                        ws.Cell(r, 4).Value = row.SizeMm;
                        ws.Cell(r, 5).Value = row.LengthMm;
                        ws.Cell(r, 6).Value = row.Material;
                        ws.Cell(r, 7).Value = row.MitreAngleDeg;
                        if (r % 2 == 0) ws.Range(r, 1, r, hdr.Length).Style.Fill.BackgroundColor = AltRowFill;
                        r++;
                    }
                    ws.RangeUsed()?.SetAutoFilter();
                    ws.Columns().AdjustToContents();
                    wb.SaveAs(path);
                }
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"ExportCutListXlsx: {ex.Message}"); return ""; }
        }

        public static string ExportWeldMapXlsx(Autodesk.Revit.DB.Document doc, IEnumerable<WeldMapRow> rows)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_pipe_welds.xlsx");
            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Weld map");
                    string[] hdr = { "Element ID", "Category", "Name", "Weld type", "Size (mm)", "Schedule" };
                    for (int c = 0; c < hdr.Length; c++) WriteHeader(ws.Cell(1, c + 1), hdr[c]);
                    int r = 2;
                    foreach (var row in rows.Where(x => x.Include))
                    {
                        ws.Cell(r, 1).Value = row.ElementId;
                        ws.Cell(r, 2).Value = row.Category;
                        ws.Cell(r, 3).Value = row.Name;
                        ws.Cell(r, 4).Value = row.WeldType;
                        ws.Cell(r, 5).Value = row.SizeMm;
                        ws.Cell(r, 6).Value = row.Schedule;
                        if (r % 2 == 0) ws.Range(r, 1, r, hdr.Length).Style.Fill.BackgroundColor = AltRowFill;
                        r++;
                    }
                    ws.RangeUsed()?.SetAutoFilter();
                    ws.Columns().AdjustToContents();
                    wb.SaveAs(path);
                }
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"ExportWeldMapXlsx: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// BOM roll-up (#10) — one workbook per Generate Package run,
        /// aggregating cut list + weld map + assembly summary. Driven
        /// by BOQ_TEMPLATE.csv if it ships with the project, else uses
        /// a built-in 5-column layout.
        /// </summary>
        public static string ExportConsolidatedBom(
            Autodesk.Revit.DB.Document doc,
            IEnumerable<PackageGroupRow> pkg,
            IEnumerable<CutListRow>      cut,
            IEnumerable<WeldMapRow>      weld)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            Directory.CreateDirectory(outDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(outDir, $"STING_v4_bom_consolidated_{stamp}.xlsx");
            try
            {
                using (var wb = new XLWorkbook())
                {
                    // Sheet 1 — Assembly roll-up
                    var ws1 = wb.Worksheets.Add("Assemblies");
                    string[] h1 = { "Discipline", "System", "Level", "Elements", "Assembly name" };
                    for (int c = 0; c < h1.Length; c++) WriteHeader(ws1.Cell(1, c + 1), h1[c]);
                    int r = 2;
                    foreach (var p in pkg.Where(x => x.Include))
                    {
                        ws1.Cell(r, 1).Value = p.Discipline;
                        ws1.Cell(r, 2).Value = p.System;
                        ws1.Cell(r, 3).Value = p.Level;
                        ws1.Cell(r, 4).Value = p.ElementCount;
                        ws1.Cell(r, 5).Value = p.AssemblyNamePreview;
                        r++;
                    }
                    ws1.RangeUsed()?.SetAutoFilter();
                    ws1.Columns().AdjustToContents();

                    // Sheet 2 — Cut list (grouped totals per system/size)
                    var ws2 = wb.Worksheets.Add("Cut list");
                    string[] h2 = { "System", "Size (mm)", "Count", "Total length (mm)", "Material" };
                    for (int c = 0; c < h2.Length; c++) WriteHeader(ws2.Cell(1, c + 1), h2[c]);
                    r = 2;
                    foreach (var grp in cut.Where(x => x.Include).GroupBy(x => (x.System, x.SizeMm, x.Material)))
                    {
                        ws2.Cell(r, 1).Value = grp.Key.System;
                        ws2.Cell(r, 2).Value = grp.Key.SizeMm;
                        ws2.Cell(r, 3).Value = grp.Count();
                        ws2.Cell(r, 4).Value = grp.Sum(x => x.LengthMm);
                        ws2.Cell(r, 5).Value = grp.Key.Material;
                        r++;
                    }
                    ws2.RangeUsed()?.SetAutoFilter();
                    ws2.Columns().AdjustToContents();

                    // Sheet 3 — Weld totals
                    var ws3 = wb.Worksheets.Add("Welds");
                    string[] h3 = { "Weld type", "Count" };
                    for (int c = 0; c < h3.Length; c++) WriteHeader(ws3.Cell(1, c + 1), h3[c]);
                    r = 2;
                    foreach (var grp in weld.Where(x => x.Include).GroupBy(x => x.WeldType))
                    {
                        ws3.Cell(r, 1).Value = grp.Key;
                        ws3.Cell(r, 2).Value = grp.Count();
                        r++;
                    }
                    ws3.Columns().AdjustToContents();

                    wb.SaveAs(path);
                }
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"ExportConsolidatedBom: {ex.Message}"); return ""; }
        }

        private static void WriteHeader(IXLCell cell, string text)
        {
            cell.Value = text;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Font.FontColor = HeaderFont;
            cell.Style.Font.Bold = true;
        }
    }
}
