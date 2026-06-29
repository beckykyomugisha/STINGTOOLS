using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L6 — baseline coverage for the common new uses, and any remaining fallback
    // is VISIBLE (the building-use axis is flagged, savings % labelled indicative).
    public class BaselineCoverageTests
    {
        private static GreenBaselineRegistry Reg()
            => GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));

        [Theory]
        [InlineData("hotel", "1A")]
        [InlineData("retail", "1A")]
        [InlineData("education", "2A")]
        [InlineData("healthcare", "4A")]
        [InlineData("restaurant", "0A")]
        public void CommonUses_NowResolveAnExactUseBaseline(string use, string zone)
        {
            var r = Reg().Resolve("*", zone, use);
            Assert.True(r.Found);
            Assert.Contains(use, r.MatchedKey);                       // matched its own use row
            Assert.DoesNotContain(r.FallbackAxes, a => a.Contains("building use"));   // use axis NOT a fallback
            Assert.True(r.Baseline.TotalEuiKwhM2Yr(2500) > 0);
        }

        [Fact]
        public void UnseededUse_FallsBack_VISIBLY_OnTheUseAxis()
        {
            // A use with no baseline row falls back, and the fallback is surfaced on the
            // building-use axis (the dashboard labels the savings % indicative).
            var r = Reg().Resolve("*", "1A", "datacentre");
            Assert.Contains(r.FallbackAxes, a => a.Contains("building use"));
            Assert.False(r.ExactMatch);
        }

        [Fact]
        public void SeededUse_TropicalDiffersFromTemperate()
        {
            var reg = Reg();
            double hot  = reg.Resolve("*", "1A", "hotel").Baseline.TotalEuiKwhM2Yr(2500);
            double cool = reg.Resolve("*", "4A", "hotel").Baseline.TotalEuiKwhM2Yr(2500);
            Assert.True(hot > cool, $"hotel 1A {hot} should exceed 4A {cool}");
        }
    }
}
