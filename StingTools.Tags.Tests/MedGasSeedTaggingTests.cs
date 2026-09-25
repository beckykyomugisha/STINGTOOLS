// DT-4: STING's own medical-gas outlet seed must be taggable by STING's own
// terminal unit tag.
//
// WHAT WENT WRONG
//
// STING - Medical Gas Terminal Unit Tag is a Plumbing Fixtures tag, and so is
// everything else in the MGPS stack: MgasNetwork and MgasFlowValidator collect
// terminal units and alarm panels from OST_PlumbingFixtures, and the Area /
// Master Alarm Panel tags are Plumbing Fixtures tags. STING_SEED_MedGasOutlet
// alone built Specialty Equipment. So a seeded outlet could not take the TU tag
// (health-medgas-pln fell back to the generic Specialty Equipment tag), and it
// was invisible to the network builder and the flow validator. It also carried
// no MGS_GAS_TYPE_TXT, the one field the TU tag prints and the network keys on.
//
// The seed moved to Plumbing Fixtures. These tests hold the two sides together
// and pin which seed type each medical-gas rule catches, because the seed family
// name ("Medical Gas Outlet") matches the TU pattern for every one of its types.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MedGasSeedTaggingTests
    {
        private const string TuTag = "STING - Medical Gas Terminal Unit Tag";
        private const string AapTag = "STING - Area Alarm Panel Tag";

        // MgasNetwork.Build's gas vocabulary (Revit-bound, so restated here).
        private static readonly HashSet<string> NetworkGases = new HashSet<string>(StringComparer.Ordinal)
            { "O2", "MA4", "MA7", "N2O", "N2", "CO2", "HE", "VAC", "AGS", "DENT" };

        private static string Data(params string[] parts)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(new[] { d.FullName, "StingTools", "Data" }.Concat(parts).ToArray());
        }

        private static JObject Seed() =>
            (JObject)JObject.Parse(File.ReadAllText(Data("Seeds", "STING_SEED_MedGasOutlet.json")))["symbols"][0];

        private static string DeclaredCategory(string tagFamily)
        {
            foreach (var line in File.ReadLines(Data("STING_TAG_CONFIG_v5_0_HEALTH.csv")))
            {
                var cells = line.Split(',');
                if (cells.Length > 3 && cells[0] == "TAG_FAMILY" && cells[1].Trim() == tagFamily)
                    return cells[3].Trim();
            }
            return null;
        }

        private static IEnumerable<(string TypeId, JObject Rule)> MedGasRules()
        {
            var cat = JObject.Parse(File.ReadAllText(Data("STING_DRAWING_TYPES.json")));
            foreach (var t in cat["drawingTypes"])
                foreach (var r in (t["annotation"]?["rules"] as JArray) ?? new JArray())
                    if ((string)r["ruleType"] == "AutoTag" && !string.IsNullOrEmpty((string)r["tagFamily"]))
                        yield return ((string)t["id"], (JObject)r);
        }

        [Fact]
        public void TheSeedIsTheCategoryTheTerminalUnitTagTags()
        {
            string tagCat = DeclaredCategory(TuTag);
            Assert.Equal("Plumbing Fixtures", tagCat);
            Assert.Equal(tagCat, (string)Seed()["category"]);
        }

        [Fact]
        public void EveryTerminalUnitTypeCarriesANetworkGasCode()
        {
            var seed = Seed();
            Assert.Contains(seed["parameters"], p => (string)p["name"] == "MGS_GAS_TYPE_TXT");
            var tus = seed["typeVariants"]
                .Where(v => ((string)v["name"] ?? "").StartsWith("TERMINAL_UNIT_", StringComparison.Ordinal)).ToList();
            Assert.Equal(8, tus.Count);
            var bad = tus.Where(v => !NetworkGases.Contains((string)v["params"]?["MGS_GAS_TYPE_TXT"] ?? ""))
                .Select(v => (string)v["name"]).ToList();
            Assert.True(bad.Count == 0,
                "A terminal unit type has no MGS_GAS_TYPE_TXT MgasNetwork recognises, so the TU tag "
                + "prints blank and the outlet joins no gas network: " + string.Join(", ", bad));
        }

        [Fact]
        public void EachSeedTypeIsCaughtByTheRightMedicalGasRule()
        {
            var seed = Seed();
            string family = (string)seed["name"];
            string category = (string)seed["category"];
            var rules = MedGasRules().Where(r => (string)r.Rule["category"] == category
                && ((string)r.Rule["tagFamily"] == TuTag || (string)r.Rule["tagFamily"] == AapTag)).ToList();
            Assert.Contains(rules, r => (string)r.Rule["tagFamily"] == TuTag);

            var bad = new List<string>();
            foreach (var v in seed["typeVariants"].Where(v => v["name"] != null))
            {
                string type = (string)v["name"];
                string expect =
                    type.StartsWith("TERMINAL_UNIT_", StringComparison.Ordinal) || type == "MAP_THEATRE_PANEL"
                        || type == "BEDHEAD_UNIT_WARD" ? TuTag
                    : type == "ALARM_PANEL_AREA" ? AapTag
                    : null;   // AVSU / VIE: not terminal units and not alarm panels (see ROADMAP DT-4)
                foreach (var (typeId, rule) in rules)
                {
                    var rx = RuleFamilyFilter.Compile((string)rule["familyMatch"], out var err);
                    Assert.True(err == null, $"{typeId}: {err}");
                    bool hit = RuleFamilyFilter.Matches(rx, family, type);
                    bool want = (string)rule["tagFamily"] == expect;
                    if (hit != want)
                        bad.Add($"{typeId}: {type} {(hit ? "is" : "is not")} tagged by {(string)rule["tagFamily"]}");
                }
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }
    }
}
