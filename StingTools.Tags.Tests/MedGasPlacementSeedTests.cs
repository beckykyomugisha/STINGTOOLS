// The medical-gas placement pack must place STING's medical-gas outlet seed.
//
// WHAT WENT WRONG
//
// All fifteen rules in STING_PLACEMENT_RULES.medical-gases.json said
// CategoryFilter "Specialty Equipment". The category -> seed map sends that to
// STING_SEED_SpecialityEquipment: a penetration seal with no connectors, no MGS_*
// parameters and no MG discipline. Their VariantHints ("O2", "VACUUM,SUCTION")
// named no type of any seed, so resolution fell through to the category's first
// symbol. A ward got a fire-stopping seal where its oxygen outlet should be, and
// the MGPS network builder (which reads Plumbing Fixtures) never saw it.
//
// The rules now name their seed (PlacementRule.SeedId) and the exact seed type,
// and the engine confines resolution to that family with no first-type fallback.
// These tests hold the pack to the seed it names.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MedGasPlacementSeedTests
    {
        private const string MedGasSeed = "STING_SEED_MedGasOutlet";

        private static string Data(params string[] parts)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(new[] { d.FullName, "StingTools", "Data" }.Concat(parts).ToArray());
        }

        private static List<JObject> Rules() =>
            ((JArray)JObject.Parse(File.ReadAllText(Data("Placement", "STING_PLACEMENT_RULES.medical-gases.json")))["Rules"])
                .Cast<JObject>().ToList();

        private static JObject SeedSymbol(string seedId)
        {
            string path = Data("Seeds", seedId + ".json");
            if (!File.Exists(path)) return null;
            return JObject.Parse(File.ReadAllText(path))["symbols"]
                .Cast<JObject>().FirstOrDefault(s => (string)s["id"] == seedId);
        }

        private static JObject SeedType(JObject symbol, string typeName) =>
            symbol?["typeVariants"]?.Cast<JObject>().FirstOrDefault(v => (string)v["name"] == typeName);

        // The gas each rule's outlet must carry (MGS_GAS_TYPE_TXT), for the single-gas rules.
        public static IEnumerable<object[]> GasByRule() => new[]
        {
            new object[] { "htmgas-oxygen-bedside", "O2" },
            new object[] { "htmgas-vacuum-bedside", "VAC" },
            new object[] { "htmgas-air-bedside", "MA4" },
            new object[] { "htmgas-nitrous-theatre", "N2O" },
            new object[] { "htmgas-co2-endoscopy", "CO2" },
            new object[] { "htmgas-oxygen-icu-4outlet", "O2" },
            new object[] { "htmgas-anaesthetic-scavenging", "AGS" },
            new object[] { "htmgas-emergency-oxygen-corridor", "O2" },
            new object[] { "htmgas-oxygen-dental", "O2" },
        };

        [Fact]
        public void EveryRuleNamesASeedTypeThatSeedDeclares()
        {
            var bad = new List<string>();
            foreach (var r in Rules())
            {
                string id = (string)r["RuleId"];
                string seedId = (string)r["SeedId"];
                if (string.IsNullOrWhiteSpace(seedId)) { bad.Add($"{id}: no SeedId — the category map decides, and it picks the wrong seed"); continue; }
                var sym = SeedSymbol(seedId);
                if (sym == null) { bad.Add($"{id}: seed '{seedId}' has no Data/Seeds/{seedId}.json symbol"); continue; }
                if (!string.Equals((string)sym["category"], (string)r["CategoryFilter"], StringComparison.OrdinalIgnoreCase))
                    bad.Add($"{id}: CategoryFilter '{r["CategoryFilter"]}' but seed '{seedId}' builds '{sym["category"]}' — resolution filters by category, so nothing resolves");
                var chain = ((string)r["VariantHint"] ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (chain.Count == 0) bad.Add($"{id}: no VariantHint — which seed type?");
                foreach (var t in chain)
                    if (SeedType(sym, t) == null) bad.Add($"{id}: VariantHint '{t}' is not a type of '{seedId}'");
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        [Fact]
        public void EveryMedicalGasRulePlacesTheMedicalGasOutletSeed()
        {
            // The one rule that is not a gas service (the sluice / decon sink) places the
            // plumbing fixture seed; every other rule places a medical-gas unit.
            var wrong = Rules()
                .Where(r => (string)r["RuleId"] != "htmgas-decontamination-sink")
                .Where(r => (string)r["SeedId"] != MedGasSeed)
                .Select(r => $"{r["RuleId"]} -> {r["SeedId"] ?? "(category map)"}").ToList();
            Assert.True(wrong.Count == 0, "Medical-gas rule(s) not placing " + MedGasSeed + ":\n" + string.Join("\n", wrong));
        }

        [Theory]
        [MemberData(nameof(GasByRule))]
        public void EachSingleGasRulePlacesAnOutletOfItsGas(string ruleId, string gas)
        {
            var r = Rules().Single(x => (string)x["RuleId"] == ruleId);
            var type = SeedType(SeedSymbol(MedGasSeed), ((string)r["VariantHint"] ?? "").Split(',')[0].Trim());
            Assert.NotNull(type);
            Assert.Equal(gas, (string)type["params"]?["MGS_GAS_TYPE_TXT"]);
        }
    }
}
