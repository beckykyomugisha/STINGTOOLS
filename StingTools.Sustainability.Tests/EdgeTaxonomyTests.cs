using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Hvac.Loads;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L7 — every edgeBuildingType is a REAL EDGE category; the non-existent
    // "Light Industry" is gone, and mappings are marked best-fit/indicative.
    public class EdgeTaxonomyTests
    {
        private static readonly HashSet<string> RealEdge = new HashSet<string>
        {
            "Homes", "Hospitality", "Retail", "Offices", "Hospitals", "Education", "Mixed Use"
        };

        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        [Fact]
        public void NoProfile_UsesTheNonExistentLightIndustryCategory()
            => Assert.DoesNotContain(Lib().ById.Values, p => p.EdgeBuildingType == "Light Industry");

        [Fact]
        public void EveryEdgeType_IsARealEdgeCategory()
        {
            foreach (var p in Lib().ById.Values)
                Assert.Contains(p.EdgeBuildingType, RealEdge);
        }

        [Theory]
        [InlineData("warehouse")]
        [InlineData("datacentre")]
        [InlineData("parking")]
        public void FormerLightIndustryUses_MapToMixedUse(string use)
            => Assert.Equal("Mixed Use", Lib().ResolveForUse(use).Profile.EdgeBuildingType);

        [Fact]
        public void EdgeMappings_AreMarkedBestFit()
        {
            foreach (var p in Lib().ById.Values)
                Assert.Contains("EDGE type:", p.Source);
        }
    }
}
