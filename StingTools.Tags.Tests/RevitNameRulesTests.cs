// DT-R11-C: Revit refuses an element name containing \ : { } [ ] | ; < > ? ` ~.
//
// A real Revit 2025 run of the Drawing Self-Test (check "d. AEC filters") had
// ParameterFilterElement.Create refuse 'STING - Struct: Concrete' and its peers:
// 263 of the 287 shipped filter names carry a colon, so in a real model almost
// none of the corporate filter library could be created — AecFilters_Create,
// the view-style-pack lazy-create and the managed templates all hit it.
//
// The data keeps its human names; RevitNameRules.Sanitize maps them at the Revit
// boundary, for creation and for every lookup. These tests run the shipped data
// through it: every result must be legal, and no two filters may collapse onto
// one Revit name (the second would find the first and silently take its place).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class RevitNameRulesTests
    {
        // Independent of the class under test, so a hole in RevitNameRules.Prohibited
        // cannot hide itself.
        private static readonly char[] RevitRefuses = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        private static bool Legal(string s) => s.IndexOfAny(RevitRefuses) < 0 && !s.Any(char.IsControl);

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_AEC_FILTERS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return dir.FullName;
        }

        private static JObject Load(string file)
            => JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Data", file)));

        private static List<string> ShippedFilterNames()
            => Load("STING_AEC_FILTERS.json")["filters"].Select(f => (string)f["name"]).ToList();

        /// <summary>Every filter name a shipped style pack refers to (filters[] and filterRules[]).</summary>
        private static List<string> ShippedPackFilterNames()
        {
            var names = new List<string>();
            foreach (var p in Load("STING_VIEW_STYLE_PACKS.json")["stylePacks"])
                foreach (var key in new[] { "filters", "filterRules" })
                    if (p[key] is JArray arr)
                        names.AddRange(arr.Select(r => (string)r["name"]).Where(n => !string.IsNullOrWhiteSpace(n)));
            return names;
        }

        [Fact]
        public void The_shipped_library_does_hold_names_Revit_refuses()
        {
            // The witness: if the data is ever renamed to legal names this is moot, and
            // says so, rather than the tests below passing for a reason nobody intended.
            var raw = ShippedFilterNames();
            Assert.Equal(287, raw.Count);
            Assert.True(raw.Count(n => !Legal(n)) > 200,
                $"Expected most shipped filter names to need sanitising; {raw.Count(n => !Legal(n))} do.");
        }

        [Fact]
        public void Every_shipped_filter_reaches_Revit_under_a_legal_name()
        {
            var illegal = ShippedFilterNames()
                .Select(n => (Raw: n, Revit: RevitNameRules.Sanitize(n)))
                .Where(x => string.IsNullOrWhiteSpace(x.Revit) || !Legal(x.Revit))
                .Select(x => $"'{x.Raw}' -> '{x.Revit}'")
                .ToList();
            Assert.True(illegal.Count == 0,
                $"{illegal.Count} filter name(s) still illegal in Revit:\n  " + string.Join("\n  ", illegal.Take(20)));
        }

        [Fact]
        public void No_two_shipped_filters_collapse_onto_one_Revit_name()
        {
            var clashes = ShippedFilterNames()
                .GroupBy(n => RevitNameRules.Sanitize(n), StringComparer.OrdinalIgnoreCase)   // Revit compares names case-insensitively
                .Where(g => g.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(g => $"'{g.Key}' <- " + string.Join(" | ", g.Distinct()))
                .ToList();
            Assert.True(clashes.Count == 0, "Sanitised filter names collide:\n  " + string.Join("\n  ", clashes));
        }

        [Fact]
        public void Every_filter_a_shipped_pack_names_reaches_Revit_under_a_legal_name()
        {
            var bad = ShippedPackFilterNames().Distinct()
                .Where(n => !Legal(RevitNameRules.Sanitize(n)))
                .ToList();
            Assert.Empty(bad);
        }

        [Fact]
        public void A_pack_filter_name_and_its_library_definition_meet_on_one_Revit_name()
        {
            // The pack names the filter as the JSON writes it; the factory creates it under
            // the sanitised name; the applier looks it up again by the pack's name. All three
            // must land on the same element.
            var library = new HashSet<string>(ShippedFilterNames().Select(RevitNameRules.Sanitize), StringComparer.OrdinalIgnoreCase);
            var packNames = ShippedPackFilterNames().Distinct().ToList();
            var orphans = packNames.Where(n => !library.Contains(RevitNameRules.Sanitize(n))).ToList();
            // A pack may name a project filter the library does not ship; it must still be
            // looked up under the same rule. What must not happen is a library filter that
            // the pack spells one way and the factory names another.
            foreach (var n in packNames.Except(orphans))
                Assert.Contains(RevitNameRules.Candidates(n), c => library.Contains(c));
        }

        [Theory]
        [InlineData("STING - Struct: Concrete", "STING - Struct - Concrete")]
        [InlineData("STING - Phase: Demolished", "STING - Phase - Demolished")]
        [InlineData("STING - HVAC: Supply Air", "STING - HVAC - Supply Air")]
        [InlineData("STING - Elec: Small Power", "STING - Elec - Small Power")]
        [InlineData("STING - Struct: Rebar >25mm", "STING - Struct - Rebar gt 25mm")]
        [InlineData("STING - Elec: High Voltage (>1kV)", "STING - Elec - High Voltage (gt 1kV)")]
        [InlineData("STING - Coord: LOD < 300", "STING - Coord - LOD lt 300")]
        [InlineData("A [x] {y} | z; w\\v ?`~", "A (x) (y) - z - w - v")]
        public void Sanitize_maps_prohibited_characters_deterministically(string raw, string expected)
        {
            Assert.Equal(expected, RevitNameRules.Sanitize(raw));
            Assert.True(Legal(RevitNameRules.Sanitize(raw)));
            Assert.Equal(expected, RevitNameRules.Sanitize(RevitNameRules.Sanitize(raw)));   // idempotent
        }

        [Theory]
        [InlineData("STING - Arch  Walls")]          // double space kept: a legal name is never touched
        [InlineData("STING_MAT_CLASS_Concrete")]
        [InlineData("sting-sys-SA")]
        [InlineData("")]
        [InlineData(null)]
        public void A_legal_name_is_returned_unchanged(string name)
            => Assert.Equal(name, RevitNameRules.Sanitize(name));

        [Fact]
        public void Matching_accepts_the_sanitised_name_and_a_legacy_raw_name()
        {
            const string data = "STING - Struct: Concrete";
            Assert.True(RevitNameRules.Matches("STING - Struct - Concrete", data));
            Assert.True(RevitNameRules.Matches("sting - struct - concrete", data));   // Revit: case-insensitive
            Assert.True(RevitNameRules.Matches("STING - Struct: Concrete", data));   // a legacy element, if one exists
            Assert.False(RevitNameRules.Matches("STING - Struct - Steel", data));
            Assert.False(RevitNameRules.Matches(null, data));
            Assert.Equal(new[] { "STING - Struct - Concrete", data }, RevitNameRules.Candidates(data));
            Assert.Equal(new[] { "sting-sys-SA" }, RevitNameRules.Candidates("sting-sys-SA"));
        }

        [Fact]
        public void A_pack_lists_both_spellings_of_a_filter_it_edits()
        {
            // The worksharing pre-check and filterEnabled:false compare view filters with
            // these names, so the Revit name must be among them.
            var names = ProductionEdgeDecisions.PackFilterNames(new[] { "STING - Struct: Concrete" }, new[] { "Steel: Grade" });
            Assert.Contains("STING - Struct - Concrete", names);
            Assert.Contains("STING - Struct: Concrete", names);
            Assert.Contains("STING_MAT_CLASS_Steel - Grade", names);
            Assert.All(names.Where(n => n.StartsWith("STING_MAT", StringComparison.Ordinal)), n => Assert.True(Legal(n)));
        }
    }
}
