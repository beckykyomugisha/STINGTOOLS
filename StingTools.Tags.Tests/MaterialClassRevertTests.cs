// ══════════════════════════════════════════════════════════════════════════
//  MaterialClassRevertTests.cs — the half of the revert worth proving is the
//  half that refuses.
//
//  A revert that reverts everything the plan lists would undo a human's later
//  decision as confidently as it undoes the tool's mistake, and the user would
//  have no way to tell which had happened. So the assertions below are mostly
//  about NOT acting: on a row the plan never wrote, on a material somebody has
//  since reclassified, on a material that is no longer in the model, and on a
//  second run of the same file.
//
//  The plan lines used here are copied verbatim out of the CSV the 2026-09-08
//  run wrote, including the one with a comma and a backslash inside a quoted
//  material name — which is why the reader is RFC 4180 and not a Split(',').
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MaterialClassRevertTests
    {
        private static MaterialClassPlanRow Row(string name, string existing, string proposed)
            => new MaterialClassPlanRow { MaterialName = name, ExistingClass = existing, ProposedClass = proposed };

        // ══════════════════════════════════════════════════════════════════════
        //  It reverts what it set
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Class_The_Plan_Set_And_Nobody_Has_Touched_Is_Put_Back()
        {
            var p = MaterialClassRevertPlanner.Plan(Row("CARPET TILE", "Unassigned", "Ceramic"), "Ceramic");
            Assert.True(p.WillRevert);
            Assert.Equal("", p.RestoreTo);          // back to no class, which is where it started
            Assert.Contains("untouched since", p.Reason);
        }

        [Fact]
        public void Unassigned_And_Blank_Are_The_Same_Absence()
        {
            // Revit reports an unset class as "Unassigned"; the plan CSV records whichever
            // of the two it saw. Restoring must not turn one into the other by accident.
            var a = MaterialClassRevertPlanner.Plan(Row("SLATE TILE", "Unassigned", "Ceramic"), "Ceramic");
            var b = MaterialClassRevertPlanner.Plan(Row("SLATE TILE", "", "Ceramic"), "Ceramic");
            Assert.Equal(a.RestoreTo, b.RestoreTo);
            Assert.True(a.WillRevert && b.WillRevert);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  It refuses
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Class_Somebody_Has_Changed_Since_Is_Left_Alone()
        {
            // The whole point. The plan set Ceramic; the material now says Stone, so a
            // human has been in there and their answer is the current one.
            var p = MaterialClassRevertPlanner.Plan(Row("GRANITE TILE", "Unassigned", "Ceramic"), "Stone");
            Assert.False(p.WillRevert);
            Assert.Contains("changed since", p.Reason);
            Assert.Contains("Stone", p.Reason);
            Assert.Contains("Ceramic", p.Reason);
        }

        [Fact]
        public void A_Row_The_Plan_Refused_Is_Not_A_Row_To_Undo()
        {
            // 171 of the 1,815 rows proposed nothing. There is nothing to put back, and a
            // revert that "restored" them would be clearing classes it never set.
            var p = MaterialClassRevertPlanner.Plan(Row("Air Openings", "Unassigned", ""), "Concrete");
            Assert.False(p.WillRevert);
            Assert.Contains("proposed nothing", p.Reason);
        }

        [Fact]
        public void An_Already_Classified_Row_Is_Not_A_Row_To_Undo()
        {
            // 1,524 rows were left alone because they already had a class. Their
            // ProposedClass is blank, so the same rule covers them — but assert it, because
            // clearing a class the tool never wrote is the worst thing this could do.
            var p = MaterialClassRevertPlanner.Plan(Row("00-win beading", "Wood", ""), "Wood");
            Assert.False(p.WillRevert);
            Assert.Equal("Wood", p.CurrentClass);
        }

        [Fact]
        public void A_Material_That_Is_No_Longer_In_The_Model_Is_Reported_Not_Skipped_Silently()
        {
            var p = MaterialClassRevertPlanner.Plan(Row("CORK TILE", "Unassigned", "Ceramic"), null);
            Assert.False(p.WillRevert);
            Assert.Contains("no longer in this model", p.Reason);
        }

        [Fact]
        public void Running_It_Twice_Does_Nothing_The_Second_Time()
        {
            var row = Row("VINYL TILE", "Unassigned", "Ceramic");

            var first = MaterialClassRevertPlanner.Plan(row, "Ceramic");
            Assert.True(first.WillRevert);

            // After the first revert the material carries first.RestoreTo. Feed that back.
            var second = MaterialClassRevertPlanner.Plan(row, first.RestoreTo);
            Assert.False(second.WillRevert);
            Assert.Contains("changed since", second.Reason);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Reading the plan file
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Plan_Reader_Handles_A_Material_Name_With_A_Comma_In_It()
        {
            var lines = new[]
            {
                "Material,ExistingClass,ProposedClass,Reason",
                "\"_ACCURENDER\\Solid Colors\\Black,Matte\",Unassigned,Gypsum,name contains 'render'",
                "CARPET TILE,Unassigned,Ceramic,name contains 'tile'",
            };
            var rows = MaterialClassRevertPlanner.ReadPlan(lines, out string err);
            Assert.Null(err);
            Assert.Equal(2, rows.Count);
            Assert.Equal(@"_ACCURENDER\Solid Colors\Black,Matte", rows[0].MaterialName);
            Assert.Equal("Gypsum", rows[0].ProposedClass);
            Assert.Equal("Ceramic", rows[1].ProposedClass);
        }

        [Fact]
        public void Columns_Are_Found_By_Name_Not_By_Position()
        {
            // A column added to the plan CSV later must not shift what this reads.
            var lines = new[]
            {
                "Material,Reason,ExistingClass,ProposedClass",
                "MARBLE TILE,name contains 'tile',Unassigned,Ceramic",
            };
            var rows = MaterialClassRevertPlanner.ReadPlan(lines, out string err);
            Assert.Null(err);
            Assert.Equal("Ceramic", rows[0].ProposedClass);
            Assert.Equal("Unassigned", rows[0].ExistingClass);
        }

        [Fact]
        public void The_Wrong_File_Says_So_Instead_Of_Reverting_Nothing_Quietly()
        {
            // Pointed at a type_rename_plan, or a spreadsheet, or an empty file, this must
            // produce a visible error. "0 to revert" would read as "already clean".
            var wrongShape = MaterialClassRevertPlanner.ReadPlan(
                new[] { "Category,CurrentName,ProposedName,Instances,Outcome,Reason", "Walls,A,B,1,rename,x" },
                out string e1);
            Assert.Empty(wrongShape);
            Assert.Contains("not a material_class_plan", e1);

            MaterialClassRevertPlanner.ReadPlan(new string[0], out string e2);
            Assert.Contains("empty", e2);

            MaterialClassRevertPlanner.ReadPlan(new[] { "Material,ExistingClass,ProposedClass,Reason" }, out string e3);
            Assert.Contains("no rows", e3);
        }

        [Fact]
        public void A_Utf8_Byte_Order_Mark_Does_Not_Hide_The_First_Column()
        {
            // Standardise.Write emits UTF-8, and Excel round-trips add a BOM. A BOM stuck to
            // "Material" would make the header lookup fail and the file look like the wrong
            // shape entirely.
            var rows = MaterialClassRevertPlanner.ReadPlan(new[]
            {
                "﻿Material,ExistingClass,ProposedClass,Reason",
                "SLATE TILE,Unassigned,Ceramic,name contains 'tile'",
            }, out string err);
            Assert.Null(err);
            Assert.Single(rows);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  End to end, on the shape the real run produced
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Summary_Counts_Every_Row_Exactly_Once()
        {
            var plan = new List<MaterialClassPlanRow>
            {
                Row("CARPET TILE",      "Unassigned", "Ceramic"),   // revert
                Row("SLATE TILE",       "Unassigned", "Ceramic"),   // revert
                Row("GRANITE TILE",     "Unassigned", "Ceramic"),   // changed since
                Row("CORK TILE",        "Unassigned", "Ceramic"),   // gone
                Row("Air Openings",     "Unassigned", ""),          // never written
                Row("00-win beading",   "Wood",       ""),          // never written
            };
            var current = new Dictionary<string, string>
            {
                ["CARPET TILE"] = "Ceramic",
                ["SLATE TILE"] = "Ceramic",
                ["GRANITE TILE"] = "Stone",
                ["Air Openings"] = "",
                ["00-win beading"] = "Wood",
            };

            var ps = MaterialClassRevertPlanner.PlanAll(plan, current);
            Assert.Equal(6, ps.Count);
            Assert.Equal(2, ps.Count(x => x.WillRevert));

            string s = MaterialClassRevertPlanner.Summary(ps);
            Assert.Contains("6 row(s)", s);
            Assert.Contains("2 will be put back", s);
            Assert.Contains("2 were never written", s);
            Assert.Contains("1 are no longer in the model", s);
            Assert.Contains("1 have been changed since", s);
        }

        [Fact]
        public void Re_Running_Materials_SetClass_Cannot_Repair_A_Bad_Write()
        {
            // This is the reason the revert exists, so it is asserted rather than asserted
            // in a comment. The planner is now correct about CARPET TILE — but the model
            // already carries the wrong answer, and rule 1 protects it.
            Assert.Equal("Textile", MaterialClassPlanner.Plan("CARPET TILE", "").ProposedClass);
            Assert.Null(MaterialClassPlanner.Plan("CARPET TILE", "Ceramic").ProposedClass);
        }
    }
}
