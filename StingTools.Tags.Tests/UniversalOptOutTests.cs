// "Universal: No" opts a tag family out of universal-label propagation.
//
// WHY IT NEEDS A GATE OF ITS OWN
//
// A skip is invisible by construction. A family wrongly INCLUDED gets the
// universal label and is recoverable from git; a family wrongly EXCLUDED is
// silently passed over on every run forever, and nothing anywhere says so. The
// two failures are not symmetric, so the parser defaults to IN and this gate
// makes every opt-out prove itself.
//
// The nine LPS tags are the case that prompted it. They are Multi-Category,
// Revit refuses to move a clone into Multi-Category, and until now that error
// was the ONLY thing stopping propagation overwriting them. Measured
// 2026-09-21: the universal master carries 71 generic ASS_* rows and ZERO
// ELC_LPS_* rows, so a successful propagation would have deleted the LPS class,
// zone, conductor material, cross-section, bond type, risk-assessment ref and
// both HIGH warnings. Build a Multi-Category master one day and that error
// stops firing with nothing to replace it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class UniversalOptOutTests
    {
        /// <summary>The families that legitimately keep their own label.</summary>
        private static readonly string[] ExpectedOptOuts =
        {
            "STING - LPS Air Terminal Tag",
            "STING - LPS Bond Tag",
            "STING - LPS Down Conductor Tag",
            "STING - LPS Earth Electrode Tag",
            "STING - LPS Foundation Earth (Structural Reuse) Tag",
            "STING - LPS Generic Component Tag",
            "STING - LPS Natural Air Termination (Architectural Reuse) Tag",
            "STING - LPS SPD Tag",
            "STING - LPS Test Clamp Tag",
        };

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        /// <summary>family key -> was it declared non-universal anywhere.</summary>
        private static Dictionary<string, bool> OptOutsFromConfig()
        {
            string data = Path.Combine(RepoRoot().FullName, "StingTools", "Data");
            var map = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (var f in Directory.GetFiles(data, "STING_TAG_CONFIG_v5_0_*.csv"))
                foreach (var d in TagConfigDeclarations.Parse(File.ReadLines(f)))
                {
                    string k = TagCategoryNameForms.NormaliseKey(d.FamilyName);
                    // Any single NO wins, matching TagCategoryResolver.
                    map[k] = (map.ContainsKey(k) && map[k]) || !d.Universal;
                }
            return map;
        }

        // ── the gate ─────────────────────────────────────────────────────────

        [Fact]
        public void EveryOptOutIsOneWeExpect()
        {
            // The direction that matters. A stray "Universal: No" - a typo, a
            // copy-paste into the wrong family block, a bulk edit that caught one
            // line too many - removes a family from every future propagation and
            // reports nothing. This is what notices.
            var optOuts = OptOutsFromConfig()
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            var expected = new HashSet<string>(
                ExpectedOptOuts.Select(TagCategoryNameForms.NormaliseKey), StringComparer.Ordinal);

            var unexpected = optOuts.Where(k => !expected.Contains(k))
                                    .OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(unexpected.Count == 0,
                $"{unexpected.Count} family/families declare \"Universal: No\" without being listed here. " +
                "A silent exclusion is the failure this gate exists to catch: either add them to " +
                "ExpectedOptOuts with a reason, or remove the declaration.\n  " +
                string.Join("\n  ", unexpected));
        }

        [Fact]
        public void EveryExpectedOptOutIsActuallyDeclared()
        {
            // The other direction, and the one that rots. If a family is dropped
            // from the config, renamed, or its TAG7 line rewritten without the
            // marker, it quietly rejoins propagation and its bespoke label is
            // overwritten on the next run. The list here would still claim it was
            // protected.
            var optOuts = OptOutsFromConfig();

            var notDeclared = ExpectedOptOuts
                .Where(nm =>
                {
                    string k = TagCategoryNameForms.NormaliseKey(nm);
                    return !optOuts.TryGetValue(k, out bool no) || !no;
                })
                .OrderBy(nm => nm, StringComparer.Ordinal)
                .ToList();

            Assert.True(notDeclared.Count == 0,
                $"{notDeclared.Count} family/families are expected to opt out but no config declares " +
                "\"Universal: No\" for them. They would be propagated over on the next run:\n  " +
                string.Join("\n  ", notDeclared));
        }

        [Fact]
        public void OptOutIsDeclaredConsistentlyAcrossEveryConfigFile()
        {
            // Every config has a _DesignConstruction twin. A family marked in one
            // and not the other resolves to NO by the any-NO-wins rule, so it
            // still works - but it works by a tie-break rather than by intent,
            // and the next edit to either file can flip it without anyone
            // noticing. The resolver warns at runtime; this fails at build time.
            string data = Path.Combine(RepoRoot().FullName, "StingTools", "Data");
            var perFile = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            foreach (var f in Directory.GetFiles(data, "STING_TAG_CONFIG_v5_0_*.csv"))
            {
                var seen = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var d in TagConfigDeclarations.Parse(File.ReadLines(f)))
                {
                    string k = TagCategoryNameForms.NormaliseKey(d.FamilyName);
                    seen[k] = (seen.ContainsKey(k) && seen[k]) || !d.Universal;
                }
                perFile[Path.GetFileName(f)] = seen;
            }

            var disagreements = new List<string>();
            foreach (string name in ExpectedOptOuts)
            {
                string k = TagCategoryNameForms.NormaliseKey(name);
                var declaring = perFile.Where(kv => kv.Value.ContainsKey(k)).ToList();
                var without = declaring.Where(kv => !kv.Value[k]).Select(kv => kv.Key).ToList();
                if (declaring.Count > 0 && without.Count > 0)
                    disagreements.Add($"{name}: declared but NOT marked in {string.Join(", ", without)}");
            }

            Assert.True(disagreements.Count == 0,
                $"{disagreements.Count} family/families are marked \"Universal: No\" in some config files " +
                "and not others:\n  " + string.Join("\n  ", disagreements));
        }

        // ── the parser ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("TAG7: X  •  Category: Multi-Category  •  Universal: No", true)]
        [InlineData("TAG7: X  •  Category: Multi-Category  •  universal: no", true)]
        [InlineData("TAG7: X  •  Category: Multi-Category  •  Universal: False", true)]
        [InlineData("TAG7: X  •  Category: Multi-Category  •  Universal: Yes", false)]
        [InlineData("TAG7: X  •  Category: Multi-Category", false)]
        // Only an explicit NO counts. A typo leaves the family opted IN, which is
        // the recoverable direction - and the gates above catch the typo itself.
        [InlineData("TAG7: X  •  Category: Multi-Category  •  Universal: N0", false)]
        [InlineData("TAG7: X  •  Category: Multi-Category  •  Universal:", false)]
        public void OnlyAnExplicitNoOptsOut(string line, bool expected)
            => Assert.Equal(expected, TagConfigDeclarations.DeclaresNonUniversal(line));

        [Fact]
        public void ADeclarationWithoutTheMarkerStaysUniversal()
        {
            var decls = TagConfigDeclarations.Parse(new[]
            {
                "Tag Family #1: STING - Plain Tag",
                "TAG7: X  •  Category: Air Terminals",
            });

            Assert.Single(decls);
            Assert.True(decls[0].Universal);
        }

        [Fact]
        public void TheMarkerIsReadFromBothDialects()
        {
            // A family declared in only one dialect would otherwise opt out in
            // prose and back in by row, and the merge would pick whichever came
            // first in directory order.
            var prose = TagConfigDeclarations.Parse(new[]
            {
                "Tag Family #1: STING - Spec Tag",
                "TAG7: X  •  Category: Multi-Category  •  Universal: No",
            });
            var row = TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,STING - Spec Tag,E,Multi-Category,Universal: No",
            });

            Assert.False(prose.Single().Universal);
            Assert.False(row.Single().Universal);
        }
    }
}
