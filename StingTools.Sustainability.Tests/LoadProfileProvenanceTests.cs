using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Hvac.Loads;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS K5 — every profile is a complete, defensible design context: full parameter
    // set + provenance (source) + an EDGE building-category mapping.
    public class LoadProfileProvenanceTests
    {
        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        // The EDGE app's building-type taxonomy STING maps onto.
        private static readonly HashSet<string> EdgeTypes = new HashSet<string>
        {
            "Offices", "Homes", "Hospitality", "Retail", "Hospitals",
            "Education", "Light Industry"
        };

        [Fact]
        public void EveryProfile_MapsToAKnownEdgeBuildingType()
        {
            foreach (var p in Lib().ById.Values)
                Assert.Contains(p.EdgeBuildingType, EdgeTypes);
        }

        [Fact]
        public void EveryProfile_CarriesProvenanceAndOperatingDays()
        {
            foreach (var p in Lib().ById.Values)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Source), $"{p.Id} blank source");
                Assert.InRange(p.OperatingDaysPerYear, 1, 366);
                Assert.True(p.DhwLPerPersonDay >= 0, $"{p.Id} negative DHW");
                Assert.True(p.OccupantDensityM2PerPerson >= 0, $"{p.Id} bad density");
                Assert.Equal(24, p.OccupancySchedule.Length);
                Assert.Equal(24, p.LightingSchedule.Length);
                Assert.Equal(24, p.EquipmentSchedule.Length);
            }
        }

        [Fact]
        public void Resolution_CarriesTheEdgeTypeAndDhw_ForMapping()
        {
            var lib = Lib();
            var resi = lib.ResolveForUse("residential").Profile;
            Assert.Equal("Homes", resi.EdgeBuildingType);
            Assert.True(resi.DhwLPerPersonDay > 0);

            var hotel = lib.ResolveForUse("hotel").Profile;
            Assert.Equal("Hospitality", hotel.EdgeBuildingType);

            var hosp = lib.ResolveForUse("healthcare").Profile;
            Assert.Equal("Hospitals", hosp.EdgeBuildingType);
        }

        [Fact]
        public void OaCarriedPerPersonAndPerM2_TheFullVentilationSet()
        {
            // K5 requires OA per-person AND per-m²; e.g. retail carries both.
            var retail = Lib().ResolveForUse("retail").Profile;
            Assert.True(retail.OaLpsPerPerson > 0);
            Assert.True(retail.OaLpsPerM2 > 0);
        }
    }
}
