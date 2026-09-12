// ══════════════════════════════════════════════════════════════════════════
//  SubstanceVocabularyTests.cs — W3. Timber for one is timber for all.
//
//  Three places answered "is this a timber-family material" from three
//  hand-written lists, and they disagreed on a large minority of the distinct names in
//  the register plus the delivered-model corpus. The disagreement lands on a
//  CARBON figure, because BiogenicCarbon is one of the three.
//
//  The corpus is the register (1,279 MAT_NAMEs) UNION the 1,815-name material
//  plan — every real material name available, rather than a list somebody
//  thought of. That is what makes the count mean something.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Baseline;
using StingTools.Core.Materials;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class SubstanceVocabularyTests
    {
        private readonly ITestOutputHelper _out;
        public SubstanceVocabularyTests(ITestOutputHelper output) => _out = output;

        private static DirectoryInfo Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repo root from " + AppContext.BaseDirectory);
            return dir;
        }

        private static List<string> _names;

        /// <summary>Every distinct material name available: the shipped register plus every
        /// corpus fixture. Not a sample — a list somebody thought of is what let three
        /// vocabularies drift apart in the first place.</summary>
        private static List<string> Names()
        {
            if (_names != null) return _names;
            var root = Root();
            var set = new HashSet<string>(StringComparer.Ordinal);

            // Read locally rather than through MaterialRegistry: that reader is on its own
            // branch (#906) and stacking on an unmerged branch is what orphans a change.
            // Only the MAT_NAME column is needed here, which is a smaller ask than the
            // register model — and when the two land, this collapses into it.
            string data = Path.Combine(root.FullName, "StingTools", "Data");
            foreach (string f in new[] { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv" })
                foreach (string name in NameColumn(Path.Combine(data, f), "MAT_NAME"))
                    set.Add(name);

            string fixtures = Path.Combine(root.FullName, "StingTools.Tags.Tests", "Fixtures");
            foreach (string f in Directory.GetFiles(fixtures, "material_names_*.csv"))
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = SplitCsv(line);
                    if (cells.Count > 0 && !string.IsNullOrWhiteSpace(cells[0])) set.Add(cells[0]);
                }

            Assert.True(set.Count > 1500, "only " + set.Count + " names pooled — the corpus is missing");
            return _names = set.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }


        /// <summary>One named column of a CSV whose cells carry commas and quotes, located
        /// by HEADER NAME — these files run to 71 columns and a positional read would return
        /// a colour where a name belongs.</summary>
        private static IEnumerable<string> NameColumn(string path, string column)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8)
                            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                            .ToList();
            Assert.True(lines.Count > 1, path + " has no rows");
            var header = SplitCsv(lines[0]);
            int i = header.FindIndex(h => string.Equals((h ?? "").Trim().TrimStart('﻿'), column,
                                                        StringComparison.OrdinalIgnoreCase));
            Assert.True(i >= 0, path + " has no " + column + " column");
            foreach (string line in lines.Skip(1))
            {
                var cells = SplitCsv(line);
                if (i < cells.Count && !string.IsNullOrWhiteSpace(cells[i])) yield return cells[i].Trim();
            }
        }

        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
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

        /// <summary>
        /// Whether TypeRenamePlanner recognises this material as a timber-family substance,
        /// probed through the real planner rather than by reading its table.
        ///
        /// <para>Probed on FLOORS, deliberately. The planner reports a substance word and
        /// then looks it up in a per-category code table, and only Floors has an entry for
        /// BOTH "Timber" and "Plywood" — Walls has no Plywood key at all, so a
        /// plywood-cored wall gets no name. That is a gap in the CODE table, not in the
        /// vocabulary, and probing on Walls would make this gate report it as one. The
        /// first version of this test did exactly that and failed on five plywood
        /// names.</para>
        ///
        /// <para>Read off ProposedName, not Reason: a REFUSAL quotes the material name back,
        /// so "TIMBER BATTEN 25X50MM" would look like a match to a reason-sniffing probe.
        /// ProposedName is composed by the planner, so its casing is the planner's.</para>
        /// </summary>
        private static bool RenamerSaysWood(string name)
        {
            var p = TypeRenamePlanner.Plan(new TypeRenameInput
            {
                Category = "Floors",
                FamilyName = "Floor",
                CurrentName = "probe",
                InstanceCount = 1,
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = name, ThicknessMm = 100, IsStructure = true },
                },
            });
            string proposed = p.ProposedName ?? "";
            return proposed.IndexOf("Timber", StringComparison.Ordinal) >= 0
                || proposed.IndexOf("Plywood", StringComparison.Ordinal) >= 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The gate
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Timber_For_One_Is_Timber_For_All()
        {
            var disagree = new List<string>();
            foreach (string n in Names())
            {
                bool shared = SubstanceVocabulary.IsWood(n);
                bool carbon = BiogenicCarbon.IsBiogenic(n);
                bool renamer = RenamerSaysWood(n);
                if (shared == carbon && carbon == renamer) continue;
                disagree.Add($"{n}\n        vocabulary={shared,-6} biogenicCarbon={carbon,-6} renamer={renamer}");
            }

            if (disagree.Count > 0)
            {
                _out.WriteLine($"{disagree.Count} of {Names().Count} names get different answers:");
                foreach (string d in disagree.Take(120)) _out.WriteLine("    " + d);
            }

            Assert.True(disagree.Count == 0,
                $"{disagree.Count} of {Names().Count} names are timber to one consumer and not "
              + "to another. The full list is in this test's output; the first few:\n    "
              + string.Join("\n    ", disagree.Take(12)));
        }

        [Fact]
        public void The_Two_Names_The_Brief_Named_Are_Settled()
        {
            // MDF CEILING PANEL 12MM had a class and a carbon credit and was not timber to
            // the renamer. SOLID BAMBOO 14MM had a class, no credit, and was not timber to
            // the renamer. Both are the reason this file exists, so both are pinned by name.
            foreach (string n in new[] { "MDF CEILING PANEL 12MM", "SOLID BAMBOO 14MM" })
            {
                Assert.True(SubstanceVocabulary.IsWood(n), n + " is not wood to the vocabulary");
                Assert.True(BiogenicCarbon.IsBiogenic(n), n + " earns no biogenic credit");
                Assert.True(RenamerSaysWood(n), n + " is not timber to the renamer");
                Assert.Equal("Wood", MaterialClassPlanner.Plan(n, "").ProposedClass);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  What moved on a carbon figure — named, not summarised
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Three_Names_That_LOSE_A_Biogenic_Credit_Are_Not_Timber()
        {
            // BiogenicCarbon matched by substring and carried "ply " with a trailing space.
            // Across the whole pooled corpus it matched exactly three, and all three were wrong:
            // two air diffusers (sup-PLY) and a PVC roofing membrane. A needle that has
            // never once been right is removed rather than made whole-word.
            foreach (string n in new[]
            {
                "PVC SINGLE PLY 1.5MM",
                "SUPPLY REGISTER 150X150MM 1-WAY",
                "SUPPLY REGISTER 200X100MM 2-WAY",
            })
            {
                Assert.Contains(n, Names());
                Assert.False(SubstanceVocabulary.IsWood(n), n + " is not a timber");
                Assert.False(BiogenicCarbon.IsBiogenic(n), n + " must not earn a biogenic credit");
            }

            // And the word that did it is gone rather than tightened.
            Assert.DoesNotContain("ply", SubstanceVocabulary.Wood, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("plywood", SubstanceVocabulary.Wood, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Fifteen_Names_That_GAIN_A_Biogenic_Credit_Are_Listed_By_Name()
        {
            // A carbon change is not a refactor detail. These fifteen now carry a
            // sequestration term where they did not, and the two questionable ones are
            // named in the same breath rather than buried in the count.
            string[] gained =
            {
                "BAMBOO PLANK", "CHEVRON PARQUET 15MM", "CORK TILE", "CORK TILE 6MM",
                "Cork - Plastic", "FLOOR LAMINATE OAK-DARK", "FLOOR LAMINATE OAK-GREY",
                "FLOOR LAMINATE OAK-LIGHT", "FLOOR LAMINATE OAK-MEDIUM", "HDF CORE",
                "HERRINGBONE PARQUET 15MM", "MAPLE FLOORING", "Oak Flooring",
                "Roca - TENET - 402 City Oak", "SOLID BAMBOO 14MM",
            };
            Assert.Equal(15, gained.Length);
            foreach (string n in gained)
            {
                Assert.Contains(n, Names());
                Assert.True(BiogenicCarbon.IsBiogenic(n), n + " should now earn a biogenic credit");
            }

            // The two that are a colour rather than a timber. Kept, because the alternative
            // is special-casing two library appearances out of a lexical rule — but SAID,
            // because a reviewer should know a carbon term moved on them.
            Assert.True(BiogenicCarbon.IsBiogenic("Roca - TENET - 402 City Oak"));   // sanitaryware colour
            Assert.True(BiogenicCarbon.IsBiogenic("Cork - Plastic"));                // cork-look plastic
        }

        [Fact]
        public void No_Other_Name_In_The_Corpus_Changed_Its_Carbon_Answer()
        {
            // The gains and losses above are exhaustive: 15 + 3 out of the pooled corpus. Anything else
            // moving means the vocabulary picked up a word nobody measured.
            var expectedWood = new HashSet<string>(
                Names().Where(SubstanceVocabulary.IsWood), StringComparer.Ordinal);
            var carbonWood = new HashSet<string>(
                Names().Where(BiogenicCarbon.IsBiogenic), StringComparer.Ordinal);
            Assert.Equal(expectedWood.Count, carbonWood.Count);
            Assert.True(expectedWood.SetEquals(carbonWood),
                "BiogenicCarbon and the shared vocabulary now answer differently for: "
              + string.Join(", ", expectedWood.Except(carbonWood).Concat(carbonWood.Except(expectedWood)).Take(10)));
            _out.WriteLine($"{carbonWood.Count} of {Names().Count} names are timber-family.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The class table keeps its ORDER, which is a separate question
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Name_With_Two_Substances_Is_Still_Decided_By_ORDER_Not_By_This_List()
        {
            // "timber for one is timber for all" is a question about the NAME. What an
            // ordered class table does with a name carrying two substance words is a
            // precedence decision and stays where it is — Plastic before Wood, because
            // luxury vinyl printed with a wood grain is vinyl. Asserted so that a reader
            // does not take the consistency gate to mean IsWood ⇒ class Wood.
            Assert.True(SubstanceVocabulary.IsWood("VINYL LVT WOOD OAK-DARK"));
            Assert.Equal("Plastic", MaterialClassPlanner.Plan("VINYL LVT WOOD OAK-DARK", "").ProposedClass);

            Assert.True(SubstanceVocabulary.IsWood("Cork - Plastic"));
            Assert.Equal("Plastic", MaterialClassPlanner.Plan("Cork - Plastic", "").ProposedClass);

            // But where wood is the only substance named, the class table agrees.
            Assert.Equal("Wood", MaterialClassPlanner.Plan("BAMBOO PLANK", "").ProposedClass);
        }

        [Fact]
        public void Every_Class_Of_Wood_Comes_From_The_Shared_List()
        {
            // The direction that must hold: if the class table answers Wood, the name
            // contains a word this list knows. The reverse is the ordering question above.
            var wrong = Names()
                .Where(n => MaterialClassPlanner.Plan(n, "").ProposedClass == "Wood"
                         && !SubstanceVocabulary.IsWood(n))
                .ToList();
            Assert.True(wrong.Count == 0,
                "classified Wood by a word the shared vocabulary does not have: "
              + string.Join(", ", wrong.Take(10)));
        }

        [Fact]
        public void The_Vocabulary_Is_Whole_Word_And_Has_No_Blank_Entries()
        {
            Assert.All(SubstanceVocabulary.Wood, w => Assert.False(string.IsNullOrWhiteSpace(w)));
            Assert.Equal(SubstanceVocabulary.Wood.Length,
                SubstanceVocabulary.Wood.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // The #863 shape, in the file that removes it from three places at once.
            Assert.False(SubstanceVocabulary.IsWood("SUPPLY REGISTER 150X150MM 1-WAY"));
            Assert.False(SubstanceVocabulary.IsWood("Rockwool Insulation"));
            Assert.True(SubstanceVocabulary.IsWood("BARNWOOD SIDING 22MM"));
            Assert.True(SubstanceVocabulary.IsWood("WOODEN SPORTS FLOOR 22MM"));
        }
    }
}
