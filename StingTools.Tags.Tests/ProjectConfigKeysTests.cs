using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGACC-26: <c>ProjectConfigKeys</c> agrees with the code. Every key the plugin reads or
    /// writes through the config API is known (so the loader does not call a real setting a
    /// typo), every known key is used somewhere, and every "not applied" key really is unread.
    /// </summary>
    public class ProjectConfigKeysTests
    {
        private static readonly string Root = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools");

        private static Dictionary<string, string> Sources()
        {
            var sep = Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep))
                .ToDictionary(f => f, File.ReadAllText);
        }

        private static string MethodBody(string src, string signature)
        {
            int i = src.IndexOf(signature);
            Assert.True(i >= 0, signature + " not found");
            int end = src.IndexOf("\n        }", i);
            return src.Substring(i, end - i);
        }

        [Fact]
        public void Every_key_the_code_reads_or_writes_is_known()
        {
            var src = Sources();
            var used = new HashSet<string>();
            foreach (var text in src.Values)
            {
                foreach (Match m in Regex.Matches(text, @"GetConfig(?:Value|Double|Bool|Int|String)?\s*\(\s*""([A-Za-z][A-Za-z0-9_]+)"""))
                    used.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(text, @"SetConfigValue\s*\(\s*""([A-Za-z][A-Za-z0-9_]+)"""))
                    used.Add(m.Groups[1].Value);
            }
            string tagConfig = src.First(kv => kv.Key.EndsWith(Path.DirectorySeparatorChar + "TagConfig.cs")).Value;
            string load = MethodBody(tagConfig, "public static void LoadFromFile(string path)");
            // Top-level reads only: data.TryGetValue("X", …) and TryDeserialize<…>(data, "X");
            // nested sections (SHEET_MARGINS' "Bottom" …) are not config keys.
            foreach (Match m in Regex.Matches(load, @"(?:\bdata\.TryGetValue\s*\(|TryDeserialize<[^(]*>\s*\(\s*data\s*,)\s*""([A-Z][A-Za-z0-9_]+)"""))
                used.Add(m.Groups[1].Value);
            string save = MethodBody(tagConfig, "public static bool SaveToFile(string path)");
            foreach (Match m in Regex.Matches(save, @"\[""([A-Z][A-Za-z0-9_]+)""\]\s*="))
                used.Add(m.Groups[1].Value);

            used.RemoveWhere(k => ProjectConfigKeys.KnownPrefixes.Contains(k)); // "BOQ_TENDER_" + suffix
            Assert.True(used.Count >= 100, $"Only {used.Count} keys found in the source — scan broken?");

            var unknown = used.Where(k => ProjectConfigKeys.Classify(k) == ProjectConfigKeys.KeyStatus.Unknown).OrderBy(k => k).ToList();
            Assert.True(unknown.Count == 0,
                "Read or written by the code but not in ProjectConfigKeys (the loader would call them typos): "
                + string.Join(", ", unknown));
        }

        [Fact]
        public void Every_known_key_is_used_and_every_not_applied_key_is_not()
        {
            var others = Sources().Where(kv => !kv.Key.EndsWith("ProjectConfigKeys.cs")).Select(kv => kv.Value).ToList();
            bool Quoted(string key) => others.Any(t => t.Contains("\"" + key + "\""));

            var unused = ProjectConfigKeys.Known.Where(k => !Quoted(k)).OrderBy(k => k).ToList();
            Assert.True(unused.Count == 0, "In ProjectConfigKeys.Known but used by no code — move to NotApplied or remove: "
                + string.Join(", ", unused));

            var used = ProjectConfigKeys.NotApplied.Keys.Where(Quoted).ToList();
            Assert.True(used.Count == 0, "Listed as NotApplied but the code now uses them — move to Known: "
                + string.Join(", ", used));

            Assert.Empty(ProjectConfigKeys.Known.Intersect(ProjectConfigKeys.NotApplied.Keys));
        }

        [Theory]
        [InlineData("SEQ_SCHEME", "Known")]
        [InlineData("COST_RETENTION_PCT", "Known")]        // was reported as a typo
        [InlineData("CATEGORY_VISUAL_POLICY", "Known")]    // SaveToFile writes it
        [InlineData("BOQ_TENDER_ANYTHING", "Known")]       // prefix family
        [InlineData("HANDOVER_MODE", "Known")]
        [InlineData("SEQ_LEVEL_RESET", "NotApplied")]
        [InlineData("seq_scheme", "Unknown")]              // reads are case-sensitive
        [InlineData("BOQ_TENDER_", "Unknown")]
        [InlineData("SEQ_SCHEM", "Unknown")]
        public void Classify(string key, string expected)
            => Assert.Equal(expected, ProjectConfigKeys.Classify(key).ToString());
    }
}
