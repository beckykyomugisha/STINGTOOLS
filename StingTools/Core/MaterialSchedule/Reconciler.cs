// ══════════════════════════════════════════════════════════════════════════
//  Reconciler.cs — MAT-SCHED post-build invariants.
//
//  The PATMAC reference sample failed on arithmetic, so the invariants are the
//  point of this feature. Three of the four defect classes are now structural:
//    D3 (Amount != Qty x Rate)  — impossible: AmountUGX is derived
//    D2 (summary != body)       — impossible: the summary projects from Stages
//  What remains is checked here.
//
//    R1  one rate per commodity key, document-wide            (PATMAC D4)
//    R2  section letters unique and sequential                (PATMAC D1)
//    R3  no unpriced commodity while prices are shown         (C3 risk)
//    R4  order quantity never below net quantity              (negative wastage)
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public static class Reconciler
    {
        public static MaterialScheduleReconciliation Check(MaterialScheduleDocument doc)
        {
            var rec = new MaterialScheduleReconciliation();
            if (doc == null) return rec;

            CheckRatesConsistent(doc, rec);
            CheckLetters(doc, rec);
            CheckPriced(doc, rec);
            CheckQuantities(doc, rec);
            CheckConversions(doc, rec);

            doc.Reconciliation = rec;
            return rec;
        }

        /// <summary>R1 — the PATMAC sand-at-two-rates defect.</summary>
        private static void CheckRatesConsistent(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            var byKey = doc.Stages
                .SelectMany(s => s.Commodities)
                .Where(c => c.RateUGX > 0 && !string.IsNullOrWhiteSpace(c.CommodityKey))
                .GroupBy(c => c.CommodityKey, StringComparer.OrdinalIgnoreCase);

            foreach (var g in byKey)
            {
                var rates = g.Select(c => Math.Round(c.RateUGX, 2)).Distinct().OrderBy(r => r).ToList();
                if (rates.Count <= 1) continue;

                rec.Issues.Add(new ReconciliationIssue
                {
                    Code = "R1",
                    CommodityKey = g.Key,
                    Message = $"Commodity '{g.Key}' is priced {rates.Count} different ways: "
                            + string.Join(", ", rates.Select(r => r.ToString("N0", CultureInfo.InvariantCulture)))
                            + " UGX. One commodity must carry one rate across the document."
                });
            }
        }

        /// <summary>R2 — the PATMAC duplicate-letter defect.</summary>
        private static void CheckLetters(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            var dupes = doc.Stages
                .Where(s => !string.IsNullOrWhiteSpace(s.Letter))
                .GroupBy(s => s.Letter, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var g in dupes)
                rec.Issues.Add(new ReconciliationIssue
                {
                    Code = "R2",
                    Message = $"Section letter '{g.Key}' is used by {g.Count()} sections: "
                            + string.Join(", ", g.Select(s => s.Title))
                });

            for (int i = 0; i < doc.Stages.Count; i++)
            {
                string expected = StageMapper.ToLetter(i);
                if (!string.Equals(doc.Stages[i].Letter, expected, StringComparison.OrdinalIgnoreCase))
                    rec.Issues.Add(new ReconciliationIssue
                    {
                        Code = "R2",
                        StageId = doc.Stages[i].StageId,
                        Message = $"Section {i + 1} ('{doc.Stages[i].Title}') is lettered "
                                + $"'{doc.Stages[i].Letter}' but its position requires '{expected}'."
                    });
            }
        }

        /// <summary>R3 — an unpriced commodity in a priced document.</summary>
        private static void CheckPriced(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            if (doc.Options == null || !doc.Options.ShowPrices) return;

            foreach (var stage in doc.Stages)
                foreach (var c in stage.Commodities.Where(x => x.IsUnpriced))
                    rec.Issues.Add(new ReconciliationIssue
                    {
                        Code = "R3",
                        StageId = stage.StageId,
                        CommodityKey = c.CommodityKey,
                        Message = $"'{c.Description}' ({c.OrderQuantity:N0} {c.SupplierUnit}) in "
                                + $"{stage.Title} has no rate. It will total zero in a priced schedule."
                    });
        }

        /// <summary>
        /// R5 — a row whose category maps to a commodity but whose type did not
        /// match its patterns. It stays in measured units on purpose; this names
        /// it so the QS either extends the rule or prices the measured row by
        /// hand, rather than wondering why one roof reads m2 and another reads No.
        /// </summary>
        private static void CheckConversions(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            foreach (var stage in doc.Stages)
                foreach (var c in stage.Commodities.Where(x => x.ConversionBlocked))
                    rec.Issues.Add(new ReconciliationIssue
                    {
                        Code = "R5",
                        StageId = stage.StageId,
                        CommodityKey = c.CommodityKey,
                        Message = $"'{c.Description}' stayed in measured units ({c.SupplierUnit}) — "
                                + c.ConversionNote
                                + ". Extend the rule's type patterns to convert it, or price it as measured."
                    });
        }

        /// <summary>R4 — wastage can only add.</summary>
        private static void CheckQuantities(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            foreach (var stage in doc.Stages)
                foreach (var c in stage.Commodities)
                {
                    if (c.OrderQuantity + 1e-9 < c.NetQuantity)
                        rec.Issues.Add(new ReconciliationIssue
                        {
                            Code = "R4",
                            StageId = stage.StageId,
                            CommodityKey = c.CommodityKey,
                            Message = $"'{c.Description}': order quantity {c.OrderQuantity:N2} is below the "
                                    + $"net measured {c.NetQuantity:N2}. Wastage can only add."
                        });

                    if (c.WastagePct < 0)
                        rec.Issues.Add(new ReconciliationIssue
                        {
                            Code = "R4",
                            StageId = stage.StageId,
                            CommodityKey = c.CommodityKey,
                            Message = $"'{c.Description}': negative wastage {c.WastagePct:N1}%."
                        });
                }
        }
    }
}
