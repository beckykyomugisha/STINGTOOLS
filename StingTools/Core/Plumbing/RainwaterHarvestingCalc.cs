// RainwaterHarvestingCalc — BS 8515 water balance + CIRIA C753 SuDS
// attenuation + BRE 365 soakaway + BS EN 12566-1 septic tank.
// Phase 178c. Pure calculator — no Revit dependency, no element writes.

using System;
using StingTools.Standards.BS8515;

namespace StingTools.Core.Plumbing
{
    public class RainwaterHarvestingResult
    {
        public double AnnualRainfallM3      { get; set; }
        public double AnnualDemandM3        { get; set; }
        public double AnnualYieldM3         { get; set; }
        public double YieldEfficiencyPct    { get; set; }
        public double RecommendedTankM3     { get; set; }
        public double[] MonthlyYieldM3      { get; } = new double[12];
        public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();
    }

    public static class RainwaterHarvestingCalc
    {
        public static RainwaterHarvestingResult Calculate(
            double roofAreaM2,
            double annualRainfallMm,
            double runoffCoefficient,
            double filterEfficiency,
            double dailyDemandM3,
            double[] monthlyRainfallMm = null)
        {
            var r = new RainwaterHarvestingResult();
            if (roofAreaM2 <= 0 || annualRainfallMm <= 0 || dailyDemandM3 <= 0)
            {
                r.Warnings.Add("RWH inputs invalid (area / rainfall / demand must be > 0)");
                return r;
            }
            if (filterEfficiency <= 0) filterEfficiency = BS8515Standards.FilterEfficiency;
            if (runoffCoefficient <= 0) runoffCoefficient = 0.75;

            r.AnnualRainfallM3 = roofAreaM2 * annualRainfallMm * runoffCoefficient * filterEfficiency / 1000.0;
            r.AnnualDemandM3   = dailyDemandM3 * 365.0;

            double[] daysInMonth = { 31, 28.25, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            double[] monthlyMm = monthlyRainfallMm;
            if (monthlyMm == null || monthlyMm.Length != 12)
            {
                monthlyMm = new double[12];
                for (int i = 0; i < 12; i++) monthlyMm[i] = annualRainfallMm * daysInMonth[i] / 365.25;
            }

            double yield = 0;
            for (int i = 0; i < 12; i++)
            {
                double catchM3 = roofAreaM2 * monthlyMm[i] * runoffCoefficient * filterEfficiency / 1000.0;
                double demandM3 = dailyDemandM3 * daysInMonth[i];
                double m = Math.Min(catchM3, demandM3);
                r.MonthlyYieldM3[i] = m;
                yield += m;
            }
            r.AnnualYieldM3        = yield;
            r.YieldEfficiencyPct   = r.AnnualDemandM3 > 0 ? yield / r.AnnualDemandM3 * 100.0 : 0;
            r.RecommendedTankM3    = BS8515Standards.GetRecommendedTankVolumeM3(r.AnnualDemandM3, r.AnnualYieldM3);
            return r;
        }

        // CIRIA C753 attenuation volume.
        public static double CalcSudsAttenuationVolumeM3(
            double postDevAreaM2, double preDevGreenAreaM2,
            double rainfallIntensityMmHr, double stormDurationHr,
            double postDevCv = 0.9, double preDevCv = 0.05,
            double climateUpliftPct = 40.0)
        {
            if (postDevAreaM2 <= 0 || rainfallIntensityMmHr <= 0 || stormDurationHr <= 0) return 0;
            double iUplift = rainfallIntensityMmHr * (1.0 + climateUpliftPct / 100.0);
            double qPost = postDevCv * iUplift * postDevAreaM2 / 1000.0 / 3600.0;
            double qPre  = preDevCv  * rainfallIntensityMmHr * Math.Max(preDevGreenAreaM2, 0) / 1000.0 / 3600.0;
            double dQ = Math.Max(qPost - qPre, 0);
            return dQ * stormDurationHr * 3600.0;
        }

        // BRE Digest 365 soakaway. Trial-pit infiltration rate from
        // 75 % → 25 % drop time gives f in m/s; pass m/hr here.
        public static double CalcSoakawayVolumeM3(
            double catchmentAreaM2,
            double rainfallIntensityMHr,
            double stormDurationHr,
            double infiltrationRateMHr)
        {
            if (catchmentAreaM2 <= 0 || rainfallIntensityMHr <= 0 || stormDurationHr <= 0 || infiltrationRateMHr <= 0)
                return 0;
            return (0.5 * rainfallIntensityMHr * catchmentAreaM2 * stormDurationHr) / (infiltrationRateMHr * 3600.0);
        }

        // BS EN 12566-1 / BS 6297 primary septic tank volume in litres.
        // V = 1500 + 190 × P  for P ≤ 50 (domestic).
        public static double CalcSepticTankVolumeLitres(int populationEquivalent)
        {
            if (populationEquivalent <= 0) return 0;
            return 1500.0 + 190.0 * populationEquivalent;
        }
    }
}
