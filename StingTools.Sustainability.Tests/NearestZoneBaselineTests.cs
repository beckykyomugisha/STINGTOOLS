using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS J4 — tropical baseline rows + nearest-zone fallback (never a hardcoded 4A).
    public class NearestZoneBaselineTests
    {
        private static GreenBaselineRegistry Shipped()
            => GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));

        [Theory]
        [InlineData("0A", 0)]
        [InlineData("1B", 1)]
        [InlineData("4A", 4)]
        [InlineData("7", 7)]
        [InlineData("*", -1)]
        [InlineData("", -1)]
        public void ZoneNumber_ParsesLeadingDigits(string zone, int expected)
            => Assert.Equal(expected, GreenBaselineRegistry.ZoneNumber(zone));

        [Theory]
        [InlineData("0B")]
        [InlineData("1A")]
        [InlineData("1B")]
        [InlineData("2A")]
        public void TropicalZones_HaveTheirOwnOfficeBaseline(string zone)
        {
            var res = Shipped().Resolve("*", zone, "office");
            Assert.True(res.Found);
            Assert.Contains(zone, res.MatchedKey);   // matched its own tropical row, not 4A
            Assert.DoesNotContain("fell back to NEAREST", res.Summary);
        }

        [Fact]
        public void TropicalOffice_DiffersFromTemperate()
        {
            var reg = Shipped();
            double hot = reg.Resolve("*", "1A", "office").Baseline.TotalEuiKwhM2Yr(2500);
            double temperate = reg.Resolve("*", "4A", "office").Baseline.TotalEuiKwhM2Yr(2500);
            Assert.True(hot > temperate, $"1A {hot} should exceed 4A {temperate}");
        }

        [Fact]
        public void UnmatchedZone_FallsToNearest_NotHardcoded4A()
        {
            // No 3A office row; nearest by number is 2A (dist 1) over 4A (dist 1) — ties
            // prefer the lower zone number, so 2A, never a hardcoded 4A.
            var res = Shipped().Resolve("*", "3A", "office");
            Assert.True(res.Found);
            Assert.Contains("2A", res.MatchedKey);
            Assert.DoesNotContain("4A", res.MatchedKey);
            Assert.Contains("NEAREST", res.Summary);
        }

        [Fact]
        public void NearestZone_PicksClosestByNumber()
        {
            // No 1B residential; residential rows exist at 1A/2A/4A → nearest to 1 is 1A.
            var res = Shipped().Resolve("*", "1B", "residential");
            Assert.True(res.Found);
            Assert.Contains("1A", res.MatchedKey);
            Assert.Contains("residential", res.MatchedKey);
            Assert.Contains("NEAREST", res.Summary);
        }

        [Fact]
        public void NonNumericZone_DoesNotUseNearest()
        {
            // "*" zone has no number → nearest is skipped; warehouse has no rows → global.
            var res = Shipped().Resolve("XX", "9Z", "warehouse");
            Assert.Equal("*/*/*", res.MatchedKey);
        }
    }
}
