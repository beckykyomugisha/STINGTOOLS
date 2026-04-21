// ══════════════════════════════════════════════════════════════════════════
//  BOQExportCommand.cs — Phase 6 of the BOQ & Cost Manager.
//  Generates a multi-sheet XLSX via ClosedXML capturing the BOQ, material
//  schedule, provisional sums, NRM2 reference, carbon, audit trail and
//  (when multiple snapshots exist) a comparison. Pre-export the command
//  writes CST_* and ASS_BOQ_* parameters back onto modeled elements so the
//  workbook and the model stay synchronised.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BOQ
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQExportCommand : IExternalCommand
    {
        private static readonly XLColor NavyFill = XLColor.FromArgb(26, 58, 92);
        private static readonly XLColor HeaderFill = XLColor.FromArgb(46, 94, 142);
        private static readonly XLColor ArchDisc = XLColor.FromArgb(214, 228, 240);
        private static readonly XLColor StrDisc = XLColor.FromArgb(235, 230, 250);
        private static readonly XLColor MepDisc = XLColor.FromArgb(255, 243, 224);
        private static readonly XLColor EleDisc = XLColor.FromArgb(252, 235, 235);
        private static readonly XLColor PlmDisc = XLColor.FromArgb(225, 245, 238);
        private static readonly XLColor PsDisc  = XLColor.FromArgb(237, 231, 246);
        private static readonly XLColor ManualRow = XLColor.FromArgb(255, 251, 230);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                // Build BOQ + (optional) paragraph coverage gate
                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq.ParagraphCoveragePct < 80)
                {
                    var td = new TaskDialog("BOQ paragraph coverage")
                    {
                        MainInstruction = $"NRM2 paragraph coverage is only {boq.ParagraphCoveragePct:F0}%",
                        MainContent = $"{boq.AllItems.Count - boq.ResolvedParagraphCount} of {boq.AllItems.Count} items will export "
                            + "with a generic fallback description. Open the NRM2 templates tab to fill in missing descriptions before exporting.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel,
                        DefaultButton = TaskDialogResult.No
                    };
                    td.VerificationText = "Continue anyway";
                    var r = td.Show();
                    if (r == TaskDialogResult.Cancel) return Result.Cancelled;
                    if (r == TaskDialogResult.No) return Result.Cancelled;
                }

                // Write cost parameters back to the model first so the workbook
                // and Revit stay aligned (Gap G3, G6, G9, G12).
                using (var tx = new Transaction(doc, "STING BOQ — write cost parameters"))
                {
                    tx.Start();
                    BOQCostManager.WriteElementParameters(doc, boq.AllItems);
                    BOQCostManager.WriteProjectParameters(doc, boq);
                    tx.Commit();
                }

                string outputPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_BOQ", ".xlsx");
                using (var wb = new XLWorkbook())
                {
                    BuildSummarySheet(wb.Worksheets.Add("BOQ Summary"), boq);
                    BuildItemScheduleSheet(wb.Worksheets.Add("Item Schedule"), boq);
                    BuildMaterialScheduleSheet(wb.Worksheets.Add("Material Schedule"), boq);
                    BuildProvisionalSumsSheet(wb.Worksheets.Add("Provisional Sums"), boq);
                    BuildNrm2ReferenceSheet(wb.Worksheets.Add("NRM2 Reference"), boq);
                    BuildCarbonSheet(wb.Worksheets.Add("Carbon & Lifecycle"), boq);
                    BuildAuditTrailSheet(wb.Worksheets.Add("Audit Trail"), boq);

                    // Snapshot comparison — only when ≥2 snapshots exist
                    var snaps = BOQCostManager.ListSnapshots(doc);
                    if (snaps.Count >= 2)
                    {
                        var diff = BOQCostManager.CompareSnapshots(snaps[1].Path, snaps[0].Path);
                        BuildSnapshotDiffSheet(wb.Worksheets.Add("Snapshot Comparison"), diff);
                    }

                    wb.SaveAs(outputPath);
                }

                var healthScore = BOQCostManager.ComputeBOQHealth(boq);
                try { Process.Start("explorer.exe", $"/select,\"{outputPath}\""); } catch (Exception ex) { StingLog.Warn($"Explorer open: {ex.Message}"); }

                UI.StingResultPanel.Create("BOQ Exported")
                    .SetSubtitle($"{boq.AllItems.Count:N0} items · grand total UGX {boq.GrandTotalUGX:N0}")
                    .AddSection("FILE")
                    .Text(outputPath)
                    .AddSection("SUMMARY")
                    .Metric("Items", boq.AllItems.Count.ToString("N0"))
                    .Metric("Modeled", $"UGX {boq.ModeledTotalUGX:N0}")
                    .Metric("Provisional", $"UGX {boq.ProvTotalUGX:N0}")
                    .Metric("Grand total", $"UGX {boq.GrandTotalUGX:N0}")
                    .Metric("Carbon", $"{boq.TotalCarbonKg:F0} kgCO₂e")
                    .Metric("Paragraph coverage", $"{boq.ParagraphCoveragePct:F0}%")
                    .Metric("Health score", $"{healthScore.OverallScore:F0}/100 ({healthScore.Grade})")
                    .Show();

                StingLog.Info($"BOQ exported: {Path.GetFileName(outputPath)} ({boq.AllItems.Count} items)");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQExportCommand", ex);
                message = ex.Message;
                TaskDialog.Show("STING BOQ", $"Export failed: {ex.Message}");
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Sheet builders — each receives an IXLWorksheet and the full BOQDocument.
        //  Common patterns:
        //    • Navy header rows 1-2 (project name + prepared by)
        //    • Row 6 column headers with HeaderFill
        //    • Frozen header row, AutoFilter on data sheets
        //    • Totals block at the bottom
        // ══════════════════════════════════════════════════════════════════

        private void BuildSummarySheet(IXLWorksheet ws, BOQDocument boq)
        {
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;

            // Row 1-2 — project banner
            ws.Cell(1, 1).Value = $"{boq.ProjectName} — Bill of Quantities";
            ws.Range(1, 1, 1, 13).Merge().Style.Font.SetBold().Font.SetFontSize(14)
                .Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(NavyFill);
            ws.Cell(2, 1).Value = $"Prepared by STING Tools | {DateTime.Now:dd MMMM yyyy} | {boq.Currency}";
            ws.Range(2, 1, 2, 13).Merge().Style.Font.SetFontSize(11)
                .Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(NavyFill);

            // Row 4 — inline metrics strip
            int r = 4;
            ws.Cell(r, 1).Value = "Project budget:"; ws.Cell(r, 1).Style.Font.SetBold();
            ws.Cell(r, 2).Value = boq.ProjectBudgetUGX;
            ws.Cell(r, 3).Value = "Modeled:"; ws.Cell(r, 3).Style.Font.SetBold();
            ws.Cell(r, 4).Value = boq.ModeledTotalUGX;
            ws.Cell(r, 5).Value = "Provisional:"; ws.Cell(r, 5).Style.Font.SetBold();
            ws.Cell(r, 6).Value = boq.ProvTotalUGX;
            ws.Cell(r, 7).Value = "Grand total:"; ws.Cell(r, 7).Style.Font.SetBold();
            ws.Cell(r, 8).Value = boq.GrandTotalUGX;
            ws.Cell(r, 9).Value = "Coverage:"; ws.Cell(r, 9).Style.Font.SetBold();
            ws.Cell(r, 10).Value = boq.BudgetCoveragePct / 100.0; ws.Cell(r, 10).Style.NumberFormat.Format = "0.0%";
            ws.Cell(r, 11).Value = "Carbon:"; ws.Cell(r, 11).Style.Font.SetBold();
            ws.Cell(r, 12).Value = boq.TotalCarbonKg; ws.Cell(r, 13).Value = "kgCO₂e";

            // Row 6 — column headers
            int hr = 6;
            string[] cols = { "NRM2 §", "Line ref", "Description (NRM2 narrative)", "Unit", "Quantity",
                "Rate UGX", "Total UGX", "Rate USD", "Total USD", "Source", "Discipline", "Level / Location", "Note" };
            for (int i = 0; i < cols.Length; i++)
            {
                ws.Cell(hr, i + 1).Value = cols[i];
            }
            ws.Range(hr, 1, hr, 13).Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(HeaderFill);
            ws.SheetView.FreezeRows(hr);

            // Column widths
            double[] widths = { 8, 10, 58, 6, 9, 14, 14, 12, 12, 9, 9, 18, 22 };
            for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];

            // Data rows — section headers + items
            int row = hr + 1;
            foreach (var sec in boq.Sections)
            {
                ws.Cell(row, 1).Value = $"§{sec.NRM2Section} — {sec.Name}";
                ws.Range(row, 1, row, 13).Merge().Style.Font.SetBold().Font.SetFontSize(11)
                    .Fill.SetBackgroundColor(DisciplineColor(sec.Discipline));
                row++;

                foreach (var item in sec.Items)
                {
                    ws.Cell(row, 1).Value = item.NRM2Section ?? "";
                    ws.Cell(row, 2).Value = item.BOQLineRef ?? "";
                    ws.Cell(row, 3).Value = item.ResolvedNRM2Paragraph ?? "";
                    ws.Cell(row, 3).Style.Alignment.WrapText = true;
                    ws.Cell(row, 4).Value = item.Unit ?? "";
                    ws.Cell(row, 5).Value = item.Quantity; ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.000";
                    ws.Cell(row, 6).Value = item.RateUGX; ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(row, 7).FormulaA1 = $"E{row}*F{row}"; ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(row, 8).Value = item.RateUSD; ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 9).FormulaA1 = $"E{row}*H{row}"; ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 10).Value = SourceLabel(item.Source);
                    ws.Cell(row, 11).Value = item.Discipline ?? "";
                    ws.Cell(row, 12).Value = JoinLevelLocation(item);
                    ws.Cell(row, 13).Value = item.Note ?? "";
                    if (item.Source != BOQRowSource.Model) ws.Range(row, 1, row, 13).Style.Fill.SetBackgroundColor(SourceFill(item.Source));
                    row++;
                }
            }

            // Totals block — 3 blank rows then subtotals
            row += 3;
            int firstDataRow = hr + 1;
            ws.Cell(row, 6).Value = "Subtotal"; ws.Cell(row, 6).Style.Font.SetBold();
            ws.Cell(row, 7).FormulaA1 = $"SUM(G{firstDataRow}:G{row - 4})"; ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            int subtotalRow = row; row++;
            ws.Cell(row, 6).Value = $"Preliminaries ({boq.PrelimPct:F0}%)";
            ws.Cell(row, 7).FormulaA1 = $"G{subtotalRow}*{boq.PrelimPct / 100:F4}"; row++;
            ws.Cell(row, 6).Value = $"Contingency ({boq.ContingencyPct:F0}%)";
            ws.Cell(row, 7).FormulaA1 = $"G{subtotalRow}*{boq.ContingencyPct / 100:F4}"; row++;
            ws.Cell(row, 6).Value = $"Overhead & profit ({boq.OverheadPct:F0}%)";
            ws.Cell(row, 7).FormulaA1 = $"G{subtotalRow}*{boq.OverheadPct / 100:F4}"; row++;
            ws.Cell(row, 6).Value = "GRAND TOTAL";
            ws.Cell(row, 7).FormulaA1 = $"SUM(G{subtotalRow}:G{row - 1})";
            ws.Range(row, 6, row, 9).Style.Fill.SetBackgroundColor(NavyFill)
                .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(12);
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

            if (boq.ProjectBudgetUGX > 0)
            {
                row += 2;
                ws.Cell(row, 6).Value = $"Budget: UGX {boq.ProjectBudgetUGX:N0} | Variance: UGX {boq.BudgetVarianceUGX:N0}";
                var vc = boq.BudgetVarianceUGX >= 0 ? XLColor.LightGreen : XLColor.MistyRose;
                ws.Range(row, 6, row, 9).Merge().Style.Fill.SetBackgroundColor(vc);
            }
        }

        private void BuildItemScheduleSheet(IXLWorksheet ws, BOQDocument boq)
        {
            BannerRow(ws, $"{boq.ProjectName} — Item Schedule (edit in place and re-import via BOQ → Import)");
            string[] cols = { "Line ref", "NRM2 §", "Category", "Discipline", "Item", "Family", "Unit", "Quantity",
                "Rate UGX", "Total UGX", "Rate USD", "Total USD", "Source", "Note", "Revit ElementId",
                "UniqueId", "Level", "Location", "Embodied kgCO2e", "Lifecycle UGX", "Rate confidence" };
            WriteHeader(ws, 3, cols);

            int row = 4;
            foreach (var it in boq.AllItems)
            {
                ws.Cell(row, 1).Value = it.BOQLineRef ?? "";
                ws.Cell(row, 2).Value = it.NRM2Section ?? "";
                ws.Cell(row, 3).Value = it.Category ?? "";
                ws.Cell(row, 4).Value = it.Discipline ?? "";
                ws.Cell(row, 5).Value = it.ItemName ?? "";
                ws.Cell(row, 6).Value = it.FamilyName ?? "";
                ws.Cell(row, 7).Value = it.Unit ?? "";
                ws.Cell(row, 8).Value = it.Quantity;
                ws.Cell(row, 9).Value = it.RateUGX;
                ws.Cell(row, 10).Value = it.TotalUGX;
                ws.Cell(row, 11).Value = it.RateUSD;
                ws.Cell(row, 12).Value = it.TotalUSD;
                ws.Cell(row, 13).Value = SourceLabel(it.Source);
                ws.Cell(row, 14).Value = it.Note ?? "";
                ws.Cell(row, 15).Value = it.RevitElementId;
                ws.Cell(row, 16).Value = it.UniqueId ?? "";
                ws.Cell(row, 17).Value = it.Level ?? "";
                ws.Cell(row, 18).Value = it.Location ?? "";
                ws.Cell(row, 19).Value = it.EmbodiedCarbonKg;
                ws.Cell(row, 20).Value = it.LifecycleCostUGX;
                ws.Cell(row, 21).Value = it.RateConfidence;
                row++;
            }
            ws.Range(3, 1, 3, cols.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(3);
            foreach (var c in ws.ColumnsUsed()) c.AdjustToContents();
        }

        private void BuildMaterialScheduleSheet(IXLWorksheet ws, BOQDocument boq)
        {
            BannerRow(ws, "Material Schedule — items with material-relevant measurement");
            string[] cols = { "NRM2 §", "Category", "Material", "Quantity", "Unit", "Rate UGX", "Rate USD",
                "Total UGX", "Source", "Level", "Carbon kgCO₂e" };
            WriteHeader(ws, 3, cols);
            int row = 4;
            // Materials view == all items where the unit indicates a material measurement
            var matItems = boq.AllItems.Where(i =>
                (i.Unit ?? "").ToLowerInvariant() is var u
                && (u == "m²" || u == "m2" || u == "m³" || u == "m3" || u == "kg")).ToList();
            foreach (var it in matItems)
            {
                ws.Cell(row, 1).Value = it.NRM2Section; ws.Cell(row, 2).Value = it.Category;
                ws.Cell(row, 3).Value = it.ItemName; ws.Cell(row, 4).Value = it.Quantity;
                ws.Cell(row, 5).Value = it.Unit; ws.Cell(row, 6).Value = it.RateUGX;
                ws.Cell(row, 7).Value = it.RateUSD; ws.Cell(row, 8).Value = it.TotalUGX;
                ws.Cell(row, 9).Value = SourceLabel(it.Source); ws.Cell(row, 10).Value = it.Level;
                ws.Cell(row, 11).Value = it.EmbodiedCarbonKg; row++;
            }
            if (matItems.Count == 0)
                ws.Cell(4, 1).Value = "No material-measured items found in this BOQ.";
            ws.Range(3, 1, 3, cols.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(3);
        }

        private void BuildProvisionalSumsSheet(IXLWorksheet ws, BOQDocument boq)
        {
            BannerRow(ws, "Provisional Sums — open PCs awaiting instruction");
            string[] cols = { "PS Ref", "NRM2 §", "Category", "Description", "Unit", "Quantity",
                "Rate UGX", "Rate USD", "Total UGX", "Status", "Note" };
            WriteHeader(ws, 3, cols);
            int row = 4;
            var psRows = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
            foreach (var it in psRows)
            {
                ws.Cell(row, 1).Value = it.BOQLineRef;
                ws.Cell(row, 2).Value = it.NRM2Section;
                ws.Cell(row, 3).Value = it.Category;
                ws.Cell(row, 4).Value = it.ResolvedNRM2Paragraph;
                ws.Cell(row, 4).Style.Alignment.WrapText = true;
                ws.Cell(row, 5).Value = it.Unit;
                ws.Cell(row, 6).Value = it.Quantity;
                ws.Cell(row, 7).Value = it.RateUGX;
                ws.Cell(row, 8).Value = it.RateUSD;
                ws.Cell(row, 9).Value = it.TotalUGX;
                ws.Cell(row, 10).Value = ExtractStatus(it.Note);
                ws.Cell(row, 11).Value = it.Note;
                row++;
            }
            if (psRows.Count == 0)
                ws.Cell(4, 1).Value = "No provisional sums registered on this project.";
            else
            {
                ws.Cell(row + 1, 8).Value = "PS total";
                ws.Cell(row + 1, 9).FormulaA1 = $"SUM(I4:I{row - 1})";
                ws.Range(row + 1, 8, row + 1, 9).Style.Font.SetBold();
            }
            ws.Range(3, 1, 3, cols.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(3);
        }

        private void BuildNrm2ReferenceSheet(IXLWorksheet ws, BOQDocument boq)
        {
            BannerRow(ws, "NRM2 Reference — one row per unique NRM2 section used in this BOQ");
            string[] cols = { "Section §", "Section name", "Discipline", "Items", "Sum UGX", "Coverage %" };
            WriteHeader(ws, 3, cols);
            int row = 4;
            foreach (var s in boq.Sections)
            {
                ws.Cell(row, 1).Value = s.NRM2Section;
                ws.Cell(row, 2).Value = s.Name;
                ws.Cell(row, 3).Value = s.Discipline;
                ws.Cell(row, 4).Value = s.Items.Count;
                ws.Cell(row, 5).Value = s.TotalUGX;
                double cov = s.Items.Count == 0 ? 0 : 100.0 * s.Items.Count(i => !string.IsNullOrEmpty(i.ResolvedNRM2Paragraph)) / s.Items.Count;
                ws.Cell(row, 6).Value = cov / 100.0; ws.Cell(row, 6).Style.NumberFormat.Format = "0%";
                row++;
            }
            row += 1;
            ws.Cell(row, 1).Value = "Paragraphs are STING-authored construction descriptions following NRM2 work section "
                + $"structure. Not reproduced from RICS NRM2. © STING Tools {DateTime.Now.Year}.";
            ws.Range(row, 1, row, 6).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
        }

        private void BuildCarbonSheet(IXLWorksheet ws, BOQDocument boq)
        {
            BannerRow(ws, "Carbon & Lifecycle — per-element embodied carbon and 25-year NPV lifecycle cost");
            string[] cols = { "Line ref", "Category", "Discipline", "Quantity", "Unit",
                "Carbon kgCO₂e", "Lifecycle UGX" };
            WriteHeader(ws, 3, cols);
            int row = 4;
            foreach (var it in boq.AllItems)
            {
                ws.Cell(row, 1).Value = it.BOQLineRef;
                ws.Cell(row, 2).Value = it.Category;
                ws.Cell(row, 3).Value = it.Discipline;
                ws.Cell(row, 4).Value = it.Quantity;
                ws.Cell(row, 5).Value = it.Unit;
                ws.Cell(row, 6).Value = it.EmbodiedCarbonKg;
                ws.Cell(row, 7).Value = it.LifecycleCostUGX;
                row++;
            }
            row += 1;
            ws.Cell(row, 1).Value = "Project total embodied carbon (kgCO₂e):";
            ws.Cell(row, 6).Value = boq.TotalCarbonKg;
            ws.Range(row, 1, row, 7).Style.Font.SetBold();
            row++;
            ws.Cell(row, 1).Value = "RIBA 2030 benchmark target: 300 kgCO₂e per m² GIA (office, residential).";
            ws.Range(row, 1, row, 7).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
            ws.SheetView.FreezeRows(3);
        }

        private void BuildAuditTrailSheet(IXLWorksheet ws, BOQDocument boq)
        {
            BannerRow(ws, "Audit Trail — per-item rate source, quantity basis, last-costed timestamp");
            string[] cols = { "Line ref", "Element id", "UniqueId", "Category", "Family",
                "Rate source", "Quantity basis", "Rate UGX", "Rate USD", "Last costed", "Snapshot ref", "Confidence" };
            WriteHeader(ws, 3, cols);
            int row = 4;
            foreach (var it in boq.AllItems)
            {
                ws.Cell(row, 1).Value = it.BOQLineRef;
                ws.Cell(row, 2).Value = it.RevitElementId;
                ws.Cell(row, 3).Value = it.UniqueId;
                ws.Cell(row, 4).Value = it.Category;
                ws.Cell(row, 5).Value = it.FamilyName;
                ws.Cell(row, 6).Value = it.RateSource;
                ws.Cell(row, 7).Value = it.Unit;
                ws.Cell(row, 8).Value = it.RateUGX;
                ws.Cell(row, 9).Value = it.RateUSD;
                ws.Cell(row, 10).Value = it.LastCosted.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                ws.Cell(row, 11).Value = it.SnapshotRef;
                ws.Cell(row, 12).Value = it.RateConfidence;
                row++;
            }
            ws.Range(3, 1, 3, cols.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(3);
        }

        private void BuildSnapshotDiffSheet(IXLWorksheet ws, BOQSnapshotDiff diff)
        {
            BannerRow(ws, $"Snapshot Comparison — {diff.LabelA} → {diff.LabelB}");
            int row = 3;
            ws.Cell(row, 1).Value = diff.PlainSummary ?? "";
            ws.Range(row, 1, row, 8).Merge().Style.Alignment.WrapText = true;
            row += 2;
            string[] cols = { "NRM2 §", "Category", "Disc", "Snap A total", "Snap B total",
                "Delta UGX", "Delta %", "Change type" };
            WriteHeader(ws, row, cols); row++;
            foreach (var cd in diff.CategoryDiffs)
            {
                ws.Cell(row, 1).Value = cd.NRM2Section;
                ws.Cell(row, 2).Value = cd.Name;
                ws.Cell(row, 3).Value = cd.Discipline;
                ws.Cell(row, 4).Value = cd.TotalA;
                ws.Cell(row, 5).Value = cd.TotalB;
                ws.Cell(row, 6).Value = cd.Delta;
                ws.Cell(row, 7).Value = cd.DeltaPct / 100.0; ws.Cell(row, 7).Style.NumberFormat.Format = "0.0%";
                ws.Cell(row, 8).Value = cd.ChangeType.ToString();
                if (cd.Delta > 0) ws.Range(row, 1, row, 8).Style.Fill.SetBackgroundColor(XLColor.LightGreen);
                else if (cd.Delta < 0) ws.Range(row, 1, row, 8).Style.Fill.SetBackgroundColor(XLColor.MistyRose);
                row++;
            }
            ws.SheetView.FreezeRows(5);
        }

        // ── Sheet helpers ──────────────────────────────────────────────────

        private void BannerRow(IXLWorksheet ws, string text)
        {
            ws.Cell(1, 1).Value = text;
            ws.Range(1, 1, 1, 16).Merge().Style.Font.SetBold().Font.SetFontSize(12)
                .Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(NavyFill);
        }

        private void WriteHeader(IXLWorksheet ws, int row, string[] cols)
        {
            for (int i = 0; i < cols.Length; i++) ws.Cell(row, i + 1).Value = cols[i];
            ws.Range(row, 1, row, cols.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(HeaderFill);
        }

        private XLColor DisciplineColor(string disc)
        {
            switch (disc ?? "")
            {
                case "A": return ArchDisc;
                case "S": return StrDisc;
                case "M": return MepDisc;
                case "E": return EleDisc;
                case "FP": return EleDisc;
                case "P": return PlmDisc;
                case "PS": return PsDisc;
                default: return XLColor.White;
            }
        }

        private XLColor SourceFill(BOQRowSource s)
        {
            switch (s)
            {
                case BOQRowSource.Manual: return ManualRow;
                case BOQRowSource.ProvisionalSum: return PsDisc;
                default: return XLColor.White;
            }
        }

        private string SourceLabel(BOQRowSource s) => s switch
        {
            BOQRowSource.Model => "Model",
            BOQRowSource.Manual => "Manual",
            BOQRowSource.ProvisionalSum => "Provisional Sum",
            _ => ""
        };

        private string JoinLevelLocation(BOQLineItem it)
        {
            if (!string.IsNullOrEmpty(it.Level) && !string.IsNullOrEmpty(it.Location))
                return $"{it.Level} / {it.Location}";
            return !string.IsNullOrEmpty(it.Level) ? it.Level : it.Location ?? "";
        }

        private string ExtractStatus(string note)
        {
            if (string.IsNullOrEmpty(note)) return "Open";
            string lower = note.ToLowerInvariant();
            if (lower.Contains("status:closed") || lower.Contains("closed")) return "Closed";
            if (lower.Contains("status:instructed") || lower.Contains("instructed")) return "Instructed";
            return "Open";
        }
    }
}
