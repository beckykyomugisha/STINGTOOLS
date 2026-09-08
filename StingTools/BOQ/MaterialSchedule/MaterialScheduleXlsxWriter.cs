// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleXlsxWriter.cs — MAT-SCHED ClosedXML renderer.
//
//  ShowPrices is a RENDERER flag: the engine computes identically either way,
//  so the priced and unpriced documents can never disagree about quantities.
//
//  Styling comes from BoqXlsxStyle so this workbook and the BOQ workbook are
//  visually one family.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using StingTools.Core.MaterialSchedule;

namespace StingTools.BOQ.MaterialSchedule
{
    internal static class MaterialScheduleXlsxWriter
    {
        public static void Write(MaterialScheduleDocument doc, string path)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));

            using (var wb = new XLWorkbook())
            {
                WriteScheduleSheet(wb.Worksheets.Add("Material Schedule"), doc);
                WriteSummarySheet(wb.Worksheets.Add("Summary"), doc);
                WriteByTypeSheet(wb, doc);
                WriteValidationSheet(wb.Worksheets.Add("Validation"), doc);

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                wb.SaveAs(path);
            }
        }

        private static void WriteScheduleSheet(IXLWorksheet ws, MaterialScheduleDocument doc)
        {
            bool priced = doc.Options.ShowPrices;
            BoqXlsxStyle.BannerRow(ws,
                $"MATERIAL SCHEDULE — {doc.ProjectName}"
                + (priced ? "" : "  (QUANTITIES ONLY — NO PRICES)"));

            // "Rate source" is column 9, AFTER Amount, not between Rate and it.
            // Amount is column 8 and is written by four separate paths (commodity,
            // labour, provisional sum, sub-total); inserting mid-table would move
            // every one of them and the F*G formula with them, to gain nothing a
            // reader would notice.
            string[] cols = priced
                ? new[] { "Item", "Description", "Unit", "Net Qty", "Waste %", "Order Qty",
                          "Rate UGX", "Amount UGX", "Rate source" }
                : new[] { "Item", "Description", "Unit", "Net Qty", "Waste %", "Order Qty" };

            int row = 3;
            foreach (var stage in doc.Stages)
            {
                // Stage heading
                var head = ws.Cell(row, 1);
                head.Value = $"{stage.Letter}    {stage.Title}";
                head.Style.Font.Bold = true;
                head.Style.Fill.BackgroundColor = BoqXlsxStyle.NavyFill;
                head.Style.Font.FontColor = XLColor.White;
                ws.Range(row, 1, row, cols.Length).Merge();
                row++;

                if (!string.IsNullOrWhiteSpace(stage.Preamble))
                {
                    var pre = ws.Cell(row, 1);
                    pre.Value = stage.Preamble;
                    pre.Style.Font.Italic = true;
                    ws.Range(row, 1, row, cols.Length).Merge();
                    row++;
                }

                BoqXlsxStyle.WriteHeader(ws, row, cols);
                row++;

                int item = 1;
                foreach (var c in stage.Commodities)
                {
                    string desc = string.IsNullOrWhiteSpace(c.Spec)
                        ? c.Description : $"{c.Description} — {c.Spec}";
                    if (c.IsMemorandum) desc += $"   [memo — {c.MemorandumNote}]";

                    ws.Cell(row, 1).Value = item++;
                    ws.Cell(row, 2).Value = desc;
                    ws.Cell(row, 3).Value = DisplayUnit(c.SupplierUnit);
                    ws.Cell(row, 4).Value = Math.Round(c.NetQuantity, 2);
                    ws.Cell(row, 5).Value = c.WastagePct;
                    ws.Cell(row, 6).Value = c.OrderQuantity;
                    if (priced)
                    {
                        // A memorandum row gets NO rate cell and NO formula. Its
                        // constituents carry the money; leaving an empty rate
                        // cell here is what invited pricing the same wall twice.
                        ws.Cell(row, 9).Value = RateProvenanceLabel.For(c);
                        if (c.IsMemorandum)
                        {
                            ws.Cell(row, 8).Value = "—";
                            ws.Range(row, 1, row, cols.Length).Style.Font.Italic = true;
                        }
                        else
                        {
                            ws.Cell(row, 7).Value = c.RateUGX;
                            // Live formula, not a baked number — the workbook stays
                            // arithmetically honest if a QS edits a rate downstream.
                            // No leading '=' — that is the convention every other
                            // FormulaA1 site in BOQExportCommand already uses.
                            ws.Cell(row, 8).FormulaA1 = $"F{row}*G{row}";
                            BoqXlsxStyle.MoneyFormat(ws.Range(row, 7, row, 8));
                            if (c.IsUnpriced)
                                ws.Range(row, 1, row, cols.Length).Style.Fill.BackgroundColor = BoqXlsxStyle.ManualRow;
                        }
                    }
                    row++;
                }

                foreach (var l in stage.Labour)
                {
                    ws.Cell(row, 2).Value = l.Description
                        + (l.SuggestedUGX.HasValue ? $"  (suggested {l.SuggestedUGX.Value:N0} — {l.SuggestionBasis})" : "");
                    ws.Cell(row, 2).Style.Font.Italic = true;
                    if (priced) { ws.Cell(row, 8).Value = l.AmountUGX; BoqXlsxStyle.MoneyFormat(ws.Range(row, 8, row, 8)); }
                    row++;
                }

                foreach (var p in stage.ProvisionalSums)
                {
                    ws.Cell(row, 2).Value = p.Description;
                    if (priced) { ws.Cell(row, 8).Value = p.AmountUGX; BoqXlsxStyle.MoneyFormat(ws.Range(row, 8, row, 8)); }
                    row++;
                }

                if (priced)
                {
                    var sub = ws.Cell(row, 2);
                    sub.Value = "Sub-Total carried to summary";
                    sub.Style.Font.Bold = true;
                    ws.Cell(row, 8).Value = stage.SubTotalUGX;
                    ws.Cell(row, 8).Style.Font.Bold = true;
                    BoqXlsxStyle.MoneyFormat(ws.Range(row, 8, row, 8));
                    row++;
                }
                row++;   // blank spacer between stages
            }

            foreach (var c in ws.ColumnsUsed()) c.AdjustToContents();
        }

        private static void WriteSummarySheet(IXLWorksheet ws, MaterialScheduleDocument doc)
        {
            bool priced = doc.Options.ShowPrices;
            BoqXlsxStyle.BannerRow(ws, priced ? "SUMMARY" : "SUMMARY — TOTAL QUANTITIES BY COMMODITY");

            if (priced)
            {
                BoqXlsxStyle.WriteHeader(ws, 3, new[] { "", "Element", "Amount UGX" });
                int row = 4;
                // Projected from the body — see MaterialScheduleDocument.Summary.
                foreach (var (letter, title, subtotal) in doc.Summary)
                {
                    ws.Cell(row, 1).Value = letter;
                    ws.Cell(row, 2).Value = title;
                    ws.Cell(row, 3).Value = subtotal;
                    BoqXlsxStyle.MoneyFormat(ws.Range(row, 3, row, 3));
                    row++;
                }
                row++;
                WriteTotal(ws, row++, "Sub-total", doc.WorksSubtotalUGX);
                WriteTotal(ws, row++, $"Add {doc.Options.ContingencyPct:N0}% contingency", doc.ContingencyUGX);
                WriteTotal(ws, row, "GRAND TOTAL", doc.GrandTotalUGX, bold: true);
            }
            else
            {
                BoqXlsxStyle.WriteHeader(ws, 3, new[] { "Commodity", "Unit", "Total Order Qty" });
                int row = 4;
                var rolled = doc.Stages.SelectMany(s => s.Commodities)
                    .GroupBy(c => new { c.CommodityKey, c.SupplierUnit })
                    .OrderBy(g => g.Key.CommodityKey);
                foreach (var g in rolled)
                {
                    ws.Cell(row, 1).Value = g.First().Description;
                    ws.Cell(row, 2).Value = DisplayUnit(g.Key.SupplierUnit);
                    ws.Cell(row, 3).Value = g.Sum(c => c.OrderQuantity);
                    row++;
                }
            }
            foreach (var c in ws.ColumnsUsed()) c.AdjustToContents();
        }

        private static void WriteTotal(IXLWorksheet ws, int row, string label, double value, bool bold = false)
        {
            ws.Cell(row, 2).Value = label;
            ws.Cell(row, 3).Value = value;
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 3).Style.Font.Bold = true;
            if (bold) ws.Range(row, 1, row, 3).Style.Fill.BackgroundColor = BoqXlsxStyle.HeaderFill;
            if (bold) ws.Range(row, 1, row, 3).Style.Font.FontColor = XLColor.White;
            BoqXlsxStyle.MoneyFormat(ws.Range(row, 3, row, 3));
        }

        /// <summary>
        /// Each commodity split across the model TYPES that produced it.
        ///
        /// The schedule aggregates because that is what you BUY — three shingle
        /// roofs are one pile of bundles. This is for the three things a
        /// quantity gets used for that aggregation destroys: checking a number
        /// against the model, phasing a delivery, and dividing work between
        /// subcontractors.
        ///
        /// NOT written when nothing was split. A sheet of single-type rows
        /// repeats the schedule, and a second document that merely restates the
        /// first is one more place for the two to disagree.
        /// </summary>
        private static void WriteByTypeSheet(XLWorkbook wb, MaterialScheduleDocument doc)
        {
            var breakdowns = CommodityTypeBreakdown.Build(
                doc.Stages.SelectMany(st => st.Commodities), doc.SourceByType);

            if (breakdowns.All(b => b.IsSingleType)) return;

            var ws = wb.Worksheets.Add("By Type");
            BoqXlsxStyle.BannerRow(ws, "BY TYPE — what produced each commodity");

            int row = 3;
            ws.Cell(row, 1).Value =
                "The ORDER line in the schedule is what you buy and is unchanged. Each part below is "
              + "that order APPORTIONED by measured share — not re-converted, because rounding each "
              + "type up to whole units separately would order more than the schedule says.";
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Range(row, 1, row, 6).Merge();
            row += 2;

            BoqXlsxStyle.WriteHeader(ws, row,
                new[] { "Commodity", "Model type", "Measured", "Share", "Order qty", "Unit" });
            row++;

            foreach (var b in breakdowns.Where(x => !x.IsSingleType))
            {
                var head = ws.Cell(row, 1);
                head.Value = b.Description;
                head.Style.Font.Bold = true;
                ws.Cell(row, 5).Value = b.OrderQuantity;
                ws.Cell(row, 6).Value = DisplayUnit(b.SupplierUnit);
                ws.Cell(row, 5).Style.Font.Bold = true;
                row++;

                foreach (var part in b.Parts)
                {
                    ws.Cell(row, 2).Value = part.TypeName;
                    ws.Cell(row, 3).Value = Math.Round(part.SourceQuantity, 2);
                    ws.Cell(row, 4).Value = Math.Round(part.Share * 100.0, 1) / 100.0;
                    ws.Cell(row, 4).Style.NumberFormat.Format = "0.0%";
                    ws.Cell(row, 5).Value = part.OrderQuantity;
                    ws.Cell(row, 6).Value = DisplayUnit(b.SupplierUnit);
                    row++;
                }
                row++;
            }

            foreach (var c in ws.ColumnsUsed()) c.AdjustToContents();
        }

        private static void WriteValidationSheet(IXLWorksheet ws, MaterialScheduleDocument doc)
        {
            BoqXlsxStyle.BannerRow(ws, "VALIDATION");

            int row = 3;

            // Export notes FIRST. They explain why the schedule looks the way it
            // does — which sections are empty and why — so they belong above the
            // per-row issues, not after them.
            if (doc.Warnings != null && doc.Warnings.Count > 0)
            {
                BoqXlsxStyle.WriteHeader(ws, row, new[] { "", "", "", "EXPORT NOTES — what this run did and did not measure" });
                row++;
                foreach (string w in doc.Warnings)
                {
                    if (string.IsNullOrWhiteSpace(w)) continue;
                    ws.Cell(row, 1).Value = "NOTE";
                    ws.Cell(row, 4).Value = w;
                    ws.Cell(row, 4).Style.Alignment.WrapText = true;
                    row++;
                }
                row++;   // one blank line between the notes and the issues
            }

            BoqXlsxStyle.WriteHeader(ws, row, new[] { "Code", "Stage", "Commodity", "Issue" });
            row++;
            int firstIssueRow = row;
            foreach (var i in doc.Reconciliation.Issues)
            {
                ws.Cell(row, 1).Value = i.Code;
                ws.Cell(row, 2).Value = i.StageId;
                ws.Cell(row, 3).Value = i.CommodityKey;
                ws.Cell(row, 4).Value = i.Message;
                ws.Cell(row, 4).Style.Alignment.WrapText = true;
                row++;
            }
            if (doc.Reconciliation.IsClean)
                ws.Cell(firstIssueRow, 1).Value = "No reconciliation issues. Rates are consistent, "
                                                + "sections are correctly lettered, and every commodity is priced.";

            ws.Column(4).Width = 90;
            foreach (var c in ws.Columns(1, 3)) c.AdjustToContents();
        }

        /// <summary>
        /// Superscript the cubic/square metre for display. A converted row shows
        /// the rule's supplier unit and an unconverted one shows the BOQ's raw
        /// token, so one export printed "m3" against bedding mortar and "m³"
        /// against in-situ concrete two rows apart. Display only — nothing
        /// downstream parses these cells.
        /// </summary>
        internal static string DisplayUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return unit;
            string u = unit.Trim();
            if (string.Equals(u, "m2", StringComparison.OrdinalIgnoreCase)) return "m²";
            if (string.Equals(u, "m3", StringComparison.OrdinalIgnoreCase)) return "m³";
            return unit;
        }

    }
}
