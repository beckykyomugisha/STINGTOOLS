using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The house standard, checked against the plugin that has to read it.
    ///
    /// <para><b>A standard the tool cannot read is the failure this catalogue exists to
    /// prevent.</b> Its whole purpose is to replace type names like
    /// <c>Exterior_CreamWhite_230</c> and <c>Generic - 225mm</c> — real names from
    /// delivered models, neither of which can ever be measured — with names that resolve.
    /// If the catalogue itself contained one, it would be prescribing the disease.</para>
    ///
    /// <para>Writing it, this gate caught three: <c>RC Steps - Terrazzo</c> (no Floors
    /// rule for steps), <c>Stone Coated Tile Roof</c> (the tile rule read "Roof Tile" but
    /// not "Tile Roof") and <c>Plastered Soffit</c> (a rendered soffit is not
    /// plasterboard). All three were gaps in the RULES, not mistakes in the names — which
    /// is the useful direction for a cross-check to fail in.</para>
    /// </summary>
    public class BaselineCatalogueTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private sealed class Catalogue
        {
            [JsonProperty("materials")] public List<BaselineMaterial> Materials;
            [JsonProperty("wallTypes")] public List<BaselineHostType> WallTypes;
            [JsonProperty("floorTypes")] public List<BaselineHostType> FloorTypes;
            [JsonProperty("roofTypes")] public List<BaselineHostType> RoofTypes;
            [JsonProperty("ceilingTypes")] public List<BaselineHostType> CeilingTypes;
        }

        private static Catalogue Load()
        {
            var c = JsonConvert.DeserializeObject<Catalogue>(
                File.ReadAllText(Path.Combine(DataDir(), "STING_PROJECT_BASELINE.json")));
            Assert.True(c?.Materials != null && c.Materials.Count > 20,
                "STING_PROJECT_BASELINE.json did not parse into a catalogue");
            return c;
        }

        private static List<(string Pattern, string ProdCode)> Rules(string category)
        {
            var outList = new List<(string, string)>();
            foreach (string raw in File.ReadAllLines(Path.Combine(DataDir(), "STING_PROD_CODES.csv")).Skip(1))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var c = line.Split(',');
                if (c.Length < 3) continue;
                if (!string.Equals(c[1].Trim(), category, StringComparison.OrdinalIgnoreCase)) continue;
                outList.Add((c[2].Trim().ToUpperInvariant(), c[0].Trim()));
            }
            return outList;
        }

        public static IEnumerable<object[]> HostTypeGroups()
        {
            var c = Load();
            foreach (var t in c.WallTypes) yield return new object[] { "Walls", t.Name };
            foreach (var t in c.FloorTypes) yield return new object[] { "Floors", t.Name };
            foreach (var t in c.RoofTypes) yield return new object[] { "Roofs", t.Name };
            foreach (var t in c.CeilingTypes) yield return new object[] { "Ceilings", t.Name };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Every name in the standard resolves
        // ══════════════════════════════════════════════════════════════════════

        [Theory]
        [MemberData(nameof(HostTypeGroups))]
        public void Every_Catalogue_Type_Resolves_To_A_Specific_Prod_Code(string category, string typeName)
        {
            // The family name is the catch-all one Revit gives every element of this
            // kind, so the TYPE name is doing all the work — same as in a real model.
            string family = category == "Walls" ? "Basic Wall"
                          : category == "Roofs" ? "Basic Roof"
                          : category == "Floors" ? "Floor" : "Compound Ceiling";

            ProdResolver.Resolve(family, typeName, category, null, Rules(category), null, out string source);

            Assert.True(ProdResolver.IsSpecific(source),
                $"The house standard prescribes '{typeName}', and the plugin resolves it to the "
                + $"{category} category default. A standard that names a type the tool cannot read "
                + "is prescribing the problem it exists to fix — add the rule, or rename the type.");
        }

        [Fact]
        public void Every_Catalogue_Material_Is_Classifiable_Or_Deliberately_Not()
        {
            // A material with no suffix is not automatically wrong — Terrazzo had none
            // until a rule was added for it — but a catalogue where MOST materials fail
            // to classify would mean the naming shape and the override table disagree.
            var rules = MaterialProdOverrideRules.Parse(
                File.ReadAllLines(Path.Combine(DataDir(), "STING_MATERIAL_PROD_OVERRIDES.csv")));

            var unclassified = Load().Materials
                .Where(m => MaterialProdOverrideRules.ResolveSuffix(rules, m.Name, "Walls") == null)
                .Select(m => m.Name).ToList();

            Assert.True(unclassified.Count == 0,
                "Catalogue materials the override table cannot classify. Either the name does not "
                + "lead with its material family (rule M1), or the table is missing that family:\n  "
                + string.Join("\n  ", unclassified));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Internal integrity
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Every_Layer_Names_A_Material_The_Catalogue_Declares()
        {
            // The minter creates materials first, then types that reference them by name.
            // A layer naming a material the list omits produces a type with a missing
            // material — which is precisely the "no material decided" state that made
            // 631 m2 of roof unmeasurable on a delivered model.
            var c = Load();
            var declared = new HashSet<string>(c.Materials.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);

            var orphans = new[] { c.WallTypes, c.FloorTypes, c.RoofTypes, c.CeilingTypes }
                .SelectMany(g => g)
                .SelectMany(t => (t.Layers ?? new List<BaselineLayer>()).Select(l => new { t.Name, l.Material }))
                .Where(x => !string.IsNullOrWhiteSpace(x.Material) && !declared.Contains(x.Material))
                .Select(x => $"{x.Name} -> '{x.Material}'")
                .Distinct().ToList();

            Assert.True(orphans.Count == 0,
                "Layers naming a material the catalogue does not declare:\n  " + string.Join("\n  ", orphans));
        }

        [Fact]
        public void Every_Structural_Type_Flags_Its_Core()
        {
            // Rule T1. Without a Structure layer the element is named after its thickest
            // layer of any function, which is how 11 rendered masonry walls came to read
            // as gypsum. A catalogue that shipped an unflagged core would teach the defect.
            var c = Load();
            var unflagged = new[] { c.WallTypes, c.FloorTypes, c.RoofTypes }
                .SelectMany(g => g)
                .Where(t => (t.Layers?.Count ?? 0) > 1)
                .Where(t => !t.Layers.Any(l => string.Equals(l.Function, "Structure", StringComparison.OrdinalIgnoreCase)))
                .Select(t => t.Name).ToList();

            Assert.True(unflagged.Count == 0,
                "Multi-layer types with no layer flagged Structure — the core must say it is the core:\n  "
                + string.Join("\n  ", unflagged));
        }

        [Fact]
        public void The_Catalogue_Covers_The_Builds_A_Real_Model_Used()
        {
            // Anchored to what two delivered models actually contained, so the standard
            // cannot shrink to a set that is easy to satisfy and useless in practice.
            var c = Load();
            var all = new[] { c.WallTypes, c.FloorTypes, c.RoofTypes, c.CeilingTypes }
                .SelectMany(g => g).Select(t => t.Name).ToList();

            foreach (string must in new[]
            {
                "Blockwork 200",       // the Uganda default wall
                "Clay Brick",          // 48 elements on one model
                "Stud Partition",      // the 97mm partition
                "Ground Slab",         // was entirely absent from the starter set
                "Hollow Pot",          // the East African economy span
                "Corrugated Sheet",    // IT4, the default roof
                "RC Flat Roof",
            })
                Assert.True(all.Any(n => n.IndexOf(must, StringComparison.OrdinalIgnoreCase) >= 0),
                    $"The catalogue no longer covers '{must}', which a delivered model used.");
        }

        [Fact]
        public void Tiled_Finishes_Survive_As_LAYERS()
        {
            // The reason the baseline exists: an export found 10 wall/floor types with ONE
            // finish layer between them, so no tiling could be measured at all. At least
            // one floor and one wall must carry a tile layer, or the catalogue has
            // regressed to the state it was written to fix.
            var c = Load();
            Assert.Contains(c.FloorTypes, t => t.HasTiledFinish);
            Assert.Contains(c.WallTypes, t => t.HasTiledFinish);
        }
    }
}
