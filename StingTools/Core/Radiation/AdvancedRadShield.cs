// Healthcare Pack H-25 — Advanced imaging shielding (PET / NM / brachy).
//
// First-pass calculators for 511 keV PET, 140 keV SPECT, and HDR/LDR
// brachytherapy. All output is QE draft.

using System;

namespace StingTools.Core.Radiation
{
    public static class Pet511Calculator
    {
        public const double TvlConcreteMm = 205.0;
        public const double TvlLeadMm = 16.6;
        public const double TvlSteelMm = 30.0;
        /// <summary>For a desired transmission factor B, return required mm of given material.</summary>
        public static double RequiredThicknessMm(double B, string material)
        {
            if (B >= 1) return 0;
            double tvl = material.ToLowerInvariant() switch {
                "lead" => TvlLeadMm, "steel" => TvlSteelMm, _ => TvlConcreteMm
            };
            double n = -Math.Log10(B);
            return n * tvl;
        }
    }
    public static class SpectCalculator
    {
        public const double TvlConcreteMm = 40.0;
        public const double TvlLeadMm = 1.0;
        public static double RequiredThicknessMm(double B, string material)
        {
            if (B >= 1) return 0;
            double tvl = material.ToLowerInvariant() switch { "lead" => TvlLeadMm, _ => TvlConcreteMm };
            double n = -Math.Log10(B);
            return n * tvl;
        }
    }
    public static class BrachyVaultCalculator
    {
        // Approximate dose-rate at 1 m from a 10 Ci HDR Ir-192 source in mGy/h.
        public const double IridiumGammaConstant_mGy_per_GBq_h_at_1m = 0.115;
        public static double DoseRateAt1m_mGyPerHour(double activityGBq) =>
            IridiumGammaConstant_mGy_per_GBq_h_at_1m * activityGBq;
    }
}
