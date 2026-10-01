// TAGFAM-9: new tag families stop receiving the 128 TAG_{size}{style}_{colour}_BOOL
// style switches. Measured in Revit 2025: ~0.78 s per FamilyManager.AddParameter, and
// a headless audit of all 211 shipped tag families found no switch associated with any
// family element. Style is the type plus TAG_STYLE_CODE_TXT; the switches are opt-in
// through tag_style_catalogue.json "family_style_switches".
//
// The shipped catalogue is parsed through TagStyleFamilyParams — the code the plugin
// runs — so an unbound or mistyped field fails here, not as a silent no-op in Revit.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagStyleFamilyParamsTests
    {
        private static string Repo(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "tag_style_catalogue.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repo root");
            return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        }

        private static JObject Catalogue()
            => JObject.Parse(File.ReadAllText(Repo("StingTools", "Data", "tag_style_catalogue.json")));

        private static List<string> Dim(JObject root, string key)
            => root[key].Select(t => (string)t).ToList();

        // The appearance params StyleParams appends after the switches (names only matter
        // here in that none of them is a switch).
        private static readonly string[] Appearance =
        {
            "TAG_BOX_COLOR_R_INT", "TAG_BOX_COLOR_G_INT", "TAG_BOX_COLOR_B_INT",
            "TAG_BOX_VISIBLE_BOOL", "TAG_BOX_STYLE_TXT",
            "TAG_LEADER_COLOR_R_INT", "TAG_LEADER_COLOR_G_INT", "TAG_LEADER_COLOR_B_INT",
            "TAG_SCALE_TIER_AUTO_BOOL", "TAG_DEPTH_TIER_INT",
        };

        private static List<string> ShippedSwitches(out List<string> rejected)
        {
            var root = Catalogue();
            rejected = new List<string>();
            return TagStyleFamilyParams.ParseSwitches(root, Dim(root, "sizes"), Dim(root, "styles"),
                Dim(root, "colours"), rejected);
        }

        // ── the catalogue field ──────────────────────────────────────────

        [Fact]
        public void The_catalogue_declares_family_style_switches_as_a_list()
        {
            var token = Catalogue()[TagStyleFamilyParams.CatalogueKey];
            Assert.NotNull(token);
            Assert.Equal(JTokenType.Array, token.Type);
        }

        [Fact]
        public void The_shipped_field_binds_through_the_real_parser_with_nothing_rejected()
        {
            var switches = ShippedSwitches(out var rejected);
            Assert.Empty(rejected);
            var listed = (JArray)Catalogue()[TagStyleFamilyParams.CatalogueKey];
            // Every listed code becomes exactly one switch ("*" expands to all of them).
            if (!listed.Any(t => (string)t == TagStyleFamilyParams.AllSwitches))
                Assert.Equal(listed.Count, switches.Count);
        }

        [Fact]
        public void Every_catalogue_entry_is_a_size_style_colour_combination()
        {
            var root = Catalogue();
            foreach (var entry in (JArray)root[TagStyleFamilyParams.CatalogueKey])
            {
                Assert.Equal(JTokenType.String, entry.Type);
                string s = (string)entry;
                if (s == TagStyleFamilyParams.AllSwitches) continue;
                Assert.True(TagStyleFamilyParams.ParseCode(s, Dim(root, "sizes"), Dim(root, "styles"),
                    Dim(root, "colours")) != null, $"'{s}' is not a size x style x colour code");
            }
        }

        [Fact]
        public void New_families_get_no_switch_the_catalogue_does_not_list()
        {
            var optedIn = ShippedSwitches(out _);
            var styleParams = TagStyleFamilyParams.Compose(optedIn, Appearance);
            var stray = styleParams.Where(TagStyleFamilyParams.LooksLikeSwitch)
                                   .Where(p => !optedIn.Contains(p)).ToList();
            Assert.Empty(stray);
            // Default catalogue: none at all, so the 128 cost nothing.
            if (((JArray)Catalogue()[TagStyleFamilyParams.CatalogueKey]).Count == 0)
                Assert.DoesNotContain(styleParams, TagStyleFamilyParams.LooksLikeSwitch);
        }

        [Fact]
        public void The_style_code_param_leads_the_style_params()
        {
            var styleParams = TagStyleFamilyParams.Compose(ShippedSwitches(out _), Appearance);
            Assert.Equal("TAG_STYLE_CODE_TXT", styleParams[0]);
            Assert.Equal(styleParams.Count, styleParams.Distinct().Count());
            foreach (var a in Appearance) Assert.Contains(a, styleParams);
        }

        // ── the plugin wires it ──────────────────────────────────────────

        [Fact]
        public void TagFamilyConfig_StyleParams_is_composed_from_the_catalogue_not_the_full_matrix()
        {
            string src = File.ReadAllText(Repo("StingTools", "Tags", "TagFamilyCreatorCommand.cs"));
            var m = Regex.Match(src, @"public static string\[\] StyleParams\s*\{(?<body>.*?)\n        \}",
                RegexOptions.Singleline);
            Assert.True(m.Success, "StyleParams property not found");
            string body = m.Groups["body"].Value;
            Assert.DoesNotContain("AllTagStyleParams", body);
            Assert.Contains("TagStyleFamilyParams.Compose(", body);
            Assert.Contains("TagStyleCatalogue.FamilyStyleSwitchParams", body);
        }

        [Fact]
        public void The_catalogue_loader_reads_the_field_through_the_tested_parser()
        {
            string src = File.ReadAllText(Repo("StingTools", "Core", "TagStyleCatalogue.cs"));
            Assert.Contains("TagStyleFamilyParams.ParseSwitches(", src);
            Assert.Contains("FamilyStyleSwitchParams", src);
        }

        [Fact]
        public void The_style_code_constant_matches_ParamRegistry()
        {
            string src = File.ReadAllText(Repo("StingTools", "Core", "ParamRegistry.cs"));
            Assert.Contains($"TAG_STYLE_CODE = \"{TagStyleFamilyParams.StyleCodeParam}\"", src);
        }

        [Fact]
        public void The_style_code_param_is_declared_text_in_MR_PARAMETERS()
        {
            var line = File.ReadLines(Repo("StingTools", "Data", "MR_PARAMETERS.txt"))
                .Select(l => l.Split('\t'))
                .FirstOrDefault(c => c.Length > 3 && c[0] == "PARAM" && c[2] == TagStyleFamilyParams.StyleCodeParam);
            Assert.NotNull(line);
            Assert.Equal("TEXT", line[3]);
        }

        [Theory]
        [InlineData("Tags/TagTypeVariantWriter.cs")]
        [InlineData("Tags/TagStyleEngine.cs")]
        public void Style_writers_record_the_style_code(string file)
        {
            string src = File.ReadAllText(Repo(new[] { "StingTools" }.Concat(file.Split('/')).ToArray()));
            Assert.Contains("ParamRegistry.TAG_STYLE_CODE", src);
            Assert.Contains("TagStyleFamilyParams.StyleCode(", src);
        }

        [Fact]
        public void Conformance_check_accepts_the_style_code()
        {
            string src = File.ReadAllText(Repo("StingTools", "Tags", "FamilyConformanceCheckCommand.cs"));
            Assert.Contains("TagStyleFamilyParams.ConformancePoints(", src);
            Assert.Contains("ParamRegistry.TAG_STYLE_CODE", src);
        }

        // ── the parser ───────────────────────────────────────────────────

        private static readonly string[] Sizes = { "2.5", "3.5", "2", "3" };
        private static readonly string[] Styles = { "NOM", "BOLD", "ITALIC", "BOLDITALIC" };
        private static readonly string[] Colours = { "BLACK", "BLUE", "GREEN", "RED", "ORANGE", "PURPLE", "GREY", "WHITE" };

        [Theory]
        [InlineData("2.5BOLD_BLUE", "2.5BOLD_BLUE")]
        [InlineData("2BOLD_RED", "2BOLD_RED")]
        [InlineData("3.5BOLDITALIC_GREY", "3.5BOLDITALIC_GREY")]
        [InlineData("TAG_3NOM_BLACK_BOOL", "3NOM_BLACK")]
        [InlineData("2.5bold_blue", "2.5BOLD_BLUE")]
        [InlineData("2.5BOLD", null)]
        [InlineData("4NOM_BLACK", null)]
        [InlineData("2.5HEAVY_BLACK", null)]
        [InlineData("2.5NOM_PINK", null)]
        [InlineData("2.5_NOM_BLACK", null)]
        [InlineData("", null)]
        public void Codes_parse_against_the_dimensions(string entry, string expected)
            => Assert.Equal(expected, TagStyleFamilyParams.ParseCode(entry, Sizes, Styles, Colours));

        [Fact]
        public void Opted_in_codes_become_switches_and_bad_entries_are_reported()
        {
            var root = JObject.Parse(@"{ ""family_style_switches"": [""2.5BOLD_BLUE"", ""TAG_3.5NOM_RED_BOOL"", ""2.5BOLD_BLUE"", ""bogus"", 7] }");
            var rejected = new List<string>();
            var got = TagStyleFamilyParams.ParseSwitches(root, Sizes, Styles, Colours, rejected);
            Assert.Equal(new[] { "TAG_2.5BOLD_BLUE_BOOL", "TAG_3.5NOM_RED_BOOL" }, got);
            Assert.Equal(new[] { "bogus", "7" }, rejected);
        }

        [Fact]
        public void Star_opts_in_the_whole_matrix()
        {
            var got = TagStyleFamilyParams.ParseSwitches(JObject.Parse(@"{ ""family_style_switches"": [""*""] }"),
                Sizes, Styles, Colours, new List<string>());
            Assert.Equal(128, got.Count);
            Assert.All(got, p => Assert.True(TagStyleFamilyParams.LooksLikeSwitch(p), p));
            Assert.Contains("TAG_2.5NOM_BLACK_BOOL", got);
        }

        [Fact]
        public void An_absent_key_means_none_and_a_non_list_is_rejected()
        {
            var rejected = new List<string>();
            Assert.Empty(TagStyleFamilyParams.ParseSwitches(new JObject(), Sizes, Styles, Colours, rejected));
            Assert.Empty(rejected);
            Assert.Empty(TagStyleFamilyParams.ParseSwitches(JObject.Parse(@"{ ""family_style_switches"": ""2.5NOM_BLACK"" }"),
                Sizes, Styles, Colours, rejected));
            Assert.Single(rejected);
        }

        // ── conformance scoring ──────────────────────────────────────────

        private static readonly string[] Sample = { "TAG_2.5NOM_BLACK_BOOL", "TAG_3BOLD_BLUE_BOOL" };

        [Fact]
        public void Style_code_alone_passes_the_style_check()
        {
            var missing = new List<string>();
            Assert.Equal(10, TagStyleFamilyParams.ConformancePoints(true, Sample, _ => false, missing));
            Assert.Empty(missing);
        }

        [Fact]
        public void Sampled_switches_alone_still_pass_the_style_check()
        {
            var missing = new List<string>();
            Assert.Equal(10, TagStyleFamilyParams.ConformancePoints(false, Sample, _ => true, missing));
            Assert.Empty(missing);
        }

        [Fact]
        public void Neither_fails_the_style_check_and_names_what_is_missing()
        {
            var missing = new List<string>();
            Assert.Equal(5, TagStyleFamilyParams.ConformancePoints(false, Sample, n => n == Sample[0], missing));
            Assert.Contains(missing, m => m.Contains("TAG_STYLE_CODE_TXT"));
            Assert.Contains(missing, m => m.Contains(Sample[1]));

            missing.Clear();
            Assert.Equal(0, TagStyleFamilyParams.ConformancePoints(false, Sample, _ => false, missing));
            Assert.Equal(3, missing.Count);
        }
    }
}
