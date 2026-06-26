using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS B5 — the EDGE-app's certified % (entered on the dashboard) overrides the
    // indicative figure for that gate AND counts as a computed/certified number, so
    // a delegated gate (EDGE materials) becomes evaluable and the determined EDGE
    // level reflects the official figure.
    public class EdgeOfficialFeedbackTests
    {
        private static GreenScheme Edge()
            => GreenSchemeRegistry.LoadFromJson(TestData.Read("STING_GREEN_SCHEMES.json")).Get("EDGE");

        // Context with genuinely-computed energy + water + materials so the
        // not-computed guards don't fire; official overrides supplied separately.
        private static SchemeContext Ctx(double energyPct, double waterPct, double matPct,
            Dictionary<string, double> official = null, int zoneCount = 1, double floor = 1000)
        {
            var energy = new EnergyEstimateResult
            {
                EnergySavingsPct = energyPct, FloorAreaM2 = floor, ZoneCount = zoneCount, BaselineEuiKwhM2Yr = 200
            };
            energy.Design.CoolingKwh = zoneCount > 0 ? 50000 : 0;
            var water = new WaterEstimateResult
            {
                WaterSavingsPct = waterPct, WaterSavingsInclAltPct = waterPct,
                BaselineLPersonDay = 50, DesignLPersonDay = 40, IsIndicativeDefault = false
            };
            var mat = new MaterialsRollupResult
            {
                EmbodiedEnergySavingsPct = matPct, WblcaCompleted = true,
                FloorAreaM2 = floor, TotalCarbonKg = 1000, TotalEnergyMj = 12000, HasEnergyBaseline = true
            };
            return new SchemeContext { Energy = energy, Water = water, Materials = mat, OfficialOverrides = official };
        }

        [Fact]
        public void OfficialEnergy_OverridesIndicative_AndPasses()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            // Indicative energy 18 fails Advanced (40); official 45 passes.
            var official = new Dictionary<string, double> { ["energy_savings_pct"] = 45 };
            var res = SchemeEvaluator.Evaluate(Edge(), "Advanced", providers, Ctx(18, 25, 22, official));

            var energy = res.Gates.Find(g => g.GateId == "energy");
            Assert.Equal(45, energy.IndicativeValue, 3);   // official replaced the 18
            Assert.True(energy.Computed);
            Assert.True(energy.Passed);
            Assert.True(res.Passed);
        }

        [Fact]
        public void OfficialMaterials_StopsBeingDelegated_AndCountsTowardLevel()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            var official = new Dictionary<string, double> { ["embodied_energy_savings_pct"] = 25 };
            var res = SchemeEvaluator.Evaluate(Edge(), "Advanced", providers, Ctx(45, 25, 22, official));

            var mat = res.Gates.Find(g => g.GateId == "materials");
            Assert.False(mat.Delegated);     // official supplied ⇒ no longer delegated
            Assert.True(mat.Computed);
            Assert.True(mat.Passed);         // 25 ≥ 20 (Advanced materials)
            Assert.True(res.Passed);
            Assert.Equal("Advanced", res.AchievedLevel);
        }

        [Fact]
        public void OfficialMaterials_BelowThreshold_BlocksLevel()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            // Energy + water pass, but the recorded official materials % is below 20.
            var official = new Dictionary<string, double> { ["embodied_energy_savings_pct"] = 10 };
            var res = SchemeEvaluator.Evaluate(Edge(), "Advanced", providers, Ctx(45, 25, 22, official));

            var mat = res.Gates.Find(g => g.GateId == "materials");
            Assert.False(mat.Delegated);
            Assert.False(mat.Passed);        // 10 < 20
            Assert.False(res.Passed);        // the now-determinable materials gate blocks
        }

        [Fact]
        public void NoOfficial_MaterialsStaysDelegated()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            var res = SchemeEvaluator.Evaluate(Edge(), "Advanced", providers, Ctx(45, 25, 22, official: null));

            var mat = res.Gates.Find(g => g.GateId == "materials");
            Assert.True(mat.Delegated);      // unchanged legacy behaviour
            Assert.True(res.Passed);         // energy + water carry the determinable result
        }

        [Fact]
        public void OfficialEnergy_RescuesNotComputedGate()
        {
            var providers = MetricProviderRegistry.CreateStandard();
            // No zones ⇒ indicative energy is the 100% zero-design artefact (not computed);
            // the recorded official % makes the gate certified + evaluable.
            var official = new Dictionary<string, double> { ["energy_savings_pct"] = 45 };
            var res = SchemeEvaluator.Evaluate(Edge(), "Advanced", providers,
                Ctx(100, 25, 22, official, zoneCount: 0, floor: 0));

            var energy = res.Gates.Find(g => g.GateId == "energy");
            Assert.True(energy.Computed);    // official figure is certified
            Assert.True(energy.Passed);
        }

        [Fact]
        public void EdgeOfficialFigures_MapsOnlyPresentValues()
        {
            var eo = new EdgeOfficialFigures { EnergySavingsPct = 45, MaterialsSavingsPct = 20 };
            var map = eo.ToMetricOverrides();

            Assert.True(eo.Any);
            Assert.Equal(45, map["energy_savings_pct"], 3);
            Assert.Equal(20, map["embodied_energy_savings_pct"], 3);
            Assert.False(map.ContainsKey("water_savings_pct"));   // null ⇒ not recorded

            var empty = new EdgeOfficialFigures();
            Assert.False(empty.Any);
            Assert.Empty(empty.ToMetricOverrides());
        }
    }
}
