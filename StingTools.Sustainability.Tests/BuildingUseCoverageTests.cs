using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS K4 — the coverage guard. The headline test FAILS if any catalog use lacks a
    // load profile + DHW + water profile; a gap reads as a visible fallback.
    public class BuildingUseCoverageTests
    {
        private static CoverageReport Report()
        {
            var profiles  = LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));
            var water     = WaterUsageProfileRegistry.LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json"));
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            return BuildingUseCoverage.Check(BuildingUseCatalog.CommonUses, profiles, water, baselines, "4A");
        }

        [Fact]
        public void EveryCatalogUse_HasLoadProfile_Dhw_AndWaterProfile()
        {
            var report = Report();
            var gaps = report.Gaps.ToList();
            Assert.True(report.AllCovered,
                "Building-use coverage gaps (add the missing load/DHW/water entry): " +
                string.Join("; ", gaps.Select(g => $"{g.Use} [{string.Join(", ", g.Fallbacks)}]")));
        }

        [Fact]
        public void Coverage_ReportsAGap_AsAVisibleFallback_NotSilent()
        {
            // A use with no load profile, no water row → must show as a gap with fallbacks,
            // never silently "covered".
            var profiles = LoadProfileLibrary.FromJson(@"{ ""profiles"": [ { ""id"":""Office"", ""dhwLPerPersonDay"":5 } ] }");
            var water    = WaterUsageProfileRegistry.LoadFromJson(@"{ ""profiles"": [ { ""buildingUse"":""office"" } ] }");
            var baselines = GreenBaselineRegistry.LoadFromJson(@"{ ""resolutionOrder"":[""global""], ""baselines"": [ { ""key"":{""country"":""*"",""climateZone"":""*"",""buildingUse"":""*""} } ] }");

            var report = BuildingUseCoverage.Check(new[] { "datacentre" }, profiles, water, baselines, "4A");
            var row = report.Rows.Single();
            Assert.False(row.IsCovered);
            Assert.False(row.WaterExact);
            Assert.Contains(row.Fallbacks, f => f.Contains("water"));
            Assert.Contains(row.Fallbacks, f => f.Contains("load profile"));   // office-default fallback
        }

        [Fact]
        public void BaselineMayFallBack_StillCovered_IfLoadAndWaterExist()
        {
            // "datacentre" has a load + water profile but no baseline row → baseline
            // fallback is allowed (logged), the use is still covered.
            var report = Report();
            var dc = report.Rows.Single(r => r.Use == "datacentre");
            Assert.True(dc.IsCovered);
            Assert.False(dc.BaselineExact);   // resolves by fallback (logged), not exact
        }
    }
}
