using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS H5 — the EDGE KPI snapshot must record the SAME inclusive water % the gate
    // uses (fixture efficiency + alternative water), not the fixture-only %, so the
    // persisted trend agrees with the on-screen pass/fail.
    public class EdgeSnapshotWaterTests
    {
        private static WaterEstimateResult AltWaterResult()
        {
            var profile = WaterUsageProfileRegistry
                .LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json")).Get("office");
            var flows = new FixtureFlows { WcLpf = 6, UrinalLpf = 4, BasinTapLpm = 8, ShowerLpm = 10, KitchenTapLpm = 8 };
            // Identical fixtures (fixture % = 0) + RWH/greywater so InclAlt > 0.
            return AnnualWaterEstimator.Estimate(flows, flows, profile, occupancy: 100,
                rwhYieldLPerYr: 30000, greywaterReuseFraction: 0.15);
        }

        [Fact]
        public void Snapshot_RecordsInclusiveGateMetric_NotFixtureOnly()
        {
            var w = AltWaterResult();
            Assert.Equal(0, w.WaterSavingsPct, 1);               // fixtures identical
            Assert.True(w.WaterSavingsInclAltPct > 0);            // alt water credited

            double snapshotWater = EdgeKpiSnapshot.GateWaterPct(w);
            Assert.Equal(w.WaterSavingsInclAltPct, snapshotWater, 6);   // snapshot == inclusive
            Assert.NotEqual(w.WaterSavingsPct, snapshotWater);          // NOT the fixture-only %
        }

        [Fact]
        public void Snapshot_WaterMatchesTheGateProviderValue()
        {
            var w = AltWaterResult();
            var ctx = new SchemeContext { Water = w };
            var gate = new AnnualWaterMetricProvider().Evaluate(ctx);

            double gateValue = gate.Numbers["water_savings_pct"];
            Assert.Equal(gateValue, EdgeKpiSnapshot.GateWaterPct(w), 6);
        }

        [Fact]
        public void GateWaterPct_NullSafe()
            => Assert.Equal(0, EdgeKpiSnapshot.GateWaterPct(null), 6);
    }
}
