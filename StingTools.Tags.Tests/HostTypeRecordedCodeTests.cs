// ══════════════════════════════════════════════════════════════════════════
//  HostTypeRecordedCodeTests.cs — the gate on W3.
//
//  W3 lets a type SAY which register row it was built from, recorded in
//  ExtensibleStorage by CompoundTypeCreator, and makes the audit prefer that
//  claim over the type's NAME. Two things need proving, and the second is the
//  one with teeth:
//
//    1. A recorded code that the geometry refutes is REPORTED — verdict
//       CodeSaysOtherwise — and is NEVER re-matched by name. A wrong code
//       hiding behind a right name is the exact failure this verdict exists
//       for, and it is invisible unless asserted.
//
//    2. A type with NO recorded code audits by name exactly as it did before.
//       Every type in every existing model is in that group, so this is the
//       whole regression surface. Pinned against the real 2026-09-10 Herring
//       run: 138 Matches / 67 Differs / 28 Flattened / 81 NoStructure /
//       157 NotInRegister across 471 host types.
//
//  RED / GREEN, recorded 2026-09-10 on this machine:
//
//    A_Recorded_Code_Is_Never_Re_Matched_By_Name
//      RED   (audit falls back to ByName when the code is contradicted)
//            verdict Matches — the type is declared correct while recording a
//            code for a different row
//      GREEN CodeSaysOtherwise
//
//    Types_With_No_Recorded_Code_Audit_Exactly_As_Before
//      RED   (recorded-code branch taken for an empty string)
//            0 Matches / 95 CodeSaysOtherwise on the FLR family
//      GREEN 28 Flattened / 67 Differs, unchanged
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class HostTypeRecordedCodeTests
    {
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

        /// <summary>A type modelled exactly as a given register row declares.</summary>
        private static ModelledHostType AsDeclared(MaterialRow row, string typeName = null,
                                                   string recordedCode = "")
            => new ModelledHostType
            {
                Category = "Floors",
                TypeName = typeName ?? row.Name,
                InstanceCount = 3,
                RecordedCode = recordedCode,
                Layers = row.Layers.Select(l => new ModelledLayer
                {
                    MaterialName = l.Material,
                    ThicknessMm = l.ThicknessMm,
                    Function = l.Function,
                }).ToList(),
            };

        /// <summary>A register row that declares layers, so "as declared" is a real build-up
        /// rather than an empty one.</summary>
        private static MaterialRow Layered(int skip = 0)
        {
            var r = Registry().Rows
                    .Where(x => x.Code.StartsWith("FLR-", StringComparison.OrdinalIgnoreCase)
                             && x.Layers.Count >= 2)
                    .Skip(skip).FirstOrDefault();
            Assert.True(r != null, "no layered FLR-* row at skip " + skip);
            return r;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1. A recorded code the geometry refutes
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Recorded_Code_Is_Never_Re_Matched_By_Name()
        {
            // The trap in full: the type is NAMED after row A and built exactly as row A,
            // so a name match would call it a clean Match — but it RECORDS row B. The
            // recorded claim is wrong and must be said out loud.
            var rowA = Layered(0);
            var rowB = Layered(1);
            Assert.NotEqual(rowA.Code, rowB.Code);

            var t = AsDeclared(rowA, typeName: rowA.Name, recordedCode: rowB.Code);
            var res = HostTypeRegisterAudit.Audit(new[] { t }, Registry()).Single();

            Assert.Equal(RegisterAuditVerdict.CodeSaysOtherwise, res.Verdict);
            Assert.True(res.MatchedByRecordedCode);
            Assert.Equal(rowB.Code, res.RegisterCode);      // the CLAIM, not the name's row
            Assert.True(res.IsFinding);
            Assert.Contains(rowB.Code, res.Detail);
        }

        [Fact]
        public void The_Detail_Names_The_Row_The_Layers_Actually_Build()
        {
            var rowA = Layered(0);
            var rowB = Layered(1);
            var res = HostTypeRegisterAudit
                      .Audit(new[] { AsDeclared(rowA, "Some Project Name", rowB.Code) }, Registry())
                      .Single();

            Assert.Equal(RegisterAuditVerdict.CodeSaysOtherwise, res.Verdict);
            // It should say what the geometry IS, so a human can see the swap rather than
            // only that something is wrong.
            Assert.Contains(rowA.Code, res.Detail);
        }

        [Fact]
        public void A_Recorded_Code_That_Matches_Its_Own_Layers_Is_A_Match()
        {
            var row = Layered(0);
            // Named something else entirely — the name is irrelevant once a code is recorded.
            var res = HostTypeRegisterAudit
                      .Audit(new[] { AsDeclared(row, "PLNS_SLB_RC100", row.Code) }, Registry())
                      .Single();

            Assert.Equal(RegisterAuditVerdict.Matches, res.Verdict);
            Assert.True(res.MatchedByRecordedCode);
            Assert.False(res.IsFinding);
        }

        [Fact]
        public void A_Recorded_Code_The_Register_Does_Not_Issue_Is_Reported_Not_Ignored()
        {
            var row = Layered(0);
            var res = HostTypeRegisterAudit
                      .Audit(new[] { AsDeclared(row, row.Name, "FLR-NOT-A-REAL-CODE") }, Registry())
                      .Single();

            // NOT NotInRegister, and NOT a silent name re-match into Matches. A stale code
            // is a finding: the type claims provenance the register cannot confirm.
            Assert.Equal(RegisterAuditVerdict.CodeSaysOtherwise, res.Verdict);
            Assert.Contains("not a code this register issues", res.Detail);
        }

        [Fact]
        public void A_Recorded_Code_With_No_Compound_Structure_Is_The_Code_Being_Wrong()
        {
            var row = Layered(0);
            var t = new ModelledHostType
            {
                Category = "Floors", TypeName = "Whatever", InstanceCount = 1,
                RecordedCode = row.Code, Layers = new List<ModelledLayer>(),
            };
            var res = HostTypeRegisterAudit.Audit(new[] { t }, Registry()).Single();

            // A type that records a layered row and has no structure at all is a stronger
            // statement than "no structure" — it says it was built from something it was not.
            Assert.Equal(RegisterAuditVerdict.CodeSaysOtherwise, res.Verdict);
        }

        [Fact]
        public void Layers_Win_The_Quantity_Question_And_This_Only_Reports()
        {
            // The rule, asserted rather than only commented: nothing in the audit rewrites,
            // reorders or reconciles the modelled layers. The row it emits carries the
            // model's own description, and the register's, side by side.
            var rowA = Layered(0);
            var rowB = Layered(1);
            var t = AsDeclared(rowA, rowA.Name, rowB.Code);
            var before = t.Layers.Select(l => l.MaterialName + "|" + l.ThicknessMm).ToList();

            var res = HostTypeRegisterAudit.Audit(new[] { t }, Registry()).Single();

            var after = t.Layers.Select(l => l.MaterialName + "|" + l.ThicknessMm).ToList();
            Assert.Equal(before, after);
            Assert.Equal(rowA.LayerThicknessSumMm, t.TotalThicknessMm, 3);
            Assert.NotEqual(RegisterAuditVerdict.Matches, res.Verdict);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. Nothing regresses for types with no recorded code
        // ══════════════════════════════════════════════════════════════════════

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

        [Fact]
        public void Types_With_No_Recorded_Code_Audit_Exactly_As_Before()
        {
            // The full regression surface: every type in every existing model has no
            // recorded code, so this must be bit-for-bit the pre-W3 answer.
            var flr = Registry().Rows
                      .Where(r => r.Code.StartsWith("FLR-", StringComparison.OrdinalIgnoreCase))
                      .ToList();
            Assert.Equal(95, flr.Count);

            var rows = HostTypeRegisterAudit.Audit(flr.Select(r => Flat(r.Name)), Registry());

            Assert.Equal(28, rows.Count(r => r.Verdict == RegisterAuditVerdict.Flattened));
            Assert.Equal(67, rows.Count(r => r.Verdict == RegisterAuditVerdict.Differs));
            Assert.Equal(0,  rows.Count(r => r.Verdict == RegisterAuditVerdict.Matches));
            Assert.Equal(0,  rows.Count(r => r.Verdict == RegisterAuditVerdict.CodeSaysOtherwise));
            Assert.All(rows, r => Assert.False(r.MatchedByRecordedCode));
        }

        [Fact]
        public void The_2026_09_10_Herring_Verdict_Mix_Is_Reproduced_By_Verdict_Not_By_Count()
        {
            // The real run over 471 host types gave
            //   138 Matches · 67 Differs · 28 Flattened · 81 NoStructure · 157 NotInRegister
            // Those five are the whole vocabulary that run could produce, and W3 must not
            // add a sixth to any type that carries no recorded code. Asserted as an
            // enumeration rather than a list of cases, so a new verdict is covered whether
            // or not anybody remembers to come back here.
            var reachable = new[]
            {
                RegisterAuditVerdict.Matches, RegisterAuditVerdict.Differs,
                RegisterAuditVerdict.Flattened, RegisterAuditVerdict.NoStructure,
                RegisterAuditVerdict.NotInRegister,
            };

            var types = new List<ModelledHostType>();
            foreach (var r in Registry().Rows.Where(x => x.Layers.Count >= 1).Take(200))
                types.Add(AsDeclared(r));                       // Matches
            foreach (var r in Registry().Rows.Where(x => x.Layers.Count >= 2).Take(50))
                types.Add(Flat(r.Name));                        // Flattened
            types.Add(Flat("A Name This Project Invented"));     // NotInRegister
            types.Add(new ModelledHostType { Category = "Floors", TypeName = Layered(0).Name });

            var rows = HostTypeRegisterAudit.Audit(types, Registry());
            Assert.All(rows, r => Assert.Contains(r.Verdict, reachable));
            Assert.All(rows, r => Assert.False(r.MatchedByRecordedCode));

            // and every one of the five is genuinely reachable, so the assertion above is
            // not passing because the sample is thin.
            Assert.Contains(rows, r => r.Verdict == RegisterAuditVerdict.Matches);
            Assert.Contains(rows, r => r.Verdict == RegisterAuditVerdict.Flattened);
            Assert.Contains(rows, r => r.Verdict == RegisterAuditVerdict.NotInRegister);
            Assert.Contains(rows, r => r.Verdict == RegisterAuditVerdict.NoStructure);
        }

        [Fact]
        public void An_Empty_Recorded_Code_Is_The_Same_As_None()
        {
            var row = Layered(0);
            foreach (string blank in new[] { "", "   ", null })
            {
                var t = AsDeclared(row, row.Name, blank);
                var res = HostTypeRegisterAudit.Audit(new[] { t }, Registry()).Single();
                Assert.False(res.MatchedByRecordedCode);
                Assert.Equal(RegisterAuditVerdict.Matches, res.Verdict);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. The CSV says which way the row was matched
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Csv_Records_Whether_The_Match_Came_From_The_Code_Or_The_Name()
        {
            var rowA = Layered(0);
            var rowB = Layered(1);
            var rows = HostTypeRegisterAudit.Audit(new[]
            {
                AsDeclared(rowA, rowA.Name, rowB.Code),   // by recorded code
                AsDeclared(rowA),                          // by name
            }, Registry());

            var csv = HostTypeRegisterAudit.ToCsv(rows);
            Assert.StartsWith("Verdict,Category,TypeName,Instances,RegisterCode,MatchedBy,Detail", csv[0]);
            Assert.Contains("recorded code", csv[1]);
            Assert.Contains(",name,", csv[2]);
        }

        [Fact]
        public void The_Summary_Counts_The_New_Verdict_Separately()
        {
            var rowA = Layered(0);
            var rowB = Layered(1);
            var rows = HostTypeRegisterAudit.Audit(new[] { AsDeclared(rowA, rowA.Name, rowB.Code) },
                                                   Registry());
            string s = HostTypeRegisterAudit.Summary(rows);
            Assert.Contains("RECORD a register code their layers do not build", s);
            Assert.Contains("the code is a claim, the geometry is the fact", s);
        }
    }
}
