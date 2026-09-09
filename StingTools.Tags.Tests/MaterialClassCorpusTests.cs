// ══════════════════════════════════════════════════════════════════════════
//  MaterialClassCorpusTests.cs — the material-class map, checked against a
//  whole delivered model instead of against fourteen names somebody thought of.
//
//  WHY THIS EXISTS. On 2026-09-08 Materials_SetClass planned 1,815 materials in
//  a live project: 1,524 already classified and left alone, 120 to set, 171
//  refused. The user applied it. Forty-one of the 120 were wrong, and they are
//  now in the model — see MaterialClassRevertPlanner for the way back out.
//
//  Both defects were invisible to a hand-written test table:
//
//    * 32 materials got Gypsum "because the name contains 'render'". Six were
//      _ACCURENDER library appearances, where "render" is a substring of the
//      library's name. Twenty-six were DWG placeholders called
//      "Render Material 128-128-128", where "render" is a whole word meaning
//      the renderer. Whole-word matching fixes the six and none of the 26.
//    * 9 materials got Ceramic because the name contains "tile". CARPET TILE,
//      CORK TILE, RUBBER TILE, VINYL TILE and five stone tiles. The same model
//      called GRANITE SLAB Stone and GRANITE TILE Ceramic — one substance, two
//      answers, because a form word was ranked above a substance word.
//
//  A table of names an author invents cannot find either of those, because an
//  author writes down the cases they already have in mind. A real corpus can,
//  and did: running it also surfaced "ducTILE iron" and "texTILE" reading as
//  Ceramic, "VINYL LVT WOOD OAK" wanting to read as Wood, and eight needles
//  ("cobblestone", "bluestone", "cpvc", "polyvinyl", "painted", "zincalume",
//  "tiles", "mdf") that only ever worked as substrings and would have been lost
//  in silence the moment matching became whole-word. None of those eight was on
//  anybody's list beforehand.
//
//  THE FIXTURE. Fixtures/material_names_20260908.csv carries all 1,815 names
//  with a pinned expectation and its provenance:
//
//    correction        41   the wrong writes, with what they must say instead
//    plan-write        79   writes that were right, and must stay right
//    plan-refusal     149   names that say no substance, and must stay blank
//    named-substance   22   names that DO say a substance the map had no word
//                           for (SLATE SLAB, MAPLE FLOORING, LVT PLANK, Chrome…)
//    agrees-with-model 406  already-classified materials where the map's answer
//                           equals the class a human independently set. Free,
//                           independent evidence — and the only thing that would
//                           have caught the eight lost needles.
//    unpinned        1118   no evidence either way; asserted only against
//                           "the class is one Revit actually has".
//
//  Names only, plus the class expected of each. No project data.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MaterialClassCorpusTests
    {
        // ══════════════════════════════════════════════════════════════════════
        //  1. The forty-one bad writes, named one by one
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Every row here was written into a real model with the class in the comment.
        /// Exact strings, because a paraphrase of a material name is not evidence.
        /// </summary>
        [Theory]
        // ── 32 that read "render" ──────────────────────────────────────────────
        //  Six AccuRender library appearances: "render" is part of _ACCURENDER.
        //  Whole-word matching is what these are for. The chrome one goes on to
        //  match a real substance word, which is why it is Metal and not blank.
        [InlineData(@"_ACCURENDER\Metals\Chrome\Polished,Plain", "Metal")]
        [InlineData(@"_ACCURENDER\Plastics\Reds and Oranges\Orange, Red,Light,Transparent", null)]
        [InlineData(@"_ACCURENDER\Plastics\Reds and Oranges\Orange,Light,Transparent", null)]
        [InlineData(@"_ACCURENDER\Plastics\White,Transparent", null)]
        [InlineData(@"_ACCURENDER\Solid Colors\Black,Matte", null)]
        [InlineData(@"_ACCURENDER\Solid Colors\Whites\Pure,Glossy", null)]
        //  Twenty-six DWG-import RGB placeholders. Here "render" IS a whole word
        //  and still means nothing about substance — NamesNoSubstance, not matching.
        [InlineData("Render Material 0-0-0", null)]
        [InlineData("Render Material 0-0-255", null)]
        [InlineData("Render Material 0-127-255", null)]
        [InlineData("Render Material 0-255-0", null)]
        [InlineData("Render Material 0-255-255", null)]
        [InlineData("Render Material 0-41-165", null)]
        [InlineData("Render Material 118-118-118", null)]
        [InlineData("Render Material 127-0-0", null)]
        [InlineData("Render Material 127-255-255", null)]
        [InlineData("Render Material 128-128-128", null)]
        [InlineData("Render Material 152-152-152", null)]
        [InlineData("Render Material 153-153-153", null)]
        [InlineData("Render Material 165-0-0", null)]
        [InlineData("Render Material 165-124-82", null)]
        [InlineData("Render Material 165-82-82", null)]
        [InlineData("Render Material 186-186-186", null)]
        [InlineData("Render Material 192-192-192", null)]
        [InlineData("Render Material 255-0-0", null)]
        [InlineData("Render Material 255-0-255", null)]
        [InlineData("Render Material 255-127-127", null)]
        [InlineData("Render Material 255-255-0", null)]
        [InlineData("Render Material 255-255-255", null)]
        [InlineData("Render Material 38-0-0", null)]
        [InlineData("Render Material 38-38-19", null)]
        [InlineData("Render Material 82-145-165", null)]
        [InlineData("Render Material 84-84-84", null)]
        // ── 9 that read "tile" before the substance word ───────────────────────
        [InlineData("CARPET TILE", "Textile")]
        [InlineData("CORK TILE", "Wood")]
        [InlineData("RUBBER TILE", "Plastic")]
        [InlineData("VINYL TILE", "Plastic")]
        [InlineData("GRANITE TILE", "Stone")]
        [InlineData("LIMESTONE TILE", "Stone")]
        [InlineData("MARBLE TILE", "Stone")]
        [InlineData("SLATE TILE", "Stone")]
        [InlineData("TRAVERTINE TILE", "Stone")]
        public void The_Forty_One_Bad_Writes_Say_Something_Else_Now(string name, string expected)
        {
            Assert.Equal(expected, MaterialClassPlanner.Plan(name, "").ProposedClass);
        }

        [Fact]
        public void A_Tile_With_No_Substance_Word_Is_Still_Ceramic()
        {
            // The point of demoting "tile" is not to stop it answering — it is to make it
            // answer LAST. VITRIFIED TILE was one of the ten "tile" hits and the only
            // one that was right; it must stay right, or the fix has traded 9 wrong
            // answers for 1.
            Assert.Equal("Ceramic", MaterialClassPlanner.Plan("VITRIFIED TILE", "").ProposedClass);
            Assert.Equal("Ceramic", MaterialClassPlanner.Plan("HEXAGONAL TILES 150MM", "").ProposedClass);
        }

        [Fact]
        public void A_Word_Is_Not_A_Run_Of_Letters()
        {
            // The #863 defect, in the same table it was fixed in once before. Each of
            // these was a live wrong answer in the delivered model.
            Assert.Equal("Metal", MaterialClassPlanner.Plan("DUCTILE IRON GATE VALVE 150MM (6 INCH)", "").ProposedClass);
            Assert.Equal("Textile", MaterialClassPlanner.Plan("Lining - Textile", "").ProposedClass);
            Assert.Equal("Textile", MaterialClassPlanner.Plan("Textile - Slate Blue", "").ProposedClass);
        }

        [Fact]
        public void The_Finish_Names_The_Substance_Not_The_Pattern_It_Imitates()
        {
            // Thirty-one materials in the corpus are luxury vinyl printed with a wood or
            // stone grain. If Wood or Stone were evaluated before Plastic, every one of
            // them would be classified as the thing it is pretending to be.
            Assert.Equal("Plastic", MaterialClassPlanner.Plan("VINYL LVT WOOD OAK-DARK", "").ProposedClass);
            Assert.Equal("Plastic", MaterialClassPlanner.Plan("VINYL LVT STONE TRAVERTINE", "").ProposedClass);
            Assert.Equal("Plastic", MaterialClassPlanner.Plan("LVT PLANK", "").ProposedClass);
            // And the same rule that already made a porcelain wood-look floor Ceramic.
            Assert.Equal("Ceramic", MaterialClassPlanner.Plan("FLOOR PORCELAIN WOOD-OAK", "").ProposedClass);
        }

        [Fact]
        public void A_Rubber_Membrane_Is_Still_A_Membrane()
        {
            // Adding "rubber" to the Plastic group is what makes RUBBER TILE work. It
            // would also quietly have demoted the one EPDM roofing membrane in the
            // corpus, which is why Membrane is evaluated first.
            Assert.Equal("Membrane", MaterialClassPlanner.Plan("EPDM RUBBER MEMBRANE 1.5MM", "").ProposedClass);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. The whole corpus
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Every_Pinned_Row_In_The_Corpus_Holds()
        {
            var rows = Corpus();
            var misses = new List<string>();

            foreach (var r in rows)
            {
                if (r.Expect == "ANY") continue;
                string want = r.Expect == "NONE" ? null : r.Expect;
                string got = MaterialClassPlanner.Plan(r.Material, "").ProposedClass;
                if (!string.Equals(want, got, StringComparison.Ordinal))
                    misses.Add($"[{r.Source}] {r.Material}  want={want ?? "(blank)"}  got={got ?? "(blank)"}"
                             + $"   ({r.File})");
            }

            if (misses.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"{misses.Count} of {rows.Count(x => x.Expect != "ANY")} pinned rows disagree.");
            foreach (var g in misses.GroupBy(m => m.Substring(0, m.IndexOf(']') + 1)).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Key} {g.Count()}");
            sb.AppendLine();
            foreach (string m in misses.Take(80)) sb.AppendLine("  " + m);
            if (misses.Count > 80) sb.AppendLine($"  … and {misses.Count - 80} more");
            Assert.Fail(sb.ToString());
        }

        [Fact]
        public void No_Name_In_The_Corpus_Gets_A_Class_Revit_Does_Not_Have()
        {
            // A class outside Revit's own vocabulary would not fail anything at write
            // time — Revit accepts free text in the field — it would just fragment the
            // grouping the carbon and cost engines rely on.
            var known = new HashSet<string>(MaterialClassPlanner.RevitClasses, StringComparer.Ordinal);
            var bad = Corpus()
                .Select(r => MaterialClassPlanner.Plan(r.Material, "").ProposedClass)
                .Where(c => c != null && !known.Contains(c))
                .Distinct().ToList();
            Assert.True(bad.Count == 0, "Classes outside Revit's vocabulary: " + string.Join(", ", bad));
        }

        [Fact]
        public void The_Fixture_Is_The_Whole_Run_Not_A_Sample()
        {
            // 1,815 materials is what the 2026-09-08 plan reported. A fixture that quietly
            // shrank would make this whole file weaker without failing anything.
            //
            // Scoped to THAT file by name, deliberately. The loader now globs, and a
            // union-wide count would move every time a corpus is added — which is exactly
            // the assertion that stops meaning anything. Per-file counts do not move.
            var rows = Corpus().Where(r => r.File == "material_names_20260908.csv").ToList();
            Assert.Equal(1815, rows.Count);

            var bySource = rows.GroupBy(r => r.Source).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(41, bySource["correction"]);
            Assert.Equal(79, bySource["plan-write"]);
            Assert.Equal(149, bySource["plan-refusal"]);
            Assert.Equal(22, bySource["named-substance"]);
            // 120 writes and 171 refusals, exactly as the plan reported them.
            Assert.Equal(120, bySource["correction"] + bySource["plan-write"]);
            Assert.Equal(171, bySource["plan-refusal"] + bySource["named-substance"]);
        }

        [Fact]
        public void Every_Corpus_File_In_Fixtures_Is_Actually_Being_Read()
        {
            // The point of globbing: dropping the next project's list in must strengthen
            // this file without anybody editing it. So assert that what is ON DISK is what
            // was read — a glob that silently matched nothing, or matched one of three,
            // would leave the tests passing on less evidence than the repo contains.
            var onDisk = Directory.GetFiles(FixtureDir(), "material_names_*.csv")
                                  .Select(Path.GetFileName).OrderBy(f => f).ToList();
            Assert.Equal(onDisk, CorpusFiles());
            Assert.Contains("material_names_20260908.csv", CorpusFiles());

            // And every file contributes pinned rows, or it is decoration.
            foreach (string f in CorpusFiles())
                Assert.True(Corpus().Any(r => r.File == f && r.Expect != "ANY"),
                    f + " contributes no pinned row — it asserts nothing.");
        }

        [Fact]
        public void An_Existing_Class_Still_Wins_Over_Every_Needle()
        {
            // The corpus is planned as if nothing were classified, which is the only way
            // to test the map. Rule 1 is what makes that safe in production, so it is
            // asserted against the corpus too rather than assumed.
            foreach (var r in Corpus().Take(200))
                Assert.Null(MaterialClassPlanner.Plan(r.Material, "Concrete").ProposedClass);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Fixture
        // ══════════════════════════════════════════════════════════════════════

        private sealed class Row
        {
            public string Material, Expect, Source;
            /// <summary>Which fixture file it came from, so a failure names the corpus.</summary>
            public string File;
        }

        private static List<Row> _corpus;
        private static List<string> _corpusFiles;

        /// <summary>
        /// EVERY <c>Fixtures/material_names_*.csv</c>, not one named file.
        ///
        /// <para>This used to name <c>material_names_20260908.csv</c> in two places. The
        /// corpus is the only thing in this file that makes it stronger than a table
        /// somebody thought of, so the next delivered model's list should strengthen the
        /// guarantee by being dropped in — not by somebody remembering to edit a string
        /// here. A gate that has to be widened by hand does not get widened.</para>
        ///
        /// <para>Files are read in name order, so a date-stamped name orders itself.</para>
        /// </summary>
        private static List<Row> Corpus()
        {
            if (_corpus != null) return _corpus;

            string dir = FixtureDir();
            var files = Directory.GetFiles(dir, "material_names_*.csv").OrderBy(f => f).ToList();
            Assert.True(files.Count > 0,
                "No material_names_*.csv in " + dir + " — the corpus is what makes this file evidence.");

            var rows = new List<Row>();
            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                int n = 0;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var f = SplitCsv(line);
                    Assert.True(f.Count == 3, $"Malformed line in {name}: {line}");
                    rows.Add(new Row { Material = f[0], Expect = f[1], Source = f[2], File = name });
                    n++;
                }
                Assert.True(n > 0, name + " has a header and no rows — an empty corpus is not a passing one.");
            }
            _corpusFiles = files.Select(Path.GetFileName).ToList();
            return _corpus = rows;
        }

        private static List<string> CorpusFiles()
        {
            Corpus();
            return _corpusFiles;
        }

        private static string FixtureDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(
                       dir.FullName, "StingTools.Tags.Tests", "Fixtures")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the Fixtures directory from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools.Tags.Tests", "Fixtures");
        }

        /// <summary>
        /// Material names carry commas, backslashes and quotes, so the fixture is real
        /// RFC 4180 and needs a real reader. Splitting on ',' silently truncated
        /// "_ACCURENDER\Solid Colors\Black,Matte" the first time this was written.
        /// </summary>
        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { cur.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (c == '"' && cur.Length == 0) quoted = true;
                else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            fields.Add(cur.ToString());
            return fields;
        }
    }
}
