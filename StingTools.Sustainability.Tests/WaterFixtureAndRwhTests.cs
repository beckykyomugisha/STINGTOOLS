using StingTools.Core.Plumbing;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS A4 / D3 — the orchestrator now reads real fixtures (classified + averaged
    // by WaterFixtureAggregator) and computes a real RWH yield via
    // RainwaterHarvestingCalc that folds into the EDGE water %. These cover the pure
    // pieces that wiring depends on.
    public class WaterFixtureAndRwhTests
    {
        // ── Fixture classification ────────────────────────────────────────────
        [Theory]
        [InlineData("WC Close-coupled", WaterFixtureAggregator.FixtureKind.Wc)]
        [InlineData("Standard Toilet", WaterFixtureAggregator.FixtureKind.Wc)]
        [InlineData("Wall Urinal", WaterFixtureAggregator.FixtureKind.Urinal)]
        [InlineData("Wash Hand Basin", WaterFixtureAggregator.FixtureKind.Basin)]
        [InlineData("Shower Mixer", WaterFixtureAggregator.FixtureKind.Shower)]
        [InlineData("Kitchen Sink", WaterFixtureAggregator.FixtureKind.Kitchen)]
        [InlineData("Floor Drain", WaterFixtureAggregator.FixtureKind.Unknown)]
        public void Classify_ByName(string name, WaterFixtureAggregator.FixtureKind expected)
            => Assert.Equal(expected, WaterFixtureAggregator.Classify(name));

        // ── Aggregation ───────────────────────────────────────────────────────
        [Fact]
        public void Aggregator_NoReadings_ReturnsNull()
        {
            var agg = new WaterFixtureAggregator();
            Assert.Equal(0, agg.ReadingCount);
            Assert.Null(agg.BuildOrNull(new FixtureFlows()));
        }

        [Fact]
        public void Aggregator_AveragesReadings_AndFallsBackPerCategory()
        {
            var agg = new WaterFixtureAggregator();
            agg.AddByName("WC low-flush", flushLitres: 4.0, flowLpm: 0);
            agg.AddByName("WC low-flush", flushLitres: 4.5, flowLpm: 0);
            agg.AddByName("Basin spray tap", flushLitres: 0, flowLpm: 5.0);

            var fallback = new FixtureFlows { WcLpf = 6, UrinalLpf = 4, BasinTapLpm = 8, ShowerLpm = 10, KitchenTapLpm = 8 };
            var flows = agg.BuildOrNull(fallback);

            Assert.NotNull(flows);
            Assert.Equal(4.25, flows.WcLpf, 2);          // (4.0 + 4.5)/2
            Assert.Equal(5.0, flows.BasinTapLpm, 2);     // single reading
            // Unread categories inherit the baseline (claim no saving).
            Assert.Equal(4, flows.UrinalLpf, 2);
            Assert.Equal(10, flows.ShowerLpm, 2);
        }

        // ── RWH yield via RainwaterHarvestingCalc folds into the EDGE water % ──
        [Fact]
        public void RwhCalc_FeedsWaterEstimate_RaisesEdgeInclusivePct()
        {
            var profile = WaterUsageProfileRegistry
                .LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json")).Get("office");
            var flows = new FixtureFlows { WcLpf = 6, UrinalLpf = 4, BasinTapLpm = 8, ShowerLpm = 10, KitchenTapLpm = 8 };
            int occ = 100;

            // Non-potable demand sizes the RWH (the demand RWH serves).
            double nonPotableLpd = AnnualWaterEstimator.NonPotableLPersonDay(flows, profile);
            double dailyDemandM3 = nonPotableLpd * occ / 1000.0;
            Assert.True(dailyDemandM3 > 0);

            // Real BS 8515 yield: a large roof in a wet climate.
            var rwh = RainwaterHarvestingCalc.Calculate(
                roofAreaM2: 1500, annualRainfallMm: 1200, runoffCoefficient: 0,
                filterEfficiency: 0, dailyDemandM3: dailyDemandM3);
            Assert.True(rwh.AnnualYieldM3 > 0);

            var withRwh = AnnualWaterEstimator.Estimate(flows, flows, profile, occ,
                rwhYieldLPerYr: rwh.AnnualYieldM3 * 1000.0);
            var noRwh = AnnualWaterEstimator.Estimate(flows, flows, profile, occ);

            // Same fixtures (no efficiency saving) — RWH alone lifts the EDGE % via
            // the alt-water credit.
            Assert.Equal(0, withRwh.WaterSavingsPct, 1);
            Assert.True(withRwh.WaterSavingsInclAltPct > noRwh.WaterSavingsInclAltPct);
            Assert.True(withRwh.WaterSavingsInclAltPct > 0);
        }
    }
}
