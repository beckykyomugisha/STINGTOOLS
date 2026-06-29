// StingTools — Whole-life carbon roll-up (WS H4).
//
// One whole-life carbon figure from the two figures the sustainability engine
// already computes: embodied (A1–A3, the materials net carbon incl. biogenic
// credit) + operational (energy × grid/fuel factor) integrated over a study
// period. Carbon ONLY — the embodied-ENERGY (MJ) track is never folded in here
// (the two material tracks stay separate per the ground rules).
//
// The default 60-year study period matches CarbonStageTracker.TotalLifecycleOver60y
// (V6 / RIBA-stage view) so the EDGE dashboard and the stage tracker agree on the
// study basis; the period is data-driven (SustainProjectSetup.StudyPeriodYears) so
// a project can use 30 / 50 / 100 without code changes.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System;

namespace StingTools.Core.Sustainability
{
    public class WholeLifeCarbonResult
    {
        /// <summary>Upfront embodied carbon A1–A3, kgCO₂e (net, incl. biogenic credit).</summary>
        public double EmbodiedA1A3Kg     { get; set; }
        /// <summary>Operational carbon, kgCO₂e per year (from the supply layer).</summary>
        public double OperationalKgPerYr { get; set; }
        public int    StudyPeriodYears   { get; set; }
        public double FloorAreaM2        { get; set; }

        /// <summary>Operational carbon over the study period, kgCO₂e.</summary>
        public double OperationalTotalKg => OperationalKgPerYr * StudyPeriodYears;
        /// <summary>Whole-life carbon = embodied A1–A3 + operational over the period, kgCO₂e.</summary>
        public double WholeLifeKg => EmbodiedA1A3Kg + OperationalTotalKg;

        public double EmbodiedKgM2        => FloorAreaM2 > 0 ? EmbodiedA1A3Kg / FloorAreaM2 : 0;
        public double OperationalKgM2Yr   => FloorAreaM2 > 0 ? OperationalKgPerYr / FloorAreaM2 : 0;
        public double WholeLifeKgM2       => FloorAreaM2 > 0 ? WholeLifeKg / FloorAreaM2 : 0;

        /// <summary>True only when a floor area exists and at least one carbon term is
        /// present — otherwise the intensity is a "not computed" zero, not a result.</summary>
        public bool Computed => FloorAreaM2 > 0 && (EmbodiedA1A3Kg != 0 || OperationalKgPerYr > 0);
    }

    public static class WholeLifeCarbon
    {
        /// <summary>Default study period (years) — matches CarbonStageTracker's 60-year
        /// life-cycle basis so the RIBA-stage view and the EDGE dashboard agree.</summary>
        public const int DefaultStudyPeriodYears = 60;

        public static WholeLifeCarbonResult Compute(
            double embodiedA1A3Kg, double operationalKgPerYr, int studyPeriodYears, double floorAreaM2)
        {
            int years = studyPeriodYears > 0 ? studyPeriodYears : DefaultStudyPeriodYears;
            return new WholeLifeCarbonResult
            {
                EmbodiedA1A3Kg     = embodiedA1A3Kg,
                OperationalKgPerYr = Math.Max(0, operationalKgPerYr),
                StudyPeriodYears   = years,
                FloorAreaM2        = Math.Max(0, floorAreaM2)
            };
        }
    }
}
