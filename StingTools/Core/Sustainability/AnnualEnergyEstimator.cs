// StingTools — Annual energy estimator (Phase 195, spec §7) — the core new math.
//
// Method: monthly quasi-steady-state energy balance (EN ISO 13790 / ISO 52016-1
// simplified) with a gain-utilisation factor. Generalises across climates and
// aligns with how EDGE estimates internally. Output: annual kWh by end-use,
// then kWh/m2.yr, then a savings % vs the resolved baseline.
//
// ANNUAL != PEAK. This is NOT BlockLoadEngine (peak Watts, plant sizing). This
// integrates 12 months of demand into annual kWh. It REUSES the LoadZone
// inventory BlockLoadEngine gathers (floor areas, LPD/EPD, envelope U/SHGC/
// orientation/area, OA, schedules) but is a separate annual integrator.
//
// Pure POCO — no Revit dependency. Has dedicated unit tests (monthly balance on
// a synthetic zone within tolerance; cooling vs heating utilisation flip;
// COP/SEER scaling; PV offset; off-grid/diesel carbon).

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Hvac.Loads;

namespace StingTools.Core.Sustainability
{
    /// <summary>Per-end-use annual energy (electricity, kWh).</summary>
    public class EnergyByEndUse
    {
        public double CoolingKwh   { get; set; }
        public double HeatingKwh   { get; set; }
        public double FansKwh      { get; set; }
        public double LightingKwh  { get; set; }
        public double EquipmentKwh { get; set; }
        public double DhwKwh       { get; set; }

        public double TotalKwh => CoolingKwh + HeatingKwh + FansKwh + LightingKwh + EquipmentKwh + DhwKwh;

        public void AddTo(EnergyByEndUse other)
        {
            other.CoolingKwh   += CoolingKwh;
            other.HeatingKwh   += HeatingKwh;
            other.FansKwh      += FansKwh;
            other.LightingKwh  += LightingKwh;
            other.EquipmentKwh += EquipmentKwh;
            other.DhwKwh       += DhwKwh;
        }
    }

    public class EnergyEstimateResult
    {
        public EnergyByEndUse Design { get; } = new EnergyByEndUse();
        public double DesignEuiKwhM2Yr   { get; set; }
        public double BaselineEuiKwhM2Yr { get; set; }
        public double EnergySavingsPct   { get; set; }

        /// <summary>Net imported energy after on-site PV, kWh/yr.</summary>
        public double NetImportKwh { get; set; }
        /// <summary>Annual on-site PV generation, kWh/yr.</summary>
        public double PvGenerationKwh { get; set; }
        /// <summary>Operational carbon kgCO2e/yr from the supply layer.</summary>
        public double OperationalCarbonKgYr { get; set; }

        public double FloorAreaM2 { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public bool AnyZoneMissingEnvelope { get; set; }
    }

