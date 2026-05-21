// StingTools — Block Load Engine.
//
// Hour-by-hour design-day load calc that peak-picks at the SYSTEM
// level (not the per-zone level). This closes the most frequent
// engineer complaint about Revit's native heating/cooling loads:
// Revit sums per-zone peaks which over-sizes plant by 20–30 %
// because zones don't peak simultaneously.
//
// Algorithm (simplified ASHRAE Radiant Time Series + CIBSE Guide A):
//   For each hour h ∈ [0..23] of the design day:
//     For each zone z:
//       Q_sensible(z,h) =
//           Σ_envelope U·A·(T_out(h) - T_set)            (conduction)
//         + Σ_glazing  A·SHGC·shade·I_sol(h, orientation) (solar)
//         + n(h)·q_sens_per_person                         (occupants)
//         + lpd·A·sched_light(h)                           (lighting)
//         + epd·A·sched_equip(h)                           (equipment)
//         + ṁ_inf·c_p·(T_out(h) - T_set)                   (infiltration)
//         + ṁ_oa·c_p·(T_out(h) - T_set)                    (vent sensible)
//
//       Q_latent(z,h) =
//           n(h)·q_lat_per_person
//         + ṁ_oa·h_fg·(w_out(h) - w_room)                  (vent latent)
//
//     System block_h = Σ_z Q_sensible(z,h)
//     Block load = max_h (block_h)
//
// Solar irradiance uses a clear-sky model (ASHRAE Clear Sky):
//   I_dir,n = A · exp(-B / sin(β))    direct-normal
//   I_diff  = C · I_dir,n             diffuse on horizontal
// A, B, C are seasonally interpolated. For each surface orientation we
// project the direct beam onto the surface normal via cos(θ).
//
// Outdoor temperature follows a 24-h sinusoid around the cooling
// design dry-bulb with the standard CIBSE Guide A daily range of
// 8 K below to 0 K above the peak, with the peak at hour 15.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Climate;

namespace StingTools.Core.Hvac.Loads
{
    public static class BlockLoadEngine
    {
        // Air thermophysical properties at ~25 °C, sea level.
        public const double AirCpJperKgK = 1005.0;
        public const double AirRhoKgM3   = 1.20;   // density (uncorrected; engine corrects via climate site)
        public const double HfgJperKg    = 2.45e6; // latent heat of water vaporisation

        /// <summary>
        /// Run the block-load calc for a set of zones against a climate
        /// site, returning per-system block-load results.
        /// </summary>
        public static List<BlockLoadResult> Run(
            IEnumerable<LoadZone> zones,
            ClimateSite climate,
            bool cooling = true)
        {
            if (zones == null) return new List<BlockLoadResult>();
            if (climate == null) climate = new ClimateSite { Cooling996DbC = 28, Cooling996McwbC = 20, Heating996DbC = -3 };

            double rho = climate.AirDensityCoolingKgM3();
            if (rho <= 0) rho = AirRhoKgM3;

            // 1. Per-zone hourly profiles
            var zoneResults = new List<ZoneLoadResult>();
            foreach (var z in zones)
            {
                var r = ComputeZoneHourly(z, climate, rho, cooling);
                zoneResults.Add(r);
            }

            // 2. Group by system, sum hour-by-hour, then pick block hour
            var bySystem = zoneResults
                .GroupBy(r => r.SystemId ?? "")
                .Select(g =>
                {
                    var blk = new BlockLoadResult
                    {
                        SystemId         = string.IsNullOrEmpty(g.Key) ? "(unzoned)" : g.Key,
                        SystemSensibleW  = new double[24],
                        SystemLatentW    = new double[24]
                    };
                    foreach (var zr in g)
                    {
                        blk.Zones.Add(zr);
                        for (int h = 0; h < 24; h++)
                        {
                            blk.SystemSensibleW[h] += zr.SensibleW[h];
                            blk.SystemLatentW[h]   += zr.LatentW[h];
                        }
                        blk.SumOfPeaksSensibleW += zr.PeakSensibleW;
                    }
                    // Peak-pick at the system level. For cooling the peak is
                    // the most-positive sensible load (hottest hour); for
                    // heating it's the most-negative (coldest, largest demand).
                    int peakH = 0; double peakW = blk.SystemSensibleW[0];
                    for (int h = 1; h < 24; h++)
                    {
                        bool isBetter = cooling
                            ? blk.SystemSensibleW[h] > peakW
                            : blk.SystemSensibleW[h] < peakW;
                        if (isBetter) { peakW = blk.SystemSensibleW[h]; peakH = h; }
                    }
                    blk.BlockSensibleW = peakW;
                    blk.BlockHour      = peakH;
                    blk.BlockLatentW   = blk.SystemLatentW[peakH];
                    return blk;
                })
                .ToList();

            return bySystem;
        }

