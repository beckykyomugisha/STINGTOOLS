// ══════════════════════════════════════════════════════════════════════════
//  PerLevelBoxCoverageTests.cs — DTW-99: per-level production skips only the
//  (drawing type, level) pairs a scope box already produces.
//
//  DTW-95 gated the MEP preset's per-level step on "no STING:: boxes at all", so
//  one box — even one bound to an architectural type, or covering a single
//  level — suppressed every whole-floor MEP plan. The decision now lives in
//  PerLevelBoxCoverage: a STING::<type>::<level> box covers that type on that
//  level, an area box covers the (type, level) pairs it produces, and nothing
//  else is skipped.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PerLevelBoxCoverageTests
    {
        private static readonly List<LevelRef> Levels = new List<LevelRef>
        {
            new LevelRef { Id = 1, Name = "Level 1", Code = "L01" },
            new LevelRef { Id = 2, Name = "Level 2", Code = "L02" },
            new LevelRef { Id = 3, Name = "Level 3", Code = "L03" },
        };

        [Fact]
        public void A_Sting_Box_Covers_Only_Its_Type_On_Its_Level()
        {
            var cov = PerLevelBoxCoverage.Build(new[] { "STING::mep-plan-A1-1to100::L02::Z01" }, Levels, null, null);
            Assert.True(cov.TryCovered("mep-plan-A1-1to100", 2, out var box));
            Assert.Equal("STING::mep-plan-A1-1to100::L02::Z01", box);
            Assert.False(cov.TryCovered("mep-plan-A1-1to100", 1, out _));
            Assert.False(cov.TryCovered("mep-plan-A1-1to100", 3, out _));
            Assert.False(cov.TryCovered("elec-power-A1-1to100", 2, out _));
        }

        [Fact]
        public void A_Box_Bound_To_Another_Type_Covers_No_Mep_Plan()
        {
            // The DTW-95 defect: an architectural box suppressed every MEP plan.
            var cov = PerLevelBoxCoverage.Build(new[] { "STING::arch-plan-A1-1to100::L01" }, Levels, null, null);
            Assert.False(cov.TryCovered("mep-plan-A1-1to100", 1, out _));
            Assert.True(cov.TryCovered("ARCH-PLAN-A1-1TO100", 1, out _));   // ids compare ignoring case
        }

        [Fact]
        public void Inside_An_Mep_Set_Only_Boxes_The_Box_Route_Produces_Count()
        {
            // An MEP workflow's box step produces MEP-bound boxes only; a box it does not
            // produce must not stand in for a per-level plan of the same type.
            var disc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["mep-plan-A1-1to100"] = "M", ["arch-plan-A1-1to100"] = "A" };
            Func<string, bool> produced = id => disc.TryGetValue(id, out var d) && HeadlessProductionInputs.IsMepDiscipline(d);
            var cov = PerLevelBoxCoverage.Build(new[]
            {
                "STING::mep-plan-A1-1to100::L01",
                "STING::arch-plan-A1-1to100::L01",
                "STING::not-in-catalogue::L01",
            }, Levels, null, null, produced);
            Assert.True(cov.TryCovered("mep-plan-A1-1to100", 1, out _));
            Assert.False(cov.TryCovered("arch-plan-A1-1to100", 1, out _));
            Assert.False(cov.TryCovered("not-in-catalogue", 1, out _));
            Assert.Equal(1, cov.Count);
        }

        [Theory]
        [InlineData("M", true)] [InlineData(" e ", true)] [InlineData("FP", true)] [InlineData("MG", true)]
        [InlineData("P", true)] [InlineData("A", false)] [InlineData("S", false)] [InlineData("", false)] [InlineData(null, false)]
        public void IsMepDiscipline(string disc, bool expected)
            => Assert.Equal(expected, HeadlessProductionInputs.IsMepDiscipline(disc));

        [Fact]
        public void A_Levelless_Or_Unresolvable_Box_Covers_Nothing_And_Says_So()
        {
            var notes = new List<string>();
            var cov = PerLevelBoxCoverage.Build(new[]
            {
                "STING::mep-plan-A1-1to100",              // no level segment
                "STING::mep-plan-A1-1to100::L09",         // no such level
                "STING::bad name::L01",                   // malformed
                "STING-AREA::A01::L01",                   // not a STING:: box
                "Scope Box 1",
            }, Levels, null, notes);
            Assert.Equal(0, cov.Count);
            foreach (var l in Levels) Assert.False(cov.TryCovered("mep-plan-A1-1to100", l.Id, out _));
            Assert.Contains(notes, n => n.Contains("STING::mep-plan-A1-1to100'") && n.Contains("no level"));
            Assert.Contains(notes, n => n.Contains("L09"));
        }

        [Fact]
        public void The_Level_Segment_Is_Read_As_The_Producer_Reads_It()
        {
            // Code first, then name, then name without punctuation (LevelSegmentResolver).
            var cov = PerLevelBoxCoverage.Build(new[] { "STING::mep-plan-A1-1to100::Level_3" }, Levels, null, null);
            Assert.True(cov.TryCovered("mep-plan-A1-1to100", 3, out _));
        }

        [Fact]
        public void Area_Box_Pairs_Cover_What_They_Produce()
        {
            var cov = PerLevelBoxCoverage.Build(null, Levels, new[]
            {
                new BoxCover { BoxName = "STING-AREA::A01", TypeId = "mep-plan-A1-1to100", LevelId = 1 },
                new BoxCover { BoxName = "STING-AREA::A01", TypeId = "mep-plan-A1-1to100", LevelId = 2 },
            }, null);
            Assert.True(cov.TryCovered("mep-plan-A1-1to100", 1, out var b1));
            Assert.Equal("STING-AREA::A01", b1);
            Assert.True(cov.TryCovered("mep-plan-A1-1to100", 2, out _));
            Assert.False(cov.TryCovered("mep-plan-A1-1to100", 3, out _));
        }

        [Fact]
        public void An_Empty_Project_Covers_Nothing()
        {
            var cov = PerLevelBoxCoverage.Build(null, null, null, null);
            Assert.Equal(0, cov.Count);
            Assert.False(cov.TryCovered("mep-plan-A1-1to100", 1, out _));
            Assert.False(cov.TryCovered(null, 1, out _));
        }

        [Fact]
        public void The_Mep_Preset_Runs_Per_Level_Whenever_Area_Boxes_Are_Absent()
        {
            // The per-level step must not be switched off by STING:: boxes: the producer
            // skips the pairs they cover. Only the area route replaces it wholesale.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null);
            var json = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "WORKFLOW_MEPDrawingProduction.json"));
            var step = Regex.Match(json, "\"commandTag\"\\s*:\\s*\"DrawingTypes_ProducePerLevel\"(.*?)\\}\\s*,?\\s*\\{", RegexOptions.Singleline);
            Assert.True(step.Success, "per-level step not found in the MEP preset");
            var cond = Regex.Match(step.Groups[1].Value, "\"condition\"\\s*:\\s*\"([^\"]+)\"");
            Assert.True(cond.Success);
            Assert.Equal("no_area_boxes", cond.Groups[1].Value);
        }
    }
}
