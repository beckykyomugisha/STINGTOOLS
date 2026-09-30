// Production commands inside a workflow preset: the inputs a dialog used to ask
// for, read from the step's "params". The defaults are the MEP set; anything the
// step names that does not exist fails the step rather than being guessed around.

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class HeadlessProductionInputsTests
    {
        private static List<DrawingType> Catalogue() => new List<DrawingType>
        {
            new DrawingType { Id = "arch-plan-A1-1to100",       Discipline = "A",  Purpose = "Plan" },
            new DrawingType { Id = "mep-hvac-duct-A1-1to100",   Discipline = "M",  Purpose = "Plan" },
            new DrawingType { Id = "elec-power-A1-1to100",      Discipline = "E",  Purpose = "Plan" },
            new DrawingType { Id = "plumb-drainage-A1-1to100",  Discipline = "P",  Purpose = "Plan" },
            new DrawingType { Id = "fire-sprinkler-A1-1to100",  Discipline = "FP", Purpose = "Plan" },
            new DrawingType { Id = "health-medgas-pln",         Discipline = "MG", Purpose = "Plan" },
            new DrawingType { Id = "elec-panel-schedule-A3",    Discipline = "E",  Purpose = "Schedule" },
            new DrawingType { Id = "mep-coord-A1-1to50",        Discipline = "M",  Purpose = "Coordination" },
            new DrawingType { Id = "struct-plan-A1-1to100",     Discipline = "S",  Purpose = "Plan" },
        };

        [Fact]
        public void With_no_types_named_every_MEP_plan_type_is_produced()
        {
            var picked = HeadlessProductionInputs.SelectTypes(Catalogue(), new List<string>(), new[] { "Plan" }, out var unknown);
            Assert.Empty(unknown);
            Assert.Equal(new[] { "mep-hvac-duct-A1-1to100", "elec-power-A1-1to100", "plumb-drainage-A1-1to100",
                                 "fire-sprinkler-A1-1to100", "health-medgas-pln" },
                         picked.Select(t => t.Id).ToArray());
        }

        [Fact]
        public void Named_types_win_and_unknown_ids_are_reported()
        {
            var picked = HeadlessProductionInputs.SelectTypes(Catalogue(),
                HeadlessProductionInputs.ParseList("elec-power-A1-1to100; ARCH-PLAN-A1-1to100, no-such-type"),
                new[] { "Plan" }, out var unknown);
            Assert.Equal(new[] { "elec-power-A1-1to100", "arch-plan-A1-1to100" }, picked.Select(t => t.Id).ToArray());
            Assert.Equal(new[] { "no-such-type" }, unknown.ToArray());
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("Views and sheets", true)]
        [InlineData("views-and-sheets", true)]
        [InlineData("Views only", false)]
        [InlineData("views", false)]
        [InlineData("sheetz", null)]
        public void Output_defaults_to_views_and_sheets_and_rejects_a_typo(string raw, bool? expected)
            => Assert.Equal(expected, HeadlessProductionInputs.ParseSheets(raw));

        [Theory]
        [InlineData(null, "Duplicate")]
        [InlineData("DuplicateAsDependent", "DuplicateAsDependent")]
        [InlineData("as dependent", "DuplicateAsDependent")]
        [InlineData("DuplicateWithDetailing", "DuplicateWithDetailing")]
        [InlineData("copy", null)]
        public void Duplicate_option_reads_the_preset_words(string raw, string expected)
            => Assert.Equal(expected, HeadlessProductionInputs.ParseDuplicateOption(raw));

        [Fact]
        public void Levels_default_to_all_and_unknown_names_are_reported()
        {
            var levels = new List<string> { "Level 1", "Level 2", "Roof" };
            Assert.Equal(levels, HeadlessProductionInputs.SelectNames(levels, new List<string>(), out var none));
            Assert.Empty(none);
            var picked = HeadlessProductionInputs.SelectNames(levels, HeadlessProductionInputs.ParseList("level 2,Basement"), out var unknown);
            Assert.Equal(new[] { "Level 2" }, picked.ToArray());
            Assert.Equal(new[] { "Basement" }, unknown.ToArray());
        }
    }
}
