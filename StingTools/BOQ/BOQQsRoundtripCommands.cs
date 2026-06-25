// ══════════════════════════════════════════════════════════════════════════
//  BOQQsRoundtripCommands.cs — P3 — Quantity-Surveyor round-trip.
//
//  A QS lives in Excel, not Revit. These two commands give a clean exchange:
//
//   • BOQQsExportCommand — export the bill to a single "QS Bill" sheet in NRM2
//     trade order (Ref / Description / Unit / Qty / Rate / Amount), per-section
//     collections + a grand summary. Priced or unpriced. Every row carries a
//     STABLE hidden key (UID:<uniqueId> for model rows, MAN:<id> for QS rows)
//     so re-import lands rates on the exact rows even after the model changes
//     and BOQ line refs renumber.
//
//   • BOQQsImportCommand — read the QS's priced workbook, match by the hidden
//     key, show a diff preview (rate changes / new rows / model-quantity drift
//     / unmatched), and on confirm apply: model rates → per-element override
//     sidecar (RateSource="QS"); manual/PS/daywork rates → manual store; QS-
//     added rows → new Manual rows. Model quantities are preserved — a QS
//     quantity change is flagged for review, never silently overwritten.
//     P3.4: model rate changes also seed <project>/_BIM_COORD/rate_card.json
//     (category → modal rate) consumed by ProjectRateCardProvider.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.BOQ
{
    // ── Stable round-trip key ──────────────────────────────────────────────
    internal static class BoqRowKey
    {
        public const string SheetName = "QS Bill";
        public const string Marker = "STING QS BILL";   // banner cell A1 used to identify the sheet

        /// <summary>A key that survives a model rebuild + BOQ-ref renumber.
        /// Model rows key on the (representative) UniqueId — exactly what
        /// ApplyModelOverrides matches on; QS rows key on their stable store Id.</summary>
        public static string For(BOQLineItem it)
        {
            if (it == null) return "";
            if (it.Source != BOQRowSource.Model) return "MAN:" + (it.Id ?? "");
            if (!string.IsNullOrEmpty(it.UniqueId)) return "UID:" + it.UniqueId;
            if (it.RevitElementId > 0) return "EID:" + it.RevitElementId.ToString(CultureInfo.InvariantCulture);
            return "REF:" + (it.BOQLineRef ?? "");
        }

        public static bool IsModelKey(string key)
            => key != null && (key.StartsWith("UID:") || key.StartsWith("EID:") || key.StartsWith("REF:"));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Export
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQQsExportCommand : IExternalCommand
    {
        private static readonly XLColor Navy = XLColor.FromArgb(26, 58, 92);
        private static readonly XLColor SectionFill = XLColor.FromArgb(46, 94, 142);
        private static readonly XLColor CollectionFill = XLColor.FromArgb(232, 238, 245);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                // Priced or unpriced?
                var td = new TaskDialog("QS Bill export")
                {
                    MainInstruction = "Export the Bill of Quantities for the QS",
                    MainContent = "Choose whether to include current rates (priced) or leave Rate/Amount "
                        + "blank for the QS to price (unpriced). Either way, re-import via 'Import QS Bill'."
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Priced — include current rates");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Unpriced — blank for the QS to price");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = td.Show();
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;
                bool priced = choice == TaskDialogResult.CommandLink1;

                // Always a work-section (trade-order) bill regardless of the panel grouping.
                var boq = BOQCostManager.BuildBOQDocument(doc, null, BoqGroupingMode.WorkSection);

                string path = OutputLocationHelper.GetTimestampedPath(
                    doc, priced ? "STING_BOQ_QS_Priced" : "STING_BOQ_QS_Unpriced", ".xlsx");

                using (var wb = new XLWorkbook())
                {
                    var ws = wb.AddWorksheet(BoqRowKey.SheetName);
                    BuildSheet(ws, boq, priced);
                    wb.SaveAs(path);
                }

                StingLog.Info($"QS Bill exported ({(priced ? "priced" : "unpriced")}): {path}");
                var done = new TaskDialog("QS Bill export")
                {
                    MainInstruction = "QS Bill exported",
                    MainContent = $"{(priced ? "Priced" : "Unpriced")} bill written to:\n{path}\n\n"
                        + "Send to the QS to price; re-import the returned workbook via 'Import QS Bill'.",
                    CommonButtons = TaskDialogCommonButtons.Ok
                };
                done.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open containing folder");
                if (done.Show() == TaskDialogResult.CommandLink1)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
                    catch (Exception ex) { StingLog.Warn($"open folder: {ex.Message}"); }
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQQsExportCommand", ex); message = ex.Message; return Result.Failed; }
        }

        private void BuildSheet(IXLWorksheet ws, BOQDocument boq, bool priced)
        {
            // Columns: 1 Ref | 2 Description | 3 Unit | 4 Qty | 5 Rate | 6 Amount | 7 _key (hidden)
            const int cRef = 1, cDesc = 2, cUnit = 3, cQty = 4, cRate = 5, cAmt = 6, cKey = 7;

            ws.Cell(1, 1).Value = BoqRowKey.Marker;
            ws.Range(1, 1, 1, cAmt).Merge().Style.Fill.SetBackgroundColor(Navy)
                .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(14);
            ws.Cell(2, 1).Value = $"{boq.ProjectName} — Bill of Quantities ({(priced ? "PRICED" : "FOR PRICING")}) "
                + $"· {DateTime.Now:dd MMM yyyy} · currency UGX";
            ws.Range(2, 1, 2, cAmt).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

            int hr = 3;
            ws.Cell(hr, cRef).Value = "Ref";
            ws.Cell(hr, cDesc).Value = "Description";
            ws.Cell(hr, cUnit).Value = "Unit";
            ws.Cell(hr, cQty).Value = "Qty";
            ws.Cell(hr, cRate).Value = "Rate UGX";
            ws.Cell(hr, cAmt).Value = "Amount UGX";
            ws.Cell(hr, cKey).Value = "_key";
            ws.Range(hr, 1, hr, cKey).Style.Fill.SetBackgroundColor(SectionFill)
                .Font.SetFontColor(XLColor.White).Font.SetBold();

            int row = hr + 1;
            var collectionCells = new List<int>();   // rows holding section collection totals

            foreach (var sec in boq.Sections.OrderBy(s => SectionSortKey(s)))
            {
                if (sec.Items.Count == 0) continue;

                // Section heading
                ws.Cell(row, cDesc).Value = string.IsNullOrWhiteSpace(sec.NRM2Section)
                    ? sec.Name : $"§{sec.NRM2Section}  {sec.Name}";
                ws.Range(row, 1, row, cKey).Style.Fill.SetBackgroundColor(CollectionFill).Font.SetBold();
                row++;

                int firstItemRow = row;
                foreach (var it in sec.Items)
                {
                    ws.Cell(row, cRef).Value = it.BOQLineRef ?? "";
                    ws.Cell(row, cDesc).Value = DescriptionFor(it);
                    ws.Cell(row, cDesc).Style.Alignment.WrapText = true;
                    ws.Cell(row, cUnit).Value = it.Unit ?? "";
                    ws.Cell(row, cQty).Value = it.Quantity;
                    if (priced && it.RateUGX > 0) ws.Cell(row, cRate).Value = it.RateUGX;
                    // Amount = Qty*Rate so an unpriced bill computes the moment the QS fills Rate.
                    ws.Cell(row, cAmt).FormulaA1 = $"={ColLetter(cQty)}{row}*{ColLetter(cRate)}{row}";
                    ws.Cell(row, cAmt).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(row, cKey).Value = BoqRowKey.For(it);
                    row++;
                }

                // Collection (section subtotal)
                ws.Cell(row, cDesc).Value = $"Collection — {(string.IsNullOrWhiteSpace(sec.NRM2Section) ? sec.Name : "§" + sec.NRM2Section)}";
                ws.Cell(row, cAmt).FormulaA1 = $"=SUM({ColLetter(cAmt)}{firstItemRow}:{ColLetter(cAmt)}{row - 1})";
                ws.Cell(row, cAmt).Style.NumberFormat.Format = "#,##0";
                ws.Range(row, 1, row, cAmt).Style.Font.SetBold().Font.SetItalic();
                collectionCells.Add(row);
                row += 2;   // blank gap between sections
            }

            // ── Grand summary ──
            int subtotalRow = row;
            ws.Cell(row, cDesc).Value = "SUBTOTAL (carried from collections)";
            ws.Cell(row, cAmt).FormulaA1 = collectionCells.Count > 0
                ? "=" + string.Join("+", collectionCells.Select(r => $"{ColLetter(cAmt)}{r}"))
                : "0";
            ws.Range(row, 1, row, cAmt).Style.Font.SetBold();
            ws.Cell(row, cAmt).Style.NumberFormat.Format = "#,##0";
            row++;
            ws.Cell(row, cDesc).Value = $"Preliminaries ({boq.PrelimPct:F0}%)";
            ws.Cell(row, cAmt).FormulaA1 = $"={ColLetter(cAmt)}{subtotalRow}*{boq.PrelimPct / 100:F4}"; row++;
            ws.Cell(row, cDesc).Value = $"Contingency ({boq.ContingencyPct:F0}%)";
            ws.Cell(row, cAmt).FormulaA1 = $"={ColLetter(cAmt)}{subtotalRow}*{boq.ContingencyPct / 100:F4}"; row++;
            ws.Cell(row, cDesc).Value = $"Overhead & profit ({boq.OverheadPct:F0}%)";
            ws.Cell(row, cAmt).FormulaA1 = $"={ColLetter(cAmt)}{subtotalRow}*{boq.OverheadPct / 100:F4}"; row++;
            ws.Cell(row, cDesc).Value = "GRAND TOTAL";
            ws.Cell(row, cAmt).FormulaA1 = $"=SUM({ColLetter(cAmt)}{subtotalRow}:{ColLetter(cAmt)}{row - 1})";
            ws.Range(row, 1, row, cAmt).Style.Fill.SetBackgroundColor(Navy)
                .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(12);
            ws.Cell(row, cAmt).Style.NumberFormat.Format = "#,##0";

            // Hidden key column + layout
            ws.Column(cKey).Hide();
            ws.Column(cDesc).Width = 60;
            ws.Range(hr, 1, hr, cAmt).SetAutoFilter();
            ws.SheetView.FreezeRows(hr);
            ws.Columns(cRef, cAmt).AdjustToContents();
            ws.Column(cDesc).Width = 60;   // re-fix description after autofit
        }

        private static string DescriptionFor(BOQLineItem it)
        {
            string p = it.ResolvedNRM2Paragraph;
            if (!string.IsNullOrWhiteSpace(p) && !p.Contains("[")) return p;
            string name = it.ItemName ?? it.Category ?? "Item";
            return it.SimilarCount > 1 ? $"{name} (×{it.SimilarCount})" : name;
        }

        private static double SectionSortKey(BOQSection s)
        {
            if (int.TryParse(s?.NRM2Section, NumberStyles.Any, CultureInfo.InvariantCulture, out int v)) return v;
            return 9999;
        }

        private static string ColLetter(int col)
        {
            string s = "";
            while (col > 0) { int m = (col - 1) % 26; s = (char)('A' + m) + s; col = (col - 1) / 26; }
            return s;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Import — diff + apply
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQQsImportCommand : IExternalCommand
    {
        // Public so the WPF diff grid can bind to it.
        public class QsDiffRow
        {
            public string Ref { get; set; }
            public string Description { get; set; }
            public string Change { get; set; }        // Rate revised / New row / Qty drift / Unmatched
            public string OldRate { get; set; }
            public string NewRate { get; set; }
            public string ModelQty { get; set; }
            public string ImportQty { get; set; }
            // not bound — used by Apply
            internal string Key;
            internal double NewRateVal;
            internal double ImportQtyVal;
            internal BOQLineItem Current;             // matched live row (model or manual)
            internal string DescVal, UnitVal, SectionVal, DiscVal;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import priced QS Bill", Filter = "Excel workbook (*.xlsx)|*.xlsx"
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                // Current state for the diff (model + manual), trade-order grouping.
                var boq = BOQCostManager.BuildBOQDocument(doc, null, BoqGroupingMode.WorkSection);
                var currentByKey = new Dictionary<string, BOQLineItem>(StringComparer.Ordinal);
                foreach (var it in boq.AllItems)
                {
                    string k = BoqRowKey.For(it);
                    if (!string.IsNullOrEmpty(k)) currentByKey[k] = it;
                }

                var diffs = new List<QsDiffRow>();
                double ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
                const double qtyTol = 0.001;

                using (var wb = new XLWorkbook(dlg.FileName))
                {
                    IXLWorksheet ws = wb.Worksheets.FirstOrDefault(w =>
                        string.Equals(w.Name, BoqRowKey.SheetName, StringComparison.OrdinalIgnoreCase))
                        ?? wb.Worksheets.FirstOrDefault();
                    if (ws == null)
                    {
                        TaskDialog.Show("STING BOQ", "No worksheet found in the selected workbook.");
                        return Result.Failed;
                    }

                    // Locate header row by "Ref" + "_key".
                    int hr = 0, cRef = 1, cDesc = 2, cUnit = 3, cQty = 4, cRate = 5, cKey = 7;
                    for (int r = 1; r <= 8; r++)
                    {
                        // Build header→column map, skipping blanks + duplicate
                        // labels (empty cells would otherwise collide as "").
                        var rowCells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        for (int c = 1; c <= 12; c++)
                        {
                            string h = ws.Cell(r, c).GetString().Trim();
                            if (!string.IsNullOrEmpty(h) && !rowCells.ContainsKey(h)) rowCells[h] = c;
                        }
                        if (rowCells.ContainsKey("Ref") && rowCells.ContainsKey("_key"))
                        {
                            hr = r;
                            cRef = rowCells.GetValueOrDefault("Ref", 1);
                            cDesc = rowCells.GetValueOrDefault("Description", 2);
                            cUnit = rowCells.GetValueOrDefault("Unit", 3);
                            cQty = rowCells.GetValueOrDefault("Qty", 4);
                            cRate = FirstKeyContaining(rowCells, "Rate", 5);
                            cKey = rowCells.GetValueOrDefault("_key", 7);
                            break;
                        }
                    }
                    if (hr == 0)
                    {
                        TaskDialog.Show("STING BOQ",
                            "This workbook is not a STING QS Bill (missing 'Ref' + '_key' header). "
                            + "Export one via 'Export QS Bill' first.");
                        return Result.Failed;
                    }

                    int last = ws.LastRowUsed()?.RowNumber() ?? hr;
                    for (int r = hr + 1; r <= last; r++)
                    {
                        string key = ws.Cell(r, cKey).GetString().Trim();
                        string desc = ws.Cell(r, cDesc).GetString().Trim();
                        double newRate = CellDouble(ws, r, cRate);
                        double impQty = CellDouble(ws, r, cQty);

                        // Structural rows (section headings / collections / summary) have no key.
                        if (string.IsNullOrEmpty(key))
                        {
                            // A genuinely QS-added priced row with a description + figures but no key.
                            if (!string.IsNullOrEmpty(desc) && (newRate > 0 || impQty > 0)
                                && !LooksStructural(desc))
                            {
                                diffs.Add(new QsDiffRow
                                {
                                    Ref = ws.Cell(r, cRef).GetString().Trim(),
                                    Description = desc, Change = "New row",
                                    OldRate = "", NewRate = Fmt(newRate),
                                    ModelQty = "", ImportQty = Fmt(impQty),
                                    Key = "", NewRateVal = newRate, ImportQtyVal = impQty,
                                    DescVal = desc, UnitVal = ws.Cell(r, cUnit).GetString().Trim()
                                });
                            }
                            continue;
                        }

                        if (currentByKey.TryGetValue(key, out var cur))
                        {
                            bool rateChanged = newRate > 0 && Math.Abs(newRate - cur.RateUGX) > 0.5;
                            bool qtyDrift = cur.Source == BOQRowSource.Model
                                && impQty > 0 && Math.Abs(impQty - cur.Quantity) > qtyTol;
                            if (!rateChanged && !qtyDrift) continue;   // unchanged → omit from diff

                            diffs.Add(new QsDiffRow
                            {
                                Ref = cur.BOQLineRef ?? "",
                                Description = DescOf(cur),
                                Change = qtyDrift ? (rateChanged ? "Rate + Qty drift" : "Qty drift (model)")
                                                  : "Rate revised",
                                OldRate = Fmt(cur.RateUGX),
                                NewRate = rateChanged ? Fmt(newRate) : Fmt(cur.RateUGX),
                                ModelQty = Fmt(cur.Quantity),
                                ImportQty = Fmt(impQty),
                                Key = key, NewRateVal = rateChanged ? newRate : cur.RateUGX,
                                ImportQtyVal = impQty, Current = cur
                            });
                        }
                        else
                        {
                            // Keyed row that no longer matches the model (element deleted / re-typed).
                            diffs.Add(new QsDiffRow
                            {
                                Ref = ws.Cell(r, cRef).GetString().Trim(),
                                Description = desc, Change = "Unmatched",
                                OldRate = "", NewRate = Fmt(newRate),
                                ModelQty = "", ImportQty = Fmt(impQty),
                                Key = key, NewRateVal = newRate, ImportQtyVal = impQty
                            });
                        }
                    }
                }

                if (diffs.Count == 0)
                {
                    TaskDialog.Show("STING BOQ — QS Import",
                        "No changes detected — the imported rates/quantities match the current bill.");
                    return Result.Succeeded;
                }

                // ── Diff preview ──
                int rateN = diffs.Count(d => d.Change.StartsWith("Rate"));
                int newN = diffs.Count(d => d.Change == "New row");
                int qtyN = diffs.Count(d => d.Change.Contains("Qty"));
                int unN = diffs.Count(d => d.Change == "Unmatched");

                var grid = new UI.StingDataGridDialog("QS Bill Import — review changes",
                    $"{rateN} rate change(s) · {newN} new row(s) · {qtyN} model-qty drift · {unN} unmatched. "
                    + "Model quantities are preserved; QS quantity changes are flagged, not applied.");
                grid.AddTextColumn("Ref", nameof(QsDiffRow.Ref), 70);
                grid.AddTextColumn("Description", nameof(QsDiffRow.Description), 280);
                grid.AddTextColumn("Change", nameof(QsDiffRow.Change), 130);
                grid.AddTextColumn("Old rate", nameof(QsDiffRow.OldRate), 90);
                grid.AddTextColumn("New rate", nameof(QsDiffRow.NewRate), 90);
                grid.AddTextColumn("Model qty", nameof(QsDiffRow.ModelQty), 80);
                grid.AddTextColumn("Import qty", nameof(QsDiffRow.ImportQty), 80);
                grid.SetItems(diffs);
                grid.AddActionButton("Cancel", "Cancel");
                grid.AddActionButton("Apply", "Apply", isPrimary: true);
                if (grid.ShowDialog() != true) return Result.Cancelled;

                // ── Apply ──
                int appliedModel = 0, appliedManual = 0, added = 0, flaggedQty = 0;
                var manualStore = BOQCostManager.LoadManualStore(doc);
                var rateCardByCategory = new Dictionary<string, (double rate, string unit)>(StringComparer.OrdinalIgnoreCase);

                foreach (var d in diffs)
                {
                    if (d.Change == "Unmatched") continue;   // nothing safe to do

                    if (d.Change == "New row")
                    {
                        manualStore.ManualRows.Add(new BOQLineItem
                        {
                            NRM2Section = "22",
                            Discipline = "A",
                            Category = "QS-added",
                            ItemName = d.DescVal ?? d.Description,
                            Unit = string.IsNullOrEmpty(d.UnitVal) ? "item" : d.UnitVal,
                            Quantity = d.ImportQtyVal > 0 ? d.ImportQtyVal : 1,
                            RateUGX = d.NewRateVal,
                            RateUSD = ugxPerUsd > 0 ? Math.Round(d.NewRateVal / ugxPerUsd, 2) : 0,
                            Source = BOQRowSource.Manual,
                            RateSource = "QS",
                            RateConfidence = 80,
                            Note = "Added by QS via Bill import"
                        });
                        added++;
                        continue;
                    }

                    if (d.Current == null) continue;
                    bool qtyDrift = d.Change.Contains("Qty");
                    if (qtyDrift) flaggedQty++;

                    if (d.Current.Source == BOQRowSource.Model)
                    {
                        // Persist the QS rate as a per-element override (survives rebuild
                        // via ApplyModelOverrides; aggregated rows key on the rep UniqueId).
                        string note = qtyDrift
                            ? $"QS rate; QS measured qty {Fmt(d.ImportQtyVal)} vs model {Fmt(d.Current.Quantity)} — review."
                            : "QS rate (Bill import)";
                        BOQCostManager.UpsertModelOverride(doc, new BOQModelOverride
                        {
                            UniqueId = d.Current.UniqueId,
                            ElementId = d.Current.RevitElementId,
                            RateUGX = d.NewRateVal,
                            RateUSD = ugxPerUsd > 0 ? Math.Round(d.NewRateVal / ugxPerUsd, 2) : 0,
                            RateSource = "QS",
                            Note = note,
                            ModifiedBy = Environment.UserName ?? ""
                        });
                        appliedModel++;
                        // P3.4 — seed the project rate card by category (modal-ish: last wins).
                        if (!string.IsNullOrEmpty(d.Current.Category))
                            rateCardByCategory[d.Current.Category] = (d.NewRateVal, d.Current.Unit ?? "each");
                    }
                    else
                    {
                        // Manual / PS / daywork / PC sum — find the store row by Id and update.
                        var stored = manualStore.ManualRows.FirstOrDefault(m => m.Id == d.Current.Id);
                        if (stored != null)
                        {
                            stored.RateUGX = d.NewRateVal;
                            stored.RateUSD = ugxPerUsd > 0 ? Math.Round(d.NewRateVal / ugxPerUsd, 2) : 0;
                            stored.RateSource = "QS";
                            if (d.ImportQtyVal > 0) stored.Quantity = d.ImportQtyVal;   // QS owns manual qty
                            appliedManual++;
                        }
                    }
                }

                BOQCostManager.SaveManualRows(doc, manualStore.ManualRows, manualStore.ProjectBudgetUGX);
                if (rateCardByCategory.Count > 0) WriteRateCard(doc, rateCardByCategory);

                // Pick up the new rate card + overrides on the next build.
                Rates.RateProviderRegistry.Invalidate();

                TaskDialog.Show("STING BOQ — QS Import",
                    $"Applied:\n"
                    + $"  • {appliedModel} model-row rate override(s) (RateSource=QS)\n"
                    + $"  • {appliedManual} manual/PS/daywork rate update(s)\n"
                    + $"  • {added} QS-added row(s) → manual store\n"
                    + $"  • {flaggedQty} model-quantity drift(s) flagged for review (model qty preserved)\n"
                    + (rateCardByCategory.Count > 0
                        ? $"  • {rateCardByCategory.Count} category rate(s) seeded into rate_card.json\n" : "")
                    + "\nRefresh the BOQ panel to see the updated bill.");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQQsImportCommand", ex); message = ex.Message; return Result.Failed; }
        }

        // P3.4 — merge category rates into <project>/_BIM_COORD/rate_card.json
        // (the file ProjectRateCardProvider reads). Existing entries kept unless
        // re-priced this import.
        private static void WriteRateCard(Document doc, Dictionary<string, (double rate, string unit)> byCat)
        {
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                string parent = Path.GetDirectoryName(bimDir);
                if (string.IsNullOrEmpty(parent)) return;
                string dir = Path.Combine(parent, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "rate_card.json");

                var entries = new List<RateCardEntry>();
                if (File.Exists(path))
                {
                    try { entries = JsonConvert.DeserializeObject<List<RateCardEntry>>(File.ReadAllText(path)) ?? new List<RateCardEntry>(); }
                    catch (Exception ex) { StingLog.Warn($"rate_card read: {ex.Message}"); }
                }
                foreach (var kv in byCat)
                {
                    var existing = entries.FirstOrDefault(e => string.Equals(e.category, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) { existing.unitRate = kv.Value.rate; existing.currency = "UGX"; existing.unit = kv.Value.unit; existing.note = "QS Bill import"; }
                    else entries.Add(new RateCardEntry { category = kv.Key, unitRate = kv.Value.rate, currency = "UGX", unit = kv.Value.unit, note = "QS Bill import" });
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
                StingLog.Info($"QS import: wrote {entries.Count} entries to rate_card.json");
            }
            catch (Exception ex) { StingLog.Warn($"WriteRateCard: {ex.Message}"); }
        }

        private class RateCardEntry
        {
            public string category { get; set; }
            public double unitRate { get; set; }
            public string currency { get; set; }
            public string unit { get; set; }
            public string note { get; set; }
        }

        private static string DescOf(BOQLineItem it)
        {
            string p = it.ResolvedNRM2Paragraph;
            if (!string.IsNullOrWhiteSpace(p) && !p.Contains("[")) return p;
            return it.ItemName ?? it.Category ?? "Item";
        }

        private static bool LooksStructural(string desc)
        {
            string d = (desc ?? "").ToLowerInvariant();
            return d.StartsWith("collection") || d.StartsWith("subtotal")
                || d.Contains("grand total") || d.StartsWith("preliminaries")
                || d.StartsWith("contingency") || d.StartsWith("overhead") || d.StartsWith("§");
        }

        private static int FirstKeyContaining(Dictionary<string, int> map, string token, int fallback)
        {
            foreach (var kv in map)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            return fallback;
        }

        private static double CellDouble(IXLWorksheet ws, int r, int c)
        {
            try { return ws.Cell(r, c).TryGetValue(out double v) ? v : 0; }
            catch { return 0; }
        }

        private static string Fmt(double v) => v.ToString("#,##0.###", CultureInfo.InvariantCulture);
    }
}
