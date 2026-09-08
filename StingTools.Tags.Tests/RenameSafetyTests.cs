using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Renaming a type must not break anything, on any project.
    ///
    /// <para>A Revit rename is safe at the ElementId level — instances, hosting, joins
    /// and geometry all keep pointing at the same type. What breaks is everything that
    /// matched the type by its NAME, and the ISO 22014 shape deliberately moves English
    /// words out of names, so this is not a theoretical risk.</para>
    ///
    /// <para>Surveyed, the shipped surfaces that key on a type name are:</para>
    /// <list type="bullet">
    /// <item><b>19 of the 290 AEC view filters</b> use <c>ALL_MODEL_TYPE_NAME</c>. One of
    /// them — "Arch: Plasterboard Ceilings", matching on "Plasterboard" — genuinely
    /// stopped catching the catalogue's plasterboard ceiling when its subtype read
    /// "Gypsum9". Fixed by putting the word back in the SUBTYPE field, which is where
    /// human-readable text belongs; the Type field stays the code.</item>
    /// <item><b>Supplier-unit conversion</b> narrows by type pattern — but resolves
    /// material FIRST, and the catalogue's materials carry the substance, so the type
    /// route is redundant there. That redundancy is asserted below rather than assumed.</item>
    /// <item><b>Stage routing</b> matches whole words via PatternMatch, so a compacted
    /// PascalCase name simply matches fewer patterns, never the wrong one.</item>
    /// <item><b>PROD resolution</b> reads the declared code, so it is immune by
    /// construction.</item>
    /// </list>
    ///
    /// <para>Writing the first version of this survey, a naive substring matcher reported
    /// a phantom break: the pattern <c>gate</c> matched "Corru<b>gate</b>d". That is the
    /// #863 defect, reproduced in the analysis rather than in the plugin — the shipped
    /// StageMapper has used whole-word matching since. The lesson is in the test below
    /// that pins it.</para>
    /// </summary>
    public class RenameSafetyTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static JObject Data(string file)
            => JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), file)));

        private sealed class Cat
        {
            [JsonProperty("wallTypes")] public List<BaselineHostType> WallTypes;
            [JsonProperty("floorTypes")] public List<BaselineHostType> FloorTypes;
            [JsonProperty("roofTypes")] public List<BaselineHostType> RoofTypes;
            [JsonProperty("ceilingTypes")] public List<BaselineHostType> CeilingTypes;
        }

        private static IEnumerable<(string Category, string OstCategory, BaselineHostType Type)> Catalogue()
        {
            var c = JsonConvert.DeserializeObject<Cat>(
                File.ReadAllText(Path.Combine(DataDir(), "STING_PROJECT_BASELINE.json")));
            foreach (var t in c.WallTypes) yield return ("Walls", "OST_Walls", t);
            foreach (var t in c.FloorTypes) yield return ("Floors", "OST_Floors", t);
            foreach (var t in c.RoofTypes) yield return ("Roofs", "OST_Roofs", t);
            foreach (var t in c.CeilingTypes) yield return ("Ceilings", "OST_Ceilings", t);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1. Shipped view filters still catch the catalogue
        // ══════════════════════════════════════════════════════════════════════

        private static bool RuleApplies(JToken rule, string name)
        {
            string op = (rule?["op"]?.ToString() ?? "").ToLowerInvariant();
            var val = rule?["value"];
            if (val == null || val.Type == JTokenType.Null)
                return op == "hasvalue" && !string.IsNullOrEmpty(name);

            string v = val.ToString();
            switch (op)
            {
                case "contains":   return name.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0;
                case "beginswith": return name.StartsWith(v, StringComparison.OrdinalIgnoreCase);
                case "endswith":   return name.EndsWith(v, StringComparison.OrdinalIgnoreCase);
                case "equals":     return string.Equals(name, v, StringComparison.OrdinalIgnoreCase);
                default:           return false;
            }
        }

        /// <summary>
        /// Every shipped filter that names a build the catalogue also describes must still
        /// catch it. This is the cross-file drift that a rename creates and that nothing
        /// else would notice: the filter keeps working, silently selecting nothing.
        /// </summary>
        [Fact]
        public void No_Shipped_View_Filter_Loses_The_Build_It_Names()
        {
            var filters = Data("STING_AEC_FILTERS.json").Properties()
                .Select(p => p.Value).OfType<JArray>().FirstOrDefault()
                ?? throw new InvalidOperationException("no filter array");

            var byName = new[]
            {
                // filter substring   →   the catalogue subtype word that must carry it
                ("Plasterboard", "Ceilings"),
                ("Screed",       "Floors"),
                ("Tile",         "Floors"),
            };

            var misses = new List<string>();
            foreach (var (word, category) in byName)
            {
                var f = filters.FirstOrDefault(x =>
                    string.Equals(x["rule"]?["param"]?.ToString(), "ALL_MODEL_TYPE_NAME", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x["rule"]?["value"]?.ToString(), word, StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;   // filter retired; nothing to protect

                bool caught = Catalogue()
                    .Where(c => c.Category == category)
                    .Any(c => RuleApplies(f["rule"], c.Type.Name));

                if (!caught)
                    misses.Add($"'{f["name"]}' (TYPE NAME contains '{word}') now catches no {category} "
                             + "type in the house standard");
            }

            Assert.True(misses.Count == 0,
                "A shipped view filter names a build the catalogue describes, and no longer selects it. "
                + "A filter that matches nothing does not fail — it just shows an empty override, which "
                + "is why this has to be checked rather than noticed:\n  " + string.Join("\n  ", misses));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. The material route carries the commodity, not the type name
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The ISO shape has no spaces, so a supplier pattern like <c>"clay tile"</c> stops
        /// matching a type called <c>ClayTile</c>. That is survivable only because
        /// conversion resolves MATERIAL before TYPE, and the catalogue's materials carry
        /// the substance — <c>Roofing - Clay Tile</c>.
        ///
        /// <para>Asserted rather than assumed: if a catalogue roof ever stops naming its
        /// covering material, the commodity silently falls back to the generic and the
        /// order quantity changes by whatever the two coverages differ by.</para>
        /// </summary>
        [Theory]
        [InlineData("Roofs", "clay tile")]
        [InlineData("Roofs", "stone coated")]
        [InlineData("Roofs", "galvanis")]
        public void A_Roof_Covering_Is_Identifiable_From_Its_MATERIAL_Not_Its_Name(
            string category, string materialNeedle)
        {
            bool found = Catalogue()
                .Where(c => c.Category == category)
                .Any(c => (c.Type.Layers ?? new List<BaselineLayer>())
                          .Any(l => (l.Material ?? "").IndexOf(materialNeedle, StringComparison.OrdinalIgnoreCase) >= 0));

            Assert.True(found,
                $"No {category} type in the house standard names a '{materialNeedle}' material. The ISO "
                + "type name cannot carry it — spaces are not allowed — so the MATERIAL is the only "
                + "route left to the right commodity.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. The phantom break, pinned
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The survey that produced this class first reported the corrugated-sheet roof
        /// losing an "external" stage. It had not: the pattern <c>gate</c> was matching
        /// "Corru<b>gate</b>d" in a naive substring matcher written for the analysis. The
        /// shipped StageMapper has matched whole words since #863.
        ///
        /// <para>Kept because the same wrong conclusion is one careless helper away, and
        /// because it demonstrates the #863 fix doing its job on a name nobody wrote it
        /// for.</para>
        /// </summary>
        [Fact]
        public void A_Corrugated_Roof_Is_Not_A_Gate()
        {
            Assert.True("Corrugated Sheet Roof".IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0,
                "the substring really is there — that is the point");

            Assert.False(StingTools.Core.MaterialSchedule.PatternMatch.Contains(
                "STING RF - Corrugated Sheet Roof - IT4 on Timber", "gate"));
            Assert.False(StingTools.Core.MaterialSchedule.PatternMatch.Contains(
                "PLNS_RSH_IT4-OnTimberPurlins", "gate"));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  4. PROD resolution is immune by construction
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The one surface a rename cannot break, and the reason the ISO Type field is the
        /// PROD code: the answer travels IN the name, so no pattern has to survive it.
        /// </summary>
        [Fact]
        public void A_Conforming_Rename_Cannot_Change_The_Product_Code()
        {
            var codes = new HashSet<string>(
                File.ReadAllLines(Path.Combine(DataDir(), "STING_PROD_CODES.csv")).Skip(1)
                    .Select(l => (l ?? "").Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .Select(l => l.Split(',')[0].Trim())
                    .Where(c => c.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (category, _, t) in Catalogue())
            {
                string declared = ProdNameCode.Extract(t.Name, codes);
                Assert.True(declared != null,
                    $"'{t.Name}' declares no known product code, so its classification depends on a "
                    + "pattern matching its name — and a rename can then change it.");

                // Rename the SUBTYPE freely; the code must not move.
                string renamed = ProdNameCode.Compose("PLNS", declared, "SomethingElse", "Entirely");
                Assert.Equal(declared, ProdNameCode.Extract(renamed, codes));
            }
        }
    }
}
