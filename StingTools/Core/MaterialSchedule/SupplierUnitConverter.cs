// ══════════════════════════════════════════════════════════════════════════
//  SupplierUnitConverter.cs — MAT-SCHED SI → supplier-unit conversion.
//
//  The BOQ measures in m3 / m2 / kg / bag / nr. A material schedule speaks the
//  vendor's language: Trips, Bags, Rolls, Pcs. This is a UNIT TABLE only — it
//  holds no measurement logic and derives no quantities.
//
//  Wastage is applied as its own visible step and never folded into NetQuantity,
//  so a QS can see and argue with the allowance.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class SupplierUnitRule
    {
        public string CommodityKey = "";
        public string Description = "";
        public string Spec = "";
        public string SupplierUnit = "";
        public string SourceUnit = "";                  // BoqUnits-normalised token
        public double SourceUnitsPerSupplierUnit = 1.0; // e.g. 12 m3 per Sino Truck trip
        public bool RoundUpToWhole = true;
        public double DefaultWastagePct = 0.0;
        /// <summary>CompoundTakeoff constituent kinds that map to this commodity.</summary>
        public List<string> MatchKinds = new List<string>();

        /// <summary>
        /// Revit categories that map to this commodity, for rows that carry no
        /// constituent kind. Roofing sheets, paint and tiles need no decomposition —
        /// they convert straight off the measured area — so category is the only
        /// handle they have.
        /// </summary>
        public List<string> MatchCategories = new List<string>();

        /// <summary>
        /// Optional type-name substrings narrowing a category match. EMPTY means
        /// the whole category converts. Non-empty means at least one must appear
        /// in the element's type name — a concrete flat roof and a corrugated-sheet
        /// roof are both category "Roofs" and buy completely differently, so a bare
        /// category match would print a confident sheet count for a slab.
        /// </summary>
        public List<string> MatchTypePatterns = new List<string>();

        /// <summary>
        /// The construction stage this commodity belongs to, overriding whatever
        /// stage the ELEMENT's category routes to. Required on any category rule.
        ///
        /// Without it a commodity silently inherits the element's stage, which is
        /// how wall paint and floor tiles ended up filed under SUPERSTRUCTURE:
        /// categories "Walls" and "Floors" route to the frame, but paint and tiles
        /// are finishes. The commodity knows its own section; the element does not.
        /// </summary>
        public string StageId = "";
    }

    /// <summary>How (or whether) a row resolved to a supplier-unit rule.</summary>
    public enum SupplierUnitMatch
    {
        None,                   // nothing matched — row keeps its measured unit, silently
        ByKind,                 // matched a CompoundTakeoff constituent kind
        ByCategory,             // matched a category (and its type pattern, if any)
        CategoryTypeMismatch    // category matched but the type did not — DO NOT convert
    }

    public struct SupplierUnitResolution
    {
        public SupplierUnitRule Rule;          // null unless Match is ByKind / ByCategory
        public SupplierUnitMatch Match;
        public string CandidateCommodityKey;   // the rule it nearly matched, for the flag message
    }

    public sealed class SupplierUnitTable
    {
        public List<SupplierUnitRule> Rules = new List<SupplierUnitRule>();

        /// <summary>
        /// Resolve a row to a rule. Constituent kind wins; category is the fallback.
        ///
        /// A category hit whose type pattern fails returns CategoryTypeMismatch with
        /// NO rule — the row must stay in measured units and be flagged, never
        /// converted on a guess and never dropped.
        /// </summary>
        public SupplierUnitResolution Resolve(string constituentKind, string category, string typeName)
        {
            var byKind = ResolveByKind(constituentKind);
            if (byKind != null)
                return new SupplierUnitResolution { Rule = byKind, Match = SupplierUnitMatch.ByKind };

            if (string.IsNullOrWhiteSpace(category))
                return new SupplierUnitResolution { Match = SupplierUnitMatch.None };

            // PERF: single pass, no LINQ closure and no candidates List per row.
            string cat = category.Trim();
            SupplierUnitRule firstCategoryHit = null;

            foreach (var r in Rules)
            {
                if (r?.MatchCategories == null) continue;
                bool catMatch = false;
                foreach (string c in r.MatchCategories)
                    if (string.Equals(c, cat, StringComparison.OrdinalIgnoreCase)) { catMatch = true; break; }
                if (!catMatch) continue;
                if (firstCategoryHit == null) firstCategoryHit = r;

                // No patterns ⇒ the whole category converts.
                if (r.MatchTypePatterns == null || r.MatchTypePatterns.Count == 0)
                    return new SupplierUnitResolution { Rule = r, Match = SupplierUnitMatch.ByCategory };

                // A blank type name can never satisfy a pattern. Guarding this
                // explicitly because "".IndexOf(p) is -1 but p.IndexOf("") is 0 —
                // get the operands the wrong way round and every row matches.
                string tn = (typeName ?? "").Trim();
                if (tn.Length > 0 && r.MatchTypePatterns.Any(p =>
                        !string.IsNullOrWhiteSpace(p)
                        && tn.IndexOf(p.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                    return new SupplierUnitResolution { Rule = r, Match = SupplierUnitMatch.ByCategory };
            }

            if (firstCategoryHit == null)
                return new SupplierUnitResolution { Match = SupplierUnitMatch.None };

            return new SupplierUnitResolution
            {
                Match = SupplierUnitMatch.CategoryTypeMismatch,
                CandidateCommodityKey = firstCategoryHit.CommodityKey
            };
        }

        /// <summary>First rule listing this constituent kind, or null.</summary>
        public SupplierUnitRule ResolveByKind(string constituentKind)
        {
            if (string.IsNullOrWhiteSpace(constituentKind)) return null;
            return Rules.FirstOrDefault(r => r.MatchKinds != null
                && r.MatchKinds.Any(k => string.Equals(k, constituentKind, StringComparison.OrdinalIgnoreCase)));
        }

        public SupplierUnitRule ResolveByCommodityKey(string commodityKey)
        {
            if (string.IsNullOrWhiteSpace(commodityKey)) return null;
            return Rules.FirstOrDefault(r =>
                string.Equals(r.CommodityKey, commodityKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    public struct SupplierUnitResult
    {
        public string SupplierUnit;
        public double NetQuantity;    // supplier units, PRE-wastage
        public double WastagePct;
        public double OrderQuantity;  // post-wastage, rounded per the rule
    }

    public static class SupplierUnitConverter
    {
        public static SupplierUnitResult Convert(SupplierUnitRule rule, double sourceQuantity)
        {
            if (rule == null)
                return new SupplierUnitResult
                {
                    SupplierUnit = "", NetQuantity = sourceQuantity,
                    WastagePct = 0, OrderQuantity = sourceQuantity
                };

            // Bad or missing conversion data must degrade to 1:1, never to
            // Infinity/NaN — a silent Infinity would print as a blank cell.
            double factor = rule.SourceUnitsPerSupplierUnit;
            if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor)) factor = 1.0;

            double net = sourceQuantity / factor;
            double waste = Math.Max(0, rule.DefaultWastagePct);
            double order = net * (1.0 + waste / 100.0);
            if (rule.RoundUpToWhole) order = Math.Ceiling(order - 1e-9);

            return new SupplierUnitResult
            {
                SupplierUnit = rule.SupplierUnit,
                NetQuantity = net,
                WastagePct = waste,
                OrderQuantity = order
            };
        }
    }
}