    public static class AnnualEnergyEstimator
    {
        // Days per month (non-leap) for hour integration.
        private static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>
        /// Estimate annual energy for a set of LoadZones against monthly climate.
        /// COP/SEER is an input on each zone (CoolingCop) or falls back to the
        /// baseline COP. Lighting/equipment/dhw come from the LoadZone densities +
        /// schedules. The result reuses the supply layer for PV + carbon.
        /// </summary>
        public static EnergyEstimateResult Estimate(
            IEnumerable<LoadZone> zones,
            ClimateMonthlySite climate,
            GreenBaseline baseline,
            double baselineCoolingCop,
            SupplyConfig supply = null,
            double pvAnnualGenerationKwh = 0)
        {
            var res = new EnergyEstimateResult();
            var zoneList = zones?.ToList() ?? new List<LoadZone>();
            if (climate == null)
            {
                res.Warnings.Add("No monthly climate data — energy estimate skipped.");
                return res;
            }

            double totalArea = 0;
            double annualOperatingHours = 0;

            foreach (var z in zoneList)
            {
                if (z.FloorAreaM2 <= 0) continue;
                totalArea += z.FloorAreaM2;

                bool hasEnvelope = z.Envelope != null && z.Envelope.Count > 0;
                if (!hasEnvelope) res.AnyZoneMissingEnvelope = true;

                // Cooling COP/SEER is the supplied baseline COP (the caller applies any
                // per-zone override before calling by passing the chosen baselineCoolingCop).
                double cop = baselineCoolingCop > 0 ? baselineCoolingCop : 3.0;

                var zoneResult = EstimateZone(z, climate, cop, ref annualOperatingHours);
                zoneResult.AddTo(res.Design);
            }

            res.FloorAreaM2 = totalArea;
            // annualOperatingHours is accumulated weighted by area inside EstimateZone;
            // normalise to an area-weighted mean for the baseline conversion.
            double meanHours = totalArea > 0 ? annualOperatingHours / totalArea : 2500;

            res.DesignEuiKwhM2Yr = totalArea > 0 ? res.Design.TotalKwh / totalArea : 0;

            if (baseline != null)
                res.BaselineEuiKwhM2Yr = baseline.TotalEuiKwhM2Yr(meanHours);
            res.EnergySavingsPct = SavingsPct(res.BaselineEuiKwhM2Yr, res.DesignEuiKwhM2Yr);

            // Supply layer: demand -> on-site generation -> net import -> carbon.
            var supplyResult = SupplyAndGenerationLayer.Apply(
                res.Design.TotalKwh, climate, supply, pvAnnualGenerationKwh);
            res.PvGenerationKwh        = supplyResult.PvGenerationKwh;
            res.NetImportKwh           = supplyResult.NetImportKwh;
            res.OperationalCarbonKgYr  = supplyResult.OperationalCarbonKgYr;

            if (res.BaselineEuiKwhM2Yr <= 0)
                res.Warnings.Add("Baseline EUI is zero — savings % not meaningful (check baseline resolution).");
            if (res.AnyZoneMissingEnvelope)
                res.Warnings.Add("One or more zones have no envelope data — conduction/solar gains under-counted.");

            return res;
        }

