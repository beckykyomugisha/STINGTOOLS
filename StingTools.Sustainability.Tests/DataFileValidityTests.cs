using System;
using System.IO;
using System.Linq;
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
        [InlineData("STING_ICE_EMBODIED_ENERGY.json")]
        [InlineData("WORKFLOW_SustainabilityAssessment.json")]
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
        public void SustainabilityWorkflow_ChainsTheSustainTags()
        {
            // WS H1 — the preset is auto-discovered by WorkflowEngine.AppendUserPresets;
            // every step command must be a Sustain_* tag registered in ResolveCommand.
            string path = RepoDataPath("WORKFLOW_SustainabilityAssessment.json");
            Assert.NotNull(path);
            var root = JObject.Parse(File.ReadAllText(path));
            var steps = (JArray)root["steps"];
            Assert.NotNull(steps);
            Assert.True(steps.Count >= 5, "expected at least 5 steps (auto-fill → baseline → dashboard → export → LCC)");
            foreach (var s in steps)
            {
                string cmd = (string)s["command"];
                Assert.False(string.IsNullOrWhiteSpace(cmd));
                Assert.StartsWith("Sustain_", cmd);
            }
            // The core chain is present.
            var cmds = steps.Select(s => (string)s["command"]).ToList();
            Assert.Contains("Sustain_AutoFill", cmds);
            Assert.Contains("Sustain_SetBaseline", cmds);
            Assert.Contains("Sustain_Dashboard", cmds);
            Assert.Contains("Sustain_EdgeExport", cmds);
            Assert.Contains("Sustain_LccBenefit", cmds);
        }

        [Fact]
        public void CategoryBindings_BindSusParamsToProjectInformation()
        {
            // WS H3 — the 6 SUS_* params must have ProjectInformation binding rows so
            // SetBaseline's stamp persists (no silent no-op).
            string path = RepoDataPath("CATEGORY_BINDINGS.csv");
            Assert.NotNull(path);
            var lines = File.ReadAllLines(path);
            string[] required =
            {
                "SUS_ENERGY_KWH_M2_NR", "SUS_WATER_L_PD_NR", "SUS_MAT_CARBON_KGM2_NR",
                "SUS_MAT_ENERGY_MJ_M2_NR", "SUS_EDGE_LEVEL_TXT", "SUS_EPD_REF_TXT"
            };
            foreach (var p in required)
                Assert.True(lines.Any(l => l.StartsWith(p + ",") && l.Contains("Project Information")),
                    $"{p} must bind to Project Information in CATEGORY_BINDINGS.csv");
        }

        [Fact]
        public void CoverageMatrix_MarksSusParamsOnProjectInformation()
        {
            // WS H3 — mirror the binding in the coverage matrix (Project Information = 1).
            string path = RepoDataPath("BINDING_COVERAGE_MATRIX.csv");
            Assert.NotNull(path);
            var lines = File.ReadAllLines(path);
            int hi = System.Array.FindIndex(lines, l => l.StartsWith("Parameter_Name,"));
            Assert.True(hi >= 0);
            var header = lines[hi].Split(',');
            int pi = System.Array.IndexOf(header, "Project Information");
            Assert.True(pi > 0);
            foreach (var p in new[] { "SUS_ENERGY_KWH_M2_NR", "SUS_MAT_CARBON_KGM2_NR" })
            {
                var row = lines.FirstOrDefault(l => l.StartsWith(p + ","));
                Assert.NotNull(row);
                Assert.Equal("1", row.Split(',')[pi]);
            }
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

        [Fact]
        public void ClimateData_BanguiCarriesRainfall()
        {
            // WS I7 — Bangui must carry a real annual rainfall (≈1,500 mm/yr) so RWH
            // yields a real number, not 0.
            string path = RepoDataPath("STING_CLIMATE_DATA.json");
            var sites = (JArray)JObject.Parse(File.ReadAllText(path))["sites"];
            var bangui = sites.FirstOrDefault(s => (string)s["id"] == "bangui");
            Assert.NotNull(bangui);
            Assert.True((double?)bangui["rainfallMmYr"] > 1000, "bangui rainfall should be ~1500 mm/yr");
        }
    }
}
