// ══════════════════════════════════════════════════════════════════════════
//  CarbonLcc.cs — carbon-as-cost (CA-4). The ONE place embodied + operational
//  carbon is monetised into a whole-life cost, shared by the BOQ LCC and the
//  EDGE LCC so both report the same carbon-inclusive figure.
//
//  Document-free (no Autodesk.Revit.*) so the carbon-cost math is unit-tested.
//
//  Carbon price is configurable and data-driven: COST_CARBON_PRICE_UGX_PER_KG in
//  project_config.json (per-project override). It defaults to 0 — until a price
//  is set, carbon adds nothing (honest: we do not fabricate a shadow price). A
//  team that prices carbon (e.g. a UK BEIS central value ≈ £250/tCO₂ ≈ ~1,175
//  UGX/kg, or an internal price) sets the key and the figure flows everywhere.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core.Sustainability
{
    public static class CarbonLcc
    {
        /// <summary>Project-config key for the carbon price (UGX per kgCO₂e).</summary>
        public const string CarbonPriceConfigKey = "COST_CARBON_PRICE_UGX_PER_KG";

        /// <summary>Whole-life carbon COST (UGX): the upfront embodied-carbon cost
        /// plus the present value of the annual operational-carbon cost over the
        /// study period. Returns 0 when no carbon price is set (price ≤ 0).</summary>
        public static double CarbonCostUgx(double embodiedKg, double operationalKgPerYr,
            double carbonPriceUgxPerKg, int years, double discountRatePct)
        {
            if (carbonPriceUgxPerKg <= 0) return 0;
            double embodiedCost = embodiedKg * carbonPriceUgxPerKg;          // A1-A3 upfront
            double opAnnualCost = operationalKgPerYr * carbonPriceUgxPerKg;  // B6 per year
            double opNpv = SustainNpv.PresentValueAnnuity(opAnnualCost, years, discountRatePct);
            return embodiedCost + opNpv;
        }

        /// <summary>The TRUE whole-life cost: the capital + maintenance LCC plus the
        /// monetised whole-life carbon. Shared by the BOQ per-element LCC (where
        /// operationalKgPerYr is 0 — operational carbon is building-level) and the
        /// EDGE building-level LCC (which supplies the operational term).</summary>
        public static double LifecycleCostInclCarbonUgx(double lifecycleCostUgx,
            double embodiedKg, double operationalKgPerYr, double carbonPriceUgxPerKg,
            int years, double discountRatePct)
            => lifecycleCostUgx + CarbonCostUgx(embodiedKg, operationalKgPerYr,
                                                carbonPriceUgxPerKg, years, discountRatePct);
    }
}
