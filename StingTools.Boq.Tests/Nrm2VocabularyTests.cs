using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// The `Nrm2` column and the bill headings are two halves of one fact, held in two
    /// files that nothing was keeping in agreement.
    ///
    /// <para>`GuessSectionName` in <c>BOQCostManager.cs</c> turns the integer into the
    /// heading a reader sees. A code it does not name falls through to the default and
    /// prints the raw Revit <em>category</em> instead — so the section does not fail, it
    /// quietly stops being a section, and the money in it is billed under a heading like
    /// "Generic Models".</para>
    ///
    /// <para>Both directions were live defects. Codes 3 (Groundworks) and 31 (Drainage
    /// below ground) were named here and used on no row — the earthworks and
    /// below-ground drainage they belong to were billed as "Foundations" and "Piped
    /// supply systems" instead, and `qs_nrm2_review.py` refused to write them because
    /// its validity test was "does another row already use it", which no unused code can
    /// ever satisfy. That is now fixed at the review tool; this pins the other
    /// direction, which nothing was watching.</para>
    ///
    /// <para>These are data regression locks over two shipped files, not arithmetic —
    /// which is why they read the sources rather than calling anything.
    /// `GuessSectionName` is private, so it is parsed, exactly as
    /// `tools/qs_nrm2_review.py` parses it for the same reason. A parse that stops
    /// matching must fail LOUDLY: a silent empty set would turn every assertion below
    /// into a vacuous pass.</para>
    /// </summary>
    public class Nrm2VocabularyTests
    {
        private static DirectoryInfo RepoRoot(string probe)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, probe)))
                dir = dir.Parent;
            Assert.True(dir != null, probe + " not found above " + AppContext.BaseDirectory);
            return dir;
        }

        private const string CostManager = "StingTools/BOQ/BOQCostManager.cs";
        private const string MapCsv = "StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv";

        /// <summary>Code → heading, parsed from GuessSectionName's switch.</summary>
        private static Dictionary<string, string> Vocabulary()
        {
            string src = File.ReadAllText(
                Path.Combine(RepoRoot(CostManager).FullName, CostManager));
            var block = Regex.Match(
                src, @"GuessSectionName\s*\([^)]*\)\s*\{(.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(block.Success,
                "GuessSectionName not found in " + CostManager + " — it is the source of truth " +
                "for what a work-section code means. If it moved or was renamed, update this " +
                "parse rather than letting the assertions below pass vacuously.");

            var codes = Regex.Matches(block.Groups[1].Value,
                                      "case\\s+\"(\\d+)\"\\s*:\\s*return\\s+\"([^\"]+)\"\\s*;")
                             .Cast<Match>()
                             .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            Assert.True(codes.Count > 0, "GuessSectionName parsed but yielded no cases — the " +
                                         "switch shape changed and this test is now blind.");
            return codes;
        }

        /// <summary>Every non-empty Nrm2 value the map carries, with its row title.</summary>
        private static List<(string Code, string Title)> CodesUsed()
        {
            var root = RepoRoot(MapCsv).FullName;
            var used = new List<(string, string)>();
            foreach (string line in File.ReadAllLines(Path.Combine(root, MapCsv)))
            {
                if (line.StartsWith("#") || line.StartsWith("Category,")) continue;
                string[] f = line.Split(',');
                if (f.Length < 7) continue;
                string code = f[6].Trim();
                if (code.Length > 0) used.Add((code, f.Length > 5 ? f[5].Trim() : ""));
            }
            Assert.True(used.Count > 0, "no Nrm2 values read from " + MapCsv);
            return used;
        }

        [Fact]
        public void Every_Code_The_Map_Bills_Under_Has_A_Heading()
        {
            var vocab = Vocabulary();
            string[] orphaned = CodesUsed()
                .Where(u => !vocab.ContainsKey(u.Code))
                .Select(u => $"{u.Code} ({u.Title})")
                .Distinct()
                .ToArray();

            Assert.True(orphaned.Length == 0,
                "These rows bill under a code GuessSectionName does not name, so they print " +
                "the raw Revit category instead of a section heading: " +
                string.Join(", ", orphaned));
        }

        [Fact]
        public void Groundworks_And_Below_Ground_Drainage_Are_Reachable()
        {
            // Both were defined-but-unused, and the review tool could not write them:
            // its test was "does another row use it", which is unsatisfiable for a code
            // used nowhere. If these fall back to 0 the trap has reopened.
            var used = CodesUsed().Select(u => u.Code).ToHashSet();
            var vocab = Vocabulary();

            Assert.Equal("Groundworks", vocab["3"]);
            Assert.Equal("Drainage below ground", vocab["31"]);
            Assert.Contains("3", used);
            Assert.Contains("31", used);
        }

        [Fact]
        public void Below_Ground_Drainage_Is_Not_Billed_As_A_Piped_Supply_System()
        {
            // Foul, surface-water and subsoil drainage sat at 32 Piped supply systems.
            // A drain is not a supply: the quantities are measured differently and the
            // money lands in the wrong section of an issued tender, plausibly either way.
            //
            // Named by SECTION, not by division. CSI division 33 is Utilities, which
            // holds water SUPPLY as well as drainage -- 33 11 00 Water Utility
            // Distribution Piping and 33 16 00 Water Utility Storage Tanks are correctly
            // 32, and a blanket "no division-33 row may be 32" rule would call those
            // defects. It did, on the first run of this test; the data was right and the
            // rule was wrong.
            var drainage = new HashSet<string>
            {
                "33 30 00", // Sanitary Sewerage Utilities
                "33 31 00", // Sanitary Utility Sewerage Piping
                "33 36 00", // Utility Septic Tanks
                "33 39 00", // Sanitary Utility Sewerage Structures
                "33 41 00", // Storm Utility Drainage Piping
                "33 44 00", // Storm Utility Water Drains
                "33 46 00", // Subdrainage
            };

            var root = RepoRoot(MapCsv).FullName;
            var rows = File.ReadAllLines(Path.Combine(root, MapCsv))
                .Where(l => !l.StartsWith("#") && !l.StartsWith("Category,"))
                .Select(l => l.Split(','))
                .Where(f => f.Length >= 7 && drainage.Contains(f[4].Trim()))
                .ToArray();

            Assert.Equal(drainage.Count, rows.Length);

            var wrong = rows.Where(f => f[6].Trim() != "31")
                            .Select(f => $"{f[4].Trim()} {f[5].Trim()} = {f[6].Trim()}")
                            .ToArray();

            Assert.True(wrong.Length == 0,
                "below-ground drainage sections must bill at 31 Drainage below ground, not " +
                "as a piped supply: " + string.Join("; ", wrong));
        }
    }
}
