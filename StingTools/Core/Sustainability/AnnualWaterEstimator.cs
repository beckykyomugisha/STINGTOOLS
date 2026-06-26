// StingTools — Annual water estimator (Phase 195, spec §8).
//
//   L_person_day  = SUM_fixture ( flow_per_use x uses_per_person_day )
//   annual_demand = L_person_day x occupancy x operatingDaysPerYear
//   net_demand    = annual_demand - RWH_yield - greywater_reuse
//   water_savings_pct = (baseline_L_person_day - design_L_person_day) / baseline x 100
//
// Building type selects the usage profile only — no code branch. Occupancy is a
// PARAMETER. Fixture flows are the DESIGN flows (low-flow fixtures from the
// model); the baseline uses the resolved baseline's fixture baselines. RWH yield
// reuses RainwaterHarvestingCalc; greywater reuse is a project fraction.
//
// Pure POCO — no Revit dependency. Has dedicated unit tests.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    /// <summary>The design fixture flows read off the model (low-flow types).
    /// Keys: wc_lpf, urinal_lpf, basin_tap_lpm, shower_lpm, kitchen_tap_lpm.</summary>
    public class FixtureFlows
    {
        public double WcLpf        { get; set; } = 6.0;
        public double UrinalLpf    { get; set; } = 4.0;
        public double BasinTapLpm  { get; set; } = 8.0;
        public double ShowerLpm    { get; set; } = 10.0;
        public double KitchenTapLpm { get; set; } = 8.0;

        public static FixtureFlows FromBaseline(GreenBaseline b)
        {
            var f = new FixtureFlows();
            if (b == null) return f;
            if (b.FixtureBaselines.TryGetValue("wc_lpf", out var v1)) f.WcLpf = v1;
            if (b.FixtureBaselines.TryGetValue("urinal_lpf", out var v2)) f.UrinalLpf = v2;
            if (b.FixtureBaselines.TryGetValue("basin_tap_lpm", out var v3)) f.BasinTapLpm = v3;
            if (b.FixtureBaselines.TryGetValue("shower_lpm", out var v4)) f.ShowerLpm = v4;
            if (b.FixtureBaselines.TryGetValue("kitchen_tap_lpm", out var v5)) f.KitchenTapLpm = v5;
            return f;
        }
    }

    public class WaterEstimateResult
    {
        public double DesignLPersonDay   { get; set; }
        public double BaselineLPersonDay { get; set; }
        /// <summary>Fixture-efficiency savings % (per-person·day, design vs baseline
        /// fixtures only). Does NOT credit alternative water.</summary>
        public double WaterSavingsPct    { get; set; }

        /// <summary>EDGE-style total mains-water reduction % vs baseline annual demand —
        /// credits fixture efficiency AND alternative water (RWH + greywater). EDGE
        /// rewards alternative water toward the water gate, so this is the metric the
        /// EDGE gate uses. Falls back to <see cref="WaterSavingsPct"/> when occupancy
        /// is unknown or no alternative water is present.</summary>
        public double WaterSavingsInclAltPct { get; set; }

        public double AnnualDemandL  { get; set; }
        public double RwhYieldL       { get; set; }
        public double GreywaterReuseL { get; set; }
        public double NetDemandL      { get; set; }

        public int    Occupancy { get; set; }
        public int    OperatingDaysPerYear { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>True when the design fixture flows are the hardcoded
        /// 25%-over-baseline placeholder (no real fixture data read off the model).
        /// Set by the orchestration engine. When true the savings % is the same on
        /// every project and must be shown as "indicative default", not a pass.</summary>
        public bool IsIndicativeDefault { get; set; }

        /// <summary>True only when the % came from real model fixture data against a
        /// non-zero baseline. False ⇒ rendered as "indicative default", never a pass.</summary>
        public bool Computed => !IsIndicativeDefault && BaselineLPersonDay > 0 && DesignLPersonDay > 0;
    }

    public static class AnnualWaterEstimator
    {
        /// <summary>
        /// L/person.day for a given flow set + usage profile.
        /// taps/showers: L = flow_lpm x min_per_use; WC/urinal: L = lpf x uses.
        /// </summary>
        public static double LitresPerPersonDay(FixtureFlows flows, WaterUsageProfile profile)
        {
            if (flows == null || profile == null) return 0;
            double total = 0;

            if (profile.Fixtures.TryGetValue("wc", out var wc))
                total += flows.WcLpf * wc.Uses;
            if (profile.Fixtures.TryGetValue("urinal", out var ur))
                total += flows.UrinalLpf * ur.Uses;
            if (profile.Fixtures.TryGetValue("basin_tap", out var bt))
                total += flows.BasinTapLpm * bt.MinPerUse * bt.Uses;
            if (profile.Fixtures.TryGetValue("shower", out var sh))
            {
                double frac = sh.HasFracPeople ? sh.FracPeople : 0;
                total += flows.ShowerLpm * sh.MinPerUse * frac;
            }
            if (profile.Fixtures.TryGetValue("kitchen_tap", out var kt))
            {
                double minPerDay = kt.HasMinPerPersonDay ? kt.MinPerPersonDay : 0;
                total += flows.KitchenTapLpm * minPerDay;
            }
            return total;
        }

        /// <summary>
        /// Full water estimate. designFlows = model low-flow fixtures;
        /// baseline = resolved baseline fixture flows; occupancy is a parameter.
        /// rwhYieldLPerYr + greywaterReuseFraction subtract from annual demand
        /// (net demand) but DO NOT change the per-person savings % (which is a
        /// fixture-efficiency comparison) — EDGE scores fixture efficiency.
        /// </summary>
        public static WaterEstimateResult Estimate(
            FixtureFlows designFlows,
            FixtureFlows baselineFlows,
            WaterUsageProfile profile,
            int occupancy,
            double rwhYieldLPerYr = 0,
            double greywaterReuseFraction = 0)
        {
            var res = new WaterEstimateResult();
            if (profile == null)
            {
                res.Warnings.Add("No water usage profile resolved — water estimate skipped.");
                return res;
            }

            res.Occupancy = occupancy;
            res.OperatingDaysPerYear = profile.OperatingDaysPerYear;

            res.DesignLPersonDay   = LitresPerPersonDay(designFlows, profile);
            res.BaselineLPersonDay = LitresPerPersonDay(baselineFlows, profile);
            res.WaterSavingsPct    = SavingsPct(res.BaselineLPersonDay, res.DesignLPersonDay);

            res.AnnualDemandL = res.DesignLPersonDay * occupancy * profile.OperatingDaysPerYear;
            res.RwhYieldL      = Math.Max(0, rwhYieldLPerYr);
            res.GreywaterReuseL = res.AnnualDemandL * Math.Min(1.0, Math.Max(0.0, greywaterReuseFraction));
            res.NetDemandL      = Math.Max(0, res.AnnualDemandL - res.RwhYieldL - res.GreywaterReuseL);

            // EDGE-style water % = mains-water reduction vs baseline annual demand,
            // crediting fixture efficiency + alternative water. When occupancy is
            // unknown the annual demand is 0, so fall back to the fixture-only %.
            double baselineAnnualL = res.BaselineLPersonDay * occupancy * profile.OperatingDaysPerYear;
            res.WaterSavingsInclAltPct = (occupancy > 0 && baselineAnnualL > 0)
                ? (baselineAnnualL - res.NetDemandL) / baselineAnnualL * 100.0
                : res.WaterSavingsPct;

            if (occupancy <= 0)
                res.Warnings.Add("Occupancy is 0 — annual demand cannot be computed (set occupancy in project setup).");
            if (res.BaselineLPersonDay <= 0)
                res.Warnings.Add("Baseline L/person.day is 0 — savings % not meaningful (check baseline fixtures).");

            return res;
        }

        public static double SavingsPct(double baseline, double design)
            => baseline > 0 ? (baseline - design) / baseline * 100.0 : 0;
    }
}
