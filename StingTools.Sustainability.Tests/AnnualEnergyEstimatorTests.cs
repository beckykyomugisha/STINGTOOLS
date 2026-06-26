using System.Collections.Generic;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Spec §14: monthly balance on a known synthetic zone within tolerance;
    // cooling-dominated vs heating-dominated; COP/SEER scaling; PV offset;
    // off-grid/diesel carbon. Plus the supply layer in isolation.
    public class AnnualEnergyEstimatorTests
    {
        private static ClimateMonthlySite HotClimate()
        {
            var s = new ClimateMonthlySite { Id = "hot", AnnualGhiKwhM2Yr = 1850 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 30; s.GhiKwhM2Day[m] = 5.0; s.MeanRhPct[m] = 70; }
            return s;
        }

        private static ClimateMonthlySite ColdClimate()
        {
            var s = new ClimateMonthlySite { Id = "cold", AnnualGhiKwhM2Yr = 1000 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 2; s.GhiKwhM2Day[m] = 1.5; s.MeanRhPct[m] = 80; }
            return s;
        }

        private static LoadZone Zone(double areaM2 = 1000)
        {
            var z = new LoadZone
            {
                Id = "z1", Name = "Open office", FloorAreaM2 = areaM2, HeightM = 3.0,
                OccupantCount = 80, LightingWPerM2 = 9, EquipmentWPerM2 = 12,
                CoolingSetpointC = 24, HeatingSetpointC = 21,
                OaLpsPerPerson = 10, OaLpsPerM2 = 0.3, InfiltrationAch = 0.3
            };
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 200, UvalueWm2K = 0.3, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 100, UvalueWm2K = 1.4, SHGC = 0.4, OrientationDeg = 180 });
            return z;
        }

        [Fact]
        public void Estimate_HotClimate_IsCoolingDominated()
        {
            var res = AnnualEnergyEstimator.Estimate(
                new[] { Zone() }, HotClimate(), baseline: null, baselineCoolingCop: 2.8);

            Assert.True(res.Design.CoolingKwh > 0);
            // In a hot climate, cooling electricity dominates heating.
            Assert.True(res.Design.CoolingKwh > res.Design.HeatingKwh);
            Assert.True(res.DesignEuiKwhM2Yr > 0);
        }

        [Fact]
        public void Estimate_ColdClimate_FlipsToHeatingDominated()
        {
            var res = AnnualEnergyEstimator.Estimate(
                new[] { Zone() }, ColdClimate(), baseline: null, baselineCoolingCop: 2.8);

            // The utilisation factor flips: cold climate -> heating demand present
            // and exceeds cooling (which is near zero when it's always 2 degC).
            Assert.True(res.Design.HeatingKwh > res.Design.CoolingKwh);
        }

        [Fact]
        public void Estimate_HigherCop_ReducesCoolingElectricity()
        {
            var lowCop  = AnnualEnergyEstimator.Estimate(new[] { Zone() }, HotClimate(), null, 2.0);
            var highCop = AnnualEnergyEstimator.Estimate(new[] { Zone() }, HotClimate(), null, 4.0);

            // Same demand, better COP -> less cooling electricity.
            Assert.True(highCop.Design.CoolingKwh < lowCop.Design.CoolingKwh);
        }

        [Fact]
        public void Estimate_PvOffset_ReducesNetImport()
        {
            var noPv = AnnualEnergyEstimator.Estimate(new[] { Zone() }, HotClimate(), null, 2.8,
                supply: new SupplyConfig { Mode = "grid_tied", PvKwp = 0 });
            var withPv = AnnualEnergyEstimator.Estimate(new[] { Zone() }, HotClimate(), null, 2.8,
                supply: new SupplyConfig { Mode = "grid_tied", PvKwp = 50, PvPerformanceRatio = 0.75 });

            Assert.True(withPv.PvGenerationKwh > 0);
            Assert.True(withPv.NetImportKwh < noPv.NetImportKwh);
        }

        [Fact]
        public void Estimate_AgainstBaseline_ProducesSavingsPct()
        {
            var reg = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            var baseline = reg.Resolve("*", "0A", "office").Baseline;

            var res = AnnualEnergyEstimator.Estimate(
                new[] { Zone() }, HotClimate(), baseline, baseline.BaselineCoolingCop);

            Assert.True(res.BaselineEuiKwhM2Yr > 0);
            // Savings % is finite (positive or negative) and computed from the gap.
            double expected = AnnualEnergyEstimator.SavingsPct(res.BaselineEuiKwhM2Yr, res.DesignEuiKwhM2Yr);
            Assert.Equal(expected, res.EnergySavingsPct, 3);
        }

        [Fact]
        public void Estimate_MissingEnvelope_Warns()
        {
            var z = new LoadZone { Id = "z", FloorAreaM2 = 500, HeightM = 3, OccupantCount = 20 };
            // No envelope segments.
            var res = AnnualEnergyEstimator.Estimate(new[] { z }, HotClimate(), null, 2.8);
            Assert.True(res.AnyZoneMissingEnvelope);
            Assert.Contains(res.Warnings, w => w.Contains("envelope"));
        }

        // ── Supply layer in isolation ─────────────────────────────────────

        [Fact]
        public void Supply_OffGrid_UsesDieselFactor()
        {
            var climate = HotClimate();
            var grid = SupplyAndGenerationLayer.Apply(100000, climate,
                new SupplyConfig { Mode = "grid_tied", GridCarbonKgco2eKwh = 0.2, DieselCarbonKgco2eKwh = 0.8 });
            var off = SupplyAndGenerationLayer.Apply(100000, climate,
                new SupplyConfig { Mode = "off_grid", GridCarbonKgco2eKwh = 0.2, DieselCarbonKgco2eKwh = 0.8 });

            // Off-grid diesel emits more carbon for the same kWh than a clean grid.
            Assert.True(off.OperationalCarbonKgYr > grid.OperationalCarbonKgYr);
            Assert.Equal(80000, off.OperationalCarbonKgYr, 1);   // 100000 x 0.8
        }

        [Fact]
        public void Supply_HybridDieselFraction_BlendsFactors()
        {
            var climate = HotClimate();
            var r = SupplyAndGenerationLayer.Apply(100000, climate,
                new SupplyConfig { Mode = "hybrid", GridCarbonKgco2eKwh = 0.2, DieselCarbonKgco2eKwh = 0.8, DieselFraction = 0.5 });
            // 0.5*0.8 + 0.5*0.2 = 0.5 -> 50000 kgCO2e.
            Assert.Equal(50000, r.OperationalCarbonKgYr, 1);
        }

        [Fact]
        public void Supply_Pv_FromGhiAndPr()
        {
            var climate = new ClimateMonthlySite { Id = "x", AnnualGhiKwhM2Yr = 1800 };
            double pv = SupplyAndGenerationLayer.PvAnnualKwh(
                new SupplyConfig { PvKwp = 10, PvPerformanceRatio = 0.75 }, climate);
            // 10 kWp x (1800 x 0.75) = 13500 kWh/yr.
            Assert.Equal(13500, pv, 1);
        }

        [Fact]
        public void Supply_Pv_YieldOverrideWinsOverGhi()
        {
            var climate = new ClimateMonthlySite { Id = "x", AnnualGhiKwhM2Yr = 1800 };
            double pv = SupplyAndGenerationLayer.PvAnnualKwh(
                new SupplyConfig { PvKwp = 10, PvYieldKwhPerKwpYr = 1500 }, climate);
            Assert.Equal(15000, pv, 1);
        }
    }
}
