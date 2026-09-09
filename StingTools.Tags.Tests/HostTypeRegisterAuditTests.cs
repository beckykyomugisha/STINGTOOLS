// ══════════════════════════════════════════════════════════════════════════
//  HostTypeRegisterAuditTests.cs — W4. The audit that names the 87.
//
//  The fixture is the real thing: type names taken from
//  type_rename_plan_20260909_075110.csv (the 87 rows that all proposed
//  PLNS_SLB_RC100), their modelled build-up taken from that plan's own Reason
//  column ("core 'Concrete, Cast-in-Place gray'"), and the expected build-up
//  read from the SHIPPED register rather than restated here.
//
//  The assertion that matters is the sentence the brief asked for, verbatim in
//  substance:
//
//      STANDARD CEMENT SCREED 50MM: register says 1 × 50 mm CEMENT SCREED,
//      model has 1 × 100 mm Concrete, Cast-in-Place gray
//
//  and the assertion that keeps it honest is the one below it: a type NOT named
//  after a register row must produce no finding at all. An audit that reported
//  every type as a mismatch would be right about the 87 and useless.
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
    public class HostTypeRegisterAuditTests
    {
        private readonly ITestOutputHelper _out;
        public HostTypeRegisterAuditTests(ITestOutputHelper output) => _out = output;

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static MaterialRegistry _reg;
        private static MaterialRegistry Registry()
            => _reg ??= MaterialRegistry.Parse(
                   File.ReadAllText(Path.Combine(DataDir(), "BLE_MATERIALS.csv")),
                   File.ReadAllText(Path.Combine(DataDir(), "MEP_MATERIALS.csv")));

        /// <summary>What the 2026-09-09 model actually had on every one of the 87: one
        /// 100 mm layer of a Revit stock material, whatever the type was called.</summary>
        private static ModelledHostType Flat(string typeName, int instances = 4)
            => new ModelledHostType
            {
                Category = "Floors", TypeName = typeName, InstanceCount = instances,
                Layers = new List<ModelledLayer>
                {
                    new ModelledLayer
                    {
                        MaterialName = "Concrete, Cast-in-Place gray",
                        ThicknessMm = 100, Function = "Structure",
                    },
                },
            };

        // ══════════════════════════════════════════════════════════════════════
        //  1. The sentence the brief asked for
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Flattened_Type_Says_What_The_Register_Declares_And_What_The_Model_Has()
        {
            var rows = HostTypeRegisterAudit.Audit(
                new[] { Flat("STANDARD CEMENT SCREED 50MM") }, Registry());

            var r = Assert.Single(rows);
            _out.WriteLine(r.Detail);

            Assert.Equal("FLR-001", r.RegisterCode);
            Assert.True(r.IsFinding);
            Assert.Contains("register says", r.Detail);
            Assert.Contains("50 mm CEMENT SCREED", r.Detail);
            Assert.Contains("model has", r.Detail);
            Assert.Contains("100 mm Concrete, Cast-in-Place gray", r.Detail);
        }

        [Fact]
        public void A_Register_Row_With_Layers_Flattened_To_One_Is_Its_Own_Verdict()
        {
            // Separated from "Differs" because it has a single cause — the layer columns
            // were never read — where Differs can be any of a dozen things. A register row
            // declaring TWO layers modelled as one is Flattened; a row declaring one is
            // merely Differs. Both are findings; only the first points at the seeding bug.
            //
            // Both rows are SELECTED from the register, not named. The first version of this
            // test named SOLID BAMBOO 14MM as the single-layer case and failed: it declares
            // BAMBOO PLANK 14 mm plus FINISH-SEAL 0.5 mm, so it is a two-layer row. The data
            // corrected the test, which is the direction that means something.
            var multi = Registry().Rows.First(x => x.Code.StartsWith("FLR-") && x.Layers.Count >= 2);
            var single = Registry().Rows.First(x => x.Code.StartsWith("FLR-") && x.Layers.Count == 1);
            _out.WriteLine($"multi:  {multi.Code} {multi.Name} ({multi.Layers.Count} layers)");
            _out.WriteLine($"single: {single.Code} {single.Name} ({single.Layers.Count} layer)");

            var rows = HostTypeRegisterAudit.Audit(
                new[] { Flat(multi.Name), Flat(single.Name) }, Registry());

            Assert.Equal(RegisterAuditVerdict.Flattened, rows[0].Verdict);
            Assert.Equal(RegisterAuditVerdict.Differs, rows[1].Verdict);
        }

        [Fact]
        public void A_Type_Not_Named_After_A_Register_Row_Is_Not_A_Finding()
        {
            // The assertion that stops this being noise. Most type names are not register
            // MAT_NAMEs, and an audit that flagged them would bury the 86 that are — which
            // is the reason "Concrete 100mm", the 87th of the 87, is correctly silent here.
            var rows = HostTypeRegisterAudit.Audit(new[]
            {
                Flat("Concrete 100mm"),
                Flat("Generic - 225mm"),
                Flat("PLNS_WBL_Blockwork200-Plastered"),
            }, Registry());

            Assert.All(rows, r =>
            {
                Assert.Equal(RegisterAuditVerdict.NotInRegister, r.Verdict);
                Assert.False(r.IsFinding);
            });
        }

        [Fact]
        public void A_Type_Built_As_The_Register_Declares_Passes()
        {
            // Built from the register row itself, so this cannot pass by the comparison
            // being permissive — if the tolerance or the name rule were broken, the row
            // constructed from the register's own numbers would fail.
            var reg = Registry().ByName("STANDARD CEMENT SCREED 50MM");
            var t = new ModelledHostType
            {
                Category = "Floors", TypeName = reg.Name, InstanceCount = 2,
                Layers = reg.Layers.Select(l => new ModelledLayer
                {
                    MaterialName = l.Material, ThicknessMm = l.ThicknessMm, Function = l.Function,
                }).ToList(),
            };

            var r = Assert.Single(HostTypeRegisterAudit.Audit(new[] { t }, Registry()));
            Assert.Equal(RegisterAuditVerdict.Matches, r.Verdict);
            Assert.False(r.IsFinding);
        }

        [Fact]
        public void Revits_Duplicate_Name_Suffix_Does_Not_Read_As_A_Different_Material()
        {
            // The model's material is CREATED from the register's layer material, and Revit
            // appends " (1)" or " 2" on a name clash — which happens constantly, because the
            // same layer material appears on dozens of rows. Equality would report every
            // such type as a mismatch, which is a wrong finding rather than a missing one.
            Assert.True(HostTypeRegisterAudit.NameAgrees("CEMENT SCREED", "CEMENT SCREED (1)"));
            Assert.True(HostTypeRegisterAudit.NameAgrees("CEMENT SCREED", "Cement Screed 2"));
            Assert.False(HostTypeRegisterAudit.NameAgrees("CEMENT SCREED", "Concrete, Cast-in-Place gray"));

            // And it is a WHOLE-WORD containment, not a substring: the #863 shape would make
            // every "…TILE…" material agree with every other.
            Assert.False(HostTypeRegisterAudit.NameAgrees("TILE", "DUCTILE IRON"));
        }

        [Fact]
        public void A_Millimetre_Of_Rounding_Is_Not_A_Mismatch()
        {
            // Revit stores feet. 50 mm round-trips through 304.8 and does not land on 50.
            var reg = Registry().ByName("STANDARD CEMENT SCREED 50MM");
            var t = new ModelledHostType
            {
                Category = "Floors", TypeName = reg.Name,
                Layers = new List<ModelledLayer>
                {
                    new ModelledLayer { MaterialName = "CEMENT SCREED", ThicknessMm = 50.0000001 },
                },
            };
            Assert.Equal(RegisterAuditVerdict.Matches,
                HostTypeRegisterAudit.Audit(new[] { t }, Registry())[0].Verdict);

            // But 10 mm is.
            t.Layers[0].ThicknessMm = 60;
            Assert.Equal(RegisterAuditVerdict.Differs,
                HostTypeRegisterAudit.Audit(new[] { t }, Registry())[0].Verdict);
        }

        [Fact]
        public void A_Type_With_No_Compound_Structure_Is_Reported_Not_Skipped()
        {
            var rows = HostTypeRegisterAudit.Audit(new[]
            {
                new ModelledHostType { Category = "Floors", TypeName = "STANDARD CEMENT SCREED 50MM" },
            }, Registry());
            Assert.Equal(RegisterAuditVerdict.NoStructure, rows[0].Verdict);
            Assert.True(rows[0].IsFinding);
            Assert.Contains("no compound structure", rows[0].Detail);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. The 87, as a set
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Every FLR-* register row, modelled the way the 2026-09-09 run found them. This is
        /// the shape of the defect at full size: 95 types that claim to be 95 different floor
        /// build-ups and are one.
        /// </summary>
        [Fact]
        public void The_Whole_Flr_Family_Modelled_As_One_Layer_Is_Reported_Type_By_Type()
        {
            var flr = Registry().Rows
                        .Where(r => r.Code.StartsWith("FLR-", StringComparison.OrdinalIgnoreCase))
                        .ToList();
            Assert.Equal(95, flr.Count);

            var rows = HostTypeRegisterAudit.Audit(flr.Select(r => Flat(r.Name)), Registry());

            Assert.Equal(95, rows.Count);
            Assert.Empty(rows.Where(r => r.Verdict == RegisterAuditVerdict.NotInRegister));
            Assert.Empty(rows.Where(r => r.Verdict == RegisterAuditVerdict.Matches));
            Assert.Equal(95, rows.Count(r => r.IsFinding));

            // 28 of the 95 declare two or three layers, so 28 are FLATTENED and 67 DIFFER.
            Assert.Equal(28, rows.Count(r => r.Verdict == RegisterAuditVerdict.Flattened));
            Assert.Equal(67, rows.Count(r => r.Verdict == RegisterAuditVerdict.Differs));

            _out.WriteLine(HostTypeRegisterAudit.Summary(rows));
            foreach (var r in rows.Take(6)) _out.WriteLine("  " + r.Detail);
        }

        [Fact]
        public void The_Summary_Does_Not_Report_Nothing_To_Compare_As_Nothing_Wrong()
        {
            // A model whose type names are all its own is a NORMAL answer, and must not read
            // as a clean bill of health. The empty-list-standing-in-for-an-error shape.
            var rows = HostTypeRegisterAudit.Audit(new[] { Flat("Some Project Type 200") }, Registry());
            string s = HostTypeRegisterAudit.Summary(rows);
            Assert.Contains("are named after a register row", s);
            Assert.Contains("nothing here to compare", s);
            Assert.Contains("normal answer, not a failure", s);
            Assert.DoesNotContain("match the register's build-up", s);
        }

        [Fact]
        public void The_Csv_Carries_Every_Type_Including_The_Ones_With_No_Finding()
        {
            var rows = HostTypeRegisterAudit.Audit(new[]
            {
                Flat("STANDARD CEMENT SCREED 50MM"), Flat("Concrete 100mm"),
            }, Registry());
            var csv = HostTypeRegisterAudit.ToCsv(rows);
            Assert.Equal(3, csv.Count);
            Assert.StartsWith("Verdict,Category,TypeName,", csv[0]);
            Assert.Contains(csv, l => l.StartsWith("NotInRegister,"));
        }
    }
}
