// The LPS master's shared block is a COPY of the universal master's, and
// copies drift.
//
// LPS_TAG_MASTER_BUILD_SHEET.md STEP 2 reproduces tiers T4-T10 from
// UNIVERSAL_TAG_LABEL_BUILD_SHEET.md verbatim - Calc Value Name, formula,
// prefix, suffix, Break - so the master can be hand-built from one document.
// Revit blocks cross-category label paste, so there is no way to share the rows
// themselves; only the specification can be shared.
//
// Which means the next edit to the universal master silently invalidates the
// LPS sheet. Nobody would notice: both files still read as complete, correct
// documents, and the nine LPS tags would simply end up with a shared label
// nobody chose. This is the same shape as the stale content library that cost
// six weeks - a document that is confidently wrong looks exactly like one that
// is right.
//
// The generator lives at tools/gen_lps_authoring_sheet.py in the session that
// wrote it; the reproduction rule is here so a drift fails the build with
// instructions rather than being discovered in a Family Editor.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class LpsMasterSheetTests
    {
        private static readonly Regex RowLine =
            new Regex(@"^\|\s*\d+\s*\|\s*(?<tier>T\d+)\s*\|(?<rest>.*)$", RegexOptions.Compiled);

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
                dir = dir.Parent;
            return dir;
        }

        private static string Doc(string name)
        {
            var root = RepoRoot();
            Assert.True(root != null, "repo root not found");
            string p = Path.Combine(root.FullName, "docs", name);
            Assert.True(File.Exists(p), name + " not found");
            return p;
        }

        /// <summary>
        /// Reads a doc that may be open in an editor or viewer.
        ///
        /// <para>File.ReadLines takes no sharing, so having the sheet open in a
        /// Markdown viewer failed all three of these tests with an IOException -
        /// a red build caused by looking at the file. A build that breaks when
        /// someone reads the documentation is a build nobody will trust.</para>
        /// </summary>
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

        /// <summary>Tier plus everything after it, for rows outside T1-T3.</summary>
        private static List<(string Tier, string Body)> SharedRows(string path)
        {
            var rows = new List<(string Tier, string Body)>();
            foreach (string line in ReadShared(path))
            {
                var m = RowLine.Match(line.TrimEnd());
                if (!m.Success) continue;
                string tier = m.Groups["tier"].Value;
                if (tier == "T1" || tier == "T2" || tier == "T3") continue;
                // Normalise only whitespace: a cell's CONTENT differing is the
                // drift this exists to catch, and must not be normalised away.
                rows.Add((tier, Regex.Replace(m.Groups["rest"].Value, @"\s+", " ").Trim()));
            }
            return rows;
        }

        [Fact]
        public void TheLpsSharedBlockStillMatchesTheUniversalMaster()
        {
            var universal = SharedRows(Doc("UNIVERSAL_TAG_LABEL_BUILD_SHEET.md"));
            var lps = SharedRows(Doc("LPS_TAG_MASTER_BUILD_SHEET.md"));

            Assert.True(universal.Count > 50,
                        $"only {universal.Count} shared rows parsed from the universal sheet - " +
                        "the table format has probably changed and this gate is no longer reading it");

            const string How =
                "\n\nThe LPS sheet's STEP 2 is a verbatim copy of the universal sheet's T4-T10 rows. " +
                "Regenerate it (tools/gen_lps_authoring_sheet.py) so the two agree — and if the LPS " +
                "master has ALREADY been hand-built, its shared rows now need the same edit applied " +
                "in the Family Editor, because nothing else will tell you.";

            Assert.True(universal.Count == lps.Count,
                $"the universal sheet has {universal.Count} shared rows and the LPS sheet has " +
                $"{lps.Count}." + How);

            var drift = new List<string>();
            for (int i = 0; i < universal.Count; i++)
            {
                if (universal[i].Tier != lps[i].Tier)
                    drift.Add($"row {i + 1}: universal is {universal[i].Tier}, LPS is {lps[i].Tier}");
                else if (!string.Equals(universal[i].Body, lps[i].Body, StringComparison.Ordinal))
                    drift.Add($"row {i + 1} ({universal[i].Tier}):\n      universal: {universal[i].Body}" +
                              $"\n      LPS:       {lps[i].Body}");
            }

            Assert.True(drift.Count == 0,
                $"{drift.Count} shared row(s) differ between the two sheets:\n  " +
                string.Join("\n  ", drift.Take(5)) +
                (drift.Count > 5 ? $"\n  ... and {drift.Count - 5} more" : "") + How);
        }

        [Fact]
        public void EveryLpsRowIsTierGatedExceptT1()
        {
            // A row without its TAG_PARA_STATE gate renders at every depth, so a
            // tier-1 tag would carry the whole LPS block. It is the kind of
            // mistake that looks fine in the Family Editor and only shows up on
            // a drawing.
            var bad = new List<string>();
            foreach (string line in ReadShared(Doc("LPS_TAG_MASTER_BUILD_SHEET.md")))
            {
                var m = RowLine.Match(line.TrimEnd());
                if (!m.Success) continue;
                string tier = m.Groups["tier"].Value;
                if (tier == "T1") continue;

                string rest = m.Groups["rest"].Value;
                if (!rest.Contains("TAG_PARA_STATE_" + tier.Substring(1) + "_BOOL"))
                    bad.Add($"{tier}: {rest.Split('|').ElementAtOrDefault(0)?.Trim()}");
            }

            Assert.True(bad.Count == 0,
                $"{bad.Count} row(s) are not gated by their own tier's TAG_PARA_STATE_n_BOOL:\n  " +
                string.Join("\n  ", bad.Take(8)));
        }

        [Fact]
        public void TheSheetSaysWhichRowsAreUnverified()
        {
            // The LPS rows' Break values are a suggestion - the declarations do
            // not record line breaks. The copied block's are proven. Losing that
            // distinction would present a guess and a measurement as the same
            // thing, which is how a build sheet stops being trustworthy.
            string text = string.Join("\n", ReadShared(Doc("LPS_TAG_MASTER_BUILD_SHEET.md")));

            Assert.Contains("Break is a suggestion", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("verbatim", text, StringComparison.OrdinalIgnoreCase);
        }
    
        [Fact]
        public void NoTextFormulaReadsANonTextParameter()
        {
            // Revit has no number-to-string conversion in family formulas, so a
            // Text calculated value referencing a NUMBER, LENGTH or YESNO
            // parameter is rejected as "Inconsistent Units" the moment it is
            // entered. Both sheets shipped rows that could not be typed in -
            // found one dialog at a time while hand-building the master, after
            // the sheets had been reviewed and committed.
            //
            // Every affected parameter already had a TEXT twin. This makes the
            // next one fail here instead of in a Family Editor.
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            string mr = Path.Combine(RepoRoot().FullName, "StingTools", "Data", "MR_PARAMETERS.txt");
            foreach (string line in ReadShared(mr))
            {
                var f = line.Split('	');
                if (f.Length > 3 && f[0] == "PARAM" && !types.ContainsKey(f[2])) types[f[2]] = f[3];
            }
            Assert.True(types.Count > 3000, $"only {types.Count} parameters parsed from MR_PARAMETERS.txt");

            var bad = new List<string>();
            foreach (string sheet in new[] { "UNIVERSAL_TAG_LABEL_BUILD_SHEET.md",
                                             "LPS_TAG_MASTER_BUILD_SHEET.md" })
            {
                foreach (string line in ReadShared(Doc(sheet)))
                {
                    // The VALUE branch of the tier gate - not the gate itself,
                    // which is a YESNO by design.
                    var m = Regex.Match(line, @"TAG_PARA_STATE_\d+_BOOL,\s*([A-Z0-9_]+)");
                    if (!m.Success) continue;
                    string param = m.Groups[1].Value;
                    string t2;
                    if (types.TryGetValue(param, out t2) && t2 != "TEXT")
                        bad.Add($"{sheet}: {param} is {t2}");
                }
            }

            Assert.True(bad.Count == 0,
                $"{bad.Count} label row(s) read a non-TEXT parameter from a Text formula. Revit " +
                "rejects these as \"Inconsistent Units\" and they cannot be entered at all. Point " +
                "them at the parameter's _TXT twin:\n  " + string.Join("\n  ", bad.Distinct().Take(10)));
        }

        [Fact]
        public void NoRowHasAStrayPipeThatShiftsItsColumns()
        {
            // A raw | ENDS a markdown cell. Three prefixes begin with one -
            // "| A4:", "| B6:", "| ", the separator between two carbon values
            // sharing a line - and printed unescaped each became an empty
            // Prefix and a Suffix holding what should have been the prefix.
            // Both sheets shipped that way, and it is exactly how those rows
            // were typed into the master on 2026-09-22.
            //
            // Column count is the check: a stray pipe adds a column, and
            // nothing else in a well-formed row does.
            foreach (string sheet in new[] { "UNIVERSAL_TAG_LABEL_BUILD_SHEET.md",
                                             "LPS_TAG_MASTER_BUILD_SHEET.md" })
            {
                int expected = -1;
                var bad = new List<string>();

                foreach (string line in ReadShared(Doc(sheet)))
                {
                    string row = line.TrimEnd();
                    bool isHeader = row.StartsWith("| # | Tier |");
                    if (!isHeader && !Regex.IsMatch(row, @"^\|\s*\d+\s*\|\s*T\d+\s*\|")) continue;

                    // An escaped pipe is content, not a separator.
                    int cells = row.Replace(@"\|", "\u0001").Split('|').Length;

                    if (isHeader) { expected = cells; continue; }
                    if (expected > 0 && cells != expected)
                        bad.Add($"row {row.Split('|').ElementAtOrDefault(1)?.Trim()}: " +
                                $"{cells} cells, header has {expected}");
                }

                Assert.True(expected > 0, sheet + ": no table header found");
                Assert.True(bad.Count == 0,
                    $"{sheet}: {bad.Count} row(s) have the wrong column count, which means a raw " +
                    "pipe inside a cell. Escape it or the Prefix and Suffix shift one column " +
                    "right:\n  " + string.Join("\n  ", bad.Take(8)));
            }
        }
}
}