        /// <summary>Monthly quasi-steady balance for one zone. Accumulates the
        /// area-weighted annual operating hours into <paramref name="annualHoursAccum"/>.</summary>
        private static EnergyByEndUse EstimateZone(
            LoadZone z, ClimateMonthlySite climate, double cop, ref double annualHoursAccum)
        {
            var e = new EnergyByEndUse();

            // Transmission + ventilation loss/gain coefficient H (W/K).
            // 0.33 Wh/(m3.K) is the volumetric heat capacity of air.
            double uA = z.Envelope?.Sum(s => s.UvalueWm2K * s.AreaM2) ?? 0;
            double infilM3h = z.VolumeM3 * z.InfiltrationAch;
            double oaM3h = z.OaLs * 3.6;   // L/s -> m3/h
            double h = uA + 0.33 * (oaM3h + infilM3h);   // W/K

            // Annual operating hours from the occupancy/equipment schedule —
            // mean daily "on" fraction x 24 x 365 (used to convert W/m2 densities).
            double occMeanFrac  = ScheduleMean(z.OccupancySchedule);
            double lightMeanFrac = ScheduleMean(z.LightingSchedule);
            double equipMeanFrac = ScheduleMean(z.EquipmentSchedule);
            double annualHours = Math.Max(1, occMeanFrac * 24 * 365);
            annualHoursAccum += annualHours * z.FloorAreaM2;

            // Per-month cooling/heating thermal demand (kWh).
            double tSetCool = z.CoolingSetpointC;
            double tSetHeat = z.HeatingSetpointC;

            for (int m = 0; m < 12; m++)
            {
                double hoursInMonth = DaysInMonth[m] * 24.0;
                double tOut = climate.MeanDbC[m];

                // Internal gains over the month (kWh): occupants + lighting + equipment.
                double occW   = z.OccupantCount * (z.OccupantSensibleW);
                double lightW = z.LightingWPerM2 * z.FloorAreaM2;
                double equipW = z.EquipmentWPerM2 * z.FloorAreaM2;
                double qIntKwh = (occW * occMeanFrac + lightW * lightMeanFrac + equipW * equipMeanFrac)
                                 * hoursInMonth / 1000.0;

                // Solar gain (kWh): project monthly GHI onto glazing area.
                // GHI is kWh/m2.day; multiply by days; reduce by a 0.5 vertical-
                // surface factor (rough projection of horizontal irradiance onto
                // facades — climate registry ships GHI, not per-facade incident).
                double qSolKwh = 0;
                if (z.Envelope != null)
                    foreach (var seg in z.Envelope.Where(s => s.Kind == SegmentKind.Window))
                        qSolKwh += seg.AreaM2 * seg.SHGC * seg.ShadingFactor
                                   * climate.GhiKwhM2Day[m] * DaysInMonth[m] * 0.5;

                // Conduction balance over the month (kWh): H x (tSet - tOut) x hours.
                // Cooling demand when it's hotter than the cooling setpoint; heating
                // demand when colder than heating setpoint. The gain-utilisation
                // factor flips sign automatically via the climate sign (spec §7).
                double coolHours = hoursInMonth;   // simplification: full-month occupancy-weighted

                // Cooling thermal demand = internal + solar gains + conduction-in
                // when tOut > tSetCool, minus utilisation of gains.
                double qCondCool = h * Math.Max(0, tOut - tSetCool) * coolHours / 1000.0; // kWh
                double qCondHeat = h * Math.Max(0, tSetHeat - tOut) * coolHours / 1000.0; // kWh

                // Gain utilisation: in cooling mode internal+solar gains ADD to the
                // cooling load; in heating mode they OFFSET the heating load (loss-
                // utilisation factor ~0.9 to avoid double-counting).
                double coolingDemandKwh = qCondCool + 0.9 * (qIntKwh + qSolKwh);
                double heatingDemandKwh = Math.Max(0, qCondHeat - 0.9 * (qIntKwh + qSolKwh));

                // Thermal -> electricity via seasonal COP/SEER (cooling) and an
                // assumed heating efficiency (electric resistance default 1.0; the
                // baseline COP applies to cooling only).
                e.CoolingKwh += coolingDemandKwh / Math.Max(1.5, cop);
                e.HeatingKwh += heatingDemandKwh / 1.0;
            }

            // Fans/pumps: proportional to ventilation, taken as 15% of cooling
            // electricity (CIBSE rule of thumb for all-air systems).
            e.FansKwh = 0.15 * e.CoolingKwh;

            // Lighting + equipment annual electricity (kWh) — densities x area x hours.
            e.LightingKwh  = z.LightingWPerM2 * z.FloorAreaM2 * (lightMeanFrac * 24 * 365) / 1000.0;
            e.EquipmentKwh = z.EquipmentWPerM2 * z.FloorAreaM2 * (equipMeanFrac * 24 * 365) / 1000.0;

            // DHW estimate from occupancy: people x 50 L/day x 30 K dT x
            // 1.16 Wh/(L.K) -> Wh/day; /1000 -> kWh/day; x 365 -> kWh/yr.
            double dhwKwhPerDay = z.OccupantCount * 50.0 * 30.0 * 1.16 / 1000.0;
            e.DhwKwh = dhwKwhPerDay * 365.0;

            return e;
        }

        private static double ScheduleMean(double[] sched)
            => (sched != null && sched.Length > 0) ? sched.Average() : 0.4;

        /// <summary>(baseline - design) / baseline x 100. 0 when baseline is 0.</summary>
        public static double SavingsPct(double baseline, double design)
            => baseline > 0 ? (baseline - design) / baseline * 100.0 : 0;
    }
}
