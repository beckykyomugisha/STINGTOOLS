using StingTools.Core.Plumbing;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS A4 / D3 — the orchestrator (SustainabilityEngine.ReadDesignFixtureFlows)
    // classifies each plumbing fixture with FixtureFlowReader.ClassifyKind, feeds its
    // rating into WaterFixtureAggregator and builds the design flows with BuildOrNull
    // against the BASELINE (DSCH-46b - it used to build them itself and gave an
    // unrated kind the class low-flow default, claiming a saving nobody modelled).
    // It also computes a real RWH yield via RainwaterHarvestingCalc that folds into
    // the EDGE water %. These cover the pure pieces that wiring depends on.
    public class WaterFixtureAndRwhTests
    {
        // ── Fixture classification: ONE classifier (FixtureFlowReader) ─────────
        // The aggregator carried a second keyword classifier that disagreed with it
        // ("pan", "tap", "vanity"); it was deleted, and these cases moved here.
        [Theory]
        [InlineData("WC Close-coupled", FixtureKind.Wc)]
        [InlineData("Standard Toilet", FixtureKind.Wc)]
        [InlineData("Wall Urinal", FixtureKind.Urinal)]
        [InlineData("Wash Hand Basin", FixtureKind.Basin)]
        [InlineData("Shower Mixer", FixtureKind.Shower)]
        [InlineData("Kitchen Sink", FixtureKind.KitchenTap)]
        [InlineData("Floor Drain", FixtureKind.Unknown)]
        public void Classify_ByName(string name, FixtureKind expected)
            => Assert.Equal(expected, FixtureFlowReader.ClassifyKind(name));

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
            agg.Add(FixtureKind.Wc, 4.0);
            agg.Add(FixtureKind.Wc, 4.5);
            agg.Add(FixtureKind.Basin, 5.0);

            var fallback = new FixtureFlows { WcLpf = 6, UrinalLpf = 4, BasinTapLpm = 8, ShowerLpm = 10, KitchenTapLpm = 8 };
            var flows = agg.BuildOrNull(fallback);

            Assert.NotNull(flows);
            Assert.Equal(4.25, flows.WcLpf, 2);          // (4.0 + 4.5)/2
            Assert.Equal(5.0, flows.BasinTapLpm, 2);     // single reading
            // Unread categories inherit the baseline (claim no saving).
            Assert.Equal(4, flows.UrinalLpf, 2);
            Assert.Equal(10, flows.ShowerLpm, 2);
        }

        // DSCH-46b - an unrated kind takes the BASELINE flow, so it claims no
        // saving. The engine's old path gave it the class low-flow default
        // (FixtureFlows() = 6/4/8/10/8), which is below every shipped baseline.
        [Fact]
        public void UnratedKinds_TakeTheBaseline_NotTheLowFlowClassDefault()
        {
            var agg = new WaterFixtureAggregator();
            agg.Add(FixtureKind.Wc, 4.0);
            var baseline = new FixtureFlows { WcLpf = 6, UrinalLpf = 4, BasinTapLpm = 12, ShowerLpm = 15, KitchenTapLpm = 12 };
            var flows = agg.BuildOrNull(baseline);
            Assert.Equal(4.0, flows.WcLpf, 3);
            Assert.Equal(12, flows.BasinTapLpm, 3);
            Assert.Equal(15, flows.ShowerLpm, 3);
            Assert.Equal(12, flows.KitchenTapLpm, 3);

            var profile = WaterUsageProfileRegistry
                .LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json")).Get("office");
            double onlyWc = AnnualWaterEstimator.Estimate(flows, baseline, profile, 100).WaterSavingsPct;
            double viaClassDefault = AnnualWaterEstimator.Estimate(
                new FixtureFlows { WcLpf = 4.0 }, baseline, profile, 100).WaterSavingsPct;
            Assert.True(onlyWc < viaClassDefault,
                "rating only the WCs must claim less saving than the old class-default path");
        }

        [Fact]
        public void Summary_NamesReadKinds_AndTheBaselineOnes()
        {
            var agg = new WaterFixtureAggregator();
            agg.Add(FixtureKind.Wc, 4.0);
            agg.Add(FixtureKind.Wc, 5.0);
            agg.Add(FixtureKind.Shower, 8.0);
            var baseline = new FixtureFlows { WcLpf = 6, UrinalLpf = 4, BasinTapLpm = 12, ShowerLpm = 15, KitchenTapLpm = 12 };
            string s = agg.Summary(agg.BuildOrNull(baseline));
            Assert.Contains("WC 4.5 L/flush (2 fixtures)", s);
            Assert.Contains("shower 8 L/min (1 fixture)", s);
            Assert.Contains("urinal, basin tap, kitchen tap", s);
            Assert.Contains("baseline", s);
        }

        [Fact]
        public void UnknownKind_IsNotAReading()
        {
            var agg = new WaterFixtureAggregator();
            agg.Add(FixtureKind.Unknown, 5.0);
            Assert.Equal(0, agg.ReadingCount);
            Assert.Null(agg.BuildOrNull(new FixtureFlows()));
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
