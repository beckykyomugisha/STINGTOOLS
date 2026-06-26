using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Spec §14: resolution fallback order + proxy-path correctness; project
    // override merge. Baselines key on CLIMATE ZONE, never country.
    public class GreenBaselineRegistryTests
    {
        private static GreenBaselineRegistry Shipped()
            => GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));

        [Fact]
        public void Resolve_HotHumidOffice_MatchesZone0A()
        {
            var reg = Shipped();
            // CAR has no country row -> climate-zone 0A office proxy hop matches.
            var res = reg.Resolve("CF", "0A", "office");

            Assert.True(res.Found);
            Assert.Contains("0A", res.MatchedKey);
            Assert.Equal("indicative", res.Provenance);
            // Proxy path records the country miss then the zone hit.
            Assert.True(res.Path.Count >= 2);
            Assert.False(res.Path[0].Matched);   // country+zone+use miss (no CF row)
            Assert.Contains(res.Path, h => h.Matched);
            Assert.Contains("fell back", res.Summary);
        }

        [Fact]
        public void Resolve_ExactCountryMatch_NoProxyHop()
        {
            // Add a project-override exact row for CF/0A/office and confirm it is
            // matched as the first hop with no fallback language.
            string proj = @"{
              ""schema"": ""sting.green.baselines/v1"",
              ""baselines"": [{
                ""key"": { ""country"": ""CF"", ""climateZone"": ""0A"", ""buildingUse"": ""office"" },
                ""source"": ""EDGE_app_official_CAR"", ""provenance"": ""indicative"",
                ""energy"": { ""method"": ""endUseIntensity"", ""endUses"": { ""cooling"": { ""eui_kwh_m2_yr"": 90 } }, ""baselineSystemCOP"": { ""cooling"": 2.8 } },
                ""water"": { ""fixtureBaselines"": { ""wc_lpf"": 6.0 } },
                ""materials"": { ""embodiedEnergyBaseline_mj_m2"": null }
              }]
            }";
            var reg = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"), proj);
            var res = reg.Resolve("CF", "0A", "office");

            Assert.True(res.Found);
            Assert.Equal("EDGE_app_official_CAR", res.Source);
            Assert.True(res.Path[0].Matched);    // exact match on first hop
            Assert.Contains("exact match", res.Summary);
        }

        [Fact]
        public void Resolve_UnknownZone_FallsBackToBuildingUseOrGlobal()
        {
            var reg = Shipped();
            // No 9Z zone anywhere -> climate-zone hop misses -> buildingUse 'office'
            // (any-zone) is also absent, so it lands on the global default.
            var res = reg.Resolve("XX", "9Z", "warehouse");

            Assert.True(res.Found);
            // warehouse has no buildingUse row either -> global default (*/*/*).
            Assert.Equal("*/*/*", res.MatchedKey);
        }

        [Fact]
        public void Resolve_NeverProxiesOnNearestCountry()
        {
            var reg = Shipped();
            // The 4A office row is country=* — a CF/0A query must NEVER pick the 4A
            // row just because it's the only 'office' entry; it must proxy on ZONE
            // (0A) first. The 0A office row exists so we get 0A, not 4A.
            var res = reg.Resolve("CF", "0A", "office");
            Assert.Contains("0A", res.MatchedKey);
            Assert.DoesNotContain("4A", res.MatchedKey);
        }

        [Fact]
        public void Resolve_TemperateResidential_DiffersFromHotHumidOffice()
        {
            var reg = Shipped();
            var office0A = reg.Resolve("*", "0A", "office");
            var res4A    = reg.Resolve("*", "4A", "residential");

            Assert.True(office0A.Found);
            Assert.True(res4A.Found);
            // Different baselines -> different total EUI (coherent, different numbers).
            double euiOffice = office0A.Baseline.TotalEuiKwhM2Yr(2500);
            double euiResi   = res4A.Baseline.TotalEuiKwhM2Yr(2500);
            Assert.NotEqual(euiOffice, euiResi, 1);
        }

        [Fact]
        public void Baseline_NullEmbodiedEnergy_MeansEdgeAppOwnsIt()
        {
            var reg = Shipped();
            var res = reg.Resolve("*", "0A", "office");
            Assert.Null(res.Baseline.EmbodiedEnergyBaselineMjM2);
        }

        [Fact]
        public void TotalEui_ConvertsWPerM2DensitiesToAnnualKwh()
        {
            var reg = Shipped();
            var res = reg.Resolve("*", "0A", "office");
            // 0A office: cooling 95 + fans 22 + dhw 4 (kWh/m2) + lighting 9 W/m2 +
            // equipment 12 W/m2 over 2500 h -> (9+12)*2500/1000 = 52.5 kWh/m2.
            double eui = res.Baseline.TotalEuiKwhM2Yr(2500);
            Assert.Equal(95 + 22 + 4 + 52.5, eui, 1);
        }
    }
}
