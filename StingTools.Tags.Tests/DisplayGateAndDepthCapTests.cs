// Guards for the two 2026-09-23 fixes: the display-gate default, and the
// per-category depth cap that had never fired.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DisplayGateRuleTests
    {
        [Theory]
        [InlineData("TAG_PARA_STATE_1_BOOL")]
        [InlineData("TAG_PARA_STATE_3_BOOL")]
        [InlineData("TAG_PARA_STATE_10_BOOL")]
        [InlineData("tag_para_state_7_bool")]      // lookup is case-insensitive; the rule must agree
        [InlineData("TAG_PARA_STATE_11_BOOL")]     // a future tier needs no edit to the rule
        public void RecognisesEveryTierGate(string name)
            => Assert.True(DisplayGateRule.IsDisplayGate(name), name);

        [Theory]
        [InlineData("ASS_TAG_3_TXT")]              // a VALUE, not a gate
        [InlineData("TAG_PARA_STATE_BOOL")]        // no tier number
        [InlineData("TAG_PARA_STATE_3_INT")]       // wrong suffix
        [InlineData("TAG_PARA_STATE_3_BOOL_X")]    // must be anchored at the end
        [InlineData("X_TAG_PARA_STATE_3_BOOL")]    // ... and at the start
        [InlineData("ELC_PNL_DESIGNATION_NAME_TXT")]
        [InlineData("")]
        [InlineData(null)]
        public void RejectsEverythingElse(string name)
        {
            // The narrowness IS the safety property. Every identifier that is not
            // a display gate must keep failing loudly when unresolvable, because
            // those are genuine data errors — a typo, or a formula running against
            // a category whose parameters it does not have. Widening this rule
            // would silence them.
            Assert.False(DisplayGateRule.IsDisplayGate(name), name ?? "(null)");
        }
    }

    public class TokenDepthOverrideKeyTests
    {
        private static string DataFile(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Data from the test output directory");
            return Path.Combine(dir.FullName, "StingTools", "Data", name);
        }

        private static List<string> CategoryKeys()
        {
            string path = DataFile("STING_TOKEN_DEPTH_OVERRIDES.json");
            Assert.True(File.Exists(path), path);
            var root = JObject.Parse(File.ReadAllText(path));
            var cats = root["categories"] as JObject;
            Assert.True(cats != null, "STING_TOKEN_DEPTH_OVERRIDES.json has no 'categories' object");
            return cats.Properties().Select(pr => pr.Name).ToList();
        }

        [Fact]
        public void KeysAreModelCategoriesNeverTagCategories()
        {
            // Set depth now resolves a tag type's cap against the category it
            // ANNOTATES, so the file must be keyed on model categories. A key like
            // "Air Terminal Tags" would match nothing and cap nothing — silently,
            // which is how this feature spent its whole life inert: the file said
            // "Air Terminals" while the carrier's own category was "Air Terminal
            // Tags", so the lookup never hit.
            var offenders = CategoryKeys()
                .Where(k => k.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(offenders.Count == 0,
                "These keys name TAG categories, which can never match a cap lookup: "
                + string.Join(", ", offenders));
        }

        [Fact]
        public void EveryDepthIsWithinTheTierRange()
        {
            string path = DataFile("STING_TOKEN_DEPTH_OVERRIDES.json");
            var cats = (JObject)JObject.Parse(File.ReadAllText(path))["categories"];
            var bad = new List<string>();
            foreach (var pr in cats.Properties())
            {
                var d = pr.Value["depth"];
                if (d == null || d.Type == JTokenType.Null) continue;
                int v = (int)d;
                if (v < 1 || v > 10) bad.Add($"{pr.Name}={v}");
            }
            // A depth outside 1..10 is clamped at runtime, so it would not throw —
            // it would just quietly mean something other than what was authored.
            Assert.True(bad.Count == 0, "depth outside 1..10: " + string.Join(", ", bad));
        }

        [Fact]
        public void TheFileIsNotEmpty()
        {
            // A control. If the locator or the parse silently produced nothing,
            // both tests above would pass vacuously — an empty list standing in
            // for a result, which is the failure mode this repo specialises in.
            Assert.True(CategoryKeys().Count >= 5,
                "expected the shipped baseline to carry several category overrides");
        }
    }
}
