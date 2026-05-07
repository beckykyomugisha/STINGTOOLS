using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Compliance
{
    /// <summary>
    /// Generates a per-circuit loop-calculation sheet — one A4-sized
    /// worksheet per circuit, designed for the design pack. Each sheet
    /// shows the full derivation: utility Ze, cable R1 + R2, computed Zs,
    /// Zs_max for the OCPD, prospective fault current, OCPD clearing time,
    /// adiabatic check, and RCD recommendation.
    ///
    /// Run order: <see cref="BS7671AuditCommand"/> first to populate
    /// <see cref="StingElectricalCommandHandler.LastBs7671Results"/>; then
    /// this command writes them to a multi-sheet workbook.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BS7671LoopCalcSheetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var rows = StingElectricalCommandHandler.LastBs7671Results;
            if (rows == null || rows.Count == 0)
            {
                TaskDialog.Show("STING BS 7671 Loop Calc",
                    "Run BS 7671 Audit first to populate the per-circuit results.");
                return Result.Cancelled;
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir,
                $"STING_BS7671_LoopCalcSheets_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");

            using var wb = new XLWorkbook();
            // Index sheet
            var ix = wb.Worksheets.Add("Index");
            ix.Cell(1, 1).Value = $"BS 7671 Loop Calculation Sheets  ·  {rows.Count} circuits  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
            ix.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ix.Cell(2, 1).Value = "Panel"; ix.Cell(2, 2).Value = "Circuit";
            ix.Cell(2, 3).Value = "Verdict"; ix.Cell(2, 4).Value = "Sheet";
            ix.Range(2, 1, 2, 4).Style.Font.Bold = true;

            int ixRow = 3;
            int sheetSeq = 1;
            foreach (var r in rows.OrderBy(x => x.PanelName).ThenBy(x => x.CircuitTag))
            {
                string sheetName = SafeSheetName($"{sheetSeq:D3}_{r.PanelName}_{r.CircuitTag}");
                WriteCircuitSheet(wb.Worksheets.Add(sheetName), r);
                ix.Cell(ixRow, 1).Value = r.PanelName;
                ix.Cell(ixRow, 2).Value = r.CircuitTag;
                ix.Cell(ixRow, 3).Value = r.Verdict;
                ix.Cell(ixRow, 4).Value = sheetName;
                var fill = r.Verdict == "PASS" ? XLColor.LightGreen
                         : r.Verdict == "PASS_VIA_RCD" ? XLColor.LightYellow
                         : XLColor.LightSalmon;
                ix.Range(ixRow, 1, ixRow, 4).Style.Fill.BackgroundColor = fill;
                ixRow++;
                sheetSeq++;
            }
            ix.Columns().AdjustToContents();

            try { wb.SaveAs(outPath); }
            catch (Exception ex) { StingLog.Error($"Loop calc save: {ex.Message}", ex); msg = ex.Message; return Result.Failed; }

            TaskDialog.Show("STING BS 7671 Loop Calc",
                $"Wrote {rows.Count} loop-calculation sheet(s) to:\n{outPath}");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch { }
            return Result.Succeeded;
        }

        private static void WriteCircuitSheet(IXLWorksheet ws, CircuitAuditResult r)
        {
            // Header band
            ws.Cell(1, 1).Value = "BS 7671:2018+A2:2022 — Loop Calculation Sheet";
            ws.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            ws.Range(1, 1, 1, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(3, 1).Value = "Panel";   ws.Cell(3, 2).Value = r.PanelName;
            ws.Cell(4, 1).Value = "Circuit"; ws.Cell(4, 2).Value = r.CircuitTag;
            ws.Cell(5, 1).Value = "Load";    ws.Cell(5, 2).Value = r.LoadName;
            ws.Cell(6, 1).Value = "Earthing system";  ws.Cell(6, 2).Value = r.EarthingSystem;
            ws.Range(3, 1, 6, 1).Style.Font.Bold = true;

            // Inputs band
            int row = 8;
            ws.Cell(row, 1).Value = "INPUTS"; ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            ws.Cell(row, 1).Value = "Ze (utility loop impedance)";  ws.Cell(row, 2).Value = r.ZeOhm;
            ws.Cell(row, 3).Value = "Ω"; row++;
            ws.Cell(row, 1).Value = "Uo (nominal phase-to-earth voltage)"; ws.Cell(row, 2).Value = 230;
            ws.Cell(row, 3).Value = "V"; row++;

            // Loop calc band
            row++;
            ws.Cell(row, 1).Value = "EARTH FAULT LOOP IMPEDANCE Zs = Ze + R1 + R2";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            ws.Cell(row, 1).Value = "Zs computed";    ws.Cell(row, 2).Value = r.ZsActualOhm;
            ws.Cell(row, 3).Value = "Ω"; row++;
            ws.Cell(row, 1).Value = "Zs max (Table 41.1)"; ws.Cell(row, 2).Value = r.ZsMaxOhm;
            ws.Cell(row, 3).Value = "Ω"; row++;
            ws.Cell(row, 1).Value = "Margin";         ws.Cell(row, 2).Value = r.ZsMarginPct;
            ws.Cell(row, 3).Value = "%"; row++;
            ws.Cell(row, 1).Value = "Result";
            ws.Cell(row, 2).Value = r.ZsPasses ? "PASS" : "FAIL";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Fill.BackgroundColor = r.ZsPasses ? XLColor.LightGreen : XLColor.LightSalmon;
            row += 2;

            // Adiabatic
            ws.Cell(row, 1).Value = "ADIABATIC CONDUCTOR CHECK §434.5.2 — (k·S)² ≥ I²·t";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            ws.Cell(row, 1).Value = "Adiabatic constant k";  ws.Cell(row, 2).Value = r.K; row++;
            ws.Cell(row, 1).Value = "Prospective fault current PSC"; ws.Cell(row, 2).Value = r.ProspectivePscA / 1000.0;
            ws.Cell(row, 3).Value = "kA"; row++;
            ws.Cell(row, 1).Value = "OCPD clearing time"; ws.Cell(row, 2).Value = r.ClearingTimeMs;
            ws.Cell(row, 3).Value = "ms"; row++;
            ws.Cell(row, 1).Value = "Min CSA required";   ws.Cell(row, 2).Value = r.AdiabaticMinCsa;
            ws.Cell(row, 3).Value = "mm²"; row++;
            ws.Cell(row, 1).Value = "Result";
            ws.Cell(row, 2).Value = r.AdiabaticPasses ? "PASS" : $"FAIL — undersized";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Fill.BackgroundColor = r.AdiabaticPasses ? XLColor.LightGreen : XLColor.LightSalmon;
            row += 2;

            // RCD
            ws.Cell(row, 1).Value = "RCD/RCBO RECOMMENDATION";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            ws.Cell(row, 1).Value = "Recommended IΔn";
            ws.Cell(row, 2).Value = r.RcdRequiredMA == 0 ? "(not required)" : $"{r.RcdRequiredMA} mA";
            row++;
            ws.Cell(row, 1).Value = "Triggered by regulation";
            ws.Cell(row, 2).Value = string.IsNullOrEmpty(r.RcdRegulation) ? "—" : r.RcdRegulation;
            row += 2;

            // Verdict band
            ws.Cell(row, 1).Value = "FINAL VERDICT";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor =
                r.Verdict == "PASS" ? XLColor.LightGreen :
                r.Verdict == "PASS_VIA_RCD" ? XLColor.LightYellow : XLColor.LightSalmon;
            row++;
            ws.Cell(row, 1).Value = r.Verdict;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 14;
            row += 2;

            // Sign-off
            ws.Cell(row, 1).Value = "Designer";   ws.Cell(row, 3).Value = "Date"; row++;
            ws.Cell(row, 1).Value = "Checker";    ws.Cell(row, 3).Value = "Date"; row++;
            ws.Range(row - 2, 1, row - 1, 4).Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = Math.Min(ws.Column(1).Width, 45);
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 1);
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        }

        private static string SafeSheetName(string s)
        {
            if (string.IsNullOrEmpty(s)) s = "Circuit";
            foreach (char c in new[] { '\\', '/', '*', '?', ':', '[', ']' }) s = s.Replace(c, '_');
            return s.Length > 31 ? s.Substring(0, 31) : s;
        }
    }
}
