// ══════════════════════════════════════════════════════════════════════════
//  WasteFactor.cs — pure wastage-allowance arithmetic for BOQ take-off.
//
//  Extracted so the legacy-fallback path in BOQCostManager.DeriveQuantity can
//  apply the same `qty *= 1 + waste%` step the data-driven TakeoffRule path
//  already applies (TakeoffRule.WastePercent). Before Z-21 the fallback path
//  applied 0% waste, under-quantifying every element that did not match a
//  take-off rule (audit §6.3).
//
//  Zero Autodesk.Revit.* dependencies on purpose — this file is linked into
//  the pure-logic test project (StingTools.Boq.Tests) the same way SeqAssigner
//  is linked into StingTools.Tags.Tests, so the waste arithmetic is unit-tested
//  without a Revit host.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ
{
    public static class WasteFactor
    {
        /// <summary>
        /// True for measured material quantities (area / volume / length /
        /// mass) where a cutting/lapping/offcut allowance is physically
        /// meaningful. False for counted items ("each" / "item") and unknown
        /// units — you do not waste 5% of a pump.
        /// </summary>
        public static bool AppliesTo(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "m²":
                case "m2":
                case "sqm":
                case "m³":
                case "m3":
                case "cum":
                case "m":
                case "kg":
                case "tonne":
                case "tonnes":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Z-21b — single-surface waste convention. Waste is applied on the
        /// QUANTITY only (never the rate; the rate carries OH&amp;P). This picks
        /// the waste% that governs an element: an explicit per-element rate
        /// override (StingCostRateOverride.WastePercent) wins; otherwise the
        /// project-default knob (COST_DEFAULT_WASTE_PCT). A non-positive / NaN
        /// override falls through to the default, so "no override" == default.
        /// </summary>
        public static double ResolveWastePercent(double overrideWastePercent, double projectDefaultPercent)
        {
            if (!double.IsNaN(overrideWastePercent) && overrideWastePercent > 0)
                return overrideWastePercent;
            return projectDefaultPercent;
        }

        /// <summary>
        /// Returns <paramref name="rawQuantity"/> grossed up by
        /// <paramref name="wastePercent"/> when the unit is a measured material
        /// quantity; otherwise returns it unchanged. Negative / NaN waste is
        /// treated as 0 so a mis-keyed config knob can never reduce a quantity.
        /// </summary>
        public static double Apply(double rawQuantity, string unit, double wastePercent)
        {
            if (!AppliesTo(unit)) return rawQuantity;
            if (double.IsNaN(wastePercent) || wastePercent <= 0) return rawQuantity;
            return rawQuantity * (1.0 + wastePercent / 100.0);
        }
    }
}
