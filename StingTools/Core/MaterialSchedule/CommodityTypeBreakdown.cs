// ══════════════════════════════════════════════════════════════════════════
//  CommodityTypeBreakdown.cs — the same commodity, split by the model TYPES
//  that produced it.
//
//  The schedule aggregates by commodity because that is what you BUY: three
//  shingle roofs become one pile of bundles, and ordering three separate
//  piles would be wrong. But it is useless for the other three things a
//  quantity gets used for — checking a number against the model, phasing a
//  delivery, and splitting work between subcontractors. "80 bundles" cannot
//  be traced back to a roof, and a wrong roof cannot be found in it.
//
//  So the breakdown is ADDITIVE and separate: the order line stays exactly as
//  it was, and this says what went into it.
//
//  THE ARITHMETIC IS DELIBERATELY NOT RE-DERIVED. Each part is the order
//  quantity apportioned by its share of the measured source, because
//  converting each type separately and rounding each up would order MORE than
//  the single line says — three roofs rounding up to whole bundles is up to
//  three extra bundles that the order line does not contain, and two
//  documents in one workbook disagreeing about a total is worse than no
//  breakdown at all.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One type's share of a commodity.</summary>
    public sealed class CommodityTypePart
    {
        public string TypeName = "";

        /// <summary>What this type measured, in the SOURCE unit (m², nr, m³…).</summary>
        public double SourceQuantity;

        /// <summary>Its share of the order, in supplier units. Apportioned, not re-converted.</summary>
        public double OrderQuantity;

        /// <summary>Share of the commodity's measured total, 0..1.</summary>
        public double Share;
    }

    public sealed class CommodityBreakdown
    {
        public string CommodityKey = "";
        public string Description = "";
        public string SupplierUnit = "";
        public double OrderQuantity;
        public readonly List<CommodityTypePart> Parts = new List<CommodityTypePart>();

        /// <summary>
        /// True when one type produced everything, so the breakdown repeats the
        /// order line and adds nothing.
        /// </summary>
        public bool IsSingleType => Parts.Count <= 1;
    }

    public static class CommodityTypeBreakdown
    {
        /// <summary>
        /// Apportion each commodity's order across the types that fed it.
        ///
        /// <paramref name="sourceByType"/> is the measured quantity per
        /// (commodityKey, typeName) — the aggregator's own numerator, so the
        /// shares cannot disagree with the line they came from.
        ///
        /// Commodities whose parts are unknown are OMITTED rather than shown
        /// whole under a made-up type name: a breakdown that invents a source
        /// is worse than one that admits it has none.
        /// </summary>
        public static List<CommodityBreakdown> Build(
            IEnumerable<MaterialCommodity> commodities,
            IReadOnlyDictionary<string, Dictionary<string, double>> sourceByType)
        {
            var outList = new List<CommodityBreakdown>();
            if (commodities == null || sourceByType == null) return outList;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in commodities)
            {
                if (c == null || c.IsMemorandum) continue;
                if (string.IsNullOrWhiteSpace(c.CommodityKey)) continue;
                if (!seen.Add(c.CommodityKey)) continue;
                if (!sourceByType.TryGetValue(c.CommodityKey, out var byType)) continue;

                var parts = (byType ?? new Dictionary<string, double>())
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0)
                    .ToList();
                if (parts.Count == 0) continue;

                double total = parts.Sum(kv => kv.Value);
                if (total <= 0) continue;

                var b = new CommodityBreakdown
                {
                    CommodityKey = c.CommodityKey,
                    Description = c.Description ?? "",
                    SupplierUnit = c.SupplierUnit ?? "",
                    OrderQuantity = c.OrderQuantity
                };

                foreach (var kv in parts.OrderByDescending(kv => kv.Value)
                                        .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                {
                    double share = kv.Value / total;
                    b.Parts.Add(new CommodityTypePart
                    {
                        TypeName = kv.Key,
                        SourceQuantity = Math.Round(kv.Value, 3),
                        // Apportioned from the ORDER, never re-converted: three
                        // roofs each rounded up to whole bundles would exceed
                        // the order line by up to three bundles.
                        OrderQuantity = Math.Round(c.OrderQuantity * share, 2),
                        Share = share
                    });
                }
                outList.Add(b);
            }

            return outList
                .OrderBy(b => b.Description, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The note for the export, or NULL when nothing was split — a sheet of
        /// single-type rows repeats the schedule and is worth saying so once
        /// rather than printing twice.
        /// </summary>
        public static string Summary(IReadOnlyCollection<CommodityBreakdown> breakdowns)
        {
            if (breakdowns == null || breakdowns.Count == 0) return null;

            int split = breakdowns.Count(b => !b.IsSingleType);
            if (split == 0)
                return "By-type breakdown: every commodity came from a single model type, so the "
                     + "breakdown repeats the schedule and adds nothing this time.";

            return $"By-type breakdown: {split} of {breakdowns.Count} commodity row(s) came from more "
                 + "than one model type and are split on their own sheet. The order line is what you "
                 + "BUY and is unchanged; the split is for checking a number against the model, "
                 + "phasing a delivery, or dividing work between subcontractors. Each part is the "
                 + "order APPORTIONED by measured share, not re-converted — converting each type "
                 + "separately and rounding each up would order more than the schedule says.";
        }
    }
}
