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

        // The EDGE app's REAL building-type taxonomy STING maps onto (WS L7).
        private static readonly HashSet<string> EdgeTypes = new HashSet<string>
        {
            "Offices", "Homes", "Hospitality", "Retail", "Hospitals",
            "Education", "Mixed Use"
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
        public void OaCarriesBothFields_OnOneConsistentBasis()
        {
            // WS L3 — the profile carries both OA fields, on ONE basis: CIBSE all-in
            // per-person for occupied spaces (per-area 0, no double-count). The field
            // still exists; the chosen basis just zeroes it for occupied uses.
            var retail = Lib().ResolveForUse("retail").Profile;
            Assert.True(retail.OaLpsPerPerson > 0);
            Assert.Equal(0, retail.OaLpsPerM2, 3);   // all-in per-person basis (L3)
            // Unoccupied spaces keep area-based ventilation.
            Assert.True(Lib().ById["Parking"].OaLpsPerM2 > 0);
        }
    }
}
