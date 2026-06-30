using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // SUS-7 — EDGE ZeroCarbon now carries a distinct op_carbon gate (net-zero operational
    // carbon, >=100% of operational electricity met by on-site renewables) so it is no
    // longer a rank-tie with Advanced. A 40/20/20 project WITHOUT net-zero reports Advanced;
    // only an on-site-renewable (PV >= consumption) project reaches ZeroCarbon.
    public class EdgeZeroCarbonGateTests
    {
        private static GreenScheme Edge()
            => GreenSchemeRegistry.LoadFromJson(TestData.Read("STING_GREEN_SCHEMES.json")).Get("EDGE");

        private static SchemeContext Ctx(double energyPct, double waterPct, double matPct,
            double pvKwh, double elecKwh)
        {
            var energy = new EnergyEstimateResult
            {
                EnergySavingsPct = energyPct, FloorAreaM2 = 1000, ZoneCount = 1,
                Occupancy = 50, BaselineEuiKwhM2Yr = 200, PvGenerationKwh = pvKwh
            };
            energy.Design.CoolingKwh = elecKwh;   // gross operational electricity
            var water = new WaterEstimateResult
            {
                WaterSavingsPct = waterPct, WaterSavingsInclAltPct = waterPct,
                BaselineLPersonDay = 50, DesignLPersonDay = 40, IsIndicativeDefault = false
            };
            var mat = new MaterialsRollupResult
            {
                EmbodiedEnergySavingsPct = matPct, WblcaCompleted = true,
                FloorAreaM2 = 1000, TotalCarbonKg = 1000, TotalEnergyMj = 12000, HasEnergyBaseline = true
            };
            return new SchemeContext { Energy = energy, Water = water, Materials = mat };
        }

        [Fact]
        public void NoOnSiteRenewables_40_20_20_ReportsAdvanced_NotZeroCarbon()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            var res = SchemeEvaluator.Evaluate(Edge(), "ZeroCarbon", providers, Ctx(45, 25, 22, pvKwh: 0, elecKwh: 50000));

            var op = res.Gates.Find(g => g.GateId == "op_carbon");
            Assert.NotNull(op);
            Assert.True(op.Computed);          // always computed (0% with no PV) so it can't block Advanced
            Assert.False(op.Passed);           // 0% < 100% at the ZeroCarbon target
            Assert.Equal("Advanced", res.AchievedLevel);   // highest achievable is Advanced, not ZeroCarbon
        }

        [Fact]
        public void OnSiteRenewablesCoverConsumption_ReachesZeroCarbon()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            // PV (60 MWh) > gross electricity (50 MWh) => offset 120% >= 100%.
            var res = SchemeEvaluator.Evaluate(Edge(), "ZeroCarbon", providers, Ctx(45, 25, 22, pvKwh: 60000, elecKwh: 50000));

            var op = res.Gates.Find(g => g.GateId == "op_carbon");
            Assert.True(op.Passed);
            Assert.Equal("ZeroCarbon", res.AchievedLevel);   // ranks above Advanced (threshold 100 > 40)
        }

        [Fact]
        public void OpCarbonGate_DoesNotBlockAdvanced()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            var res = SchemeEvaluator.Evaluate(Edge(), "Advanced", providers, Ctx(45, 25, 22, pvKwh: 0, elecKwh: 50000));

            var op = res.Gates.Find(g => g.GateId == "op_carbon");
            Assert.True(op.Passed);            // Advanced threshold is 0 => trivially met
            Assert.Equal("Advanced", res.AchievedLevel);
        }
    }
}
