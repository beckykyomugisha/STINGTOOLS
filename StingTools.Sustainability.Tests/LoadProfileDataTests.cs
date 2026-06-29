using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS K1/K5 — the load-profile library is comprehensive (≥22 profiles) and every
    // profile carries the full design-parameter set + the four new fields with
    // provenance + aliases + three 24-hour schedules.
    public class LoadProfileDataTests
    {
        private static string Path_() => DataFileValidityTestsRepo("STING_LOAD_PROFILES.json");

        // Reuse the repo-tree walk from DataFileValidityTests.
        private static string DataFileValidityTestsRepo(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, "StingTools", "Data", fileName);
                if (File.Exists(c)) return c;
                dir = dir.Parent;
            }
            return null;
        }

        private static JArray Profiles()
        {
            string p = Path_(); Assert.NotNull(p);
            return (JArray)JObject.Parse(File.ReadAllText(p))["profiles"];
        }

        [Fact]
        public void Library_HasAtLeast22Profiles_IncludingTheNewUses()
        {
            var profs = Profiles();
            Assert.True(profs.Count >= 22, $"expected ≥22 profiles, got {profs.Count}");
            var ids = profs.Select(p => (string)p["id"]).ToList();
            foreach (var id in new[] { "Office", "Residential", "HotelGuestroom", "Retail", "Restaurant",
                                       "PatientRoom", "Classroom", "Lab", "Warehouse", "DataCentre",
                                       "GymFitness", "WorshipAssembly", "CinemaTheatre", "Parking" })
                Assert.Contains(id, ids);
        }

        [Fact]
        public void EveryProfile_CarriesTheFullFieldSet()
        {
            foreach (var p in Profiles())
            {
                string id = (string)p["id"];
                foreach (var field in new[]
                {
                    "occupantDensityM2PerPerson","lightingWPerM2","equipmentWPerM2",
                    "occupantSensibleW","occupantLatentW","oaLpsPerPerson","oaLpsPerM2",
                    "coolingSetpointC","heatingSetpointC","infiltrationAch",
                    "dhwLPerPersonDay","operatingDaysPerYear","source","edgeBuildingType"
                })
                    Assert.True(p[field] != null, $"{id} missing {field}");

                Assert.False(string.IsNullOrWhiteSpace((string)p["source"]), $"{id} blank source");
                Assert.False(string.IsNullOrWhiteSpace((string)p["edgeBuildingType"]), $"{id} blank edgeBuildingType");
                foreach (var sched in new[] { "occupancySchedule", "lightingSchedule", "equipmentSchedule" })
                    Assert.Equal(24, ((JArray)p[sched]).Count);
            }
        }

        [Fact]
        public void Residential_HasResidentialDensityAndDhw_NotOffice()
        {
            var resi = Profiles().First(p => (string)p["id"] == "Residential");
            Assert.Equal(35.0, (double)resi["occupantDensityM2PerPerson"], 1);   // not office 10
            Assert.Equal(45.0, (double)resi["dhwLPerPersonDay"], 1);             // dwelling DHW
            Assert.Equal("Homes", (string)resi["edgeBuildingType"]);
            var aliases = ((JArray)resi["aliases"]).Select(a => (string)a).ToList();
            Assert.Contains("house", aliases);
            Assert.Contains("dwelling", aliases);
        }

        [Fact]
        public void EveryProfile_MarkedSeedIndicative()
        {
            foreach (var p in Profiles())
                Assert.Contains("seed", ((string)p["source"]).ToLowerInvariant());
        }
    }
}
