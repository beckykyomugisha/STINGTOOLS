// Tag colour scheme names travel from style packs and buttons to TagStyleEngine,
// and an unknown one used to be ignored without a word. These hold the vocabulary,
// the engine's tables and the shipped packs to one list.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagColorSchemeNamesTests
    {
        private static string Repo(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()))) dir = dir.Parent;
            Assert.True(dir != null, "Could not locate " + string.Join("/", parts));
            return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        }

        [Theory]
        [InlineData("Monochrome", "Mono")]
        [InlineData("mono", "Mono")]
        [InlineData("STING Discipline", "Discipline")]
        [InlineData("Zone", "Zone")]
        [InlineData("MedicalGas", null)]
        [InlineData("", null)]
        public void Names_and_aliases_resolve(string name, string canonical)
            => Assert.Equal(canonical, TagColorSchemeNames.Resolve(name));

        [Fact]
        public void The_engine_tables_carry_exactly_the_listed_schemes()
        {
            string src = File.ReadAllText(Repo("StingTools", "Tags", "TagStyleEngine.cs"));
            var discipline = Regex.Matches(src, @"d\[""([^""]+)""\]\s*=\s*new ColorScheme\b").Select(m => m.Groups[1].Value).ToList();
            var variable   = Regex.Matches(src, @"d\[""([^""]+)""\]\s*=\s*new VariableColorScheme\b").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(TagColorSchemeNames.Discipline.OrderBy(x => x), discipline.OrderBy(x => x));
            Assert.Equal(TagColorSchemeNames.Variable.OrderBy(x => x), variable.OrderBy(x => x));
        }

        // Pack schemes that no engine table carries yet (ROADMAP TAGSCHEME-1). This list may
        // only shrink: a new unknown name fails the test instead of silently doing nothing.
        private static readonly string[] NotYetImplemented =
            { "RAG Status", "MedicalGas", "Pressure", "ElectricalSupply", "FireRating", "Radiation", "AntiLigature", "WaterSafety" };

        [Fact]
        public void Every_style_pack_tag_scheme_resolves_or_is_a_known_gap()
        {
            var root = JObject.Parse(File.ReadAllText(Repo("StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            var names = root["stylePacks"].Select(p => (string)p["tagColorScheme"]).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            Assert.True(names.Count > 10, $"only {names.Count} pack tag schemes found");
            var bad = names.Where(n => TagColorSchemeNames.Resolve(n) == null && !NotYetImplemented.Contains(n)).Distinct().ToList();
            Assert.True(bad.Count == 0, "Unknown tag colour schemes: " + string.Join(", ", bad));
            // A gap that has been implemented must leave the list.
            foreach (var gap in NotYetImplemented) Assert.Null(TagColorSchemeNames.Resolve(gap));
        }
    }
}
