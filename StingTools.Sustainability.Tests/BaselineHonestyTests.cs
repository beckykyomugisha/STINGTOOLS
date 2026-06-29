using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I2 — the proxy log must be honest: a wildcard/defaulted axis is a
    // fallback/default proxy, NEVER an "exact match".
    public class BaselineHonestyTests
    {
        private static GreenBaselineRegistry Shipped()
            => GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));

        [Fact]
        public void WildcardCountry_IsNotExactMatch()
        {
            // Country unset (*) → the resolution is a default proxy, not exact.
            var res = Shipped().Resolve("*", "0A", "office");
            Assert.True(res.Found);
            Assert.False(res.ExactMatch);
            Assert.DoesNotContain("exact match", res.Summary);
            Assert.Contains("default proxy", res.Summary);
            Assert.Contains(res.FallbackAxes, a => a.Contains("country") && a.Contains("unset"));
        }

        [Fact]
        public void FullyResolvedKey_IsExactMatch()
        {
            // A project-override exact row for all three axes → genuinely exact.
            const string proj = @"{ ""baselines"": [{
                ""key"": { ""country"": ""CF"", ""climateZone"": ""0A"", ""buildingUse"": ""office"" },
                ""source"": ""project_exact"", ""provenance"": ""indicative"",
                ""energy"": { ""endUses"": { ""cooling"": { ""eui_kwh_m2_yr"": 90 } } } }] }";
            var res = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"), proj)
                .Resolve("CF", "0A", "office");
            Assert.True(res.ExactMatch);
            Assert.Empty(res.FallbackAxes);
            Assert.Contains("exact match", res.Summary);
        }

        [Fact]
        public void CountryFallback_RecordsAxis_AndKeepsFellBackWording()
        {
            // CF has no row → falls back to climate-zone 0A: country axis is a proxy.
            var res = Shipped().Resolve("CF", "0A", "office");
            Assert.False(res.ExactMatch);
            Assert.Contains("fell back", res.Summary);
            Assert.Contains(res.FallbackAxes, a => a.Contains("country") && a.Contains("CF"));
        }

        [Fact]
        public void UnsetUse_RecordsUseFallback()
        {
            var res = Shipped().Resolve("*", "0A", "*");
            Assert.False(res.ExactMatch);
            Assert.Contains(res.FallbackAxes, a => a.Contains("building use") && a.Contains("unset"));
        }
    }
}
