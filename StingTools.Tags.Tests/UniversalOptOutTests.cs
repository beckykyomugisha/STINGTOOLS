// LabelMaster groups: which master a tag family takes its label from.
//
// WHY IT NEEDS A GATE OF ITS OWN
//
// A skip is invisible by construction. A family wrongly put in the UNIVERSAL
// group gets the universal label and is recoverable from git; a family wrongly
// put in another group is silently passed over on every universal run forever,
// and nothing anywhere says so. The two failures are not symmetric, so the
// parser defaults to universal and this gate makes every other group prove
// itself - in both directions, and consistently across every config file.
//
// The nine LPS tags are the case that prompted it. They are Multi-Category,
// Revit refuses to move a clone into Multi-Category, and that error was for a
// while the ONLY thing stopping propagation overwriting them. Measured
// 2026-09-21: the universal master carries 71 generic ASS_* rows and ZERO
// ELC_LPS_* rows, so a successful propagation would have deleted the LPS class,
// zone, conductor material, cross-section, bond type, risk-assessment ref and
// both HIGH warnings.
//
// It began as a boolean, "Universal: No", which meant SKIP whichever master was
// running. That protected the nine from the universal master and would equally
// have blocked the LPS master from reaching those same nine - a skip that
// cannot tell which master is asking is not a rule, it is a wall. The group
// says which master serves the family, so both runs do the right thing.

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
        /// <summary>
        /// The families that legitimately take their label from a master other
        /// than the universal one, and which master that is.
        /// </summary>
        private static readonly (string Family, string Group)[] ExpectedGroups =
        {
            ("STING - LPS Air Terminal Tag", "LPS"),
            ("STING - LPS Bond Tag", "LPS"),
            ("STING - LPS Down Conductor Tag", "LPS"),
            ("STING - LPS Earth Electrode Tag", "LPS"),
            ("STING - LPS Foundation Earth (Structural Reuse) Tag", "LPS"),
            ("STING - LPS Generic Component Tag", "LPS"),
            ("STING - LPS Natural Air Termination (Architectural Reuse) Tag", "LPS"),
            ("STING - LPS SPD Tag", "LPS"),
            ("STING - LPS Test Clamp Tag", "LPS"),
            // Not a shipped tag family - the master the nine take their
            // label FROM. It is declared so propagation can resolve which
            // group is running; without it the LPS master reads as
            // universal and skips its own nine targets.
            ("STING_LPS_Tag_Universal", "LPS"),
            // Hand-built specialist tags (docs/SPECIALIST_TAG_BUILD_SHEET.md).
            // Each is a group of ONE, not a shared "specialist" group: a shared
            // group would make whichever of them is run as a master overwrite
            // the other three with its own bespoke label.
            ("STING - Fire Door Tag", "FireDoor"),
            ("STING - Accessible Door Tag", "AccessibleDoor"),
            ("STING - Room Finish Tag", "RoomFinish"),
            ("STING - Fire Compartment Tag", "FireCompartment"),
            // Rebuilt in place 2026-09-24 as the material callout. A material tag reads
            // the MATERIAL, so the universal label printed blank on every material;
            // opting out keeps propagation from putting it back (MaterialTagLabelTests).
            ("STING - Materials Tag", "MaterialsTag"),
        };

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        /// <summary>family key -> declared label-master group, first one wins.</summary>
        private static Dictionary<string, string> GroupsFromConfig()
        {
            string data = Path.Combine(RepoRoot().FullName, "StingTools", "Data");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var f in Directory.GetFiles(data, "STING_TAG_CONFIG_v5_0_*.csv"))
                foreach (var d in TagConfigDeclarations.Parse(File.ReadLines(f)))
                {
                    string k = TagCategoryNameForms.NormaliseKey(d.FamilyName);
                    // First non-universal declaration wins, matching
                    // TagCategoryResolver.
                    if (!d.Universal && !map.ContainsKey(k)) map[k] = d.LabelMaster;
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
            var optOuts = GroupsFromConfig().Keys.ToList();

            var expected = new HashSet<string>(
                ExpectedGroups.Select(e => TagCategoryNameForms.NormaliseKey(e.Family)),
                StringComparer.Ordinal);

            var unexpected = optOuts.Where(k => !expected.Contains(k))
                                    .OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(unexpected.Count == 0,
                $"{unexpected.Count} family/families declare a non-universal LabelMaster without being " +
                "listed here. A silent exclusion is the failure this gate exists to catch: either add " +
                "them to ExpectedGroups with a reason, or remove the declaration.\n  " +
                string.Join("\n  ", unexpected));
        }

        [Fact]
        public void EveryExpectedGroupIsActuallyDeclaredWithTheRightMaster()
        {
            // The other direction, and the one that rots. If a family is dropped
            // from the config, renamed, or its TAG7 line rewritten without the
            // marker, it quietly rejoins propagation and its bespoke label is
            // overwritten on the next run. The list here would still claim it was
            // protected.
            var groups = GroupsFromConfig();

            var wrong = ExpectedGroups
                .Select(e =>
                {
                    string k = TagCategoryNameForms.NormaliseKey(e.Family);
                    string actual;
                    if (!groups.TryGetValue(k, out actual))
                        return $"{e.Family}: expected group '{e.Group}', but no config declares one - " +
                               "it would be propagated over by the universal master";
                    if (!string.Equals(actual, e.Group, StringComparison.OrdinalIgnoreCase))
                        return $"{e.Family}: expected group '{e.Group}', config says '{actual}' - " +
                               "it would be served by the wrong master, or by none";
                    return null;
                })
                .Where(x => x != null)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            Assert.True(wrong.Count == 0,
                $"{wrong.Count} family/families are not declared with the master they need:\n  " +
                string.Join("\n  ", wrong));
        }

        [Fact]
        public void TheGroupIsDeclaredConsistentlyAcrossEveryConfigFile()
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
            foreach (var e in ExpectedGroups)
            {
                string name = e.Family;
                string k = TagCategoryNameForms.NormaliseKey(name);
                var declaring = perFile.Where(kv => kv.Value.ContainsKey(k)).ToList();
                var without = declaring.Where(kv => !kv.Value[k]).Select(kv => kv.Key).ToList();
                if (declaring.Count > 0 && without.Count > 0)
                    disagreements.Add($"{name}: declared but NOT marked in {string.Join(", ", without)}");
            }

            Assert.True(disagreements.Count == 0,
                $"{disagreements.Count} family/families declare a LabelMaster group in some config files " +
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
    
        // ── groups ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("TAG7: X  -  Category: Multi-Category  -  LabelMaster: LPS", "LPS")]
        [InlineData("TAG7: X  -  Category: Multi-Category  -  labelmaster: lps", "lps")]
        // Saying the default out loud is normalised, so "universal" and
        // "Universal" cannot become two groups that never match each other.
        [InlineData("TAG7: X  -  Category: Air Terminals  -  LabelMaster: Universal", "universal")]
        [InlineData("TAG7: X  -  Category: Air Terminals", "universal")]
        // Legacy. It names no master, so nothing propagates to it - the old
        // behaviour exactly - but it is a DISTINCT value, so a gate can tell
        // "opted out the old way, migrate me" from "belongs to a named group".
        [InlineData("TAG7: X  -  Category: Multi-Category  -  Universal: No", "(unnamed)")]
        // A typo leaves the family in the universal group, which is the
        // recoverable direction; the gates above catch the typo itself.
        [InlineData("TAG7: X  -  Category: Multi-Category  -  LabelMastr: LPS", "universal")]
        public void TheDeclaredGroupIsRead(string line, string expected)
            => Assert.Equal(expected, TagConfigDeclarations.DeclaredLabelMaster(line));

        [Fact]
        public void AGroupSurvivesBothDialects()
        {
            var prose = TagConfigDeclarations.Parse(new[]
            {
                "Tag Family #1: STING - Spec Tag",
                "TAG7: X  -  Category: Multi-Category  -  LabelMaster: LPS",
            });
            var row = TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,STING - Spec Tag,E,Multi-Category,LabelMaster: LPS",
            });

            Assert.Equal("LPS", prose.Single().LabelMaster);
            Assert.Equal("LPS", row.Single().LabelMaster);
            Assert.False(prose.Single().Universal);
        }

        [Fact]
        public void TheLpsMasterIsDeclaredInItsOwnGroup()
        {
            // The one that makes the whole change work. Propagation asks the
            // MASTER which group it serves; an undeclared master reads as
            // universal, and the LPS master would then skip all nine of its own
            // targets while reporting a clean run.
            var groups = GroupsFromConfig();
            string k = TagCategoryNameForms.NormaliseKey("STING_LPS_Tag_Universal");

            Assert.True(groups.ContainsKey(k),
                "STING_LPS_Tag_Universal is not declared. Propagation would read it as the universal " +
                "master and skip every LPS family it exists to serve.");
            Assert.Equal("LPS", groups[k]);
        }

        [Fact]
        public void TheNineAndTheirMasterShareOneGroup()
        {
            // A master and its targets in different groups is the failure this
            // whole mechanism exists to prevent, and it reports as a clean run
            // with everything skipped.
            var groups = GroupsFromConfig();
            string master = groups[TagCategoryNameForms.NormaliseKey("STING_LPS_Tag_Universal")];

            var strays = ExpectedGroups
                .Where(e => e.Family != "STING_LPS_Tag_Universal")
                // The LPS family set, by what this list EXPECTS - the config is
                // then checked against the master's actual group below.
                .Where(e => string.Equals(e.Group, "LPS", StringComparison.Ordinal))
                .Where(e => !string.Equals(groups[TagCategoryNameForms.NormaliseKey(e.Family)],
                                           master, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Family)
                .ToList();

            Assert.True(strays.Count == 0,
                $"{strays.Count} family/families are not in the '{master}' group their master serves, " +
                "so that master would skip them:\n  " + string.Join("\n  ", strays));
        }

        [Fact]
        public void NoLegacyUniversalNoIsLeftInTheConfig()
        {
            // "(unnamed)" names no master, so a family left on the legacy
            // spelling is unreachable by EVERY master, for good. It works today
            // only because the universal master also skips it.
            string data = Path.Combine(RepoRoot().FullName, "StingTools", "Data");
            var stragglers = new List<string>();

            foreach (var f in Directory.GetFiles(data, "STING_TAG_CONFIG_v5_0_*.csv"))
                foreach (var d in TagConfigDeclarations.Parse(File.ReadLines(f)))
                    if (string.Equals(d.LabelMaster, TagConfigDeclarations.UnnamedGroup,
                                      StringComparison.Ordinal))
                        stragglers.Add($"{d.FamilyName} ({Path.GetFileName(f)})");

            Assert.True(stragglers.Count == 0,
                $"{stragglers.Count} declaration(s) still use the legacy \"Universal: No\". No master " +
                "serves that group, so these are unreachable by every master. Migrate them to " +
                "\"LabelMaster: <group>\":\n  " + string.Join("\n  ", stragglers.Distinct()));
        }
}
}
