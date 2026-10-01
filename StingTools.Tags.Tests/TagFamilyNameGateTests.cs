// TAGFAM-7. Three tie-in tag families were built and shipped as
// "STING - Tie-In Point Tag (Duct — HVAC) Tag" — the creator carried the verbose
// declared name as its suffix, so "{prefix} - {suffix} Tag" doubled the "Tag" —
// while the MEP tag config and PerFamilyTierMap declared the name without it.
// Nothing failed: the tier-plan lookup missed and fell to a category plan that
// happened to be the same default. These gates make the next such drift a red test.
//
// The creator is Revit-bound, so its names come from DrawingTypeTagFamilyGateTests,
// which reads TagFamilyCreatorCommand.cs as source text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagFamilyNameGateTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "TagFamilies")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data/TagFamilies");
            return dir.FullName;
        }

        private static List<string> CsvDeclaredNames()
        {
            var names = new List<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot(), "StingTools", "Data"), "STING_TAG_CONFIG_v5_0_*.csv"))
                names.AddRange(TagConfigDeclarations.Parse(File.ReadLines(f)).Select(d => d.FamilyName));
            return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        }

        private static List<string> DeclaredNames()
            => CsvDeclaredNames().Concat(PerFamilyTierMap.KnownFamilyNames).Distinct().ToList();

        private static List<string> ShippedFamilyNames()
            => Directory.GetFiles(Path.Combine(RepoRoot(), "StingTools", "Data", "TagFamilies"), "*.rfa")
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(n => !Regex.IsMatch(n, @"\.\d{4}$"))   // Revit backups are not shipped families
                        .ToList();

        [Fact]
        public void The_inputs_are_not_empty()
        {
            Assert.True(DrawingTypeTagFamilyGateTests.BuiltInCreatorFamilyNames().Count > 150);
            Assert.True(CsvDeclaredNames().Count > 100, "CSV declarations not parsed");
            Assert.True(PerFamilyTierMap.KnownFamilyNames.Count() > 50, "tier map empty");
            Assert.True(ShippedFamilyNames().Count > 100, "shipped .rfa set empty");
        }

        /// <summary>
        /// Known disagreements, each tracked on the ROADMAP. Not TAGFAM-7's three: the
        /// creator builds "Specialty Equipment Tag Asset Tag" where the config declares
        /// it without the last " Tag" (TAGFAM-10). Remove an entry when it is fixed; the
        /// test below fails if an entry stops disagreeing, so the list cannot go stale.
        /// </summary>
        private static readonly string[] KnownNameDisagreements =
        {
            "STING - Specialty Equipment Tag Asset Tag",
            "STING - Specialty Equipment Tag General Tag",
        };

        private static List<string> NameDisagreements()
        {
            // Correspondence is found on the lenient key (case, spacing, a trailing " Tag",
            // forbidden characters); agreement is then judged on the strict one, which
            // forgives only "/" read as "-".
            var declared = DeclaredNames();
            var bad = new List<string>();
            foreach (var g in DrawingTypeTagFamilyGateTests.BuiltInCreatorFamilyNames())
            {
                string key = TagCategoryNameForms.NormaliseKey(g);
                var matches = declared.Where(d => TagCategoryNameForms.NormaliseKey(d) == key).ToList();
                if (matches.Count == 0) continue;
                if (!matches.Any(d => TagFamilyNameAliases.SameFamily(d, g))) bad.Add(g);
            }
            return bad;
        }

        [Fact]
        public void Every_generated_name_equals_its_declaration_or_has_none()
        {
            var bad = NameDisagreements().Except(KnownNameDisagreements, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(bad.Count == 0, "Creator names that differ from their declaration:\n" + string.Join("\n", bad));
        }

        [Fact]
        public void The_known_disagreements_still_disagree()
        {
            var bad = NameDisagreements();
            var fixedNow = KnownNameDisagreements.Where(k => !bad.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            Assert.True(fixedNow.Count == 0, "No longer disagree, remove from KnownNameDisagreements:\n" + string.Join("\n", fixedNow));
        }

        [Fact]
        public void No_generated_name_doubles_the_Tag_after_a_parenthesis()
        {
            var doubled = DrawingTypeTagFamilyGateTests.BuiltInCreatorFamilyNames()
                .Where(n => Regex.IsMatch(n, @"\bTag\b.*\)\s+Tag$")).ToList();
            Assert.True(doubled.Count == 0, "Generated names with a doubled \"Tag\":\n" + string.Join("\n", doubled));
        }

        [Fact]
        public void Every_shipped_family_is_a_generated_or_declared_name()
        {
            var known = DrawingTypeTagFamilyGateTests.CreatorFamilyNames().Concat(DeclaredNames()).ToList();
            var stray = ShippedFamilyNames().Where(s => !known.Any(k => TagFamilyNameAliases.SameFamily(k, s))).ToList();
            Assert.True(stray.Count == 0, "Shipped .rfa files nothing generates or declares:\n" + string.Join("\n", stray));
        }

        [Fact]
        public void No_legacy_name_ships()
        {
            var shipped = ShippedFamilyNames();
            var legacy = TagFamilyNameAliases.LegacyFamilyNames.Keys
                .Where(l => shipped.Any(s => TagFamilyNameAliases.SameFamily(s, l))).ToList();
            Assert.True(legacy.Count == 0, "Legacy family names still shipped:\n" + string.Join("\n", legacy));
        }

        [Fact]
        public void Every_legacy_name_maps_to_a_canonical_name_that_is_built_and_shipped()
        {
            Assert.NotEmpty(TagFamilyNameAliases.LegacyFamilyNames);
            var generated = DrawingTypeTagFamilyGateTests.BuiltInCreatorFamilyNames();
            var shipped = ShippedFamilyNames();
            foreach (var kv in TagFamilyNameAliases.LegacyFamilyNames)
            {
                Assert.False(TagFamilyNameAliases.SameFamily(kv.Key, kv.Value), $"alias maps '{kv.Key}' to itself");
                Assert.True(generated.Any(g => TagFamilyNameAliases.SameFamily(g, kv.Value)),
                    $"'{kv.Value}' (canonical for '{kv.Key}') is not a name the creator generates");
                Assert.True(shipped.Any(s => TagFamilyNameAliases.SameFamily(s, kv.Value)),
                    $"'{kv.Value}' (canonical for '{kv.Key}') has no shipped .rfa");
                Assert.Null(TagFamilyNameAliases.CanonicalForLegacy(kv.Value));
            }
        }

        [Fact]
        public void The_tier_map_resolves_legacy_and_dash_spellings_to_the_family_plan()
        {
            foreach (var kv in TagFamilyNameAliases.LegacyFamilyNames)
            {
                var canonical = PerFamilyTierMap.Resolve(kv.Value, null);
                Assert.NotSame(PerFamilyTierMap.DefaultPlan, canonical);
                Assert.Same(canonical, PerFamilyTierMap.Resolve(kv.Key, null));
            }
            var slash = PerFamilyTierMap.Resolve("STING - Tie-In Point Tag (Conduit — Electrical LV/ELV)", null);
            Assert.NotSame(PerFamilyTierMap.DefaultPlan, slash);
            Assert.Same(slash, PerFamilyTierMap.Resolve("STING - Tie-In Point Tag (Conduit — Electrical LV-ELV)", null));
            Assert.Same(PerFamilyTierMap.DefaultPlan, PerFamilyTierMap.Resolve("STING - No Such Family", null));
        }

        [Theory]
        [InlineData("Duct", false, "Rename")]
        [InlineData("Duct", true, "BothPresent")]
        public void A_legacy_family_is_renamed_only_when_the_canonical_one_is_absent(string _, bool canonicalToo, string expected)
        {
            const string canonical = "STING - Tie-In Point Tag (Duct — HVAC)";
            var project = new List<string> { "STING - Door Tag", "sting - tie-in point tag (duct — hvac) tag" };
            if (canonicalToo) project.Add(canonical);
            var action = TagFamilyNameAliases.Decide(canonical, project, out string legacy);
            Assert.Equal(expected, action.ToString());
            Assert.Equal("sting - tie-in point tag (duct — hvac) tag", legacy);
        }

        [Fact]
        public void Nothing_to_do_without_a_legacy_family()
        {
            Assert.Equal(TagFamilyNameAliases.LegacyAction.None,
                TagFamilyNameAliases.Decide("STING - Tie-In Point Tag (Duct — HVAC)", new[] { "STING - Door Tag" }, out _));
            Assert.Equal(TagFamilyNameAliases.LegacyAction.None,
                TagFamilyNameAliases.Decide("STING - Door Tag", new[] { "STING - Tie-In Point Tag (Duct — HVAC) Tag" }, out _));
        }
    }
}
