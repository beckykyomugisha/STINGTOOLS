using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L8 — the embodied-carbon headline reads "indicative — review quantities"
    // whenever a sanity flag fires or stamped coverage < 80%, and the coverage is
    // shown alongside. The number is surfaced honestly, never hidden.
    public class MaterialsHeadlineTests
    {
        private static MaterialsRollupResult Complete()
            => new MaterialsRollupResult
            {
                FloorAreaM2 = 1000, TotalCarbonKg = 300_000,   // 300 kgCO2e/m² — plausible
                TotalLines = 31, CarbonStampedLines = 28       // 90% stamped
            };

        [Fact]
        public void Complete_PlausibleRun_IsNotFlagged()
        {
            var m = Complete();
            Assert.False(m.CarbonHeadlineFlagged);
            Assert.True(m.CarbonStampedCoverageFraction >= 0.80);
        }

        [Fact]
        public void SingleMaterialDominance_FlagsHeadline()
        {
            var m = Complete();
            m.DominantHotspotImplausible = true;   // e.g. Steel Purlins 92%
            Assert.True(m.CarbonHeadlineFlagged);
        }

        [Fact]
        public void ImplausibleIntensity_FlagsHeadline()
        {
            var m = Complete();
            m.IntensityImplausible = true;          // e.g. 5213 kgCO2e/m²
            Assert.True(m.CarbonHeadlineFlagged);
        }

        [Fact]
        public void LowCoverage_FlagsHeadline()
        {
            // 15/31 carbon-stamped ≈ 48% < 80% → flagged, partial figure surfaced.
            var m = new MaterialsRollupResult { FloorAreaM2 = 1000, TotalCarbonKg = 300_000,
                                                TotalLines = 31, CarbonStampedLines = 15 };
            Assert.True(m.CarbonHeadlineFlagged);
            Assert.InRange(m.CarbonStampedCoverageFraction, 0.45, 0.50);
        }

        [Fact]
        public void IndicativeOnlyCarbon_FlagsHeadline()
        {
            // No stamped lines, only indicative factors → flagged (CarbonIsIndicative).
            var m = new MaterialsRollupResult { FloorAreaM2 = 1000, TotalCarbonKg = 100_000,
                                                TotalLines = 10, CarbonStampedLines = 0, IndicativeCarbonLines = 10 };
            Assert.True(m.CarbonIsIndicative);
            Assert.True(m.CarbonHeadlineFlagged);
        }

        [Fact]
        public void CoverageSummary_ShowsStampedVsTotal()
        {
            var m = new MaterialsRollupResult { TotalLines = 31, CarbonStampedLines = 15 };
            Assert.Contains("15/31", m.CoverageSummary);
        }
    }
}
