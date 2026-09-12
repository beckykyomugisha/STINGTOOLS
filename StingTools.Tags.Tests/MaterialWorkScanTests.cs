// ══════════════════════════════════════════════════════════════════════════
//  MaterialWorkScanTests.cs — the gate on W5.
//
//  W5 adds two workflow conditions, has_unclassed_materials and
//  has_uncoded_materials, so Materials_SetClass and Materials_StampCodes skip
//  cleanly on a re-run. The thing that can go wrong is not the plumbing — it is
//  the PREDICATE, and it can go wrong in a way that never announces itself:
//
//    A condition of "any material whose class is blank" is TRUE FOREVER on this
//    model, because Materials_SetClass deliberately refuses to guess a class for
//    a name that says nothing. The step would then never skip, the command would
//    run on every kickoff, scan 1,815 materials and write nothing, and the
//    workflow report would say SUCCEEDED. That is a working-looking no-op — the
//    exact shape CLAUDE.md warns about.
//
//  So these tests pin that the condition asks what the COMMAND would do, over
//  the real 1,815-name corpus and the real shipped register.
//
//  RED / GREEN, recorded 2026-09-10 on this machine:
//
//    A_Model_Whose_Work_Is_Done_Reports_Nothing_To_Do
//      RED   predicate relaxed to "class is blank"   HasUnclassed TRUE on a
//            model where every classifiable material is already classified
//      GREEN FALSE, reason "no material can be classified from its name"
//
//    The_Corpus_Needs_Exactly_What_The_Commands_Would_Write
//      RED   registry lookup severed   NeedCode 0 (of 1,279 expected)
//      GREEN NeedCode 1,279 / NeedClass measured below
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Materials;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class MaterialWorkScanTests
    {
        private readonly ITestOutputHelper _out;
        public MaterialWorkScanTests(ITestOutputHelper output) => _out = output;

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return dir;
        }

        private static MaterialRegistry _reg;
        private static MaterialRegistry Shipped()
        {
            if (_reg == null)
            {
                string d = Path.Combine(RepoRoot().FullName, "StingTools", "Data");
                _reg = MaterialRegistry.Parse(
                    File.ReadAllText(Path.Combine(d, "BLE_MATERIALS.csv")),
                    File.ReadAllText(Path.Combine(d, "MEP_MATERIALS.csv")));
            }
            return _reg;
        }

        /// <summary>The 1,815 material names harvested off the Herring model on 2026-09-08.</summary>
        private static List<string> Corpus()
        {
            string p = Path.Combine(RepoRoot().FullName, "StingTools.Tags.Tests",
                                    "Fixtures", "material_names_20260908.csv");
            Assert.True(File.Exists(p), "corpus fixture missing: " + p);
            var names = new List<string>();
            bool first = true;
            foreach (string line in File.ReadAllLines(p))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                string n = MaterialRegistry.SplitCsvLine(line).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n.Trim());
            }
            return names;
        }

        private static List<MaterialWorkState> Virgin(IEnumerable<string> names)
            => names.Select(n => new MaterialWorkState { Name = n }).ToList();

        // ══════════════════════════════════════════════════════════════════════
        //  1. The real model, untouched
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Corpus_Needs_Exactly_What_The_Commands_Would_Write()
        {
            var t = MaterialWorkScan.Scan(Virgin(Corpus()), Shipped());

            Assert.Equal(1815, t.Total);
            Assert.Equal(0, t.Unreadable);

            // 1,279 corpus names ARE register rows — the whole register appears in this
            // model — so that is exactly what Materials_StampCodes would stamp. Pinned as
            // a number because W1 of the code-as-key work measured the same 1,279 and the
            // two must not drift.
            Assert.Equal(1279, t.NeedCode);
            Assert.True(t.HasUncoded);

            // NeedClass is whatever MaterialClassPlanner can name; asserted as a range
            // with both ends meaningful rather than a magic number: it must be more than
            // nothing (or the condition is useless) and less than everything (or the
            // planner is guessing, which is the thing it refuses to do).
            Assert.InRange(t.NeedClass, 1, 1814);
            Assert.True(t.HasUnclassed);

            _out.WriteLine($"corpus {t.Total}: NeedClass={t.NeedClass} NeedCode={t.NeedCode}");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. The re-run. This is the whole point of W5.
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Model_Whose_Work_Is_Done_Reports_Nothing_To_Do()
        {
            // Run the commands, conceptually: everything classifiable is classified,
            // everything the register names carries its code. A second kickoff must SKIP.
            var after = Corpus().Select(n =>
            {
                var s = new MaterialWorkState { Name = n };
                var cls = MaterialClassPlanner.Plan(n, "");
                if (cls.WillWrite) s.MaterialClass = cls.ProposedClass;
                var code = MaterialCodeStampPlanner.Plan(n, "", Shipped());
                if (code.WillWrite) s.MatCode = code.RegisterCode;
                return s;
            }).ToList();

            var t = MaterialWorkScan.Scan(after, Shipped());

            Assert.Equal(0, t.NeedClass);
            Assert.Equal(0, t.NeedCode);
            Assert.False(t.HasUnclassed);
            Assert.False(t.HasUncoded);

            // And the SKIPPED line says the number, so "nothing to do" cannot be read as
            // "nothing detectable".
            Assert.Contains("no material can be classified", t.ClassReason);
            Assert.Contains("1815", t.ClassReason);
            Assert.Contains("already carries its code", t.CodeReason);
        }

        [Fact]
        public void A_Blank_Class_Is_Not_By_Itself_Work_To_Do()
        {
            // The trap, isolated: a name the planner refuses has a blank class forever.
            // If the condition counted blanks, the step would never skip and the command
            // would run on every kickoff writing nothing.
            var refused = Corpus()
                .Where(n => !MaterialClassPlanner.Plan(n, "").WillWrite)
                .Take(50).ToList();
            Assert.NotEmpty(refused);

            var t = MaterialWorkScan.Scan(Virgin(refused), MaterialRegistry.Empty);

            Assert.Equal(refused.Count, t.Total);
            Assert.Equal(0, t.NeedClass);        // every class is blank, and none is work
            Assert.False(t.HasUnclassed);
        }

        [Fact]
        public void A_Material_The_Register_Does_Not_Name_Is_Not_Work_To_Do()
        {
            var t = MaterialWorkScan.Scan(
                Virgin(new[] { "SOMETHING THIS PROJECT INVENTED 42MM" }), Shipped());
            Assert.Equal(1, t.Total);
            Assert.Equal(0, t.NeedCode);
            Assert.False(t.HasUncoded);
        }

        [Fact]
        public void A_Code_Already_Present_Is_Not_Work_To_Do()
        {
            var row = Shipped().ByCode("FLR-028");
            Assert.NotNull(row);
            var t = MaterialWorkScan.Scan(new[]
            {
                new MaterialWorkState { Name = row.Name, MatCode = "FLR-999" },
            }, Shipped());

            // Materials_StampCodes never overwrites, so neither does the condition.
            Assert.Equal(0, t.NeedCode);
            Assert.False(t.HasUncoded);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. Degenerate inputs answer "no work", never "unknown"
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void With_No_Register_Deployed_Nothing_Needs_A_Code()
        {
            // MaterialRegistry.Empty is what a missing CSV produces. Every material then
            // resolves no code, which must read as NO WORK rather than as work the
            // command would fail to do.
            var t = MaterialWorkScan.Scan(Virgin(Corpus()), MaterialRegistry.Empty);
            Assert.Equal(1815, t.Total);
            Assert.Equal(0, t.NeedCode);
            Assert.False(t.HasUncoded);
        }

        [Fact]
        public void A_Model_With_No_Materials_Has_No_Work()
        {
            var t = MaterialWorkScan.Scan(new List<MaterialWorkState>(), Shipped());
            Assert.Equal(0, t.Total);
            Assert.False(t.HasUnclassed);
            Assert.False(t.HasUncoded);
            Assert.Contains("0 scanned", t.ClassReason);
        }

        [Fact]
        public void Null_And_Blank_Rows_Are_Dropped_Not_Counted()
        {
            var t = MaterialWorkScan.Scan(new[]
            {
                null,
                new MaterialWorkState { Name = "" },
                new MaterialWorkState { Name = "   " },
            }, Shipped());
            Assert.Equal(0, t.Total);
            Assert.Equal(0, t.Unreadable);
        }

        [Fact]
        public void A_Null_Collection_Is_No_Work_Rather_Than_A_Crash()
        {
            var t = MaterialWorkScan.Scan(null, Shipped());
            Assert.Equal(0, t.Total);
            Assert.False(t.HasUnclassed);
            Assert.False(t.HasUncoded);
        }

        [Fact]
        public void The_Reason_Names_A_Number_In_Both_Directions()
        {
            var work = MaterialWorkScan.Scan(Virgin(Corpus()), Shipped());
            Assert.Contains("1279", work.CodeReason);
            Assert.Contains("1815", work.CodeReason);

            var none = MaterialWorkScan.Scan(Virgin(new[] { "NOT A REGISTER NAME" }), Shipped());
            Assert.Contains("1 scanned", none.CodeReason);
        }
    }
}
