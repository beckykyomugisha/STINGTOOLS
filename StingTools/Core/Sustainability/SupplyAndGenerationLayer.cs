// StingTools — Supply + generation layer (Phase 195, spec §7).
//
// Clean pipeline: demand -> on-site generation -> net import -> carbon factor.
// Each stage optional and data-driven.
//   PV:  annual_PV_kWh = kWp x (annualGHI x PR)   (GHI from the climate registry,
//        never a fixed kWh/kWp). kWp = 0 -> no-op.
//   supply.mode = grid_tied | off_grid | hybrid; dieselFraction blends factors.
//
// Energy % (kWh) is computed BEFORE carbon (in the estimator). Carbon (LEED +
// dashboard) = net_import_kWh x gridFactor + diesel_kWh x dieselFactor, applied
// DOWNSTREAM here. So a cleaner grid improves carbon only; PV improves both.
//
// Pure POCO — no Revit dependency.

using System;

namespace StingTools.Core.Sustainability
{
    public class SupplyResult
    {
        public double PvGenerationKwh      { get; set; }
        public double NetImportKwh         { get; set; }
        public double OperationalCarbonKgYr { get; set; }
        public string Mode                 { get; set; } = "grid_tied";
    }

    public static class SupplyAndGenerationLayer
    {
        public const double DefaultPerformanceRatio = 0.75;

        /// <summary>Annual PV generation: kWp x (annual GHI x PR). When
        /// supply.PvYieldKwhPerKwpYr is set it overrides the GHI-derived figure.</summary>
        public static double PvAnnualKwh(SupplyConfig supply, ClimateMonthlySite climate)
        {
            if (supply == null || supply.PvKwp <= 0) return 0;
            if (supply.PvYieldKwhPerKwpYr.HasValue && supply.PvYieldKwhPerKwpYr.Value > 0)
                return supply.PvKwp * supply.PvYieldKwhPerKwpYr.Value;
            double ghi = climate?.AnnualGhiKwhM2Yr ?? 0;
            double pr  = supply.PvPerformanceRatio > 0 ? supply.PvPerformanceRatio : DefaultPerformanceRatio;
            return supply.PvKwp * ghi * pr;
        }

        /// <summary>
        /// Apply the supply chain to a demand figure. <paramref name="pvOverrideKwh"/>
        /// (when &gt; 0) is used instead of re-deriving PV from the config — lets a
        /// caller pre-compute PV once.
        /// </summary>
        public static SupplyResult Apply(
            double demandKwh, ClimateMonthlySite climate, SupplyConfig supply, double pvOverrideKwh = 0)
        {
            var r = new SupplyResult();
            supply = supply ?? new SupplyConfig();
            r.Mode = supply.Mode;

            double pv = pvOverrideKwh > 0 ? pvOverrideKwh : PvAnnualKwh(supply, climate);
            r.PvGenerationKwh = pv;

            double net = Math.Max(0, demandKwh - pv);
            r.NetImportKwh = net;

            double gridF   = supply.GridCarbonKgco2eKwh > 0
                ? supply.GridCarbonKgco2eKwh
                : (climate?.GridCarbonKgco2eKwh ?? 0.45);
            double dieselF = supply.DieselCarbonKgco2eKwh > 0 ? supply.DieselCarbonKgco2eKwh : 0.8;
            double dieselFrac = Math.Min(1.0, Math.Max(0.0, supply.DieselFraction));

            // off_grid -> all net import is from the diesel/genset factor; hybrid
            // blends; grid_tied -> grid factor with optional diesel top-up fraction.
            double blendedFactor;
            switch ((supply.Mode ?? "grid_tied").ToLowerInvariant())
            {
                case "off_grid":
                    blendedFactor = dieselF; break;
                case "hybrid":
                    blendedFactor = dieselFrac * dieselF + (1 - dieselFrac) * gridF; break;
                default: // grid_tied
                    blendedFactor = dieselFrac * dieselF + (1 - dieselFrac) * gridF; break;
            }

            r.OperationalCarbonKgYr = net * blendedFactor;
            return r;
        }
    }
}
