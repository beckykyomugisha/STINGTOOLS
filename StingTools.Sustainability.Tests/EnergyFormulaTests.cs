using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS C1 — energy estimator formula upgrades: EN ISO 13790 gain/loss utilisation
    // factor (not a flat 0.9), per-façade orientation solar (not a flat 0.5),
    // heating source/efficiency as an input with the right fuel carbon, and a
    // configurable fan-energy fraction.
    public class EnergyFormulaTests
    {
        private static ClimateMonthlySite Climate(double meanDb, double ghi)
        {
            var s = new ClimateMonthlySite { Id = "c", AnnualGhiKwhM2Yr = ghi * 365 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = meanDb; s.GhiKwhM2Day[m] = ghi; s.MeanRhPct[m] = 70; }
            return s;
        }

        private static LoadZone Zone(double windowOrientationDeg = 180)
        {
            var z = new LoadZone
            {
                Id = "z", Name = "office", FloorAreaM2 = 1000, HeightM = 3,
                OccupantCount = 80, LightingWPerM2 = 9, EquipmentWPerM2 = 12,
                CoolingSetpointC = 24, HeatingSetpointC = 21,
                OaLpsPerPerson = 10, OaLpsPerM2 = 0.3, InfiltrationAch = 0.3
            };
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 200, UvalueWm2K = 0.3, OrientationDeg = windowOrientationDeg });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 120, UvalueWm2K = 1.4, SHGC = 0.5, OrientationDeg = windowOrientationDeg });
            return z;
        }

        // ── Utilisation factor (EN ISO 13790 §12.2) ───────────────────────────
        [Fact]
        public void Utilisation_HasIso13790Properties()
        {
            double a = 2.0;
            Assert.Equal(1.0, AnnualEnergyEstimator.Utilisation(0, a), 6);     // γ→0 ⇒ all used
            Assert.Equal(a / (a + 1), AnnualEnergyEstimator.Utilisation(1, a), 6); // γ=1
            Assert.True(AnnualEnergyEstimator.Utilisation(5, a) < 0.5);        // γ large ⇒ low
            // Monotonic decreasing in γ.
            Assert.True(AnnualEnergyEstimator.Utilisation(0.5, a) > AnnualEnergyEstimator.Utilisation(2, a));
            // Bounded [0,1].
            Assert.InRange(AnnualEnergyEstimator.Utilisation(3, a), 0, 1);
        }

        // ── Per-façade orientation solar ──────────────────────────────────────
        [Fact]
        public void VerticalSolarFactor_SouthExceedsEastWestExceedsNorth()
        {
            double s = AnnualEnergyEstimator.VerticalSolarFactor(180);
            double e = AnnualEnergyEstimator.VerticalSolarFactor(90);
            double w = AnnualEnergyEstimator.VerticalSolarFactor(270);
            double n = AnnualEnergyEstimator.VerticalSolarFactor(0);
            Assert.True(s > e);
            Assert.True(e > n);
            Assert.Equal(e, w, 6);          // symmetric east/west
            Assert.True(s > 0.5 && n < 0.35);
        }

        [Fact]
        public void OrientationSolar_SouthGlazing_DrivesMoreCoolingThanNorth()
        {
            var hot = Climate(30, 5.0);
            var south = AnnualEnergyEstimator.Estimate(new[] { Zone(180) }, hot, null, 2.8);
            var north = AnnualEnergyEstimator.Estimate(new[] { Zone(0) }, hot, null, 2.8);

            // Identical zones bar window orientation ⇒ orientation now affects the result.
            Assert.True(south.Design.CoolingKwh > north.Design.CoolingKwh);
        }

        // ── Heating source / efficiency ───────────────────────────────────────
        [Fact]
        public void HeatPump_ReducesHeatingElectricity_VsResistance()
        {
            var cold = Climate(2, 1.5);
            var resistance = AnnualEnergyEstimator.Estimate(new[] { Zone() }, cold, null, 2.8,
                supply: new SupplyConfig { HeatingSeasonalEfficiency = 1.0, HeatingIsElectric = true });
            var heatPump = AnnualEnergyEstimator.Estimate(new[] { Zone() }, cold, null, 2.8,
                supply: new SupplyConfig { HeatingSeasonalEfficiency = 3.0, HeatingIsElectric = true });

            Assert.True(resistance.Design.HeatingKwh > 0);
            Assert.True(heatPump.Design.HeatingKwh < resistance.Design.HeatingKwh);
        }

        [Fact]
        public void GasHeating_ExcludedFromElectricity_CarbonViaFuelFactor()
        {
            var cold = Climate(2, 1.5);
            var res = AnnualEnergyEstimator.Estimate(new[] { Zone() }, cold, null, 2.8,
                supply: new SupplyConfig
                {
                    HeatingIsElectric = false, HeatingSeasonalEfficiency = 0.9,
                    HeatingFuelCarbonKgco2eKwh = 0.21
                });

            Assert.True(res.Design.HeatingFuelKwh > 0);             // tracked as fuel
            Assert.Equal(0, res.Design.HeatingKwh, 6);             // not electric
            Assert.True(res.Design.ElectricityKwh < res.Design.TotalKwh); // fuel excluded from elec
            // Operational carbon includes the gas portion (≥ fuel kWh × factor).
            Assert.True(res.OperationalCarbonKgYr >= res.Design.HeatingFuelKwh * 0.21 - 1e-6);
        }

        // ── Configurable fan-energy fraction ──────────────────────────────────
        [Fact]
        public void FanFraction_Configurable_ZeroAndDefault()
        {
            var hot = Climate(30, 5.0);
            var def = AnnualEnergyEstimator.Estimate(new[] { Zone() }, hot, null, 2.8);   // default 0.15
            Assert.Equal(0.15 * def.Design.CoolingKwh, def.Design.FansKwh, 3);

            var noFans = AnnualEnergyEstimator.Estimate(new[] { Zone() }, hot, null, 2.8,
                supply: new SupplyConfig { FanEnergyFraction = 0 });
            Assert.Equal(0, noFans.Design.FansKwh, 6);
        }
    }
}
