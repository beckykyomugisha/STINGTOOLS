// ══════════════════════════════════════════════════════════════════════════
//  Boq5DEstimateAssembler.cs — the ONE place a 4D/5D cost estimate JSON is
//  assembled from already-costed BOQ line rows.
//
//  P0-7 consolidation. Before this, Scheduling4DEngine.GenerateCostEstimate ran
//  its OWN element take-off (its own qty fallbacks, a hardcoded 0.888 kg/m rebar
//  constant, its own qty×rate and its own flat markup). That parallel procedure
//  drifted from the BOQ Cost Manager. Now GenerateCostEstimate sources its line
//  items from BOQCostManager.BuildBOQDocument and hands the already-costed rows
//  to this assembler, which:
//
//    • groups the rows by category (the historic line_items shape),
//    • SUMS the canonical per-line totals (never re-derives qty×rate), and
//    • applies the ONE markup waterfall (BoqTotals.Compute, incl VAT).
//
//  This type is Document-free (no Autodesk.Revit.* imports) so the consolidation
//  invariant — "the 5D estimate consumes canonical line totals + the canonical
//  markup, with no second qty×rate" — is covered by a headless xUnit test.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.BOQ
{
    /// <summary>One already-costed BOQ line, projected for the 5D estimate.
    /// <see cref="LineTotalUgx"/> is the CANONICAL total (Quantity × RateUgx as
    /// the BOQ computed it) — the assembler sums it verbatim and never multiplies
    /// quantity by rate again.</summary>
    public struct Boq5DRow
    {
        public string Category;
        public string Discipline;
        public double Quantity;
        public string Unit;
        public double RateUgx;
        public double LineTotalUgx;
        public string Description;
    }

    /// <summary>The markup percentages + absolute prelims that drive the
    /// canonical grand total. Populated from the BOQDocument on the Revit side
    /// (BuildBOQDocument resolves these from the tender-config store).</summary>
    public struct Boq5DMarkup
    {
        public double PrelimsAbsoluteUgx;   // itemised Σ, or works × prelim% (resolved upstream)
        public double PrelimPct;            // shown in the report header only
        public double OverheadPct;
        public double ContingencyPct;
        public double VatPct;
    }

    /// <summary>
    /// Assembles the 5D cost-estimate JObject from canonical, already-costed
    /// rows. The output shape is byte-compatible with the historic
    /// Scheduling4DEngine.GenerateCostEstimate so the cash-flow / S-curve and the
    /// AutoCost / CostReport / Export5D consumers are unchanged.
    /// </summary>
    public static class Boq5DEstimateAssembler
    {
        public static JObject Assemble(
            IEnumerable<Boq5DRow> rows,
            Boq5DMarkup markup,
            string projectName,
            string currency,
            string generatedDate,
            int skippedElementCount = 0,
            IReadOnlyDictionary<string, int> skippedCategories = null)
        {
            var estimate = new JObject
            {
                ["project_name"] = projectName ?? "",
                ["generated_date"] = generatedDate ?? "",
                ["currency"] = string.IsNullOrEmpty(currency) ? "UGX" : currency
            };

            var rowList = (rows ?? Enumerable.Empty<Boq5DRow>()).ToList();
            var lineItems = new JArray();
            double works = 0;

            // Group by category — the historic line_items granularity. Quantity
            // and total are SUMMED from the canonical rows; the unit rate shown is
            // the value-weighted average (total ÷ quantity) so a mixed-rate
            // category still reports a representative figure without re-pricing.
            foreach (var grp in rowList
                .GroupBy(r => r.Category ?? "")
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                double qty = Math.Round(grp.Sum(r => r.Quantity), 2);
                double total = grp.Sum(r => r.LineTotalUgx);
                works += total;

                string unit = grp.Select(r => r.Unit).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? "each";
                double unitRate = qty > 0 ? Math.Round(total / qty, 2)
                                          : Math.Round(grp.Select(r => r.RateUgx).FirstOrDefault(), 2);
                string disc = grp.Select(r => r.Discipline).FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? "";
                string desc = grp.Select(r => r.Description).FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? grp.Key;

                lineItems.Add(new JObject
                {
                    ["category"] = grp.Key,
                    ["discipline"] = disc,
                    ["quantity"] = qty,
                    ["unit"] = unit,
                    ["unit_rate"] = unitRate,
                    ["total"] = Math.Round(total, 2),
                    ["description"] = desc
                });
            }

            estimate["line_items"] = lineItems;
            estimate["subtotal"] = Math.Round(works, 2);

            // ── The ONE markup waterfall (incl VAT). ─────────────────────────
            var b = BoqTotals.Compute(works, markup.PrelimsAbsoluteUgx,
                markup.OverheadPct, markup.ContingencyPct, markup.VatPct);

            estimate["preliminaries_pct"] = markup.PrelimPct;
            estimate["preliminaries"] = Math.Round(b.Prelims, 2);
            estimate["overhead_profit_pct"] = markup.OverheadPct;
            estimate["overhead_profit"] = Math.Round(b.Overhead, 2);
            estimate["contingency_pct"] = markup.ContingencyPct;
            estimate["contingency"] = Math.Round(b.Contingency, 2);
            estimate["vat_pct"] = markup.VatPct;
            estimate["vat"] = Math.Round(b.Vat, 2);
            estimate["net_ex_vat"] = Math.Round(b.NetExVat, 2);
            estimate["grand_total"] = b.GrandTotal;   // canonical Contract Sum incl VAT

            // Breakdown by discipline (from the line-item totals).
            var byDisc = lineItems
                .GroupBy(i => i["discipline"]?.ToString() ?? "?")
                .ToDictionary(g => g.Key, g => g.Sum(i => (double)(i["total"] ?? 0)));
            estimate["discipline_totals"] = JObject.FromObject(
                byDisc.OrderByDescending(kv => kv.Value)
                      .ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)));

            estimate["skipped_element_count"] = skippedElementCount;
            if (skippedElementCount > 0 && skippedCategories != null && skippedCategories.Count > 0)
            {
                var skippedDetail = new JObject();
                foreach (var sk in skippedCategories.OrderByDescending(x => x.Value))
                    skippedDetail[sk.Key] = sk.Value;
                estimate["skipped_categories"] = skippedDetail;
                estimate["warning"] = $"{skippedElementCount} elements in {skippedCategories.Count} categories were skipped (no cost rate defined). Add rates for: {string.Join(", ", skippedCategories.Keys.OrderBy(k => k).Take(10))}{(skippedCategories.Count > 10 ? "..." : "")}";
            }

            return estimate;
        }
    }
}
