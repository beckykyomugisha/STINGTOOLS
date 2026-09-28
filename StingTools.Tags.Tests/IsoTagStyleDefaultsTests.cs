// TAGISO-1: the tag style matrix is built at 2, 2.5, 3 and 3.5 mm; only 2.5 and 3.5
// are ISO 3098 heights. Defaults — the style catalogue's discipline defaults and
// pre-created variants, every rule preset, the scale tiers — must use ISO sizes.
// The 2 / 3 mm rows stay available for a project that picks them explicitly.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core.Drawing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class IsoTagStyleDefaultsTests
    {
        private static string Data(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", file))) dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data/" + file);
            return Path.Combine(dir.FullName, "StingTools", "Data", file);
        }

        private static string SizeOf(string typeName) => Regex.Match(typeName ?? "", @"^[\d.]+").Value;

        [Theory]
        [InlineData("2.5", true)] [InlineData("3.5", true)] [InlineData("1.8", true)] [InlineData("5mm", true)]
        [InlineData("2", false)] [InlineData("3", false)] [InlineData("1.5", false)] [InlineData("", false)]
        public void Iso_heights_are_recognised(string size, bool iso) => Assert.Equal(iso, IsoTagText.IsIso(size));

        [Theory]
        [InlineData("2", "2.5")] [InlineData("3", "3.5")] [InlineData("1.5", "2.5")]
        [InlineData("2.5", "2.5")] [InlineData("3.5", "3.5")] [InlineData("7", "3.5")] [InlineData(null, "2.5")]
        public void Defaults_move_up_to_the_next_iso_size_in_the_matrix(string size, string iso)
            => Assert.Equal(iso, IsoTagText.ToIso(size));

        [Theory]
        [InlineData("2BOLD_RED", "2.5BOLD_RED")]
        [InlineData("3NOM_BLACK", "3.5NOM_BLACK")]
        [InlineData("2.5NOM_BLACK", "2.5NOM_BLACK")]
        [InlineData("2_NOM_BLACK_None_T1", "2.5_NOM_BLACK_None_T1")]
        [InlineData("Standard", "Standard")]
        public void Type_names_keep_everything_but_the_size(string name, string iso)
            => Assert.Equal(iso, IsoTagText.ToIsoTypeName(name));

        [Fact]
        public void The_style_catalogue_defaults_and_pre_created_variants_are_iso()
        {
            var root = JObject.Parse(File.ReadAllText(Data("tag_style_catalogue.json")));
            Assert.True(IsoTagText.IsIso((string)root["default_size"]));
            var defaults = ((JObject)root["defaults_per_discipline"]).Properties().ToList();
            Assert.True(defaults.Count >= 8);
            foreach (var d in defaults) Assert.True(IsoTagText.IsIso((string)d.Value["size"]), $"default {d.Name}");
            var variants = (JArray)root["standard_variants"];
            Assert.NotEmpty(variants);
            foreach (var v in variants) Assert.True(IsoTagText.IsIso((string)v["size"]), v.ToString());
            // The non-ISO rows are still offered, just never by default.
            var sizes = root["sizes"].Select(t => (string)t).ToList();
            Assert.Equal(new[] { "2.5", "3.5" }, sizes.Take(2));
            Assert.Contains("2", sizes);
        }

        [Fact]
        public void Every_rule_preset_uses_iso_sizes_and_types_the_catalog_lists()
        {
            var root = JObject.Parse(File.ReadAllText(Data("TAG_STYLE_RULES.json")));
            var catalog = root["tag_type_catalog"]["types"].Select(t => (string)t["name"]).ToHashSet();
            // The catalog is exactly the matrix the TAG_{size}{style}_{colour}_BOOL rows exist for.
            var catSizes = root["tag_type_catalog"]["types"].Select(t => ((double)t["size"]).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Distinct().OrderBy(x => x);
            Assert.Equal(new[] { "2", "2.5", "3", "3.5" }, catSizes);
            int n = 0;
            foreach (var p in ((JObject)root["presets"]).Properties())
            {
                var types = new[] { (string)p.Value["default_type"] }
                    .Concat(((JArray)p.Value["rules"] ?? new JArray()).Select(r => (string)r["tag_type"]))
                    .Where(t => !string.IsNullOrEmpty(t));
                foreach (var t in types)
                {
                    n++;
                    Assert.True(IsoTagText.IsIso(SizeOf(t)), $"{p.Name}: {t}");
                    Assert.Contains(t, catalog);
                }
            }
            Assert.True(n > 50, $"only {n} preset types checked");
        }

        [Fact]
        public void The_discipline_rule_preset_matches_the_style_catalogue()
        {
            // The catalogue is the single source for discipline tag styles; the rule preset
            // used to disagree with it for E, S, FP and G.
            var cat = (JObject)JObject.Parse(File.ReadAllText(Data("tag_style_catalogue.json")))["defaults_per_discipline"];
            var preset = JObject.Parse(File.ReadAllText(Data("TAG_STYLE_RULES.json")))["presets"]["Discipline"];
            int n = 0;
            foreach (var r in (JArray)preset["rules"])
            {
                string disc = (string)r["conditions"]["ASS_DISCIPLINE_COD_TXT"];
                if (disc == null || cat[disc] == null) continue;
                var c = cat[disc];
                Assert.Equal($"{c["size"]}{c["style"]}_{c["colour"]}", (string)r["tag_type"]);
                n++;
            }
            Assert.Equal(8, n);
        }

        [Fact]
        public void Scale_tiers_do_not_shrink_the_text()
        {
            var root = JObject.Parse(File.ReadAllText(Data("SCALE_TIERS.json")));
            var tiers = (JArray)root["tiers"];
            Assert.NotEmpty(tiers);
            foreach (var t in tiers) Assert.Equal(IsoTagText.DefaultSize, (string)t["text_size_mm"]);
        }
    }
}
