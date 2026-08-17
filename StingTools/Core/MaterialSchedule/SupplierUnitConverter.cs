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
    }

    public sealed class SupplierUnitTable
    {
        public List<SupplierUnitRule> Rules = new List<SupplierUnitRule>();

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
