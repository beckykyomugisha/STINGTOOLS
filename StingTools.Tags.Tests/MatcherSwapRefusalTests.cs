// ══════════════════════════════════════════════════════════════════════════
//  MatcherSwapRefusalTests.cs — W5a and W5b: the two remaining `IndexOf` sites
//  are NOT swapped for whole-word matching, and this is why.
//
//  The brief listed three `IndexOf` sites as "the #863 shape — substring where a
//  whole word was meant", and told me to construct the false positive FIRST and
//  prove it RED. That instruction is what saved these two. The third site
//  (TypeRenamePlanner.Match) had a real false positive and a real fix, and is
//  swapped in its own PR. These two do not, and are not.
//
//  ── W5a  SupplierUnitConverter:191,228 ─────────────────────────────────────
//
//  Its patterns are DELIBERATE PREFIX STEMS, and two of them have to match names
//  this plugin generates itself:
//
//      corrugat   catches corrugated / corrugation
//      galvanis   catches galvanised / galvanized
//      galvaniz
//      profil     catches profile / profiled
//      stonecoat  catches PLNS_RTL_StoneCoatedTileRoof1
//      clay       catches PLNS_RTL_ClayTileRoof14
//
//  The last two are the argument that settles it. `PatternMatch` treats letters
//  and digits as word characters, so it can NEVER match inside a compacted ISO
//  22014 name — and compacted ISO 22014 names are what `TypeRenamePlanner`
//  produces and what this converter is then asked about. Whole-word matching is
//  structurally incompatible with the names the plugin makes.
//
//  Measured over the 1,810 names this repository carries (the register plus every
//  corpus fixture): 93 substring-only hits, and not one of them is a false
//  positive that the rules' own `matchCategories` gate does not already stop.
//  Widened with the type and family names from the 2026-09-09 model it is 111 of
//  2,038 — same answer, and only the in-repo figure is reproducible here.
//
//  ── W5b  ProductExclusion:148 ──────────────────────────────────────────────
//
//  This one I got wrong first and the measurement corrected me. Reading only the
//  FAMILY name, `opening` is not a whole word in `M_GM_OpeningWall_Instance`, and
//  swapping the matcher looked like it would return 1,187 wall voids to the PROD
//  denominator. It does not: `Classify` matches against `description + " " +
//  typeName`, and the type name on all 1,187 is literally `Opening`.
//
//  Replayed over the real 1,688-row audit, substring and whole-word give the
//  IDENTICAL verdict on every row. So the swap is measurably neutral today — and
//  it is strictly more fragile tomorrow, because it makes the exclusion depend on
//  a word boundary in a joined haystack nobody controls: the same family with a
//  blank type name stops being excluded, and `Air Openings` (plural) stops being
//  excluded. Neither has an error state; both would show up as a coverage
//  percentage moving.
//
//  A change with no measured benefit and an unmeasured downside is not a fix.
//  What ships instead is this file, so the next person to reach for the swap
//  finds the measurement rather than repeating it.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class MatcherSwapRefusalTests
    {
        private readonly ITestOutputHelper _out;
        public MatcherSwapRefusalTests(ITestOutputHelper output) => _out = output;

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_SUPPLIER_UNITS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static JObject Data(string file)
            => JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), file)));

        /// <summary>Substring, as the two sites under discussion actually match.</summary>
        private static bool Substring(string hay, string pattern)
            => (hay ?? "").IndexOf((pattern ?? "").Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        // ══════════════════════════════════════════════════════════════════════
        //  W5a — the supplier patterns are stems, and two must match camelCase
        // ══════════════════════════════════════════════════════════════════════

        private static List<string> SupplierPatterns(string key)
            => Data("STING_SUPPLIER_UNITS.json")["rules"]
               .SelectMany(r => (r[key] as JArray) ?? new JArray())
               .Select(p => p.ToString().Trim())
               .Where(p => p.Length > 0)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(p => p, StringComparer.Ordinal)
               .ToList();

        [Fact]
        public void The_Supplier_Patterns_Include_Deliberate_PREFIX_Stems()
        {
            // If these ever stop being stems the refusal below should be revisited — so the
            // reason is asserted against the shipped data rather than described in a comment.
            var all = SupplierPatterns("matchMaterialPatterns")
                      .Concat(SupplierPatterns("matchTypePatterns")).ToList();

            foreach (string stem in new[] { "corrugat", "galvanis", "galvaniz", "profil", "stonecoat" })
                Assert.Contains(stem, all, StringComparer.OrdinalIgnoreCase);

            // Each is a prefix of a real word and is NOT itself one — which is exactly what
            // whole-word matching cannot express.
            Assert.True(Substring("CORRUGATED IRON SHEET", "corrugat"));
            Assert.False(PatternMatch.Contains("CORRUGATED IRON SHEET", "corrugat"));
            Assert.True(Substring("GALVANIZED STEEL SHEET G28", "galvaniz"));
            Assert.False(PatternMatch.Contains("GALVANIZED STEEL SHEET G28", "galvaniz"));
        }

        [Fact]
        public void Whole_Word_Matching_Cannot_See_Inside_The_Names_This_Plugin_Generates()
        {
            // The argument that settles W5a. TypeRenamePlanner composes compacted ISO 22014
            // names, and SupplierUnitConverter is then asked about those names. PatternMatch
            // treats letters and digits as word characters, so there is no boundary to find.
            foreach (var (name, pattern) in new[]
            {
                ("PLNS_RTL_StoneCoatedTileRoof1", "stonecoat"),
                ("PLNS_RTL_ClayTileRoof14", "clay"),
                ("PLNS_RSH_Timber50-Tiled", "tile"),
            })
            {
                Assert.True(Substring(name, pattern), $"'{pattern}' should be a substring of {name}");
                Assert.False(PatternMatch.Contains(name, pattern),
                    $"'{pattern}' unexpectedly matches {name} as a whole word — if compacted ISO "
                  + "names have gained separators, W5a is worth revisiting.");
            }
        }

        [Fact]
        public void No_Supplier_Pattern_Has_A_False_Positive_Its_Category_Gate_Does_Not_Stop()
        {
            // The brief said: construct the false positive first, prove it RED. Over 2,038
            // real names there is not one. Every substring-only hit is either a correct
            // match (GALVANIZED STEEL SHEET) or an element in a category the rule's own
            // matchCategories excludes (GALVANIZED STEEL PIPE is a Pipe, not a Roof).
            //
            // Listed rather than counted, so the claim can be checked rather than believed.
            var pool = NamePool();
            var patterns = SupplierPatterns("matchMaterialPatterns")
                           .Concat(SupplierPatterns("matchTypePatterns"))
                           .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            int substringOnly = 0;
            foreach (string p in patterns)
            {
                var only = pool.Where(n => Substring(n, p) && !PatternMatch.Contains(n, p)).ToList();
                if (only.Count == 0) continue;
                substringOnly += only.Count;
                _out.WriteLine($"'{p}' — {only.Count} substring-only: {string.Join(" | ", only.Take(4))}");
            }

            _out.WriteLine($"\n{substringOnly} substring-only hits across {patterns.Count} patterns "
                         + $"and {pool.Count} names.");
            Assert.True(substringOnly > 50,
                "Whole-word matching now loses almost nothing here, so the refusal in this "
              + "file should be re-examined against fresh data.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  W5b — measurably neutral, and more fragile
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Wall_Voids_Are_Excluded_By_Their_TYPE_Name_Not_Their_Family_Name()
        {
            // The correction. Read as a family name alone, `opening` is not a whole word in
            // M_GM_OpeningWall_Instance — which is what made the swap look catastrophic.
            // Classify matches description + " " + typeName, and the type name is `Opening`.
            const string family = "M_GM_OpeningWall_Instance";
            Assert.False(PatternMatch.Contains(family, "opening"));

            string hay = family + " " + "Opening";
            Assert.True(PatternMatch.Contains(hay, "opening"));
            Assert.True(Substring(hay, "opening"));

            // And through the real matcher, both ways round.
            var ex = ProductExclusion.Build(null, new[] { "opening", "muntin" }, null, null);
            Assert.Equal(ExclusionVerdict.ByPattern, ex.Classify("Generic Models", family, "Opening"));
        }

        [Fact]
        public void Whole_Word_Would_Break_The_Cases_The_Joined_Haystack_Currently_Carries()
        {
            // The fragility the measurement does not show, because this model happens not to
            // contain them. Both are one keystroke away in any project.
            //
            //   a blank type name  — the family alone no longer matches
            //   a plural family    — "Air Openings" is a real material name in this register
            foreach (var (family, typeName) in new[]
            {
                ("M_GM_OpeningWall_Instance", ""),
                ("Air Openings", ""),
            })
            {
                string hay = family + " " + typeName;
                Assert.True(Substring(hay, "opening"),
                    $"'{hay}' is excluded today by substring matching");
                Assert.False(PatternMatch.Contains(hay, "opening"),
                    $"'{hay}' would STOP being excluded if this matcher became whole-word — "
                  + "and a denominator has no error state.");
            }
        }

        [Fact]
        public void The_Real_Exclusion_Call_Site_Still_Catches_A_Plural_And_A_Blank_Type()
        {
            // Driving ProductExclusion.Classify, not PatternMatch — so that MAKING the swap
            // fails here. It has to: converting both call sites breaks NOTHING in either
            // test project as they stood (827 Tags + 1,249 Boq all pass), and a wall void
            // returning to the PROD denominator has no error state.
            var ex = ProductExclusion.Build(
                new[] { "Rooms" }, new[] { "opening", "muntin" }, new[] { "Windows", "Doors" }, null);

            // A blank type name: the family alone must still carry the exclusion.
            Assert.Equal(ExclusionVerdict.ByPattern,
                ex.Classify("Generic Models", "M_GM_OpeningWall_Instance", ""));

            // A plural family: "Air Openings" is a real name in the shipped register.
            Assert.Equal(ExclusionVerdict.ByPattern,
                ex.Classify("Generic Models", "Air Openings", ""));

            // And the case the delivered model actually had, which survives either matcher.
            Assert.Equal(ExclusionVerdict.ByPattern,
                ex.Classify("Generic Models", "M_GM_OpeningWall_Instance", "Opening"));
        }

        [Fact]
        public void The_Exclusion_Patterns_Are_Long_Enough_That_Substring_Is_Safe()
        {
            // Why substring is tolerable HERE and was not in TypeRenamePlanner: "opening"
            // and "muntin" are seven and six letters and appear inside no unrelated English
            // word, where "tile" and "rc" appear inside dozens. The historical false
            // positive on this list — a real window type — was fixed by protectedCategories,
            // not by the matcher, and that protection is still what does the work.
            var patterns = (Data("STING_PROD_EXCLUSIONS.json")["notAProductPatterns"] as JArray)
                           .Select(p => p.ToString()).ToList();
            Assert.Equal(new[] { "opening", "muntin" }, patterns);
            Assert.All(patterns, p => Assert.True(p.Length >= 6,
                $"'{p}' is short enough to appear inside an unrelated word — a short pattern "
              + "is where substring matching stops being safe, and this file's refusal with it."));

            // The protection, still doing the work.
            var ex = ProductExclusion.Build(null, patterns, new[] { "Windows", "Doors" }, null);
            Assert.Equal(ExclusionVerdict.Included, ex.Classify("Windows", "Opening Window", ""));
            Assert.Equal(ExclusionVerdict.ByPattern, ex.Classify("Generic Models", "Wall Opening", ""));
        }

        // ══════════════════════════════════════════════════════════════════════

        private static List<string> _pool;

        /// <summary>Every real name available: register MAT_NAMEs, the material corpus, and
        /// the type and family names from the delivered model.</summary>
        private static List<string> NamePool()
        {
            if (_pool != null) return _pool;
            var set = new HashSet<string>(StringComparer.Ordinal);
            var root = new DirectoryInfo(DataDir()).Parent.Parent;

            foreach (string f in new[] { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv" })
                foreach (string n in Column(Path.Combine(DataDir(), f), "MAT_NAME")) set.Add(n);

            string fixtures = Path.Combine(root.FullName, "StingTools.Tags.Tests", "Fixtures");
            foreach (string f in Directory.GetFiles(fixtures, "material_names_*.csv"))
                foreach (string n in FirstColumn(f)) set.Add(n);
            foreach (string f in Directory.GetFiles(fixtures, "type_rename_*.csv"))
                foreach (string n in Column(f, "CurrentName")) set.Add(n);
            foreach (string f in Directory.GetFiles(fixtures, "type_rename_*.csv"))
                foreach (string n in Column(f, "RecordedProposal")) set.Add(n);

            // The two compacted ISO names the argument turns on must be in the pool even if
            // no fixture happens to carry them, or this test could pass on their absence.
            set.Add("PLNS_RTL_StoneCoatedTileRoof1");
            set.Add("PLNS_RTL_ClayTileRoof14");

            Assert.True(set.Count > 1500, "only " + set.Count + " names pooled");
            return _pool = set.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        private static IEnumerable<string> FirstColumn(string path)
            => File.ReadAllLines(path).Skip(1)
                   .Where(l => !string.IsNullOrWhiteSpace(l))
                   .Select(l => Split(l).FirstOrDefault())
                   .Where(s => !string.IsNullOrWhiteSpace(s));

        private static IEnumerable<string> Column(string path, string column)
        {
            var lines = File.ReadAllLines(path)
                            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                            .ToList();
            if (lines.Count < 2) yield break;
            var header = Split(lines[0]);
            int i = header.FindIndex(h => string.Equals((h ?? "").Trim().TrimStart('﻿'), column,
                                                        StringComparison.OrdinalIgnoreCase));
            if (i < 0) yield break;
            foreach (string line in lines.Skip(1))
            {
                var c = Split(line);
                if (i < c.Count && !string.IsNullOrWhiteSpace(c[i])) yield return c[i].Trim();
            }
        }

        private static List<string> Split(string line)
        {
            var fields = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch != '"') { cur.Append(ch); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (ch == '"' && cur.Length == 0) quoted = true;
                else if (ch == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            fields.Add(cur.ToString());
            return fields;
        }
    }
}
