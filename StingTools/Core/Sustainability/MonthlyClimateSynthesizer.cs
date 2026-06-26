// StingTools — Monthly-climate synthesizer (Phase 195, WS A1).
//
// Pure POCO / Revit-free + unit-tested. Derives a 12-month profile (mean dry-bulb
// + GHI) for ANY of the 41-city design-day ClimateRegistry sites, so the monthly
// layer (ClimateMonthlyRegistry) reads from the SINGLE locational source instead
// of a separate 4-city table. The 4-city STING_CLIMATE_MONTHLY.json becomes a
// precise OVERRIDE; everything else synthesises from the design-day site here,
// flagged as indicative.
//
// Hemisphere-aware (southern sites peak in January); seasonal amplitude scales
// with |latitude| (tropics near-flat, high latitudes strongly seasonal). Annual
// GHI is the measured value when available, else an indicative latitude estimate.

using System;

namespace StingTools.Core.Sustainability
{
    public static class MonthlyClimateSynthesizer
    {
        private static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>Indicative annual GHI (kWh/m²·yr) from latitude when no measured
        /// value exists — a documented clear-sky/latitude correlation, never a
        /// certified figure: tropics ≈ 1950, mid-latitude ≈ 1150, polar floor 700.</summary>
        public static double EstimateAnnualGhiFromLatitude(double latDeg)
        {
            double g = 1950.0 - 16.0 * Math.Abs(latDeg);
            return Math.Max(700.0, Math.Min(2100.0, g));
        }

        /// <summary>Fill <paramref name="site"/>'s MeanDbC / MeanRhPct / GhiKwhM2Day /
        /// AnnualGhi from a design-day site's cooling/heating temperatures + latitude
        /// (+ optional measured annual GHI). Marks the site as design-day-derived.</summary>
        public static void Fill(ClimateMonthlySite site,
            double coolingDesignDbC, double heatingDesignDbC, double latDeg,
            double annualGhiKwhM2Yr = 0)
        {
            if (site == null) return;

            double annualGhi = annualGhiKwhM2Yr > 0 ? annualGhiKwhM2Yr : EstimateAnnualGhiFromLatitude(latDeg);
            site.AnnualGhiKwhM2Yr = annualGhi;

            // Monthly-mean temperature swing as a fraction of the 99.6/0.4% design
            // spread (the design extremes overshoot the monthly means).
            double warmMean = (coolingDesignDbC + heatingDesignDbC) / 2.0 + 0.30 * (coolingDesignDbC - heatingDesignDbC);
            double coldMean = (coolingDesignDbC + heatingDesignDbC) / 2.0 - 0.30 * (coolingDesignDbC - heatingDesignDbC);
            double midT = (warmMean + coldMean) / 2.0;
            double ampT = (warmMean - coldMean) / 2.0;

            bool southern = latDeg < 0;
            int warmMonth = southern ? 0 : 6;   // Jan (S) / Jul (N) peak
            // GHI seasonality grows with |latitude|: ~flat at the equator, strong at
            // high latitudes (capped so winter GHI stays positive).
            double ghiAmp = Math.Min(0.6, Math.Abs(latDeg) / 90.0 * 1.1);

            var weights = new double[12];
            double wsum = 0;
            for (int m = 0; m < 12; m++)
            {
                double phase = Math.Cos((m - warmMonth) / 12.0 * 2 * Math.PI);
                site.MeanDbC[m]   = midT + ampT * phase;
                site.MeanRhPct[m] = 65;
                weights[m] = Math.Max(0, 1.0 + ghiAmp * phase);
                wsum += weights[m] * DaysInMonth[m];
            }
            // Normalise so Σ(GHI_day × days) == annual GHI.
            for (int m = 0; m < 12; m++)
                site.GhiKwhM2Day[m] = wsum > 0 ? annualGhi * weights[m] / wsum : annualGhi / 365.0;

            site.FellBackToDesignDay = true;
            site.Source = annualGhiKwhM2Yr > 0
                ? "synthesised from design-day registry (measured annual GHI)"
                : "synthesised from design-day registry (latitude-estimated GHI)";
        }
    }
}
