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
        /// <summary>Electric heating final energy (resistance / heat-pump), kWh.</summary>
        public double HeatingKwh   { get; set; }
        /// <summary>WS C1 — non-electric heating final energy (gas/oil boiler), kWh.
        /// Tracked separately so it does NOT draw on PV / grid electricity carbon —
        /// its carbon uses the heating fuel factor.</summary>
        public double HeatingFuelKwh { get; set; }
        public double FansKwh      { get; set; }
        public double LightingKwh  { get; set; }
        public double EquipmentKwh { get; set; }
        public double DhwKwh       { get; set; }

        /// <summary>Total final energy across all end-uses (for EUI).</summary>
        public double TotalKwh => CoolingKwh + HeatingKwh + HeatingFuelKwh + FansKwh + LightingKwh + EquipmentKwh + DhwKwh;

        /// <summary>The ELECTRICITY portion (everything except non-electric heating
        /// fuel) — what the PV / grid supply layer operates on. WS C1.</summary>
        public double ElectricityKwh => CoolingKwh + HeatingKwh + FansKwh + LightingKwh + EquipmentKwh + DhwKwh;

        public void AddTo(EnergyByEndUse other)
        {
            other.CoolingKwh     += CoolingKwh;
            other.HeatingKwh     += HeatingKwh;
            other.HeatingFuelKwh += HeatingFuelKwh;
            other.FansKwh        += FansKwh;
            other.LightingKwh    += LightingKwh;
            other.EquipmentKwh   += EquipmentKwh;
            other.DhwKwh         += DhwKwh;
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

        /// <summary>Number of zones that fed the estimate (0 ⇒ nothing computed).</summary>
        public int ZoneCount { get; set; }

        /// <summary>WS J3 — total occupants across the zones. 0 ⇒ degenerate input
        /// (no people); the EDGE energy % is meaningless, so it is not "computed".</summary>
        public int Occupancy { get; set; }

        /// <summary>True only when a real design EUI was computed from zones with
        /// floor area AND occupancy against a non-zero baseline. False ⇒ the savings %
        /// is a zero-design/degenerate artefact (floor 0 / occ 0 / (baseline-0)/baseline
        /// = 100%) and must NOT be shown as a computed result (WS J3).</summary>
        public bool Computed => ZoneCount > 0 && FloorAreaM2 > 0 && Occupancy > 0
                                && Design.TotalKwh > 0 && BaselineEuiKwhM2Yr > 0;
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

            // WS C1 — heating source + fan-energy inputs (default = legacy behaviour).
            double heatEff   = (supply != null && supply.HeatingSeasonalEfficiency > 0) ? supply.HeatingSeasonalEfficiency : 1.0;
            bool   heatElec  = supply?.HeatingIsElectric ?? true;
            double fanFrac   = supply != null ? supply.FanEnergyFraction : 0.15;
            double heatFuelCarbon = supply?.HeatingFuelCarbonKgco2eKwh ?? 0.21;

            double totalArea = 0;
            double annualOperatingHours = 0;
            int totalOccupants = 0;

            foreach (var z in zoneList)
            {
                if (z.FloorAreaM2 <= 0) continue;
                totalArea += z.FloorAreaM2;
                totalOccupants += Math.Max(0, z.OccupantCount);

                bool hasEnvelope = z.Envelope != null && z.Envelope.Count > 0;
                if (!hasEnvelope) res.AnyZoneMissingEnvelope = true;

                // Cooling COP/SEER is the supplied baseline COP (the caller applies any
                // per-zone override before calling by passing the chosen baselineCoolingCop).
                double cop = baselineCoolingCop > 0 ? baselineCoolingCop : 3.0;

                var zoneResult = EstimateZone(z, climate, cop, heatEff, heatElec, fanFrac, ref annualOperatingHours);
                zoneResult.AddTo(res.Design);
            }

            res.FloorAreaM2 = totalArea;
            res.Occupancy = totalOccupants;
            res.ZoneCount = zoneList.Count(z => z.FloorAreaM2 > 0);
            // annualOperatingHours is accumulated weighted by area inside EstimateZone;
            // normalise to an area-weighted mean for the baseline conversion.
            double meanHours = totalArea > 0 ? annualOperatingHours / totalArea : 2500;

            res.DesignEuiKwhM2Yr = totalArea > 0 ? res.Design.TotalKwh / totalArea : 0;

            if (baseline != null)
                res.BaselineEuiKwhM2Yr = baseline.TotalEuiKwhM2Yr(meanHours);
            res.EnergySavingsPct = SavingsPct(res.BaselineEuiKwhM2Yr, res.DesignEuiKwhM2Yr);

            // Supply layer operates on the ELECTRICITY only (PV/grid don't offset a
            // gas boiler); non-electric heating fuel carbon is added separately. WS C1.
            var supplyResult = SupplyAndGenerationLayer.Apply(
                res.Design.ElectricityKwh, climate, supply, pvAnnualGenerationKwh);
            res.PvGenerationKwh        = supplyResult.PvGenerationKwh;
            res.NetImportKwh           = supplyResult.NetImportKwh;
            res.OperationalCarbonKgYr  = supplyResult.OperationalCarbonKgYr
                                         + res.Design.HeatingFuelKwh * heatFuelCarbon;

            if (res.ZoneCount == 0)
                res.Warnings.Add("Energy NOT computed — no MEP Spaces and no zone floor area. " +
                                 "Add Spaces, or enter floor area (GFA) in Setup, then re-run.");
            else if (res.Design.TotalKwh <= 0)
                res.Warnings.Add("Energy NOT computed — zones produced zero design energy " +
                                 "(check floor area / occupancy / COP).");
            else if (res.Occupancy <= 0)
                res.Warnings.Add("Energy NOT computed — occupancy is 0. Enter occupancy in Setup " +
                                 "(or model 'Number of People' on Spaces), then re-run. (WS J3)");
            if (res.BaselineEuiKwhM2Yr <= 0)
                res.Warnings.Add("Baseline EUI is zero — savings % not meaningful (check baseline resolution).");
            if (res.AnyZoneMissingEnvelope)
                res.Warnings.Add("One or more zones have no envelope data — conduction/solar gains under-counted.");

            return res;
        }

        /// <summary>Monthly quasi-steady balance for one zone (EN ISO 13790 §12.2
        /// gain/loss utilisation). Accumulates the area-weighted annual operating
        /// hours into <paramref name="annualHoursAccum"/>. WS C1.</summary>
        private static EnergyByEndUse EstimateZone(
            LoadZone z, ClimateMonthlySite climate, double cop,
            double heatingSeasonalEfficiency, bool heatingIsElectric, double fanEnergyFraction,
            ref double annualHoursAccum)
        {
            var e = new EnergyByEndUse();

            // Transmission + ventilation loss/gain coefficient H (W/K).
            // 0.33 Wh/(m3.K) is the volumetric heat capacity of air.
            double uA = z.Envelope?.Sum(s => s.UvalueWm2K * s.AreaM2) ?? 0;
            double infilM3h = z.VolumeM3 * z.InfiltrationAch;
            double oaM3h = z.OaLs * 3.6;   // L/s -> m3/h
            double h = uA + 0.33 * (oaM3h + infilM3h);   // W/K

            // WS C1 — EN ISO 13790 numeric utilisation parameter a = a0 + τ/τ0,
            // a0 = 1, τ0 = 15 h (monthly). τ = C/H, C = internal heat capacity. Use
            // the envelope's areal heat capacity when present, else the ISO "medium"
            // class default (165 kJ/m²K). Clamped so a degenerate H/τ can't blow up.
            double cmKJperM2K = z.Envelope?.Where(s => s.ThermalMassKJperM2K > 0)
                                  .Select(s => s.ThermalMassKJperM2K).DefaultIfEmpty(0).Max() ?? 0;
            if (cmKJperM2K <= 0) cmKJperM2K = 165.0;   // ISO 13790 Table 12 medium class
            double capacityJ = cmKJperM2K * 1000.0 * z.FloorAreaM2;
            double tauH = h > 1e-6 ? capacityJ / (h * 3600.0) : 1000.0;
            tauH = Math.Min(1000.0, Math.Max(1.0, tauH));
            double aParam = 1.0 + tauH / 15.0;

            // Annual operating hours from the occupancy schedule — mean daily "on"
            // fraction x 8760. WS C1: this ONE basis drives both the baseline EUI
            // conversion (meanHours) AND the design lighting/equipment electricity, so
            // the savings % compares like-for-like operating assumptions.
            double occMeanFrac   = ScheduleMean(z.OccupancySchedule);
            double lightMeanFrac = ScheduleMean(z.LightingSchedule);
            double equipMeanFrac = ScheduleMean(z.EquipmentSchedule);
            double operatingHours = Math.Max(1, occMeanFrac * 8760.0);
            annualHoursAccum += operatingHours * z.FloorAreaM2;

            double tSetCool = z.CoolingSetpointC;
            double tSetHeat = z.HeatingSetpointC;

            double coolingThermalKwh = 0, heatingThermalKwh = 0;

            for (int m = 0; m < 12; m++)
            {
                double hoursInMonth = DaysInMonth[m] * 24.0;
                double tOut = climate.MeanDbC[m];

                // Internal gains over the month (kWh): occupants + lighting + equipment.
                double occW   = z.OccupantCount * z.OccupantSensibleW;
                double lightW = z.LightingWPerM2 * z.FloorAreaM2;
                double equipW = z.EquipmentWPerM2 * z.FloorAreaM2;
                double qIntKwh = (occW * occMeanFrac + lightW * lightMeanFrac + equipW * equipMeanFrac)
                                 * hoursInMonth / 1000.0;

                // WS C1 — solar gain projected onto each glazing façade by ORIENTATION
                // (the OrientationDeg field was previously ignored). Replaces the flat
                // 0.5 vertical factor with a per-façade vertical transposition of the
                // monthly horizontal GHI (equator-facing max, north min).
                double qSolKwh = 0;
                if (z.Envelope != null)
                    foreach (var seg in z.Envelope.Where(s => s.Kind == SegmentKind.Window))
                        qSolKwh += seg.AreaM2 * seg.SHGC * seg.ShadingFactor
                                   * climate.GhiKwhM2Day[m] * DaysInMonth[m]
                                   * VerticalSolarFactor(seg.OrientationDeg);

                double qGain = qIntKwh + qSolKwh;

                // Heating month: transfer loss Q_ht = H(tSetHeat - tOut) when positive.
                // Gains usefully offset losses by the gain-utilisation factor η_gn.
                double qHtHeat = h * Math.Max(0, tSetHeat - tOut) * hoursInMonth / 1000.0;
                if (qHtHeat > 0)
                {
                    double etaGn = Utilisation(qGain / qHtHeat, aParam);   // η of gains
                    heatingThermalKwh += Math.Max(0, qHtHeat - etaGn * qGain);
                }

                // Cooling: signed transfer Q_ht = H(tSetCool - tOut). Positive ⇒ heat
                // flows OUT (a loss that offsets cooling, utilised by η_ls); negative ⇒
                // heat flows IN and adds fully to the cooling load.
                double qHtCool = h * (tSetCool - tOut) * hoursInMonth / 1000.0;
                if (qGain > 0)
                {
                    if (qHtCool > 0)
                    {
                        double etaLs = Utilisation(qHtCool / qGain, aParam);   // η of losses
                        coolingThermalKwh += Math.Max(0, qGain - etaLs * qHtCool);
                    }
                    else
                    {
                        coolingThermalKwh += qGain - qHtCool;   // gain + conduction-in
                    }
                }
            }

            // Cooling electricity via seasonal COP/SEER.
            e.CoolingKwh = coolingThermalKwh / Math.Max(1.5, cop);

            // Heating final energy via the seasonal heating efficiency/COP. Electric
            // heating draws on the electricity supply; a fuel (gas/oil) is tracked
            // separately so PV/grid carbon doesn't apply to it. WS C1.
            double heatingFinalKwh = heatingThermalKwh / Math.Max(0.3, heatingSeasonalEfficiency);
            if (heatingIsElectric) e.HeatingKwh = heatingFinalKwh;
            else                   e.HeatingFuelKwh = heatingFinalKwh;

            // Fans/pumps as a configurable fraction of cooling electricity. WS C1.
            e.FansKwh = Math.Max(0, fanEnergyFraction) * e.CoolingKwh;

            // Lighting + equipment annual electricity (kWh) — densities x area x the
            // SAME operating-hours basis the baseline uses (WS C1 consistency).
            e.LightingKwh  = z.LightingWPerM2 * z.FloorAreaM2 * operatingHours / 1000.0;
            e.EquipmentKwh = z.EquipmentWPerM2 * z.FloorAreaM2 * operatingHours / 1000.0;

            // DHW estimate from occupancy: people x (L/person.day) x 30 K dT x
            // 1.16 Wh/(L.K) -> Wh/day; /1000 -> kWh/day; x 365 -> kWh/yr.
            // L/person.day is building-use dependent (office ~5, residential ~45) —
            // see LoadZone.DhwLPerPersonDay; a flat 50 inflated office DHW ~10x.
            double dhwLpd = z.DhwLPerPersonDay > 0 ? z.DhwLPerPersonDay : 5.0;
            double dhwKwhPerDay = z.OccupantCount * dhwLpd * 30.0 * 1.16 / 1000.0;
            e.DhwKwh = dhwKwhPerDay * 365.0;

            return e;
        }

        /// <summary>EN ISO 13790 §12.2 gain/loss utilisation factor. <paramref name="gamma"/>
        /// is the ratio (gains/losses for heating; losses/gains for cooling). Returns
        /// 1 as γ→0 (all of the smaller term is usefully used), a/(a+1) at γ=1, and
        /// →0 as γ→∞. WS C1.</summary>
        public static double Utilisation(double gamma, double a)
        {
            if (gamma <= 0) return 1.0;
            if (Math.Abs(gamma - 1.0) < 1e-9) return a / (a + 1.0);
            double eta = (1.0 - Math.Pow(gamma, a)) / (1.0 - Math.Pow(gamma, a + 1.0));
            return Math.Max(0.0, Math.Min(1.0, eta));
        }

        /// <summary>Indicative monthly transposition of horizontal GHI onto a vertical
        /// façade, by orientation. ~180° (equator-facing in the N hemisphere) is the
        /// maximum, north the minimum, east/west between. Replaces the flat 0.5 factor
        /// so a façade's orientation finally affects its solar gain. A full anisotropic
        /// transposition (needs site latitude + DNI split) is a documented follow-on.
        /// WS C1.</summary>
        public static double VerticalSolarFactor(double orientationDeg)
        {
            double rad = (orientationDeg - 180.0) * Math.PI / 180.0;
            return 0.27 + 0.35 * 0.5 * (1.0 + Math.Cos(rad));   // 0.27 (N) … 0.62 (S)
        }

        private static double ScheduleMean(double[] sched)
            => (sched != null && sched.Length > 0) ? sched.Average() : 0.4;

        /// <summary>(baseline - design) / baseline x 100, guarded against NaN/∞ and
        /// a non-positive baseline (WS F — delegates to <see cref="SustainSavings.Pct"/>).</summary>
        public static double SavingsPct(double baseline, double design)
            => SustainSavings.Pct(baseline, design);
    }
}