        private static ZoneLoadResult ComputeZoneHourly(LoadZone z, ClimateSite c, double rho, bool cooling)
        {
            double tSet  = cooling ? z.CoolingSetpointC : z.HeatingSetpointC;
            double tPeak = cooling ? c.Cooling996DbC    : c.Heating996DbC;
            double wRoom = HumidityRatio(tSet, 50);                             // assume 50% RH at setpoint
            double wOa   = HumidityRatio(c.Cooling996DbC, RhFromMcwb(c.Cooling996DbC, c.Cooling996McwbC));

            var sens = new double[24];
            var lat  = new double[24];

            // Outdoor air mass flow (vent + infiltration), kg/s.
            double oaLs   = z.OaLs;                                              // L/s
            double infLs  = z.InfiltrationAch * z.VolumeM3 * 1000.0 / 3600.0;    // ACH·V → m³/h → L/s
            double mdotOa = (oaLs + infLs) * 1e-3 * rho;                          // kg/s

            for (int h = 0; h < 24; h++)
            {
                double tOut  = OutdoorTempC(tPeak, h, cooling);
                double iSol  = cooling ? ClearSkyDirectNormalWm2(c, h) : 0;     // ignore solar for heating peak (night)
                double iDiff = cooling ? 0.15 * iSol : 0;

                // 1. Conduction through every envelope segment
                double qCond = 0;
                double qSolar = 0;
                foreach (var seg in z.Envelope)
                {
                    qCond += seg.UvalueWm2K * seg.AreaM2 * (tOut - tSet);
                    if (seg.Kind == SegmentKind.Window && cooling)
                    {
                        double cosTheta = Math.Max(0, IncidenceFactor(seg.OrientationDeg, h));
                        double solSurf = iSol * cosTheta + iDiff;
                        qSolar += seg.AreaM2 * seg.SHGC * seg.ShadingFactor * solSurf;
                    }
                }

                // 2. Internal gains (use schedules clipped to 0..1)
                double occ = Sched(z.OccupancySchedule, h);
                double lit = Sched(z.LightingSchedule, h);
                double eqp = Sched(z.EquipmentSchedule, h);
                double qOcc  = z.OccupantCount * occ * z.OccupantSensibleW;
                double qLite = z.FloorAreaM2 * z.LightingWPerM2  * lit;
                double qEqp  = z.FloorAreaM2 * z.EquipmentWPerM2 * eqp;

                // 3. Outdoor + infiltration sensible
                double qVentS = mdotOa * AirCpJperKgK * (tOut - tSet);

                // 4. Latent (occupant + outdoor moisture)
                double qOccL  = z.OccupantCount * occ * z.OccupantLatentW;
                double qVentL = mdotOa * HfgJperKg * (wOa - wRoom);

                sens[h] = qCond + qSolar + qOcc + qLite + qEqp + qVentS;
                lat[h]  = qOccL + qVentL;
            }

            // For cooling, "peak" is the maximum sensible load. For
            // heating, the minimum (largest-magnitude negative).
            int peakHour = 0;
            double peak = sens[0];
            for (int h = 1; h < 24; h++)
            {
                if (cooling ? sens[h] > peak : sens[h] < peak)
                { peak = sens[h]; peakHour = h; }
            }

            return new ZoneLoadResult
            {
                ZoneId        = z.Id,
                ZoneName      = z.Name,
                SystemId      = z.SystemId,
                SensibleW     = sens,
                LatentW       = lat,
                PeakSensibleW = peak,
                PeakLatentW   = lat[peakHour],
                PeakHour      = peakHour,
                AreaM2        = z.FloorAreaM2,
                OaLs          = oaLs
            };
        }

        // ── Climate helpers ─────────────────────────────────────────

