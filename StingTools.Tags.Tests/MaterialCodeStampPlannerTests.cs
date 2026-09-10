// ══════════════════════════════════════════════════════════════════════════
//  MaterialCodeStampPlannerTests.cs — the gate on W1.
//
//  The claim being gated: before this work, NOTHING resolved a MAT_CODE for a
//  material created by CompoundTypeCreator or inherited from an older model, so
//  RateProviders Pass C — the most specific rate lookup in the BOQ — could never
//  fire. The corpus test below is written so that the pre-change world is a
//  MEASURABLE state of the same code, not a story: run it against a register
//  that is not consulted and every one of the 1,815 names resolves no code.
//
//  RED / GREEN, recorded 2026-09-10 on this machine:
//
//    Corpus_Resolves_The_Registers_Codes
//      RED   (register lookup severed in the planner) 0 stamped / 1,815 no-row
//      GREEN (shipped register)                   1,279 stamped /   536 no-row
//
//    A_Code_Already_There_Is_Never_Overwritten
//      RED   (planner's existing-code branch removed) FLR-999 proposed -> FLR-028
//      GREEN                                          no write proposed at all
//
//  The second is the one that matters more. The suite does not discriminate on
//  a wrong write — 2,230 tests passed on this tree before any of this existed —
//  so the overwrite rule needs its own assertion or it is not defended.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MaterialCodeStampPlannerTests
    {
        // ── the shipped register and the shipped corpus, both read from source ──

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return dir;
        }

        private static MaterialRegistry _shipped;

        private static MaterialRegistry Shipped()
        {
            if (_shipped == null)
            {
                string data = Path.Combine(RepoRoot().FullName, "StingTools", "Data");
                _shipped = MaterialRegistry.Parse(
                    File.ReadAllText(Path.Combine(data, "BLE_MATERIALS.csv")),
                    File.ReadAllText(Path.Combine(data, "MEP_MATERIALS.csv")));
            }
            return _shipped;
        }

        /// <summary>The 1,815-name corpus harvested off a real model on 2026-09-08.
        /// Column 0 is the material name; the other two columns belong to the PROD work.</summary>
        private static List<string> Corpus()
        {
            string path = Path.Combine(RepoRoot().FullName, "StingTools.Tags.Tests",
                                       "Fixtures", "material_names_20260908.csv");
            Assert.True(File.Exists(path), "corpus fixture missing: " + path);
            var names = new List<string>();
            bool first = true;
            foreach (string line in File.ReadAllLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                string name = MaterialRegistry.SplitCsvLine(line).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
            }
            return names;
        }

        private static List<KeyValuePair<string, string>> Uncoded(IEnumerable<string> names)
            => names.Select(n => new KeyValuePair<string, string>(n, "")).ToList();

        // ══════════════════════════════════════════════════════════════════════
        //  1. The corpus. RED is the same body against a register that says nothing.
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void With_No_Register_Consulted_Every_Material_Resolves_No_Code()
        {
            // This is the state of the world before W1, expressed as an assertion rather
            // than as prose: nothing stamped, so nothing resolves, so Pass C cannot fire.
            var corpus = Corpus();
            var rows = MaterialCodeStampPlanner.PlanAll(Uncoded(corpus), MaterialRegistry.Empty);
            var t = MaterialCodeStampPlanner.Tally(rows);

            Assert.Equal(1815, t.Total);
            Assert.Equal(0, t.Stamped);
            Assert.Equal(1815, t.NoRegisterRow);
            Assert.All(rows, r => Assert.Equal("", r.RegisterCode));
        }

        [Fact]
        public void Corpus_Resolves_The_Registers_Codes()
        {
            var corpus = Corpus();
            var rows = MaterialCodeStampPlanner.PlanAll(Uncoded(corpus), Shipped());
            var t = MaterialCodeStampPlanner.Tally(rows);

            Assert.Equal(1815, t.Total);

            // 1,279 of the corpus names ARE register rows — the whole register appears in
            // this model — and 536 are not. Pinned as numbers because the point of the
            // work is that the second number is readable at all.
            Assert.Equal(1279, t.Stamped);
            Assert.Equal(536, t.NoRegisterRow);
            Assert.Equal(0, t.AlreadyCoded);
            Assert.Equal(536, t.RegisterSilent);

            // Every stamped row carries a real code from the register, not a blank that
            // happened to count. An empty string is the failure this whole area produces.
            Assert.All(rows.Where(r => r.WillWrite), r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.RegisterCode));
                Assert.Equal(r.RegisterCode, Shipped().ByName(r.MaterialName)?.Code);
            });
        }

        [Fact]
        public void A_Named_Row_Resolves_The_Code_The_Register_Gives_It()
        {
            // FLR-028 is the row the whole chain is traced through in W4.
            var reg = Shipped();
            var flr028 = reg.ByCode("FLR-028");
            Assert.NotNull(flr028);

            var row = MaterialCodeStampPlanner.Plan(flr028.Name, "", reg);
            Assert.Equal(MaterialCodeVerdict.Stamp, row.Verdict);
            Assert.Equal("FLR-028", row.RegisterCode);
            Assert.True(row.WillWrite);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. Never overwrite. The rule with no other defender.
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Code_Already_There_Is_Never_Overwritten()
        {
            var reg = Shipped();
            var flr028 = reg.ByCode("FLR-028");

            var row = MaterialCodeStampPlanner.Plan(flr028.Name, "FLR-999", reg);

            Assert.Equal(MaterialCodeVerdict.AlreadyCoded, row.Verdict);
            Assert.False(row.WillWrite);
            Assert.Equal("FLR-999", row.ExistingCode);
            Assert.True(row.CodeDisagrees);
            Assert.Contains("FLR-028", row.Reason);   // the disagreement is SAID, not settled
        }

        [Fact]
        public void An_Agreeing_Code_Is_Not_A_Disagreement()
        {
            var reg = Shipped();
            var flr028 = reg.ByCode("FLR-028");
            var row = MaterialCodeStampPlanner.Plan(flr028.Name, "flr-028", reg);

            Assert.Equal(MaterialCodeVerdict.AlreadyCoded, row.Verdict);
            Assert.False(row.CodeDisagrees);          // case, not conflict
            Assert.False(row.WillWrite);
        }

        [Fact]
        public void A_Coded_Material_The_Register_Does_Not_Know_Is_Left_Alone()
        {
            var row = MaterialCodeStampPlanner.Plan(
                "SOMETHING THIS PROJECT INVENTED", "PRJ-001", Shipped());

            Assert.Equal(MaterialCodeVerdict.AlreadyCoded, row.Verdict);
            Assert.False(row.WillWrite);
            Assert.False(row.CodeDisagrees);          // nothing to disagree WITH
            Assert.Equal("", row.RegisterCode);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. The three counts are mutually exclusive, and the fourth is not one of them
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Three_Verdicts_Partition_The_Materials()
        {
            var reg = Shipped();
            var flr028 = reg.ByCode("FLR-028");
            var input = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(flr028.Name, ""),          // Stamp
                new KeyValuePair<string, string>(flr028.Name, "FLR-999"),   // AlreadyCoded
                new KeyValuePair<string, string>("NOT A REGISTER NAME", ""),// NoRegisterRow
                new KeyValuePair<string, string>("ALSO NOT ONE", "PRJ-9"),  // AlreadyCoded
            };
            var t = MaterialCodeStampPlanner.Tally(MaterialCodeStampPlanner.PlanAll(input, reg));

            Assert.Equal(4, t.Total);
            Assert.Equal(1, t.Stamped);
            Assert.Equal(2, t.AlreadyCoded);
            Assert.Equal(1, t.NoRegisterRow);
            Assert.Equal(t.Total, t.Stamped + t.AlreadyCoded + t.NoRegisterRow);

            // RegisterSilent crosses the verdicts on purpose: two of these four are not in
            // the register, but only ONE of them is a NoRegisterRow, because the other
            // already has a code and the headline counts describe the write decision.
            Assert.Equal(2, t.RegisterSilent);
            Assert.Equal(1, t.Disagreements);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  4. Exact means exact
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Revits_Clash_Suffix_Is_Not_Matched()
        {
            // Revit renames a colliding material to "NAME 2". That may be a copy of the
            // register row or somebody's variant, and stamping a governed code onto it on
            // the strength of a prefix is exactly the guess this design refuses.
            var reg = Shipped();
            var flr028 = reg.ByCode("FLR-028");

            var row = MaterialCodeStampPlanner.Plan(flr028.Name + " 2", "", reg);
            Assert.Equal(MaterialCodeVerdict.NoRegisterRow, row.Verdict);
            Assert.False(row.WillWrite);
        }

        [Fact]
        public void Name_Matching_Ignores_Case_And_Surrounding_Space()
        {
            var reg = Shipped();
            var flr028 = reg.ByCode("FLR-028");

            var row = MaterialCodeStampPlanner.Plan("  " + flr028.Name.ToLowerInvariant() + "  ", "", reg);
            Assert.Equal(MaterialCodeVerdict.Stamp, row.Verdict);
            Assert.Equal("FLR-028", row.RegisterCode);
        }

        [Fact]
        public void A_Blank_Name_Is_Dropped_Rather_Than_Planned()
        {
            var input = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("", ""),
                new KeyValuePair<string, string>("   ", "X"),
                new KeyValuePair<string, string>(null, ""),
            };
            Assert.Empty(MaterialCodeStampPlanner.PlanAll(input, Shipped()));
        }

        [Fact]
        public void A_Model_With_No_Materials_Says_So()
        {
            var rows = MaterialCodeStampPlanner.PlanAll(
                new List<KeyValuePair<string, string>>(), Shipped());
            Assert.Contains("no materials", MaterialCodeStampPlanner.Summary(rows));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  5. The CSV a human reads
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Plan_Csv_Carries_Every_Row_Including_The_Ones_Not_Written()
        {
            var reg = Shipped();
            var flr028 = reg.ByCode("FLR-028");
            var rows = MaterialCodeStampPlanner.PlanAll(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(flr028.Name, ""),
                new KeyValuePair<string, string>("NOT A REGISTER NAME", ""),
            }, reg);

            var csv = MaterialCodeStampPlanner.ToCsv(rows);
            Assert.Equal(3, csv.Count);                       // header + both rows
            Assert.StartsWith("Material,ExistingCode,RegisterCode,Verdict,Reason", csv[0]);
            Assert.Contains(csv, l => l.Contains("NoRegisterRow"));
            Assert.Contains(csv, l => l.Contains("FLR-028"));
        }

        [Fact]
        public void A_Name_With_A_Comma_Survives_The_Csv()
        {
            var row = MaterialCodeStampPlanner.Plan("Concrete, Cast-in-Place gray", "", Shipped());
            string line = MaterialCodeStampPlanner.ToCsv(new[] { row })[1];
            Assert.StartsWith("\"Concrete, Cast-in-Place gray\"", line);
        }

        [Fact]
        public void The_Summary_Names_The_Vocabulary_Number()
        {
            var rows = MaterialCodeStampPlanner.PlanAll(Uncoded(Corpus()), Shipped());
            string s = MaterialCodeStampPlanner.Summary(rows);
            Assert.Contains("1279 will be stamped", s);
            Assert.Contains("536 have no register row", s);
            Assert.Contains("own vocabulary", s);
        }
    }
}
