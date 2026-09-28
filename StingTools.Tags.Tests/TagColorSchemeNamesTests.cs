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
        [InlineData("MedicalGas", "MedicalGas")]
        [InlineData("RAG Status", "Status")]
        [InlineData("NoSuchScheme", null)]
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

        [Fact]
        public void Every_style_pack_tag_scheme_resolves()
        {
            var root = JObject.Parse(File.ReadAllText(Repo("StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            var names = root["stylePacks"].Select(p => (string)p["tagColorScheme"]).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            Assert.True(names.Count > 10, $"only {names.Count} pack tag schemes found");
            var bad = names.Where(n => TagColorSchemeNames.Resolve(n) == null).Distinct().ToList();
            Assert.True(bad.Count == 0, "Unknown tag colour schemes: " + string.Join(", ", bad));
        }

        [Theory]
        [InlineData("60", "60")]
        [InlineData("60.0", "60")]
        [InlineData(" 120 min", "120")]
        [InlineData("POS", "POS")]
        [InlineData("LIFE-SAF", "LIFE-SAF")]
        [InlineData("2.5BOLD", "2.5BOLD")]
        [InlineData(null, "")]
        public void Values_are_normalised_before_matching(string raw, string expected)
            => Assert.Equal(expected, TagColorSchemeNames.NormaliseValue(raw));

        // Every tag style a scheme picks must be a row the tag families carry: a colour
        // outside the catalogue (YELLOW was one) switches nothing.
        [Fact]
        public void Scheme_tag_styles_use_catalogue_styles_and_colours()
        {
            var cat = JObject.Parse(File.ReadAllText(Repo("StingTools", "Data", "tag_style_catalogue.json")));
            var colours = cat["colours"].Select(t => (string)t).ToHashSet();
            var styles = cat["styles"].Select(t => (string)t).ToHashSet();
            string src = File.ReadAllText(Repo("StingTools", "Tags", "TagStyleEngine.cs"));
            var used = Regex.Matches(src, @"\(""[^""]+"",\s*""([A-Z]+)"",\s*""([A-Z]+)""\)")
                .Select(m => (style: m.Groups[1].Value, colour: m.Groups[2].Value)).ToList();
            Assert.True(used.Count >= 30, $"only {used.Count} parameter-scheme styles found");
            var bad = used.Where(u => !styles.Contains(u.style) || !colours.Contains(u.colour)).Distinct().ToList();
            Assert.True(bad.Count == 0, "Not in the tag style catalogue: " + string.Join(", ", bad));
        }

        // A parameter scheme reading a parameter the project cannot bind would colour nothing.
        [Fact]
        public void Parameter_schemes_read_shared_parameters()
        {
            string src = File.ReadAllText(Repo("StingTools", "Tags", "TagStyleEngine.cs"));
            string shared = File.ReadAllText(Repo("StingTools", "Data", "MR_PARAMETERS.txt"));
            var names = Regex.Matches(src, @"ParameterName\s*=\s*""([A-Z0-9_]+)""").Select(m => m.Groups[1].Value).ToList();
            Assert.True(names.Count >= 5, $"only {names.Count} parameter schemes found");
            var missing = names.Where(n => !Regex.IsMatch(shared, @"\t" + n + @"\t")).ToList();
            Assert.True(missing.Count == 0, "Not in MR_PARAMETERS.txt: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_System_scheme_colours_every_runtime_system()
        {
            string defaults = File.ReadAllText(Repo("StingTools", "Core", "TagConfig.Defaults.cs"));
            int a = defaults.IndexOf("DefaultSysMap()", StringComparison.Ordinal), b = defaults.IndexOf("DefaultProdMap()", StringComparison.Ordinal);
            var runtime = Regex.Matches(defaults.Substring(a, b - a), @"\{\s*""([A-Z]+)"",\s*new List<string>").Select(m => m.Groups[1].Value).ToList();
            string src = File.ReadAllText(Repo("StingTools", "Tags", "TagStyleEngine.cs"));
            int s1 = src.IndexOf("d[\"System\"] = new VariableColorScheme", StringComparison.Ordinal);
            int s2 = src.IndexOf("ValueStyles", s1, StringComparison.Ordinal);
            var coloured = Regex.Matches(src.Substring(s1, s2 - s1), @"\{\s*""([A-Z]+)"",\s*new Color").Select(m => m.Groups[1].Value).ToList();
            Assert.True(runtime.Count >= 40, "runtime systems not read");
            Assert.Equal(runtime.OrderBy(x => x), coloured.OrderBy(x => x));
        }
    }
}