        /// <summary>
        /// Sinusoidal outdoor temperature over 24 h. CIBSE Guide A 2.4:
        /// daily range = 8 K, peak at hour 15. For heating, returns a
        /// flat value (heating design is the cold steady state).
        /// </summary>
        public static double OutdoorTempC(double designC, int hour, bool cooling)
        {
            if (!cooling) return designC;
            const double range = 8.0;
            // peak at h=15, min at h=3 (range/2 each way)
            double phase = (hour - 15.0) / 24.0 * 2 * Math.PI;
            return designC - 0.5 * range * (1 - Math.Cos(phase));
        }

        /// <summary>
        /// ASHRAE Clear Sky direct-normal irradiance on a horizontal
        /// surface, simplified: A·exp(-B/sin(β)). β = solar altitude.
        /// Uses July 21 noon solar geometry (peak cooling design day).
        /// Returned in W/m². Returns 0 when sun is below horizon.
        /// </summary>
        public static double ClearSkyDirectNormalWm2(ClimateSite c, int hour)
        {
            // Solar altitude from a simplified noon-shifted sinusoid:
            // β_max ≈ 90° - |lat - 23.45° (June)|. Symmetric around solar noon (h=12).
            double declinationJul = 23.45 * Math.Sin(2 * Math.PI * (172 - 81) / 365.25); // ≈ 21°
            double latRad = c.Lat * Math.PI / 180.0;
            double decRad = declinationJul * Math.PI / 180.0;
            double hAngle = (hour - 12.0) * 15.0 * Math.PI / 180.0;       // 15°/h hour angle
            double sinAlt = Math.Sin(latRad) * Math.Sin(decRad)
                          + Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(hAngle);
            if (sinAlt <= 0.001) return 0;

            // July ASHRAE A=1090 W/m², B=0.207 (clear sky model)
            const double A = 1090, B = 0.207;
            return A * Math.Exp(-B / sinAlt);
        }

        /// <summary>
        /// Cosine of incidence angle for a vertical surface at the given
        /// orientation (0=N, 90=E, 180=S, 270=W) at the given hour.
        /// Simplified projection: treats the surface as receiving the
        /// direct beam when sun azimuth points toward the surface normal.
        /// </summary>
        public static double IncidenceFactor(double orientationDeg, int hour)
        {
            // Sun azimuth swings from ~90° (east) at 06:00 through 180° (south)
            // at 12:00 to ~270° (west) at 18:00. Linearise for simplicity.
            double azimuth = 90 + (hour - 6) * 15.0;
            if (hour < 6 || hour > 18) return 0;
            double delta = Math.Abs(azimuth - orientationDeg);
            if (delta > 90) return 0;
            return Math.Cos(delta * Math.PI / 180.0);
        }

        // ── Psychrometric helpers (CIBSE Guide C App. 2 simplified) ──

        /// <summary>Humidity ratio (kg_w / kg_da) from dry-bulb (°C) + RH (%).</summary>
        public static double HumidityRatio(double dbC, double rhPct)
        {
            double pws = SaturationPressurePa(dbC);
            double pw  = (rhPct / 100.0) * pws;
            double patm = 101325.0;
            return 0.62198 * pw / (patm - pw);
        }

        /// <summary>Saturation water-vapour pressure (Pa) from dry-bulb (°C),
        /// Magnus-Tetens approximation.</summary>
        public static double SaturationPressurePa(double dbC)
        {
            return 611.2 * Math.Exp(17.62 * dbC / (243.12 + dbC));
        }

        /// <summary>Back-out RH from coincident MCWB (rough — assumes
        /// thermodynamic wet bulb).</summary>
        public static double RhFromMcwb(double dbC, double wbC)
        {
            // Stull (2011) one-line approx: e/es = ((Tw+273)/(T+273))^8.
            // Yields % within ~5% for typical HVAC design ranges.
            double r = Math.Pow((wbC + 273.15) / (dbC + 273.15), 8.0) * 100.0;
            if (r > 100) r = 100; if (r < 5) r = 5;
            return r;
        }

        private static double Sched(double[] sched, int hour)
        {
            if (sched == null || sched.Length == 0) return 0;
            if (hour < 0 || hour >= sched.Length) return 0;
            double v = sched[hour];
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }
    }
}
