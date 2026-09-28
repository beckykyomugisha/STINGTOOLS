// Tests for BindingProfiles - opt-in parameter->category binding sets.
//
// The property that matters most is ADDITIVE-ONLY. A profile widening a
// category set can never hide a parameter that was visible before, so enabling
// one is a safe, reversible act. If merging could drop a baseline category,
// switching on a profile could silently blank a tag somewhere else entirely -
// and nothing would report it.
//
// The shipped-file tests check the real STING_BINDING_PROFILES.json, because
// the whole point is that the LPS profile carries the 93 bindings the three
// Multi-Category reuse tags need. A profile that parses but binds nothing looks
// exactly like a profile that was never enabled.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class BindingProfilesTests
    {
        private static BindingProfileLibrary Lib(params (string id, (string p, string[] c)[] entries)[] profiles)
        {
            return new BindingProfileLibrary
            {
                Profiles = profiles.Select(pr => new BindingProfile
                {
                    Id = pr.id,
                    Bindings = pr.entries.Select(e => new BindingProfileEntry
                    {
                        Param = e.p,
                        Categories = e.c.ToList()
                    }).ToList()
                }).ToList()
            };
        }

        private static Dictionary<string, List<string>> Base(params (string p, string[] c)[] rows)
            => rows.ToDictionary(r => r.p, r => r.c.ToList(), StringComparer.Ordinal);

        // ── the additive property ────────────────────────────────────────────

        [Fact]
        public void MergeNeverRemovesABaselineCategory()
        {
            var baseline = Base(("ELC_X", new[] { "Generic Models", "Electrical Equipment" }));
            var lib = Lib(("p1", new[] { ("ELC_X", new[] { "Roofs" }) }));

            var merged = BindingProfiles.Merge(baseline, lib, new[] { "p1" }, out _, out int added);

            Assert.Equal(new[] { "Generic Models", "Electrical Equipment", "Roofs" }, merged["ELC_X"]);
            Assert.Equal(1, added);
        }

        [Fact]
        public void MergeDoesNotMutateTheBaseline()
        {
            // The baseline is a cached static in SharedParamGuids. Mutating it
            // would make the profile leak into every later document in the session.
            var baseline = Base(("ELC_X", new[] { "Generic Models" }));
            var lib = Lib(("p1", new[] { ("ELC_X", new[] { "Roofs" }) }));

            BindingProfiles.Merge(baseline, lib, new[] { "p1" }, out _, out _);

            Assert.Equal(new[] { "Generic Models" }, baseline["ELC_X"]);
        }

        [Fact]
        public void NothingEnabledReturnsTheBaselineUnchanged()
        {
            var baseline = Base(("ELC_X", new[] { "Generic Models" }));
            var lib = Lib(("p1", new[] { ("ELC_X", new[] { "Roofs" }) }));

            var merged = BindingProfiles.Merge(baseline, lib, new string[0], out _, out int added);

            Assert.Equal(0, added);
            Assert.Equal(new[] { "Generic Models" }, merged["ELC_X"]);
        }

        [Fact]
        public void AProfileMayIntroduceAParameterTheBaselineNeverBound()
        {
            var merged = BindingProfiles.Merge(Base(), Lib(("p1", new[] { ("ELC_NEW", new[] { "Roofs" }) })),
                                               new[] { "p1" }, out _, out int added);

            Assert.Equal(new[] { "Roofs" }, merged["ELC_NEW"]);
            Assert.Equal(1, added);
        }

        [Fact]
        public void ACategoryAlreadyInTheBaselineIsNotCountedTwice()
        {
            var merged = BindingProfiles.Merge(Base(("ELC_X", new[] { "Roofs" })),
                                               Lib(("p1", new[] { ("ELC_X", new[] { "roofs", "Walls" }) })),
                                               new[] { "p1" }, out _, out int added);

            Assert.Equal(new[] { "Roofs", "Walls" }, merged["ELC_X"]);
            Assert.Equal(1, added);       // Walls only - "roofs" already present, case aside
        }

        [Fact]
        public void TwoProfilesTouchingOneParameterUnion()
        {
            var merged = BindingProfiles.Merge(
                Base(),
                Lib(("a", new[] { ("ELC_X", new[] { "Roofs" }) }),
                    ("b", new[] { ("ELC_X", new[] { "Walls" }) })),
                new[] { "a", "b" }, out _, out int added);

            Assert.Equal(new[] { "Roofs", "Walls" }, merged["ELC_X"]);
            Assert.Equal(2, added);
        }

        // ── failure reporting ────────────────────────────────────────────────

        [Fact]
        public void AnUnknownProfileIdIsReportedNotIgnored()
        {
            // A typo in an enabled list must not look like "nothing to add".
            BindingProfiles.Merge(Base(), Lib(("p1", new (string, string[])[0])),
                                  new[] { "p1", "typo-here" }, out var unknown, out _);

            Assert.Equal(new[] { "typo-here" }, unknown);
        }

        [Fact]
        public void BadLibraryJsonReportsTheReason()
        {
            var lib = BindingProfiles.ParseLibrary("{ not json", out string err);
            Assert.NotNull(err);
            Assert.Empty(lib.Profiles);
        }

        [Fact]
        public void BadEnabledJsonEnablesNothing()
        {
            // Safe direction: the project behaves as it did before profiles existed.
            var enabled = BindingProfiles.ParseEnabled("{ not json", out string err);
            Assert.NotNull(err);
            Assert.Empty(enabled);
        }

        [Fact]
        public void AnEmptyEnabledFileIsNotAnError()
        {
            Assert.Empty(BindingProfiles.ParseEnabled("{ \"enabled\": [] }", out string err));
            Assert.Null(err);
        }

        [Fact]
        public void EnabledIdsAreTrimmedAndDeduplicated()
        {
            var e = BindingProfiles.ParseEnabled("{ \"enabled\": [\" a \", \"a\", \"b\", \"\", null] }", out _);
            Assert.Equal(new[] { "a", "b" }, e);
        }

        [Fact]
        public void NullsAreNotAnError()
        {
            Assert.Empty(BindingProfiles.ParseEnabled(null, out _));
            var merged = BindingProfiles.Merge(null, null, null, out var unknown, out int added);
            Assert.Empty(merged);
            Assert.Empty(unknown);
            Assert.Equal(0, added);
        }

        // ── the shipped profile ──────────────────────────────────────────────

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir == null ? null : Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static BindingProfileLibrary Shipped()
        {
            string data = DataDir();
            Assert.True(data != null, "StingTools/Data not found");
            string path = Path.Combine(data, "STING_BINDING_PROFILES.json");
            Assert.True(File.Exists(path), "STING_BINDING_PROFILES.json is missing");

            var lib = BindingProfiles.ParseLibrary(File.ReadAllText(path), out string err);
            Assert.True(err == null, "shipped profile library does not parse: " + err);
            return lib;
        }

        [Fact]
        public void TheShippedLibraryParsesAndEveryProfileHasAnId()
        {
            var lib = Shipped();
            Assert.NotEmpty(lib.Profiles);
            Assert.All(lib.Profiles, p => Assert.False(string.IsNullOrWhiteSpace(p.Id)));
            Assert.Equal(lib.Profiles.Count,
                         lib.Profiles.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void TheLpsProfileCarriesTheBindingsTheReuseTagsNeed()
        {
            // Measured 2026-09-21: the three Multi-Category LPS reuse tags display
            // 13 ELC_LPS_* parameters across 15 host categories - 93 bindings, of
            // which 91 do not exist in CATEGORY_BINDINGS.csv. Without them those
            // tags place and render BLANK.
            var lps = Shipped().Profiles.SingleOrDefault(p =>
                string.Equals(p.Id, "lps-natural-components", StringComparison.OrdinalIgnoreCase));
            Assert.True(lps != null, "the lps-natural-components profile is missing");

            Assert.Equal(13, lps.Bindings.Count);
            Assert.Equal(93, lps.Bindings.Sum(b => b.Categories.Count));

            // Spot-check one host from each of the three families.
            Assert.Contains("Structural Rebar",
                lps.Bindings.Single(b => b.Param == "ELC_LPS_EARTH_TYPE_TXT").Categories);
            Assert.Contains("Roofs",
                lps.Bindings.Single(b => b.Param == "ELC_LPS_AIRTERM_TAG_TXT").Categories);
            Assert.Contains("Specialty Equipment",
                lps.Bindings.Single(b => b.Param == "ELC_LPS_COMPLIANCE_STATUS_TXT").Categories);
        }

        [Fact]
        public void EveryShippedProfileBindingNamesARealCategory()
        {
            // A category name that resolves to nothing is dropped at bind time with
            // a warning. Catching it here means a typo fails a test instead of
            // quietly costing one binding in a project nobody is watching.
            string data = DataDir();
            var known = new HashSet<string>(
                ReadCategoryNames(Path.Combine(data, "PARAMETER_REGISTRY.json")),
                StringComparer.OrdinalIgnoreCase);
            Assert.True(known.Count > 100, $"category_enum_map looks wrong: {known.Count} entries");

            var bad = Shipped().Profiles
                .SelectMany(p => p.Bindings.SelectMany(b => b.Categories.Select(c => (p.Id, b.Param, c))))
                .Where(t => !known.Contains(t.c))
                .Select(t => $"{t.Id} / {t.Param} / {t.c}")
                .ToList();

            Assert.True(bad.Count == 0, "profile bindings naming unknown categories:\n  " + string.Join("\n  ", bad));
        }

        [Fact]
        public void EveryShippedProfileParameterIsADeclaredSharedParameter()
        {
            string data = DataDir();
            string mr = Path.Combine(data, "MR_PARAMETERS.txt");
            Assert.True(File.Exists(mr), "MR_PARAMETERS.txt is missing");

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(mr))
            {
                if (!line.StartsWith("PARAM\t", StringComparison.Ordinal)) continue;
                var f = line.Split('\t');
                if (f.Length > 2 && f[2].Length > 0) declared.Add(f[2]);
            }
            Assert.True(declared.Count > 1000, $"MR_PARAMETERS.txt looks wrong: {declared.Count} params");

            var bad = Shipped().Profiles
                .SelectMany(p => p.Bindings.Select(b => (p.Id, b.Param)))
                .Where(t => !declared.Contains(t.Param))
                .Select(t => t.Id + " / " + t.Param)
                .ToList();

            Assert.True(bad.Count == 0,
                        "profile parameters absent from MR_PARAMETERS.txt:\n  " + string.Join("\n  ", bad));
        }

        private static IEnumerable<string> ReadCategoryNames(string registryPath)
        {
            // Small, dependency-free read of the "category_enum_map" keys.
            string json = File.ReadAllText(registryPath);
            int i = json.IndexOf("\"category_enum_map\"", StringComparison.Ordinal);
            if (i < 0) yield break;
            int open = json.IndexOf('{', i);
            int depth = 0;
            for (int k = open; k < json.Length; k++)
            {
                if (json[k] == '{') depth++;
                else if (json[k] == '}') { depth--; if (depth == 0) { json = json.Substring(open, k - open + 1); break; } }
            }
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"OST_"))
                yield return m.Groups[1].Value;
        }
    }
}
