// ══════════════════════════════════════════════════════════════════════════
//  TenderAdjudicationCommands.cs — WP4a Tool 1: Tender Comparison & Adjudication.
//
//  Import N priced QS-Bill returns (each exported via "Export QS Bill", so they
//  carry the stable hidden _key column that survives a model rebuild), join them
//  back onto the live BOQ rows, build a per-line × bidder comparison matrix, run
//  arithmetic-error / zero-rate / rate-outlier / front-loading checks, rank the
//  bidders by corrected total and recommend the most advantageous, then persist
//  the evaluation and (optionally) stamp the winner's rates back onto the bill as
//  RateSource = "tender:<bidder>" so the awarded prices become the contract
//  baseline.
//
//  Persists to <project>/_BIM_COORD/tender_adjudication.json.
//  Command tag: Tender_Adjudicate.
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
using StingTools.BOQ;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Cost
{
    // ── Persistence model ─────────────────────────────────────────────────

    public class TenderAdjudication
    {
        public string SchemaVersion = "1.0";
        public string ProjectName = "";
        public string GeneratedDate = "";
        public string Currency = "UGX";
        public double PreTenderEstimateUGX;     // the live BOQ grand total as the QS estimate
        public List<TenderBidResult> Bids = new List<TenderBidResult>();
        public string RecommendedBidder = "";
        public string RecommendationNote = "";
    }

    public class TenderBidResult
    {
        public string Bidder = "";
        public int LinesPriced;
        public double TenderTotalUGX;           // Σ stated amount (as submitted)
        public double CorrectedTotalUGX;        // Σ rate × live quantity (arithmetic-corrected)
        public int ArithmeticErrors;            // rows where stated amount ≠ rate × qty
        public double ArithmeticCorrectionUGX;  // Σ signed (corrected − stated)
        public int ZeroRatedMeasured;           // measured rows left unpriced
        public int RateOutliers;                // rows far from the bidder median
        public bool FrontLoadingFlag;
        public int Rank;
    }

    internal static class TenderAdjudicationStore
    {
        private static string PathFor(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "tender_adjudication.json");
            }
            catch { return null; }
        }

        public static void Save(Document doc, TenderAdjudication a)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || a == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(a, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"TenderAdjudicationStore.Save: {ex.Message}"); }
        }

        public static TenderAdjudication Load(Document doc)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || !File.Exists(p)) return null;
                return JsonConvert.DeserializeObject<TenderAdjudication>(File.ReadAllText(p));
            }
            catch (Exception ex) { StingLog.Warn($"TenderAdjudicationStore.Load: {ex.Message}"); return null; }
        }
    }

    // Parsed bidder workbook: key → priced row.
    internal class BidLine
    {
        public double Rate;
        public double Qty;
        public double StatedAmount;
        public string Section = "";
    }

    // ── Command ────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TenderAdjudicateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq == null || boq.AllItems.Count == 0)
                {
                    TaskDialog.Show("Tender Adjudication", "No BOQ rows to adjudicate against.");
                    return Result.Cancelled;
                }
                string ccy = boq.Currency ?? "UGX";

                // Live bill by stable key — the join target. Model rows only (the
                // measured bill the bidders priced).
                var liveByKey = new Dictionary<string, BOQLineItem>(StringComparer.Ordinal);
                foreach (var it in boq.AllItems)
                {
                    string k = BoqRowKey.For(it);
                    if (!string.IsNullOrEmpty(k)) liveByKey[k] = it;
                }
                var orderedKeys = boq.AllItems
                    .Where(i => !string.IsNullOrEmpty(BoqRowKey.For(i)))
                    .Select(i => BoqRowKey.For(i)).Distinct().ToList();

                // Pick the bidder workbooks.
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select the priced QS-Bill returns (one .xlsx per bidder)",
                    Filter = "Excel workbooks (*.xlsx)|*.xlsx",
                    Multiselect = true
                };
                if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return Result.Cancelled;

                // Parse each bidder.
                var bidders = new List<string>();
                var bidData = new Dictionary<string, Dictionary<string, BidLine>>();
                foreach (var file in dlg.FileNames)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    var parsed = ParseBidWorkbook(file);
                    if (parsed.Count == 0) { StingLog.Warn($"Tender: {name} parsed 0 priced rows (not a QS Bill?)."); continue; }
                    bidders.Add(name);
                    bidData[name] = parsed;
                }
                if (bidders.Count == 0)
                {
                    StingResultPanel.Create("Tender Adjudication")
                        .AddSection("NO VALID RETURNS")
                        .Text("None of the selected workbooks is a STING QS Bill (need the 'Ref' + '_key' header). "
                            + "Each bidder must price a workbook produced by Export QS Bill.")
                        .Show();
                    return Result.Cancelled;
                }

                double outlierPct = TagConfig.GetConfigDouble("COST_TENDER_OUTLIER_PCT", 25.0);

                // Per-row median rate across bidders (for outlier detection).
                var medianRate = new Dictionary<string, double>();
                foreach (var key in orderedKeys)
                {
                    var rates = bidders.Select(b => bidData[b].TryGetValue(key, out var bl) ? bl.Rate : 0)
                        .Where(x => x > 0).ToList();
                    medianRate[key] = Median(rates);
                }

                // Evaluate each bidder.
                var results = new List<TenderBidResult>();
                foreach (var b in bidders)
                {
                    var lines = bidData[b];
                    var res = new TenderBidResult { Bidder = b };
                    var earlyRates = new List<double>(); var lateRates = new List<double>();
                    int third = Math.Max(1, orderedKeys.Count / 3);

                    for (int idx = 0; idx < orderedKeys.Count; idx++)
                    {
                        string key = orderedKeys[idx];
                        if (!liveByKey.TryGetValue(key, out var live)) continue;
                        bool measured = IsMeasured(live.Unit);
                        double qty = live.Quantity;   // adjudicate on the authoritative bill quantity
                        if (!lines.TryGetValue(key, out var bl))
                        {
                            if (measured && live.Source == BOQRowSource.Model) res.ZeroRatedMeasured++;
                            continue;
                        }
                        double rate = bl.Rate;
                        if (rate <= 0)
                        {
                            if (measured && live.Source == BOQRowSource.Model) res.ZeroRatedMeasured++;
                            continue;
                        }
                        res.LinesPriced++;
                        double corrected = rate * qty;
                        res.CorrectedTotalUGX += corrected;
                        res.TenderTotalUGX += bl.StatedAmount > 0 ? bl.StatedAmount : corrected;

                        // Arithmetic check: stated amount vs rate × qty.
                        if (bl.StatedAmount > 0 && Math.Abs(bl.StatedAmount - corrected) > Math.Max(1.0, corrected * 0.001))
                        {
                            res.ArithmeticErrors++;
                            res.ArithmeticCorrectionUGX += corrected - bl.StatedAmount;
                        }

                        // Outlier vs the cross-bidder median.
                        double med = medianRate.TryGetValue(key, out var mr) ? mr : 0;
                        if (med > 0 && Math.Abs(rate - med) / med * 100.0 > outlierPct) res.RateOutliers++;

                        if (idx < third) earlyRates.Add(rate);
                        else if (idx >= orderedKeys.Count - third) lateRates.Add(rate);
                    }

                    // Basic front-loading: early-section rates inflated vs late.
                    double earlyAvg = earlyRates.Count > 0 ? earlyRates.Average() : 0;
                    double lateAvg = lateRates.Count > 0 ? lateRates.Average() : 0;
                    res.FrontLoadingFlag = lateAvg > 0 && earlyAvg / lateAvg > 1.20;

                    res.CorrectedTotalUGX = Math.Round(res.CorrectedTotalUGX, 0);
                    res.TenderTotalUGX = Math.Round(res.TenderTotalUGX, 0);
                    res.ArithmeticCorrectionUGX = Math.Round(res.ArithmeticCorrectionUGX, 0);
                    results.Add(res);
                }

                // Rank by corrected total (most advantageous = lowest after correction).
                var ranked = results.OrderBy(r => r.CorrectedTotalUGX).ToList();
                for (int i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
                var winner = ranked.FirstOrDefault();

                var adj = new TenderAdjudication
                {
                    ProjectName = boq.ProjectName ?? doc.Title,
                    GeneratedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    Currency = ccy,
                    PreTenderEstimateUGX = Math.Round(boq.GrandTotalUGX, 0),
                    Bids = ranked,
                    RecommendedBidder = winner?.Bidder ?? "",
                    RecommendationNote = winner == null ? "" :
                        $"Lowest corrected tender. {winner.ArithmeticErrors} arithmetic error(s) corrected " +
                        $"({(winner.ArithmeticCorrectionUGX >= 0 ? "+" : "")}{ccy} {winner.ArithmeticCorrectionUGX:N0}); " +
                        $"{winner.ZeroRatedMeasured} unpriced measured row(s); {winner.RateOutliers} rate outlier(s)" +
                        (winner.FrontLoadingFlag ? "; front-loading suspected." : ".")
                };
                TenderAdjudicationStore.Save(doc, adj);

                string xlsx = ExportXlsx(doc, adj, boq, liveByKey, orderedKeys, bidData);

                // Report.
                var panel = StingResultPanel.Create("Tender Adjudication")
                    .AddSection("RECOMMENDATION")
                    .Metric("Most advantageous", winner?.Bidder ?? "—")
                    .Metric("Corrected total", winner != null ? $"{ccy} {winner.CorrectedTotalUGX:N0}" : "—")
                    .Metric("QS pre-tender estimate", $"{ccy} {adj.PreTenderEstimateUGX:N0}")
                    .Text(adj.RecommendationNote)
                    .AddSection("RANKING (corrected total)");
                foreach (var r in ranked)
                    panel.Metric($"#{r.Rank} {r.Bidder}",
                        $"{ccy} {r.CorrectedTotalUGX:N0}  ·  {r.ArithmeticErrors} arith · {r.ZeroRatedMeasured} unpriced · {r.RateOutliers} outlier{(r.FrontLoadingFlag ? " · front-load?" : "")}");
                if (xlsx != null) panel.SetCsvPath(xlsx);
                panel.Show();

                // Offer to stamp the winner's rates as the contract baseline.
                if (winner != null)
                {
                    var td = new TaskDialog("Tender Adjudication")
                    {
                        MainInstruction = $"Award to {winner.Bidder}?",
                        MainContent = $"Stamp {winner.Bidder}'s rates onto the bill as RateSource = \"tender:{winner.Bidder}\" "
                            + "(written as protected model overrides — the awarded prices become the contract baseline on the next Refresh).",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No
                    };
                    if (td.Show() == TaskDialogResult.Yes)
                    {
                        int stamped = StampWinnerRates(doc, winner.Bidder, bidData[winner.Bidder], liveByKey);
                        StingLog.Info($"Tender award: stamped {stamped} rate(s) from {winner.Bidder}.");
                        TaskDialog.Show("Tender Adjudication",
                            $"Stamped {stamped} rate(s) from {winner.Bidder}. Refresh the bill to apply.");
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Tender_Adjudicate", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Bidder workbook parse (reuses the QS-Bill _key spine) ──────────
        private static Dictionary<string, BidLine> ParseBidWorkbook(string file)
        {
            var map = new Dictionary<string, BidLine>(StringComparer.Ordinal);
            try
            {
                using (var wb = new XLWorkbook(file))
                {
                    IXLWorksheet ws = wb.Worksheets.FirstOrDefault(w =>
                        string.Equals(w.Name, BoqRowKey.SheetName, StringComparison.OrdinalIgnoreCase))
                        ?? wb.Worksheets.FirstOrDefault();
                    if (ws == null) return map;

                    int hr = 0, cQty = 4, cRate = 5, cAmt = 6, cKey = 7, cSec = 1;
                    for (int r = 1; r <= 8; r++)
                    {
                        var cells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        for (int c = 1; c <= 14; c++)
                        {
                            string h = ws.Cell(r, c).GetString().Trim();
                            if (!string.IsNullOrEmpty(h) && !cells.ContainsKey(h)) cells[h] = c;
                        }
                        if (cells.ContainsKey("Ref") && cells.ContainsKey("_key"))
                        {
                            hr = r;
                            cQty = cells.GetValueOrDefault("Qty", 4);
                            cRate = FirstContaining(cells, "Rate", 5);
                            cAmt = FirstContaining(cells, "Amount", 6);
                            cKey = cells.GetValueOrDefault("_key", 7);
                            cSec = cells.GetValueOrDefault("Ref", 1);
                            break;
                        }
                    }
                    if (hr == 0) return map;

                    int last = ws.LastRowUsed()?.RowNumber() ?? hr;
                    for (int r = hr + 1; r <= last; r++)
                    {
                        string key = ws.Cell(r, cKey).GetString().Trim();
                        if (string.IsNullOrEmpty(key) || !BoqRowKey.IsModelKey(key) && !key.StartsWith("MAN:")) continue;
                        double rate = CellDouble(ws, r, cRate);
                        if (rate <= 0) continue;
                        map[key] = new BidLine
                        {
                            Rate = rate,
                            Qty = CellDouble(ws, r, cQty),
                            StatedAmount = CellDouble(ws, r, cAmt),
                            Section = ws.Cell(r, cSec).GetString().Trim()
                        };
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ParseBidWorkbook({Path.GetFileName(file)}): {ex.Message}"); }
            return map;
        }

        private static int StampWinnerRates(Document doc, string bidder,
            Dictionary<string, BidLine> winnerLines, Dictionary<string, BOQLineItem> liveByKey)
        {
            int n = 0;
            try
            {
                var store = BOQCostManager.LoadModelOverrides(doc);
                string src = $"tender:{bidder}";
                foreach (var kv in winnerLines)
                {
                    if (!kv.Key.StartsWith("UID:")) continue;       // model rows only
                    if (!liveByKey.TryGetValue(kv.Key, out var live)) continue;
                    string uid = kv.Key.Substring(4);
                    var ovr = store.Overrides.FirstOrDefault(o =>
                        string.Equals(o.UniqueId, uid, StringComparison.Ordinal));
                    if (ovr == null)
                    {
                        ovr = new BOQModelOverride { UniqueId = uid, ElementId = live.RevitElementId };
                        store.Overrides.Add(ovr);
                    }
                    ovr.RateUGX = kv.Value.Rate;
                    ovr.RateSource = src;
                    ovr.Modified = DateTime.UtcNow;
                    ovr.ModifiedBy = Environment.UserName;
                    n++;
                }
                BOQCostManager.SaveModelOverrides(doc, store);
            }
            catch (Exception ex) { StingLog.Error("Tender StampWinnerRates", ex); }
            return n;
        }

        // ── XLSX: matrix + summary + qualifications ───────────────────────
        private static string ExportXlsx(Document doc, TenderAdjudication adj, BOQDocument boq,
            Dictionary<string, BOQLineItem> liveByKey, List<string> orderedKeys,
            Dictionary<string, Dictionary<string, BidLine>> bidData)
        {
            try
            {
                string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_TenderAdjudication", ".xlsx");
                var bidders = adj.Bids.Select(b => b.Bidder).ToList();
                using (var wb = new XLWorkbook())
                {
                    // Matrix.
                    var wm = wb.AddWorksheet("Comparison Matrix");
                    int c = 1;
                    wm.Cell(1, c++).Value = "Ref";
                    wm.Cell(1, c++).Value = "Description";
                    wm.Cell(1, c++).Value = "Unit";
                    wm.Cell(1, c++).Value = "Qty";
                    var bidRateCol = new Dictionary<string, int>();
                    foreach (var b in bidders) { wm.Cell(1, c).Value = $"{b} rate"; bidRateCol[b] = c; wm.Cell(1, c + 1).Value = $"{b} amount"; c += 2; }
                    wm.Row(1).Style.Font.SetBold(true);

                    int row = 2;
                    foreach (var key in orderedKeys)
                    {
                        if (!liveByKey.TryGetValue(key, out var live)) continue;
                        double qty = live.Quantity;
                        int cc = 1;
                        wm.Cell(row, cc++).Value = live.BOQLineRef ?? "";
                        wm.Cell(row, cc++).Value = string.IsNullOrEmpty(live.ResolvedNRM2Paragraph) ? (live.ItemName ?? live.Category ?? "") : live.ResolvedNRM2Paragraph;
                        wm.Cell(row, cc++).Value = live.Unit ?? "";
                        wm.Cell(row, cc++).Value = Math.Round(qty, 3);
                        foreach (var b in bidders)
                        {
                            double rate = bidData[b].TryGetValue(key, out var bl) ? bl.Rate : 0;
                            wm.Cell(row, bidRateCol[b]).Value = rate;
                            wm.Cell(row, bidRateCol[b] + 1).Value = Math.Round(rate * qty, 0);
                            wm.Range(row, bidRateCol[b], row, bidRateCol[b] + 1).Style.NumberFormat.SetFormat("#,##0");
                        }
                        row++;
                    }
                    // Grand-total row.
                    wm.Cell(row, 2).Value = "CORRECTED GRAND TOTAL";
                    wm.Cell(row, 2).Style.Font.SetBold(true);
                    foreach (var b in bidders)
                    {
                        var br = adj.Bids.First(x => x.Bidder == b);
                        wm.Cell(row, bidRateCol[b] + 1).Value = br.CorrectedTotalUGX;
                        wm.Cell(row, bidRateCol[b] + 1).Style.NumberFormat.SetFormat("#,##0").Font.SetBold(true);
                    }
                    wm.Columns().AdjustToContents();

                    // Summary / adjudication.
                    var wsum = wb.AddWorksheet("Adjudication");
                    wsum.Cell(1, 1).Value = "TENDER ADJUDICATION"; wsum.Cell(1, 1).Style.Font.SetBold(true).Font.SetFontSize(14);
                    wsum.Cell(2, 1).Value = adj.ProjectName;
                    wsum.Cell(3, 1).Value = $"Generated {adj.GeneratedDate} · Currency {adj.Currency} · QS estimate {adj.Currency} {adj.PreTenderEstimateUGX:N0}";
                    int hr = 5;
                    string[] heads = { "Rank", "Bidder", "Tender total", "Corrected total", "Arith errors", "Correction", "Unpriced measured", "Rate outliers", "Front-loading" };
                    for (int i = 0; i < heads.Length; i++) wsum.Cell(hr, i + 1).Value = heads[i];
                    wsum.Row(hr).Style.Font.SetBold(true);
                    int rr = hr + 1;
                    foreach (var b in adj.Bids)
                    {
                        wsum.Cell(rr, 1).Value = b.Rank;
                        wsum.Cell(rr, 2).Value = b.Bidder;
                        wsum.Cell(rr, 3).Value = b.TenderTotalUGX;
                        wsum.Cell(rr, 4).Value = b.CorrectedTotalUGX;
                        wsum.Cell(rr, 5).Value = b.ArithmeticErrors;
                        wsum.Cell(rr, 6).Value = b.ArithmeticCorrectionUGX;
                        wsum.Cell(rr, 7).Value = b.ZeroRatedMeasured;
                        wsum.Cell(rr, 8).Value = b.RateOutliers;
                        wsum.Cell(rr, 9).Value = b.FrontLoadingFlag ? "YES" : "";
                        wsum.Range(rr, 3, rr, 6).Style.NumberFormat.SetFormat("#,##0");
                        rr++;
                    }
                    rr += 1;
                    wsum.Cell(rr, 1).Value = $"RECOMMENDATION: {adj.RecommendedBidder}"; wsum.Cell(rr, 1).Style.Font.SetBold(true).Font.SetFontColor(XLColor.DarkGreen); rr++;
                    wsum.Cell(rr, 1).Value = adj.RecommendationNote;
                    wsum.Columns().AdjustToContents();

                    // Qualifications / exclusions register (template for the QS to fill).
                    var wq = wb.AddWorksheet("Qualifications");
                    wq.Cell(1, 1).Value = "Bidder"; wq.Cell(1, 2).Value = "Qualification / Exclusion"; wq.Cell(1, 3).Value = "Value impact"; wq.Cell(1, 4).Value = "Accepted?";
                    wq.Row(1).Style.Font.SetBold(true);
                    int qr = 2;
                    foreach (var b in bidders) { wq.Cell(qr, 1).Value = b; qr++; }
                    wq.Columns().AdjustToContents();

                    wb.SaveAs(path);
                }
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"Tender ExportXlsx: {ex.Message}"); return null; }
        }

        // ── helpers ───────────────────────────────────────────────────────
        private static bool IsMeasured(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "each": case "item": case "nr": case "no": case "": return false;
                default: return true;
            }
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            var s = values.Where(v => v > 0).OrderBy(v => v).ToList();
            if (s.Count == 0) return 0;
            int m = s.Count / 2;
            return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2.0;
        }

        private static int FirstContaining(Dictionary<string, int> map, string token, int fallback)
        {
            foreach (var kv in map)
                if (kv.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            return fallback;
        }

        private static double CellDouble(IXLWorksheet ws, int r, int c)
        {
            try
            {
                var cell = ws.Cell(r, c);
                if (cell.TryGetValue(out double d)) return d;
                string s = cell.GetString().Trim().Replace(",", "");
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;
            }
            catch { return 0; }
        }
    }
}
