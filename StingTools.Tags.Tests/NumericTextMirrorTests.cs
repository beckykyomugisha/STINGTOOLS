// The pairing rule behind NumericTextMirror.
//
// A Text label formula cannot reference a NUMBER, LENGTH or YESNO parameter -
// Revit rejects it as "Inconsistent Units" - so twelve rows across the two
// build sheets read a _TXT twin instead. The twins were defined and nothing
// ever filled them, which would have rendered those rows blank on every
// element forever, with "the data is missing" as the obvious explanation.
//
// These test the rule, not the Revit write: which numeric pairs with which
// twin, and whether the shipped parameter file actually provides one for every
// parameter the build sheets now point at.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class NumericTextMirrorTests
    {
        private static KeyValuePair<string, string> P(string n, string t)
            => new KeyValuePair<string, string>(n, t);

        // ── the pairing rule ─────────────────────────────────────────────────

        [Fact]
        public void AUnitSuffixMayBeKeptOrDropped()
        {
            // Both spellings are in the shipped file, and trying only one
            // reported the other as having no twin - which would have silently
            // dropped a real label row.
            var pairs = NumericTextMirrorRule.Pairs(new[]
            {
                P("ELC_LPS_PROTECTION_ANGLE_DEG", "NUMBER"),
                P("ELC_LPS_PROTECTION_ANGLE_TXT", "TEXT"),            // unit dropped
                P("ELC_LPS_INSPECTION_INTERVAL_MONTHS", "NUMBER"),
                P("ELC_LPS_INSPECTION_INTERVAL_MONTHS_TXT", "TEXT"),  // unit kept
            });

            Assert.Equal("ELC_LPS_PROTECTION_ANGLE_TXT", pairs["ELC_LPS_PROTECTION_ANGLE_DEG"]);
            Assert.Equal("ELC_LPS_INSPECTION_INTERVAL_MONTHS_TXT",
                         pairs["ELC_LPS_INSPECTION_INTERVAL_MONTHS"]);
        }

        [Fact]
        public void TheUnitKeptSpellingWins()
        {
            // When both exist, X_MONTHS_TXT is the more specific twin of
            // X_MONTHS. Preferring the shorter one would pair it with a
            // parameter that belongs to a different quantity.
            var pairs = NumericTextMirrorRule.Pairs(new[]
            {
                P("ELC_X_MONTHS", "NUMBER"),
                P("ELC_X_MONTHS_TXT", "TEXT"),
                P("ELC_X_TXT", "TEXT"),
            });
            Assert.Equal("ELC_X_MONTHS_TXT", pairs["ELC_X_MONTHS"]);
        }

        [Fact]
        public void ATextParameterIsNeverPaired()
        {
            // It needs no mirror, and pairing one would let a real value be
            // overwritten by a formatted copy of something else.
            var pairs = NumericTextMirrorRule.Pairs(new[]
            {
                P("ELC_LPS_CLASS_TXT", "TEXT"),
                P("ELC_LPS_CLASS_TXT_TXT", "TEXT"),
            });
            Assert.Empty(pairs);
        }

        [Fact]
        public void ANumericWithNoTwinIsLeftAlone()
        {
            // Not an error - most numerics are not shown in a tag. Inventing a
            // twin name would create a parameter nothing binds.
            var pairs = NumericTextMirrorRule.Pairs(new[] { P("ELC_LONELY_NR", "NUMBER") });
            Assert.Empty(pairs);
        }

        [Fact]
        public void ATwinThatIsNotTextDoesNotCount()
        {
            // Two numerics whose names happen to pair off would otherwise mirror
            // into each other.
            var pairs = NumericTextMirrorRule.Pairs(new[]
            {
                P("ELC_A_NR", "NUMBER"),
                P("ELC_A_TXT", "NUMBER"),
            });
            Assert.Empty(pairs);
        }

        [Theory]
        [InlineData("YESNO")]
        [InlineData("LENGTH")]
        [InlineData("INTEGER")]
        [InlineData("NUMBER")]
        public void EveryNonTextStorageIsMirrored(string type)
        {
            // YESNO is easy to forget and is exactly what row 42 of the
            // universal sheet needed.
            var pairs = NumericTextMirrorRule.Pairs(new[]
            {
                P("ELC_V_NR", type),
                P("ELC_V_TXT", "TEXT"),
            });
            Assert.Single(pairs);
        }

        [Fact]
        public void NullsAndEmptiesAreTolerated()
        {
            Assert.Empty(NumericTextMirrorRule.Pairs(null));
            Assert.Empty(NumericTextMirrorRule.Candidates(null));
            Assert.Empty(NumericTextMirrorRule.Candidates(""));
        }

        // ── against the shipped data ─────────────────────────────────────────

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        private static IEnumerable<string> ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null) yield return line;
            }
        }

        private static List<KeyValuePair<string, string>> ShippedDefinitions()
        {
            var defs = new List<KeyValuePair<string, string>>();
            string p = Path.Combine(RepoRoot().FullName, "StingTools", "Data", "MR_PARAMETERS.txt");
            foreach (string line in ReadShared(p))
            {
                var f = line.Split('\t');
                if (f.Length > 3 && f[0] == "PARAM") defs.Add(P(f[2], f[3]));
            }
            return defs;
        }

        [Fact]
        public void EveryTwinTheBuildSheetsUseIsActuallyFedByAPair()
        {
            // The one that matters. A build-sheet row pointing at a _TXT twin
            // that no numeric mirrors into renders BLANK on every element, and
            // looks exactly like missing data. Nothing else checks this.
            var pairs = NumericTextMirrorRule.Pairs(ShippedDefinitions());
            var fed = new HashSet<string>(pairs.Values, StringComparer.Ordinal);

            var used = new HashSet<string>(StringComparer.Ordinal);
            var root = RepoRoot();
            foreach (string sheet in new[] { "UNIVERSAL_TAG_LABEL_BUILD_SHEET.md",
                                             "LPS_TAG_MASTER_BUILD_SHEET.md" })
            {
                string p = Path.Combine(root.FullName, "docs", sheet);
                if (!File.Exists(p)) continue;
                foreach (string line in ReadShared(p))
                {
                    // Only rows that SAY they read a twin. Every other _TXT
                    // parameter is a genuine text field that needs no mirror.
                    if (line.IndexOf("reads its TEXT twin", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var m = Regex.Match(line, @"TAG_PARA_STATE_\d+_BOOL,\s*([A-Z0-9_]+)");
                    if (m.Success) used.Add(m.Groups[1].Value);
                }
            }

            Assert.True(used.Count >= 12,
                        $"only {used.Count} twin-reading rows found across the sheets - expected at " +
                        "least the 12 that were corrected on 2026-09-22");

            var unfed = used.Where(t => !fed.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();

            Assert.True(unfed.Count == 0,
                $"{unfed.Count} twin(s) are read by a label row but no numeric mirrors into them, so " +
                "those rows would render blank on every element:\n  " + string.Join("\n  ", unfed));
        }

        [Fact]
        public void NoTwinIsFedByTwoDifferentNumerics()
        {
            // Two numerics writing one twin means whichever the mirror reaches
            // last wins, and the tag shows a different quantity depending on
            // dictionary order.
            var pairs = NumericTextMirrorRule.Pairs(ShippedDefinitions());

            var clashes = pairs.GroupBy(kv => kv.Value, StringComparer.Ordinal)
                               .Where(g => g.Count() > 1)
                               .Select(g => $"{g.Key} <- " + string.Join(", ", g.Select(x => x.Key)))
                               .OrderBy(x => x, StringComparer.Ordinal)
                               .ToList();

            Assert.True(clashes.Count == 0,
                $"{clashes.Count} twin(s) are written by more than one numeric:\n  " +
                string.Join("\n  ", clashes.Take(10)));
        }
    
        [Fact]
        public void ADirectClaimBeatsOneReachedByStrippingASuffix()
        {
            // Found in the shipped file: ASS_WEIGHT_KG and ASS_WEIGHT_KG_NR both
            // reach ASS_WEIGHT_KG_TXT - the first directly, the second only
            // after dropping _NR. Without precedence the winner depended on
            // dictionary order, and the tag would show a different quantity
            // depending on which the loop reached last.
            var pairs = NumericTextMirrorRule.Pairs(new[]
            {
                P("ASS_WEIGHT_KG", "NUMBER"),
                P("ASS_WEIGHT_KG_NR", "NUMBER"),
                P("ASS_WEIGHT_KG_TXT", "TEXT"),
            });

            Assert.Equal("ASS_WEIGHT_KG_TXT", pairs["ASS_WEIGHT_KG"]);
            Assert.False(pairs.ContainsKey("ASS_WEIGHT_KG_NR"),
                         "the suffix-stripped claim should have yielded to the direct one");
        }

        [Fact]
        public void PrecedenceDoesNotDependOnInputOrder()
        {
            // The bug was order-dependence itself, so the fix has to be proven
            // against both orders rather than the one that happened to work.
            var a = NumericTextMirrorRule.Pairs(new[]
            {
                P("ASS_WEIGHT_KG_NR", "NUMBER"),
                P("ASS_WEIGHT_KG", "NUMBER"),
                P("ASS_WEIGHT_KG_TXT", "TEXT"),
            });
            var b = NumericTextMirrorRule.Pairs(new[]
            {
                P("ASS_WEIGHT_KG", "NUMBER"),
                P("ASS_WEIGHT_KG_NR", "NUMBER"),
                P("ASS_WEIGHT_KG_TXT", "TEXT"),
            });
            Assert.Equal(a, b);
            Assert.Single(a);
        }
}
}
