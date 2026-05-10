// PlumbingSustainabilityCalc — Phase 179e calculator façade.
//
// Wraps the existing RainwaterHarvestingCalc plus four new pure
// calculators (roof drainage Qr, soakaway BRE 365, septic BS EN 12566-1,
// expansion vessel reuse) into a single entry point for the STORM tab.
// All methods are static, deterministic, no Revit dependency.

using System;

namespace StingTools.Core.Plumbing
{
    public static class PlumbingSustainabilityCalc
    {
        // BS EN 12056-3 roof drainage. Q_r = A · C_r · r · f.
        public static double RoofDrainageLps(double catchmentAreaM2, double runoffCoefficient,
            double rainfallIntensityLpsM2, double safetyFactor = 1.5)
        {
            return catchmentAreaM2 * runoffCoefficient * rainfallIntensityLpsM2 * safetyFactor;
        }

        // CIRIA C753 attenuation volume — proxy delegating to RainwaterHarvestingCalc
        // for backwards-compat with the existing Phase 178 impl.
        public static double SudsAttenuationM3(double postDevAreaM2, double preDevGreenAreaM2,
            double rainfallIntensityMmHr, double stormDurationHr, double postDevCv = 0.9,
            double preDevCv = 0.05, double climateUpliftPct = 40)
        {
            return RainwaterHarvestingCalc.CalcSudsAttenuationVolumeM3(postDevAreaM2,
                preDevGreenAreaM2, rainfallIntensityMmHr, stormDurationHr, postDevCv, preDevCv, climateUpliftPct);
        }

        // BRE Digest 365 soakaway design.
        public static double SoakawayVolumeM3(double catchmentAreaM2, double rainfallIntensityMHr,
            double stormDurationHr, double infiltrationRateMHr)
        {
            return RainwaterHarvestingCalc.CalcSoakawayVolumeM3(catchmentAreaM2, rainfallIntensityMHr,
                stormDurationHr, infiltrationRateMHr);
        }

        // BS EN 12566-1 septic primary chamber volume in litres.
        public static double SepticTankLitres(int populationEquivalent)
            => RainwaterHarvestingCalc.CalcSepticTankVolumeLitres(populationEquivalent);

        // BS 8515 RWH yield.
        public static RainwaterHarvestingResult RwhYield(double roofAreaM2, double annualRainfallMm,
            double runoffCoefficient = 0.75, double filterEfficiency = 0.90, double dailyDemandM3 = 1.5)
        {
            return RainwaterHarvestingCalc.Calculate(roofAreaM2, annualRainfallMm,
                runoffCoefficient, filterEfficiency, dailyDemandM3);
        }
    }
}
