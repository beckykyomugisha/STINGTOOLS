using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Placement;
using Xunit;

namespace StingTools.Placement.Tests
{
    /// <summary>
    /// Runtime verification of the shipped placement data.
    ///
    /// The rule packs and the category map were previously only "build verified"
    /// — valid JSON, compiling code — with actual deserialisation happening for
    /// the first time inside Revit. These tests exercise the REAL Newtonsoft path
    /// (PlacementRuleSet + StringOrCsvArrayConverter) against the real files, so
    /// a malformed ApplicableStandards array or a broken v2 category-map field
    /// fails here rather than at a user's first run.
    /// </summary>
    public class RulePackDataTests
    {
        // ── Locating the shipped data ────────────────────────────────

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "StingTools", "Data", "Placement");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "StingTools/Data/Placement not found walking up from " + AppContext.BaseDirectory);
        }

        private static string[] PackFiles() =>
            Directory.GetFiles(DataDir(), "STING_PLACEMENT_RULES*.json").OrderBy(p => p).ToArray();

        private static List<(string pack, PlacementRule rule)> AllRules()
        {
            var all = new List<(string, PlacementRule)>();
            foreach (var f in PackFiles())
            {
                var set = JsonConvert.DeserializeObject<PlacementRuleSet>(File.ReadAllText(f));
                Assert.NotNull(set);
                foreach (var r in set.Rules ?? new List<PlacementRule>())
                    all.Add((Path.GetFileName(f), r));
            }
            return all;
        }

        // ── Deserialisation ──────────────────────────────────────────

        [Fact]
        public void EveryRulePackDeserialisesThroughTheRealConverter()
        {
            var files = PackFiles();
            Assert.True(files.Length >= 16, $"expected >=16 rule packs, found {files.Length}");

            foreach (var f in files)
            {
                var set = JsonConvert.DeserializeObject<PlacementRuleSet>(File.ReadAllText(f));
                Assert.True(set != null, $"{Path.GetFileName(f)} deserialised to null");
                Assert.True(set.Rules != null && set.Rules.Count > 0,
                            $"{Path.GetFileName(f)} produced no rules — the pack would silently load empty");
            }
        }

        [Fact]
        public void TheWholeLibraryLoads()
        {
            var all = AllRules();
            // Floor rather than an exact count so adding rules doesn't fail the
            // suite; the point is that no pack silently drops to zero.
            Assert.True(all.Count >= 440, $"expected >=440 rules across all packs, got {all.Count}");
            Assert.All(all, t => Assert.False(string.IsNullOrWhiteSpace(t.rule.RuleId),
                                              $"{t.pack} has a rule with no RuleId"));
        }

        [Fact]
        public void ApplicableStandardsArraysBecomeSemicolonJoinedStrings()
        {
            // StringOrCsvArrayConverter turns ["BS7671","BS6701"] into "BS7671;BS6701".
            // If it ever regresses, Newtonsoft throws on StartArray and the whole
            // pack loads zero rules — the historical "only 147 rules load" bug.
            var tagged = AllRules().Where(t => !string.IsNullOrEmpty(t.rule.ApplicableStandards)).ToList();
            Assert.True(tagged.Count >= 440, $"expected almost every rule tagged, got {tagged.Count}");
            Assert.All(tagged, t => Assert.DoesNotContain("[", t.rule.ApplicableStandards));
        }

        // ── Standards-coverage invariants ────────────────────────────

        [Fact]
        public void NoRuleCarriesStandardRefWithoutApplicableStandards()
        {
            var offenders = AllRules()
                .Where(t => !string.IsNullOrWhiteSpace(t.rule.StandardRef)
                            && string.IsNullOrWhiteSpace(t.rule.ApplicableStandards))
                .Select(t => $"{t.pack}::{t.rule.RuleId}")
                .ToList();
            Assert.True(offenders.Count == 0,
                        "rules citing standards only as free text: " + string.Join(", ", offenders));
        }

        [Fact]
        public void EveryRuleIsTaggedSoNothingBypassesTheStandardsGate()
        {
            // An untagged rule passes the gate unconditionally. That was the
            // documented default for 117 rules; they are now all tagged.
            var untagged = AllRules()
                .Where(t => string.IsNullOrWhiteSpace(t.rule.ApplicableStandards)
                            && string.IsNullOrWhiteSpace(t.rule.StandardRef))
                .Select(t => $"{t.pack}::{t.rule.RuleId}")
                .ToList();
            Assert.True(untagged.Count == 0,
                        $"{untagged.Count} rule(s) bypass the standards gate: " +
                        string.Join(", ", untagged.Take(10)));
        }

        // ── Token hygiene ────────────────────────────────────────────

        [Fact]
        public void StandardsTokensAreCleanIdentifiersNotGluedProse()
        {
            // Regression against the original backfill, which built tokens by
            // stripping whitespace out of free text and produced entries like
            // "ADA§604.4(432-483mmseatheight)" and "BS8233daylight".
            var glued = new Regex(@"[a-z]{4,}");
            var bad = new List<string>();

            foreach (var (pack, rule) in AllRules())
            {
                foreach (var tok in (rule.ApplicableStandards ?? "").Split(';'))
                {
                    var t = tok.Trim();
                    if (t.Length == 0) continue;
                    if (t.Length > 24) bad.Add($"{pack}::{rule.RuleId}: '{t}' (too long)");
                    else if (t.Contains("(") && t != "Building Regs M4(3)")
                        bad.Add($"{pack}::{rule.RuleId}: '{t}' (parenthetical prose)");
                    else if (glued.IsMatch(t) && !StartsWithNamedFamily(t))
                        bad.Add($"{pack}::{rule.RuleId}: '{t}' (glued prose)");
                }
            }
            Assert.True(bad.Count == 0, "unclean standards tokens:\n  " + string.Join("\n  ", bad.Take(15)));
        }

        // Standards whose names legitimately contain words.
        private static bool StartsWithNamedFamily(string t) =>
            t.StartsWith("Approved Doc", StringComparison.Ordinal)
            || t.StartsWith("CIBSE ", StringComparison.Ordinal)
            || t.StartsWith("Equality Act", StringComparison.Ordinal)
            || t.StartsWith("Building Regs", StringComparison.Ordinal)
            || t.StartsWith("NHS Estates", StringComparison.Ordinal)
            || t.StartsWith("WRAS", StringComparison.Ordinal)
            || t.StartsWith("Uniclass", StringComparison.Ordinal)
            || t.Contains(" section ");

        // ── Gate behaviour against the real packs ────────────────────

        [Fact]
        public void ElectricalRulesSurviveABs7671Profile()
        {
            var electrical = AllRules()
                .Where(t => t.pack.Contains("electrical", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.NotEmpty(electrical);

            int kept = electrical.Count(t =>
                StandardsTokenMatcher.Matches(t.rule.ApplicableStandards, new[] { "BS 7671" }));
            Assert.True(kept > 0,
                        "no rule in the electrical packs matches a BS 7671 profile — the gate would drop them all");
        }

        [Fact]
        public void EmergencyLightingRulesDoNotLeakIntoATaskLightingProfile()
        {
            // The BS EN 1838 false positive, checked against the real data: a
            // profile activating only BS EN 12464 (task lighting) must not keep
            // emergency-lighting rules.
            var emergency = AllRules()
                .Where(t => (t.rule.ApplicableStandards ?? "").Contains("BS EN 1838"))
                .ToList();
            Assert.NotEmpty(emergency);

            foreach (var (pack, rule) in emergency)
            {
                // Only assert on rules whose tag set is purely emergency-lighting;
                // a rule legitimately citing both is not a leak.
                if ((rule.ApplicableStandards ?? "").Contains("12464")) continue;
                Assert.False(
                    StandardsTokenMatcher.Matches(rule.ApplicableStandards, new[] { "BS EN 12464" }),
                    $"{pack}::{rule.RuleId} ('{rule.ApplicableStandards}') leaked into a BS EN 12464 profile");
            }
        }

        // ── Category map v2 contract ─────────────────────────────────

        [Fact]
        public void CategoryToSeedMapDeclaresTheV2PlaceabilityContract()
        {
            var path = Path.Combine(DataDir(), "STING_CATEGORY_TO_SEED_MAP.json");
            Assert.True(File.Exists(path), path + " not found");

            var root = JObject.Parse(File.ReadAllText(path));
            var map = root["map"] as JArray;
            Assert.True(map != null && map.Count > 0, "category map has no entries");

            int nonPlaceable = 0;
            foreach (var e in map)
            {
                var cat = (string)e["category"];
                Assert.False(string.IsNullOrWhiteSpace(cat), "map entry with no category");

                var placeableTok = e["placeable"];
                bool placeable = placeableTok == null || placeableTok.Type == JTokenType.Null
                                 || placeableTok.Value<bool>();
                if (!placeable)
                {
                    nonPlaceable++;
                    // A disabled checkbox with no reason is worse than no checkbox.
                    Assert.False(string.IsNullOrWhiteSpace((string)e["reason"]),
                                 $"non-placeable category '{cat}' has no reason");
                }
            }
            Assert.True(nonPlaceable >= 5,
                        $"expected the routing-output/host categories to be non-placeable, found {nonPlaceable}");
        }

        [Fact]
        public void RoutingOutputCategoriesAreNotPlaceable()
        {
            var path = Path.Combine(DataDir(), "STING_CATEGORY_TO_SEED_MAP.json");
            var map = (JArray)JObject.Parse(File.ReadAllText(path))["map"];

            foreach (var expected in new[] { "Conduits", "Pipes", "Ducts", "Cable Trays", "Stairs" })
            {
                var entry = map.FirstOrDefault(e =>
                    string.Equals((string)e["category"], expected, StringComparison.OrdinalIgnoreCase));
                Assert.True(entry != null, $"'{expected}' missing from the category map");
                Assert.False(entry["placeable"] == null || entry["placeable"].Value<bool>(),
                             $"'{expected}' is a routing output / host element and must not be placeable");
            }
        }
    }
}
