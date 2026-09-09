// ══════════════════════════════════════════════════════════════════════════
//  MaterialRegisterReconciliationTests.cs — W2. The register and the needle
//  table checked against each other, neither treated as the answer, and the
//  disagreements PRINTED so a human can act on them.
//
//  This gate is a COUNT CEILING, not a zero. Zero is not reachable: 85 of the
//  1,279 rows are genuine two-opinion conflicts and settling each one is a
//  judgement about a governed data file, not a code change. A gate that demanded
//  zero would be switched off within a week; a gate that fails when the count
//  GROWS catches the thing that actually goes wrong — somebody adding a needle,
//  or a register row, that disagrees with the other side by accident.
//
//  The ceilings below are the measured counts on 2026-09-09. Lower them when
//  rows are settled. If one RISES, the run prints exactly which rows moved.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.Core.Materials;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class MaterialRegisterReconciliationTests
    {
        private readonly ITestOutputHelper _out;
        public MaterialRegisterReconciliationTests(ITestOutputHelper output) => _out = output;

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static List<ReconcileRow> _rows;

        private static List<ReconcileRow> Rows()
            => _rows ??= MaterialRegisterReconciliation.Reconcile(MaterialRegistry.Parse(
                   File.ReadAllText(Path.Combine(DataDir(), "BLE_MATERIALS.csv")),
                   File.ReadAllText(Path.Combine(DataDir(), "MEP_MATERIALS.csv"))));

        // Measured 2026-09-09 over all 1,279 shipped rows.
        private const int Conflicts = 85;
        private const int OnlyRegister = 342;
        private const int OnlyNeedles = 352;
        private const int Agree = 348;

        [Fact]
        public void The_Reconciliation_Covers_Every_Register_Row_Exactly_Once()
        {
            var rows = Rows();
            Assert.Equal(1279, rows.Count);
            Assert.Equal(1279, rows.Select(r => r.Code + "|" + r.Name).Distinct().Count());
            // Every row lands in exactly one bucket — the enum is exhaustive by construction,
            // so this is really asserting nothing silently fell out of the loop.
            Assert.Equal(1279, Enum.GetValues(typeof(ReconcileVerdict)).Cast<ReconcileVerdict>()
                                   .Sum(v => rows.Count(r => r.Verdict == v)));
        }

        [Fact]
        public void The_Disagreement_Count_Has_Not_Grown()
        {
            var rows = Rows();
            int conflict = rows.Count(r => r.Verdict == ReconcileVerdict.Conflict);
            int onlyReg = rows.Count(r => r.Verdict == ReconcileVerdict.OnlyRegister);
            int onlyNdl = rows.Count(r => r.Verdict == ReconcileVerdict.OnlyNeedles);
            int agree = rows.Count(r => r.Verdict == ReconcileVerdict.Agree);

            _out.WriteLine(MaterialRegisterReconciliation.Summary(rows));
            _out.WriteLine("");
            _out.WriteLine("── CONFLICTS: both have an opinion and they differ ──");
            foreach (var r in rows.Where(x => x.Verdict == ReconcileVerdict.Conflict)
                                  .OrderBy(x => x.NeedleClass).ThenBy(x => x.RegisterClass).ThenBy(x => x.Name))
                _out.WriteLine($"  needles={r.NeedleClass,-11} register={r.RegisterClass,-11} "
                             + $"(raw {r.RegisterClassRaw,-10} {r.Code,-13}) {r.Name}");

            Assert.True(conflict <= Conflicts,
                $"conflicts grew {Conflicts} → {conflict}. A new needle or a new register row now "
                + "contradicts the other side. The full list is in this test's output.");
            Assert.True(onlyReg <= OnlyRegister, $"register-only answers grew {OnlyRegister} → {onlyReg}");
            Assert.True(onlyNdl <= OnlyNeedles, $"needle-only answers grew {OnlyNeedles} → {onlyNdl}");
            Assert.True(agree >= Agree,
                $"agreement FELL {Agree} → {agree}. Something that used to be settled no longer is.");
        }

        [Fact]
        public void The_Conflicts_Are_Not_One_Side_Being_Wrong()
        {
            // The finding that stopped the register being wired in as the answer, pinned as
            // four named rows rather than left in a commit message. Two where the register
            // is right, two where it classifies by trade and the needle table is right.
            var by = Rows().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

            // Register right: masonry PAINT is paint; cement plaster is cementitious, not gypsum.
            Assert.Equal("Paint", by["MASONRY PAINT WHITE"].RegisterClass);
            Assert.Equal("Masonry", by["MASONRY PAINT WHITE"].NeedleClass);
            Assert.Equal("Concrete", by["CEMENT PLASTER FINISH 12MM"].RegisterClass);
            Assert.Equal("Gypsum", by["CEMENT PLASTER FINISH 12MM"].NeedleClass);

            // Needles right: a skirting is "Wood" in the register whatever it is made of,
            // and a lightweight screed is "Metal".
            Assert.Equal("Wood", by["GRANITE SKIRTING 100MM"].RegisterClass);
            Assert.Equal("Stone", by["GRANITE SKIRTING 100MM"].NeedleClass);
            Assert.Equal("Metal", by["LIGHTWEIGHT SCREED 40MM"].RegisterClass);
            Assert.Equal("Concrete", by["LIGHTWEIGHT SCREED 40MM"].NeedleClass);

            foreach (string n in new[]
            {
                "MASONRY PAINT WHITE", "CEMENT PLASTER FINISH 12MM",
                "GRANITE SKIRTING 100MM", "LIGHTWEIGHT SCREED 40MM",
            })
                Assert.Equal(ReconcileVerdict.Conflict, by[n].Verdict);
        }

        [Fact]
        public void Agreement_Rate_Does_Not_Identify_The_Trustworthy_Classes()
        {
            // The measurement that rules out every mechanical rule: if agreement rate
            // tracked correctness, a whitelist would be safe. It does not. Concrete agrees
            // least and is mostly RIGHT where it differs; Wood agrees well and is wrong.
            // Asserted so that a future author reaching for "just trust the classes that
            // usually agree" fails here first.
            var rows = Rows().Where(r => r.Verdict == ReconcileVerdict.Agree
                                      || r.Verdict == ReconcileVerdict.Conflict).ToList();

            double Rate(string raw)
            {
                var g = rows.Where(r => string.Equals(r.RegisterClassRaw, raw, StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.NotEmpty(g);
                return (double)g.Count(r => r.Verdict == ReconcileVerdict.Agree) / g.Count;
            }

            double concrete = Rate("Concrete");
            double wood = Rate("Wood");
            _out.WriteLine($"Concrete agreement {concrete:P0} (mostly right where it differs)");
            _out.WriteLine($"Wood     agreement {wood:P0} (wrong where it differs — skirtings, paving)");

            Assert.True(concrete < 0.60, $"Concrete agreement is now {concrete:P0}");
            Assert.True(wood > 0.75, $"Wood agreement is now {wood:P0}");
            Assert.True(wood > concrete,
                "The premise of this file is that agreement rate and correctness point in "
              + "OPPOSITE directions here. If that stops being true, the refusal to consume "
              + "the class column should be reconsidered on the new evidence.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The cheapest edits to propose
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Rows_Whose_Own_Name_Contradicts_Their_Class_Are_Listed()
        {
            // The brief started from 31 GYPSUM-named rows classed Generic and said to expect
            // more. There are 352 — every row where the NAME states a substance the needle
            // table recognises and the class column names none. That is the cheapest set to
            // propose data edits for, because the evidence is inside the row.
            var rows = MaterialRegisterReconciliation.NameSaysMoreThanClassDoes(Rows());

            _out.WriteLine($"{rows.Count} rows where MAT_NAME states a substance and "
                         + "BLE_APP-IDENTITY-CLASS names none:");
            foreach (var g in rows.GroupBy(r => r.RegisterClassRaw).OrderByDescending(g => g.Count()))
            {
                _out.WriteLine($"  {g.Key}: {g.Count()}");
                foreach (var r in g.OrderBy(x => x.Code).Take(60))
                    _out.WriteLine($"      {r.Code,-13} {r.Name,-52} name says {r.NeedleClass}");
            }

            Assert.Equal(OnlyNeedles, rows.Count);

            // The 31 the brief named, still there and still the clearest case.
            var gypsum = rows.Where(r => r.Name.IndexOf("GYPSUM", StringComparison.OrdinalIgnoreCase) >= 0
                                      && string.Equals(r.RegisterClassRaw, "Generic", StringComparison.OrdinalIgnoreCase))
                             .ToList();
            Assert.Equal(31, gypsum.Count);
            Assert.Contains(gypsum, r => r.Name == "GYPSUM BOARD STANDARD 9.5MM");
            Assert.All(gypsum, r => Assert.Equal("Gypsum", r.NeedleClass));
        }

        [Fact]
        public void The_Report_Csv_Carries_Every_Row_Including_The_Agreements()
        {
            // A report showing only disagreements hides its own denominator, which is how a
            // reader concludes 85 problems out of 85 rather than out of 1,279.
            var csv = MaterialRegisterReconciliation.ToCsv(Rows());
            Assert.Equal(1280, csv.Count);                       // header + 1,279
            Assert.StartsWith("Verdict,Code,Name,", csv[0]);
            Assert.Contains(csv, l => l.StartsWith("Agree,"));
            Assert.Contains(csv, l => l.StartsWith("Conflict,"));
            Assert.Contains(csv, l => l.StartsWith("OnlyNeedles,"));
            Assert.Contains(csv, l => l.StartsWith("OnlyRegister,"));
        }

        [Fact]
        public void The_Summary_Says_Both_Sides_Are_Challengers()
        {
            string s = MaterialRegisterReconciliation.Summary(Rows());
            Assert.Contains("1279 register row(s)", s);
            Assert.Contains("CONFLICT", s);
            Assert.Contains("reports and does not write", s);
        }
    }
}
