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
        public const double HfgJperKg    = 2.45e6; // latent heat of water vaporisation at 25 °C (legacy constant)

        /// <summary>
        /// Latent heat of vaporisation of water (J/kg) as a function of
        /// temperature (°C). Standard ASHRAE handbook fit:
        ///     hfg(T) ≈ 2501 − 2.381·T   [kJ/kg], T in °C.
        /// Returns J/kg. Improves the latent-load calc by ~2 % over the
        /// flat 2.45 MJ/kg constant — meaningful in humid hot-design days.
        /// </summary>
        public static double HfgAtC(double tempC)
        {
            double hfgKj = 2501.0 - 2.381 * tempC;
            return hfgKj * 1000.0;
        }

        /// <summary>
        /// Run the block-load calc for a set of zones against a climate
        /// site, returning per-system block-load results.
        ///
        /// <para>RTS lag — when <paramref name="rts"/> is anything but
        /// <see cref="RtsConstructionClass.Reactive"/>, each hourly gain
        /// stream is split into radiant + convective (per ASHRAE 2021
        /// Table 14) and the radiant fraction is convolved with the
        /// matching Radiant Time Factor row from Table 19a. Heavy-mass
        /// buildings see ~15-25 % lower peak cooling loads with the lag
        /// applied; light-mass buildings ~5 %. Defaults to Reactive so
        /// existing call sites are unchanged.</para>
        /// </summary>
        public static List<BlockLoadResult> Run(
            IEnumerable<LoadZone> zones,
            ClimateSite climate,
            bool cooling = true,
            RtsConstructionClass rts = RtsConstructionClass.Reactive,
            Autodesk.Revit.DB.Document docHint = null)
        {
            if (zones == null) return new List<BlockLoadResult>();
            if (climate == null) climate = new ClimateSite { Cooling996DbC = 28, Cooling996McwbC = 20, Heating996DbC = -3 };

            double rho = climate.AirDensityCoolingKgM3();
            if (rho <= 0) rho = AirRhoKgM3;

            // 1. Per-zone hourly profiles
            var zoneResults = new List<ZoneLoadResult>();
            foreach (var z in zones)
            {
                var r = ComputeZoneHourly(z, climate, rho, cooling, rts, docHint);
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

        private static ZoneLoadResult ComputeZoneHourly(LoadZone z, ClimateSite c, double rho, bool cooling,
            RtsConstructionClass rts = RtsConstructionClass.Reactive,
            Autodesk.Revit.DB.Document docHint = null)
        {
            double tSet  = cooling ? z.CoolingSetpointC : z.HeatingSetpointC;
            double tPeak = cooling ? c.Cooling996DbC    : c.Heating996DbC;
            double wRoom = HumidityRatio(tSet, 50);                             // assume 50% RH at setpoint
            double wOa   = HumidityRatio(c.Cooling996DbC, RhFromMcwb(c.Cooling996DbC, c.Cooling996McwbC));

            // Track each gain stream separately so RTS can apply the
            // construction-class radiant time factors per-stream. Conduction
            // and solar are additionally split by 8 orientation bins so the
            // lag re-emission is correct per cardinal direction (south-facing
            // walls heat up at noon; west-facing in the afternoon — the
            // re-emission lag matters per-orientation, not in aggregate).
            //
            // ASHRAE Handbook Fundamentals 2021 Ch.18 §RTS calls this out
            // explicitly; aggregating before convolution under-predicts the
            // afternoon peak on west-glass-heavy zones by ~5-8 %.
            const int NumOrient = 8;                          // 45° bins, 0 = N
            var condByOrient  = new double[NumOrient][];
            var solarByOrient = new double[NumOrient][];
            for (int b = 0; b < NumOrient; b++)
            {
                condByOrient[b]  = new double[24];
                solarByOrient[b] = new double[24];
            }
            var occW    = new double[24];
            var litW    = new double[24];
            var eqpW    = new double[24];
            var ventSW  = new double[24];
            var lat     = new double[24];

            // Outdoor air mass flow (vent + infiltration), kg/s.
            // Phase 187h — infiltration model:
            //   * If Q4PaM3PerHperM2 is set, layer CIBSE Guide A §4.6 stack
            //     + wind pressure onto the Q4 reference value:
            //       ΔP_stack = ρ·g·h·(T_in - T_out)/T_in
            //       ΔP_wind  = 0.5·Cp·ρ·v²            (Cp ≈ 0.6 windward)
            //       ΔP_total = √(ΔP_stack² + ΔP_wind²)
            //       Q_inf    = Q4Pa · A · (ΔP_total/4)^0.65
            //     → infiltration rises on cold/windy days, drops on still-mild
            //       days. Replaces the flat InfiltrationAch.
            //   * Else fall back to InfiltrationAch.
            double oaLs   = z.OaLs;                                              // L/s
            double infLs;
            if (z.Q4PaM3PerHperM2 > 0)
            {
                double envM2 = z.InfiltrationEnvelopeAreaM2 > 0
                    ? z.InfiltrationEnvelopeAreaM2
                    : (z.Envelope != null
                        ? z.Envelope
                            .Where(seg => seg.Kind == SegmentKind.ExteriorWall
                                       || seg.Kind == SegmentKind.Window
                                       || seg.Kind == SegmentKind.Roof)
                            .Sum(seg => seg.AreaM2)
                        : 0);
                infLs = (envM2 > 0)
                    ? CibseInfiltrationLs(z.Q4PaM3PerHperM2, envM2, z.HeightM,
                                          tSet, tPeak, c.DesignWindMs)
                    : z.InfiltrationAch * z.VolumeM3 * 1000.0 / 3600.0;
            }
            else
            {
                infLs = z.InfiltrationAch * z.VolumeM3 * 1000.0 / 3600.0;
            }
            double mdotOa = (oaLs + infLs) * 1e-3 * rho;                          // kg/s

            // ASHRAE design days: cooling = July 21 (DOY 202), heating = January 21 (DOY 21).
            int designDoy = cooling ? 202 : 21;

            // Phase 187g — local clock → solar time conversion.
            // Two corrections layered onto the local hour:
            //   1. DST: subtract 1 h on the cooling design day when the site
            //      observes summer DST (the design day is in July for N-hemi
            //      observers, so DST is in effect).
            //   2. Longitude offset from time-zone meridian:
            //      Δh = (lon − 15·utcOffset) / 15.
            //      Tokyo is east of its zone-meridian — solar noon is BEFORE
            //      clock noon — so this correction can be tens of minutes.
            //   Engine math assumes "hour 12" is solar noon, so we shift the
            //   local-clock hour by these corrections before computing
            //   irradiance / azimuth.
            double dstShift = (c.ObservesDstInSummer && cooling) ? 1.0 : 0.0;
            double lonShiftH = (c.Lon - 15.0 * c.UtcOffsetHours) / 15.0;
            double localToSolarShiftH = -dstShift + lonShiftH;

            for (int h = 0; h < 24; h++)
            {
                // Solar-time hour for this local-clock hour.
                double solarH = h + localToSolarShiftH;
                while (solarH < 0)  solarH += 24;
                while (solarH >= 24) solarH -= 24;

                double tOut  = OutdoorTempC(tPeak, h, cooling);
                double iSol  = cooling ? ClearSkyDirectNormalWm2(c, solarH, designDoy) : 0;     // ignore solar for heating peak (night)
                double iDiff = cooling ? 0.15 * iSol : 0;

                // 1. Conduction + solar by envelope segment, binned per
                //    orientation (45° increments, 0 = North).
                foreach (var seg in z.Envelope)
                {
                    int bin = OrientationBin(seg.OrientationDeg, NumOrient);
                    double qCond = seg.UvalueWm2K * seg.AreaM2 * (tOut - tSet);
                    condByOrient[bin][h] += qCond;
                    if (seg.Kind == SegmentKind.Window && cooling)
                    {
                        double cosTheta = Math.Max(0, IncidenceFactor(seg.OrientationDeg, solarH, c.Lat, designDoy));
                        double solSurf = iSol * cosTheta + iDiff;
                        solarByOrient[bin][h] += seg.AreaM2 * seg.SHGC * seg.ShadingFactor * solSurf;
                    }
                }

                // 2. Internal gains (use schedules clipped to 0..1)
                double occ = Sched(z.OccupancySchedule, h);
                double lit = Sched(z.LightingSchedule, h);
                double eqp = Sched(z.EquipmentSchedule, h);
                occW[h] = z.OccupantCount * occ * z.OccupantSensibleW;
                litW[h] = z.FloorAreaM2 * z.LightingWPerM2  * lit;
                eqpW[h] = z.FloorAreaM2 * z.EquipmentWPerM2 * eqp;

                // 3. Outdoor + infiltration sensible (all convective — bypasses RTS)
                ventSW[h] = mdotOa * AirCpJperKgK * (tOut - tSet);

                // 4. Latent — Hfg evaluated at setpoint (Phase 187d)
                double hfg = HfgAtC(tSet);
                double qOccL  = z.OccupantCount * occ * z.OccupantLatentW;
                double qVentL = mdotOa * hfg * (wOa - wRoom);
                lat[h] = qOccL + qVentL;
            }

            // RTS lag — applied per-stream with the canonical ASHRAE
            // radiant fraction for that gain type. Reactive class is a
            // no-op pass-through so legacy callers see no behaviour change.
            //
            // Phase 187f — conduction + solar convolved per-orientation
            // before aggregation.
            // Phase 187g — when the zone's envelope segments carry
            // ThermalMassKJperM2K data, derive a per-zone RTF by area-
            // weighting + interpolating between Light/Medium/Heavy tables
            // (rigorous ASHRAE CTF would require Laplace-domain inversion;
            // this is the practical middle ground). Falls back to the
            // class-based RTF when no thermal-mass data is present.
            // RadiantTimeSeries.RadiantFraction is a static class — access const members through the type.
            double[] zoneRtf = null;
            if (rts != RtsConstructionClass.Reactive && z.Envelope != null && z.Envelope.Count > 0)
            {
                // Tier-3 first — derive RTF from area-weighted CTF Y-series
                // when any envelope segment carries a ConstructionTypeId
                // present in STING_CTF_COEFFICIENTS.json. This is the most
                // rigorous RTS shipped (no Laplace inversion at runtime —
                // coefficients are pre-tabulated per construction).
                if (z.Envelope.Any(s => !string.IsNullOrEmpty(s?.ConstructionTypeId)))
                {
                    try
                    {
                        var ctfLib = CtfRtsRegistry.Get(docHint);
                        zoneRtf = CtfRtsRegistry.DeriveZoneRtf(z.Envelope, ctfLib);
                    }
                    catch (Exception ex)
                    { StingTools.Core.StingLog.Warn($"CTF RTF derive: {ex.Message}"); }
                }
                // Tier-2 fallback — thermal-mass interpolation when no CTF
                // hit but at least one envelope segment carries
                // ThermalMassKJperM2K data.
                if (zoneRtf == null)
                {
                    double sumAreaMass = 0, sumArea = 0;
                    foreach (var seg in z.Envelope)
                    {
                        if (seg.ThermalMassKJperM2K <= 0 || seg.AreaM2 <= 0) continue;
                        sumAreaMass += seg.AreaM2 * seg.ThermalMassKJperM2K;
                        sumArea     += seg.AreaM2;
                    }
                    if (sumArea > 0 && sumAreaMass > 0)
                    {
                        double avgMass = sumAreaMass / sumArea;
                        zoneRtf = RadiantTimeSeries.FactorsForThermalMass(avgMass);
                    }
                }
            }

            for (int b = 0; b < NumOrient; b++)
            {
                if (zoneRtf != null)
                {
                    condByOrient[b]  = RadiantTimeSeries.ApplyRtsToGainWithRtf(condByOrient[b],  RadiantTimeSeries.RadiantFraction.Conduction, zoneRtf);
                    solarByOrient[b] = RadiantTimeSeries.ApplyRtsToGainWithRtf(solarByOrient[b], RadiantTimeSeries.RadiantFraction.SolarGlass, zoneRtf);
                }
                else
                {
                    condByOrient[b]  = RadiantTimeSeries.ApplyRtsToGain(condByOrient[b],  RadiantTimeSeries.RadiantFraction.Conduction, rts);
                    solarByOrient[b] = RadiantTimeSeries.ApplyRtsToGain(solarByOrient[b], RadiantTimeSeries.RadiantFraction.SolarGlass, rts);
                }
            }
            if (zoneRtf != null)
            {
                occW = RadiantTimeSeries.ApplyRtsToGainWithRtf(occW, RadiantTimeSeries.RadiantFraction.Occupant,  zoneRtf);
                litW = RadiantTimeSeries.ApplyRtsToGainWithRtf(litW, RadiantTimeSeries.RadiantFraction.Lighting,  zoneRtf);
                eqpW = RadiantTimeSeries.ApplyRtsToGainWithRtf(eqpW, RadiantTimeSeries.RadiantFraction.Equipment, zoneRtf);
            }
            else
            {
                occW = RadiantTimeSeries.ApplyRtsToGain(occW, RadiantTimeSeries.RadiantFraction.Occupant,  rts);
                litW = RadiantTimeSeries.ApplyRtsToGain(litW, RadiantTimeSeries.RadiantFraction.Lighting,  rts);
                eqpW = RadiantTimeSeries.ApplyRtsToGain(eqpW, RadiantTimeSeries.RadiantFraction.Equipment, rts);
            }

            var sens = new double[24];
            for (int h = 0; h < 24; h++)
            {
                double cond = 0, solar = 0;
                for (int b = 0; b < NumOrient; b++)
                {
                    cond  += condByOrient[b][h];
                    solar += solarByOrient[b][h];
                }
                sens[h] = cond + solar + occW[h] + litW[h] + eqpW[h] + ventSW[h];
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

            // Phase 187h — DCV hourly OA profile per ASHRAE 62.1.
            var dcv = DcvVentilationCalc.HourlyOa(z);
            double avgOa = oaLs, savings = 0;
            if (dcv != null)
                (_, avgOa, savings) = DcvVentilationCalc.Stats(dcv);

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
                OaLs          = oaLs,
                HourlyOaLs    = dcv,
                AverageOaLs   = avgOa,
                DcvSavingsPct = savings
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
        /// ASHRAE Clear Sky direct-normal irradiance for the given hour
        /// and day-of-year. Default DOY = 202 (July 21, cooling design).
        /// Returns 0 when sun is below horizon. `solarHour` is the
        /// solar-time hour (not local-clock); caller is responsible for
        /// any DST + longitude shift via <see cref="ClimateSite.UtcOffsetHours"/>
        /// + <see cref="ClimateSite.ObservesDstInSummer"/>.
        /// </summary>
        public static double ClearSkyDirectNormalWm2(ClimateSite c, double solarHour, int dayOfYear = 202)
        {
            double dec = 23.45 * Math.Sin(2 * Math.PI * (dayOfYear - 81) / 365.25);
            double latRad = c.Lat * Math.PI / 180.0;
            double decRad = dec * Math.PI / 180.0;
            double hAngle = (solarHour - 12.0) * 15.0 * Math.PI / 180.0;  // 15°/h hour angle
            double sinAlt = Math.Sin(latRad) * Math.Sin(decRad)
                          + Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(hAngle);
            if (sinAlt <= 0.001) return 0;

            // Seasonal ASHRAE clear-sky coefficients (Handbook Fundamentals
            // 2021 Ch.14 Table 7). Interpolated linearly from monthly entries.
            var (A, B) = ClearSkyCoeff(dayOfYear);
            return A * Math.Exp(-B / sinAlt);
        }

        private static (double A, double B) ClearSkyCoeff(int dayOfYear)
        {
            // Monthly A (W/m²) and B (dimensionless) — ASHRAE 2021 Ch.14 Table 7.
            // Interpolated by day-of-year between the mid-month values.
            double[] aMonth = { 1230, 1215, 1186, 1136, 1104, 1088, 1085, 1107, 1152, 1193, 1221, 1234 };
            double[] bMonth = { 0.142, 0.144, 0.156, 0.180, 0.196, 0.205, 0.207, 0.201, 0.177, 0.160, 0.149, 0.142 };
            double idx = (dayOfYear / 365.0) * 12.0;     // 0..12
            int lo = ((int)Math.Floor(idx)) % 12;
            int hi = (lo + 1) % 12;
            double t = idx - Math.Floor(idx);
            double A = aMonth[lo] * (1 - t) + aMonth[hi] * t;
            double B = bMonth[lo] * (1 - t) + bMonth[hi] * t;
            return (A, B);
        }

        /// <summary>
        /// Cosine of incidence angle for a vertical surface at the given
        /// orientation (0=N, 90=E, 180=S, 270=W) at the given hour, computed
        /// from real solar geometry (declination, latitude, hour angle).
        ///
        /// Replaces the earlier linearised "azimuth = 90 + 15·(hour-6)"
        /// approximation which under-predicted east/west walls by ~10° at
        /// mid-latitudes and missed the early-morning / late-afternoon
        /// solar peaks for non-south orientations entirely.
        ///
        /// Refs: ASHRAE Handbook Fundamentals 2021 Ch.14 §4 solar angles.
        ///   sin(α) = sin(φ)sin(δ) + cos(φ)cos(δ)cos(H)
        ///   sin(γ) = cos(δ)sin(H) / cos(α)
        /// where α = altitude, γ = azimuth from south (E negative, W positive),
        /// φ = latitude, δ = declination, H = hour angle (15° per hour from noon).
        ///
        /// Returns 0 when the sun is below the horizon or behind the surface.
        /// </summary>
        public static double IncidenceFactor(double orientationDeg, double solarHour,
            double latitudeDeg = 51.5, int dayOfYear = 172)
        {
            double phi = latitudeDeg * Math.PI / 180.0;
            double dec = 23.45 * Math.Sin(2 * Math.PI * (dayOfYear - 81) / 365.25) * Math.PI / 180.0;
            double H = (solarHour - 12.0) * 15.0 * Math.PI / 180.0;

            double sinAlt = Math.Sin(phi) * Math.Sin(dec)
                          + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(H);
            if (sinAlt <= 0) return 0;                       // sun below horizon
            double altitude = Math.Asin(sinAlt);
            double cosAlt = Math.Cos(altitude);

            // Azimuth from south (− east, + west). Atan2 with the
            // standard ASHRAE sign convention.
            double sinAzS = Math.Cos(dec) * Math.Sin(H) / Math.Max(cosAlt, 1e-9);
            double cosAzS = (Math.Sin(altitude) * Math.Sin(phi) - Math.Sin(dec))
                          / Math.Max(cosAlt * Math.Cos(phi), 1e-9);
            double azFromSouth = Math.Atan2(sinAzS, cosAzS);                // radians
            double azFromNorthDeg = 180.0 + azFromSouth * 180.0 / Math.PI;  // 0=N, 90=E, 180=S, 270=W

            // Incidence angle on a vertical surface = angle between sun
            // azimuth and surface normal in the horizontal plane, projected
            // through the cosine of altitude.
            double dDeg = Math.Abs(azFromNorthDeg - orientationDeg);
            while (dDeg > 180) dDeg = 360 - dDeg;
            if (dDeg >= 90) return 0;                        // sun behind the wall
            return Math.Cos(dDeg * Math.PI / 180.0) * cosAlt;
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

        /// <summary>
        /// Back-out RH from coincident dry-bulb + wet-bulb using the
        /// thermodynamic-wet-bulb definition (ASHRAE Handbook Fundamentals
        /// 2021 Ch.1 §25-32). Solves the implicit psychrometric equation:
        ///
        ///     W_sat(Tw) = ((2501 − 2.326·Tw)·W − 1.006·(T − Tw))
        ///                 / (2501 + 1.86·T − 4.186·Tw)
        ///
        /// Iteratively for W (the actual humidity ratio at the (T,Tw) pair),
        /// then compute RH = pw / pws(T).
        ///
        /// Replaces the Stull (2011) one-line approximation which was
        /// accurate to ~5 % RH; this is accurate to ~0.5 % RH in the
        /// HVAC design range.
        /// </summary>
        public static double RhFromMcwb(double dbC, double wbC)
        {
            if (wbC > dbC) wbC = dbC;                         // physical bound
            const double atmPa = 101325.0;
            double pwsWb = SaturationPressurePa(wbC);
            double wSatWb = 0.62198 * pwsWb / (atmPa - pwsWb);

            // ASHRAE Eq. (33): W from thermodynamic wet-bulb.
            double numerator   = (2501.0 - 2.326 * wbC) * wSatWb - 1.006 * (dbC - wbC);
            double denominator = 2501.0 + 1.86 * dbC - 4.186 * wbC;
            double w = numerator / Math.Max(denominator, 1e-6);
            if (w < 0) w = 0;

            double pw  = atmPa * w / (0.62198 + w);
            double pws = SaturationPressurePa(dbC);
            double rh  = 100.0 * pw / Math.Max(pws, 1e-6);
            if (rh > 100) rh = 100;
            if (rh < 1) rh = 1;
            return rh;
        }

        private static double Sched(double[] sched, int hour)
        {
            if (sched == null || sched.Length == 0) return 0;
            if (hour < 0 || hour >= sched.Length) return 0;
            double v = sched[hour];
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        /// <summary>
        /// CIBSE Guide A §4.6 infiltration rate from Q4Pa air-permeability,
        /// envelope area, building height + design Tin/Tout/wind. Returns L/s.
        ///
        /// Stack pressure:  ΔP_stack = ρ_a·g·h·(T_in - T_out) / T_in_abs
        /// Wind pressure :  ΔP_wind  = 0.5·Cp·ρ·v²       (Cp ≈ 0.6 windward)
        /// Combined      :  ΔP       = √(ΔP_stack² + ΔP_wind²)
        /// Leakage law   :  Q = Q4Pa · A · (ΔP / 4)^n    (n = 0.65 typical)
        /// </summary>
        public static double CibseInfiltrationLs(
            double q4PaM3PerHperM2, double envAreaM2, double heightM,
            double tIn, double tOut, double windMs)
        {
            const double rho = 1.20;
            const double g   = 9.81;
            const double cp  = 0.6;             // windward wall pressure coefficient
            const double n   = 0.65;            // leakage exponent
            double tInK  = tIn + 273.15;
            double dpStack = Math.Abs(rho * g * heightM * (tIn - tOut) / Math.Max(tInK, 1e-3));
            double dpWind  = 0.5 * cp * rho * windMs * windMs;
            double dp      = Math.Sqrt(dpStack * dpStack + dpWind * dpWind);
            if (dp < 0.1) dp = 0.1;
            double qM3H = q4PaM3PerHperM2 * envAreaM2 * Math.Pow(dp / 4.0, n);
            return qM3H * 1000.0 / 3600.0;     // m³/h → L/s
        }

        /// <summary>
        /// Map a degrees-from-north orientation onto a fixed bin count
        /// (typically 8, i.e. 45° bins centred on N / NE / E / SE / S /
        /// SW / W / NW). Defensive against negative + out-of-range inputs.
        /// </summary>
        private static int OrientationBin(double degFromNorth, int bins)
        {
            if (bins <= 0) return 0;
            double d = degFromNorth % 360.0;
            if (d < 0) d += 360.0;
            double step = 360.0 / bins;
            int bin = (int)Math.Round(d / step) % bins;
            if (bin < 0) bin += bins;
            return bin;
        }
    }
}
