using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Validates that the large hand-edited data files still parse as JSON after
    // the Phase 195 param + climate edits (PARAMETER_REGISTRY.json + climate).
    // Reads from the repo source tree (walks up from the test bin dir).
    public class DataFileValidityTests
    {
        private static string RepoDataPath(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "StingTools", "Data", fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        [Theory]
        [InlineData("PARAMETER_REGISTRY.json")]
        [InlineData("STING_CLIMATE_DATA.json")]
        [InlineData("STING_GREEN_SCHEMES.json")]
        [InlineData("STING_GREEN_BASELINES.json")]
        [InlineData("STING_WATER_USAGE_PROFILES.json")]
        [InlineData("STING_GREEN_MEASURES.json")]
        [InlineData("STING_CLIMATE_MONTHLY.json")]
        public void DataFile_ParsesAsValidJson(string fileName)
        {
            string path = RepoDataPath(fileName);
            Assert.NotNull(path);   // file located in the repo source tree
            var text = File.ReadAllText(path);
            var parsed = JToken.Parse(text);   // throws if invalid JSON
            Assert.NotNull(parsed);
        }

        [Fact]
        public void ParameterRegistry_HasSustainabilityParams()
        {
            string path = RepoDataPath("PARAMETER_REGISTRY.json");
            var root = JObject.Parse(File.ReadAllText(path));
            var support = (JArray)root["support_params"];
            bool found = false;
            foreach (var p in support)
                if ((string)p["param_name"] == "SUS_ENERGY_KWH_M2_NR") { found = true; break; }
            Assert.True(found, "SUS_ENERGY_KWH_M2_NR must be registered in support_params");
        }

        [Fact]
        public void ClimateData_HasBanguiSite()
        {
            string path = RepoDataPath("STING_CLIMATE_DATA.json");
            var root = JObject.Parse(File.ReadAllText(path));
            var sites = (JArray)root["sites"];
            bool found = false;
            foreach (var s in sites)
                if ((string)s["id"] == "bangui") { found = true; break; }
            Assert.True(found, "bangui must be present in STING_CLIMATE_DATA.json");
        }
    }
}
