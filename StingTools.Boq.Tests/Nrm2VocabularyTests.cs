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

        [Fact]
        public void External_Works_Have_Sections_Of_Their_Own()
        {
            // The scheme had none. Roads, paving, kerbs, fencing and soft landscaping all
            // carried 4, so they printed under a heading reading "Foundations" — a bill a
            // reader would take at face value, because a foundations section on a project
            // with foundations is not suspicious.
            var vocab = Vocabulary();
            Assert.Equal("External works — roads, paving and kerbs", vocab["40"]);
            Assert.Equal("Fencing, gates and barriers", vocab["41"]);
            Assert.Equal("Soft landscaping", vocab["42"]);
        }

        [Fact]
        public void No_Exterior_Improvement_Row_Bills_As_Foundations()
        {
            // CSI division 32 is Exterior Improvements. Nothing in it is a foundation.
            // Division 31 is deliberately NOT covered: it is Site Preparation, and its
            // piling and shoring rows bill at 4 correctly.
            var root = RepoRoot(MapCsv).FullName;
            string[] wrong = File.ReadAllLines(Path.Combine(root, MapCsv))
                .Where(l => !l.StartsWith("#") && !l.StartsWith("Category,"))
                .Select(l => l.Split(','))
                .Where(f => f.Length >= 7 && f[4].Trim().StartsWith("32 ") && f[6].Trim() == "4")
                .Select(f => $"{f[4].Trim()} {f[5].Trim()}")
                .ToArray();

            Assert.True(wrong.Length == 0,
                "exterior-improvement rows still billing under 4 Foundations: " +
                string.Join("; ", wrong));
        }

        [Fact]
        public void The_Mixed_Fence_And_Balustrade_Rule_Was_Split()
        {
            // One rule matched fence|gate|balustrade and gave all three one code. A
            // balustrade is a railing, not a fence; they belong in different sections and
            // no single answer was right. Matching is score-based rather than positional,
            // so the split rule was APPENDED — the review sheet carries row numbers, and
            // inserting mid-file would have renumbered every row after it.
            string path = Path.Combine(AppContext.BaseDirectory, "Data",
                                       "STING_CSI_MASTERFORMAT_MAP.csv");
            Assert.True(File.Exists(path), "shipped map not copied to the test output: " + path);
            var rules = StingTools.Core.Classification.CsiMasterFormat
                                  .ParseCsvLines(File.ReadAllLines(path));

            var fence = StingTools.Core.Classification.CsiMasterFormat.Resolve(
                rules, "Generic Models", "Boundary Fence 1800mm", "Galvanised", null);
            Assert.NotNull(fence);
            Assert.Equal("32 31 00", fence.Section);
            Assert.Equal("41", fence.Nrm2);

            var balustrade = StingTools.Core.Classification.CsiMasterFormat.Resolve(
                rules, "Generic Models", "Glass Balustrade", "Frameless", null);
            Assert.NotNull(balustrade);
            Assert.Equal("05 52 00", balustrade.Section);
            Assert.Equal("20", balustrade.Nrm2);
        }

        [Fact]
        public void Entourage_Never_Reaches_Takeoff()
        {
            // Entourage is Revit's presentation context — the cars, people and trees that
            // make a render read as a place. Nobody buys it. Unlike the 2D content in the
            // same exclusion set it is real 3D geometry, so it does not arrive looking like
            // noise: it prices as plausible "each" rows, and it was classified in the CSI
            // map as Site Improvements, which is how it survived.
            //
            // The Nrm2 column cannot express this — every element that reaches takeoff gets
            // a section, from the rule or from DeriveNrm2Section's keyword fallback. "Not
            // measured" is a collection decision, so it is enforced at collection.
            string src = File.ReadAllText(
                Path.Combine(RepoRoot(CostManager).FullName, CostManager));
            var block = Regex.Match(src, @"_defaultExcludedBic\s*=\s*new HashSet<BuiltInCategory>\s*\{(.*?)\}",
                                    RegexOptions.Singleline);
            Assert.True(block.Success,
                "_defaultExcludedBic not found in " + CostManager + " — this test cannot " +
                "pass vacuously, so the parse must be updated if the field moved.");
            Assert.Contains("OST_Entourage", block.Groups[1].Value);
        }

        [Fact]
        public void Both_Gas_Rows_Bill_Under_The_Same_Section()
        {
            // One gas installation, two rules: the in-building run (CSI 23 11 23) and the
            // buried site main (33 51 00). Which one an element matches turns on whether
            // its pipe TYPE NAME happens to contain "buried"/"external"/"underground" —
            // so if the two rows carry different sections, the same installation bills
            // under two headings on a naming accident. The pairing is the invariant; the
            // value they share is the judgement (both moved 33 -> 32 on verification,
            // section 32 being "Piped supply systems", which is what a gas main is).
            var root = RepoRoot(MapCsv).FullName;
            string[] codes = File.ReadAllLines(Path.Combine(root, MapCsv))
                .Where(l => !l.StartsWith("#"))
                .Select(l => l.Split(','))
                .Where(f => f.Length >= 7 && f[3].Trim() == "GAS")
                .Select(f => f[6].Trim())
                .ToArray();

            Assert.Equal(2, codes.Length);
            Assert.True(codes.Distinct().Count() == 1,
                "the in-building and buried gas rules bill under different sections (" +
                string.Join(" vs ", codes) + ") — the same installation would bill two ways " +
                "depending on whether the pipe type name says 'buried'.");
        }

        [Fact]
        public void Every_Section_Has_A_Class_In_Both_Cross_Walks()
        {
            // A bill can be issued under CESMM4 or POMI, and both cross-walk the section
            // code to their own class. Both used a `_ => "Z"` default, so a section they
            // did not know became Miscellaneous with nothing logged — and nine defined
            // sections were unmapped, which meant groundworks, both drainage sections,
            // carpentry, finishes and fittings all collapsed into one undifferentiated
            // class on a bill that looked complete.
            //
            // The cross-walks are Revit-dependent, so this asserts over their SOURCE, the
            // way the vocabulary itself is read. A `Z` written deliberately is a
            // classification; a `Z` reached by omission is a silent failure, and the two
            // are indistinguishable in the output — so completeness is the only thing
            // that can be checked from outside.
            const string Standards = "StingTools/BOQ/MeasurementStandard/MeasurementStandards.cs";
            string src = File.ReadAllText(
                Path.Combine(RepoRoot(Standards).FullName, Standards));

            foreach (string mapName in new[] { "Cesmm4ByCode", "PomiByCode" })
            {
                var block = Regex.Match(src, mapName + @"\s*=[^{]*\{(.*?)\n        \};",
                                        RegexOptions.Singleline);
                Assert.True(block.Success, mapName + " not found in " + Standards +
                                           " — this test cannot pass vacuously.");
                var mapped = Regex.Matches(block.Groups[1].Value, "\\[\"(\\d+)\"\\]")
                                  .Cast<Match>().Select(m => m.Groups[1].Value).ToHashSet();
                Assert.True(mapped.Count > 0, mapName + " parsed but yielded no codes.");

                string[] missing = Vocabulary().Keys.Where(c => !mapped.Contains(c))
                                                    .OrderBy(int.Parse).ToArray();
                Assert.True(missing.Length == 0,
                    mapName + " has no class for section(s) " + string.Join(", ", missing) +
                    " — they would fall through to the miscellaneous class, collapsing a " +
                    "whole trade with nothing said.");
            }
        }
    }
}
