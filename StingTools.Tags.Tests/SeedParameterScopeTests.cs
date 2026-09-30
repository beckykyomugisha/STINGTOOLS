// Seed parameter scope: instance vs type, and every seed key bound.
//
// Every Data/Seeds/STING_SEED_*.json declares its parameters with "isInstance",
// but ParameterDefinition bound only "instance". Newtonsoft skips an unknown
// key without a word, so every seed parameter was built as an INSTANCE
// parameter, including the 61 declared "isInstance": false. Nothing failed:
// the build was green, the JSON was valid, and the families built. Readers that
// look for those values on the type (CableScheduleBuilder's ELC_JB_IP_RATING_TXT,
// LightingGrid's ELC_LIGHTING_UF/MF_FACTOR) simply found nothing.
//
// Three guards:
//   1. a real seed deserialises to the scope it declares (and the legacy
//      "instance" spelling the Data/Symbols libraries use still binds);
//   2. no seed carries a key the POCO model does not bind, so this class of
//      bug fails here instead of in a Revit session;
//   3. a parameter the code writes per placed element is never declared type
//      (a type parameter is unreachable through the instance, and where a write
//      does land it stamps one element's value on every sibling of that type).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Symbols;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SeedParameterScopeTests
    {
        private static string DataDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(d.FullName, "StingTools", "Data");
        }

        private static IEnumerable<string> SeedFiles()
        {
            var files = Directory.GetFiles(Path.Combine(DataDir(), "Seeds"), "STING_SEED_*.json")
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.NotEmpty(files);
            return files;
        }

        private static SymbolLibrary Load(string file) =>
            JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(file));

        private static ParameterDefinition Param(string seedFile, string name)
        {
            var lib = Load(Path.Combine(DataDir(), "Seeds", seedFile));
            var p = lib.Symbols.SelectMany(s => s.Parameters ?? new List<ParameterDefinition>())
                .FirstOrDefault(x => x.Name == name);
            Assert.True(p != null, $"{seedFile} declares no parameter {name}");
            return p;
        }

        // ── 1. the declared scope is the deserialised scope ─────────────

        [Fact]
        public void ATypeParameterDeserialisesAsType()
        {
            // "isInstance": false in the shipped sprinkler seed.
            Assert.False(Param("STING_SEED_Sprinkler.json", "FLS_SPR_K_FACTOR").IsInstance);
            Assert.False(Param("STING_SEED_JunctionBox.json", "ELC_JB_IP_RATING_TXT").IsInstance);
        }

        [Fact]
        public void AnInstanceParameterDeserialisesAsInstance()
        {
            Assert.True(Param("STING_SEED_Sprinkler.json", "FLS_SPR_TYPE_TXT").IsInstance);
            Assert.True(Param("STING_SEED_Sprinkler.json", "ASS_PRODCT_COD_TXT").IsInstance);
        }

        [Fact]
        public void TheLegacyInstanceSpellingStillBinds()
        {
            // Data/Symbols/*.json libraries spell it "instance".
            var p = JsonConvert.DeserializeObject<ParameterDefinition>(
                "{\"name\":\"X\",\"type\":\"Text\",\"shared\":false,\"instance\":false}");
            Assert.False(p.IsInstance);
            Assert.True(JsonConvert.DeserializeObject<ParameterDefinition>("{\"name\":\"X\"}").IsInstance);
        }

        [Fact]
        public void EveryDeclaredScopeSurvivesDeserialisation()
        {
            // Compare the raw JSON flag against the POCO, parameter by parameter.
            var wrong = new List<string>();
            int typeParams = 0;
            foreach (var file in SeedFiles())
            {
                var raw = JObject.Parse(File.ReadAllText(file));
                var lib = Load(file);
                var rawSymbols = (JArray)raw["symbols"];
                for (int i = 0; i < rawSymbols.Count; i++)
                {
                    var rawParams = rawSymbols[i]["parameters"] as JArray;
                    if (rawParams == null) continue;
                    for (int j = 0; j < rawParams.Count; j++)
                    {
                        var declared = rawParams[j]["isInstance"];
                        if (declared == null) continue;
                        bool want = declared.Value<bool>();
                        if (!want) typeParams++;
                        var got = lib.Symbols[i].Parameters[j];
                        if (got.IsInstance != want)
                            wrong.Add($"{Path.GetFileName(file)} {got.Name}: declared {want}, bound {got.IsInstance}");
                    }
                }
            }
            Assert.True(typeParams > 0, "no seed declares a type parameter — the test proves nothing");
            Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        }

        // ── 2. no seed key is silently ignored ──────────────────────────

        /// <summary>Keys starting with "_" are the seed files' comment convention
        /// (_notes, _comment, _group, _connector_notes) and are not data.</summary>
        private static void StripComments(JToken t)
        {
            if (t is JObject o)
            {
                foreach (var p in o.Properties().Where(p => p.Name.StartsWith("_", StringComparison.Ordinal)).ToList())
                    p.Remove();
                foreach (var p in o.Properties()) StripComments(p.Value);
            }
            else if (t is JArray a)
            {
                foreach (var x in a) StripComments(x);
            }
        }

        [Fact]
        public void EverySeedKeyBindsToTheModel()
        {
            var strict = JsonSerializer.Create(new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
            });
            var failures = new List<string>();
            foreach (var file in SeedFiles())
            {
                var tok = JToken.Parse(File.ReadAllText(file));
                StripComments(tok);
                try { tok.ToObject<SymbolLibrary>(strict); }
                catch (JsonSerializationException ex)
                {
                    failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
            Assert.True(failures.Count == 0,
                "seed keys the POCO model ignores:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void TheStrictCheckCatchesAnUnboundKey()
        {
            // Prove the gate can fail: a misspelt key must throw.
            var strict = JsonSerializer.Create(new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
            });
            var tok = JToken.Parse("{\"symbols\":[{\"id\":\"S\",\"parameters\":[{\"name\":\"X\",\"isInstanse\":false}]}]}");
            Assert.Throws<JsonSerializationException>(() => tok.ToObject<SymbolLibrary>(strict));
        }

        // ── 3. per-element parameters are never type ────────────────────

        /// <summary>
        /// Parameters the code writes per placed element. Declaring any of them
        /// type makes the write unreachable (LookupParameter on an instance does
        /// not see type parameters) or stamps one element's value on every
        /// sibling of that type.
        /// </summary>
        private static readonly Dictionary<string, string> WrittenPerElement = new Dictionary<string, string>
        {
            // SwapToManufacturerCommand stamps the matched UL / EN 1366-3 system per swapped seal.
            ["PEN_CERTIFICATION_TXT"] = "SwapToManufacturerCommand",
            // FrpPenetrationPlacer writes each seal's rating; ULSystemMatcher and
            // PenetrationCoverageValidator read it back from the instance.
            ["PEN_FIRE_RATING_TXT"] = "FrpPenetrationPlacer",
            // BatchAssignCircuitsCommand writes the group key on each panel it fills.
            ["ELC_PNL_CIRCUIT_GROUP_TXT"] = "BatchAssignCircuitsCommand",
            // SeedTypeMigrator restamps the product code per instance after a type rename.
            ["ASS_PRODCT_COD_TXT"] = "SeedTypeMigrator",
            // SeedTypeSwapUpdater restamps the gas per instance after a type swap (MG-1).
            ["MGS_GAS_TYPE_TXT"] = "SeedTypeSwapUpdater",
            // BatchPanelSchedules / PanelScheduleApplyEngine write the panel's ratings on the instance.
            ["ELC_PNL_MAIN_BRK_A"] = "PanelScheduleApplyEngine",
            ["ELC_PNL_NUM_OF_WAYS_NR"] = "PanelScheduleApplyEngine",
            // The tagging pipeline writes the ISO 19650 tokens and tag per element.
            ["ASS_DISCIPLINE_COD_TXT"] = "TokenAutoPopulator",
            ["ASS_LOC_TXT"] = "TokenAutoPopulator",
            ["ASS_ZONE_TXT"] = "TokenAutoPopulator",
            ["ASS_LVL_COD_TXT"] = "TokenAutoPopulator",
            ["ASS_SYSTEM_TYPE_TXT"] = "TokenAutoPopulator",
            ["ASS_FUNC_TXT"] = "TokenAutoPopulator",
            ["ASS_SEQ_NUM_TXT"] = "TagConfig.BuildAndWriteTag",
            ["ASS_TAG_1_TXT"] = "TagConfig.BuildAndWriteTag",
            // Swap history is appended per swapped element.
            ["STING_SWAP_HISTORY_TXT"] = "SwapToManufacturerCommand",
        };

        [Fact]
        public void NoPerElementParameterIsDeclaredType()
        {
            var bad = new List<string>();
            foreach (var file in SeedFiles())
                foreach (var s in Load(file).Symbols)
                    foreach (var p in s.Parameters ?? new List<ParameterDefinition>())
                        if (!p.IsInstance && p.Name != null && WrittenPerElement.TryGetValue(p.Name, out var writer))
                            bad.Add($"{s.Id}.{p.Name} is declared type but {writer} writes it per element");
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        /// <summary>
        /// Seed TYPE parameters the project binds Instance, on purpose — every entry is a
        /// known disagreement, and a new one fails. The photometric set (SEED-1): every
        /// reader and writer works on the FamilySymbol (AssignPhotometric, LightingGrid,
        /// LuminaireRegistry, PhotometricPreflight, DIALuxExport), because photometry is a
        /// property of the luminaire type; CATEGORY_BINDINGS.csv binds them Instance on
        /// Lighting Fixtures. The binding is the outlier, and rebinding ELC_ parameters is
        /// a held decision. In the family the shared parameter is a type parameter, which
        /// is what the readers see — to be confirmed in Revit.
        /// </summary>
        private static readonly HashSet<string> TypeInFamilyInstanceInProject = new HashSet<string>(StringComparer.Ordinal)
        {
            "ELC_PHOTO_LUMENS_NR", "ELC_PHOTO_WATTS_NR", "ELC_PHOTO_FILE_PATH_TXT", "ELC_PHOTO_CCT_K", "ELC_PHOTO_CRI_NR",
        };

        [Fact]
        public void NoSeedTypeParameterIsInstanceBoundInTheProject()
        {
            // Derived rather than listed: a seed parameter the project binds as
            // Instance (CATEGORY_BINDINGS.csv) must not be a type parameter in the
            // family, or the family and the project disagree about the same GUID.
            var instanceBound = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(DataDir(), "CATEGORY_BINDINGS.csv")))
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == '﻿') continue;
                var cols = line.Split(',');
                if (cols.Length >= 3 && cols[2].Trim() == "Instance") instanceBound.Add(cols[0].Trim());
            }
            Assert.NotEmpty(instanceBound);

            var bad = new List<string>();
            var excepted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in SeedFiles())
                foreach (var s in Load(file).Symbols)
                    foreach (var p in s.Parameters ?? new List<ParameterDefinition>())
                        if (!p.IsInstance && p.Name != null && instanceBound.Contains(p.Name))
                        {
                            if (TypeInFamilyInstanceInProject.Contains(p.Name)) excepted.Add(p.Name);
                            else bad.Add($"{s.Id}.{p.Name} is declared type but bound Instance in CATEGORY_BINDINGS.csv");
                        }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
            // No stale exception: each one must still be a real disagreement.
            Assert.Equal(TypeInFamilyInstanceInProject.OrderBy(x => x), excepted.OrderBy(x => x));
        }
    }
}
