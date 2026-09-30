// Revit-free halves of the review fixes on fix/review-six:
//   SeedBuildPresetMode    — Seeds_Build's mode inside a workflow preset
//   SeedFollowTypeWrite    — a followsType "clear" on numeric / id storage
//   IsFamilyOfSeed         — seed migration reaches Revit's digit-suffixed copies
//   AreaBoxResolution      — area boxes produce without a saved plan
//   ExportSheetScope       — Produce & Export limited to the current revision's sheets

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using StingTools.Core.Symbols;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SeedBuildPresetModeTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_preset_with_no_mode_builds_missing_only(string raw)
        {
            Assert.Equal(SeedBuildPresetMode.MissingOnly, SeedBuildPresetMode.Resolve(raw, out var err));
            Assert.Null(err);
        }

        [Theory]
        [InlineData("MissingOnly", SeedBuildPresetMode.MissingOnly)]
        [InlineData("missing-only", SeedBuildPresetMode.MissingOnly)]
        [InlineData("RebuildAll", SeedBuildPresetMode.RebuildAll)]
        [InlineData("rebuild all", SeedBuildPresetMode.RebuildAll)]
        [InlineData("RebuildUnfinalized", SeedBuildPresetMode.RebuildUnfinalized)]
        [InlineData("rebuild_unfinalized", SeedBuildPresetMode.RebuildUnfinalized)]
        public void A_named_mode_is_honoured(string raw, string expected)
            => Assert.Equal(expected, SeedBuildPresetMode.Resolve(raw, out _));

        [Theory]
        [InlineData("Rebuild")]
        [InlineData("all")]
        [InlineData("bogus")]
        public void An_unknown_mode_fails_with_the_reason(string raw)
        {
            Assert.Null(SeedBuildPresetMode.Resolve(raw, out var err));
            Assert.Contains(raw, err);
            Assert.Contains("MissingOnly", err);
        }
    }

    public class SeedFollowTypeWriteTests
    {
        [Theory]
        [InlineData(FollowStorage.Integer)]
        [InlineData(FollowStorage.ElementId)]
        public void A_clear_on_numeric_or_id_storage_with_a_value_clears_it(FollowStorage storage)
            => Assert.Equal(FollowWriteAction.Clear, SeedFollowTypeWrite.Plan(storage, false, true, "", out _, out _));

        [Fact]
        public void A_clear_on_a_unitless_double_with_a_value_clears_it()
            => Assert.Equal(FollowWriteAction.Clear, SeedFollowTypeWrite.Plan(FollowStorage.Double, true, true, "  ", out _, out _));

        [Theory]
        [InlineData(FollowStorage.Integer)]
        [InlineData(FollowStorage.ElementId)]
        public void A_clear_on_an_empty_parameter_does_nothing(FollowStorage storage)
            => Assert.Equal(FollowWriteAction.AlreadyClear, SeedFollowTypeWrite.Plan(storage, false, false, "", out _, out _));

        [Fact]
        public void Text_is_still_cleared_by_writing_empty_text()
            => Assert.Equal(FollowWriteAction.SetText, SeedFollowTypeWrite.Plan(FollowStorage.Text, false, true, "", out _, out _));

        [Fact]
        public void Numbers_are_parsed_for_their_storage()
        {
            Assert.Equal(FollowWriteAction.SetInteger, SeedFollowTypeWrite.Plan(FollowStorage.Integer, false, true, "630", out var i, out _));
            Assert.Equal(630, i);
            Assert.Equal(FollowWriteAction.SetInteger, SeedFollowTypeWrite.Plan(FollowStorage.Integer, false, true, "630.0", out i, out _));
            Assert.Equal(630, i);
            Assert.Equal(FollowWriteAction.SetDouble, SeedFollowTypeWrite.Plan(FollowStorage.Double, true, false, "2.5", out _, out var d));
            Assert.Equal(2.5, d);
        }

        [Fact]
        public void Values_that_cannot_be_written_are_refused()
        {
            Assert.Equal(FollowWriteAction.Invalid, SeedFollowTypeWrite.Plan(FollowStorage.Integer, false, true, "O2", out _, out _));
            Assert.Equal(FollowWriteAction.Invalid, SeedFollowTypeWrite.Plan(FollowStorage.Double, false, true, "3000", out _, out _));
            Assert.Equal(FollowWriteAction.Invalid, SeedFollowTypeWrite.Plan(FollowStorage.Double, false, true, "", out _, out _));
            Assert.Equal(FollowWriteAction.Invalid, SeedFollowTypeWrite.Plan(FollowStorage.ElementId, false, true, "12", out _, out _));
            Assert.Equal(FollowWriteAction.Invalid, SeedFollowTypeWrite.Plan(FollowStorage.Other, false, true, "", out _, out _));
        }
    }

    public class SeedFamilyMatchTests
    {
        [Theory]
        [InlineData("STING_SEED_MedGasOutlet")]
        [InlineData("sting_seed_medgasoutlet")]
        [InlineData("STING_SEED_MedGasOutlet1")]
        [InlineData("STING_SEED_MedGasOutlet12")]
        public void The_seed_and_its_digit_suffixed_copies_match(string family)
            => Assert.True(SeedFollowTypeCatalog.IsFamilyOfSeed(family, "STING_SEED_MedGasOutlet"));

        [Theory]
        [InlineData("STING_SEED_MedGasOutletX")]
        [InlineData("STING_SEED_MedGas")]
        [InlineData("STING_SEED_LabFixture1")]
        [InlineData("")]
        public void Other_families_do_not(string family)
            => Assert.False(SeedFollowTypeCatalog.IsFamilyOfSeed(family, "STING_SEED_MedGasOutlet"));
    }

    public class AreaBoxResolutionTests
    {
        private static readonly string[] Levels = { "L00", "L01", "L02" };
        private static readonly string[] Defaults = { "mep-hvac-duct-A1-1to100", "elec-power-A1-1to100" };

        [Fact]
        public void Without_a_plan_a_level_less_box_is_produced_on_every_level_with_the_defaults()
        {
            Assert.True(AreaBoxResolution.Resolve(null, "STING-AREA::A1-01", Defaults, Levels,
                out var types, out var levels, out var fromPlan, out var why), why);
            Assert.False(fromPlan);
            Assert.Equal(Defaults, types);
            Assert.Equal(Levels, levels);
        }

        [Fact]
        public void Without_a_plan_a_box_naming_its_level_is_produced_on_that_level()
        {
            Assert.True(AreaBoxResolution.Resolve(null, "STING-AREA::A1-01::L01", Defaults, Levels,
                out _, out var levels, out _, out _));
            Assert.Equal(new[] { "L01" }, levels);
        }

        [Fact]
        public void A_box_the_plan_lists_is_produced_as_planned()
        {
            var plan = new ScopeBoxPlanFile();
            plan.Boxes.Add(new ScopeBoxPlanFile.BoxEntry
            {
                Name = "STING-AREA::A1-01", ClassKey = "A1-100",
                DrawingTypes = new List<string> { "mep-coord-A1-1to50" }, Levels = new List<string> { "L02" },
            });
            Assert.True(AreaBoxResolution.Resolve(plan, "STING-AREA::A1-01", Defaults, Levels,
                out var types, out var levels, out var fromPlan, out _));
            Assert.True(fromPlan);
            Assert.Equal(new[] { "mep-coord-A1-1to50" }, types);
            Assert.Equal(new[] { "L02" }, levels);

            Assert.True(AreaBoxResolution.Resolve(plan, "STING-AREA::B1-01", Defaults, Levels,
                out types, out _, out fromPlan, out _));
            Assert.False(fromPlan);
            Assert.Equal(Defaults, types);
        }

        [Fact]
        public void No_plan_and_no_defaults_is_reported_not_guessed()
        {
            Assert.False(AreaBoxResolution.Resolve(null, "STING-AREA::A1-01", new string[0], Levels,
                out _, out _, out _, out var why));
            Assert.Contains("no saved plan", why);
        }

        [Fact]
        public void A_malformed_area_name_fails_with_the_grammar()
        {
            Assert.False(AreaBoxResolution.Resolve(null, "STING-AREA::bad name", Defaults, Levels,
                out _, out _, out _, out var why));
            Assert.Equal(ScopeBoxNames.AreaPatternReason, why);
        }
    }

    public class ExportSheetScopeTests
    {
        private sealed class Sheet { public string No; public long[] Revs; }

        private static readonly List<Sheet> Sheets = new List<Sheet>
        {
            new Sheet { No = "M-001", Revs = new long[] { 10, 11 } },
            new Sheet { No = "M-002", Revs = new long[] { 10 } },
            new Sheet { No = "E-001", Revs = new long[0] },
        };

        [Fact]
        public void Current_revision_keeps_only_the_sheets_carrying_it()
        {
            var picked = ExportSheetScope.Select(Sheets, s => s.Revs, 11, ExportSheetScope.CurrentRevision, out int excluded);
            Assert.Equal(new[] { "M-001" }, picked.Select(s => s.No));
            Assert.Equal(2, excluded);
        }

        [Fact]
        public void All_or_no_revision_keeps_every_sheet()
        {
            Assert.Equal(3, ExportSheetScope.Select(Sheets, s => s.Revs, 11, ExportSheetScope.All, out var x1).Count);
            Assert.Equal(0, x1);
            Assert.Equal(3, ExportSheetScope.Select(Sheets, s => s.Revs, null, ExportSheetScope.CurrentRevision, out var x2).Count);
            Assert.Equal(0, x2);
        }

        [Theory]
        [InlineData(null, ExportSheetScope.CurrentRevision)]
        [InlineData("", ExportSheetScope.CurrentRevision)]
        [InlineData("current-revision", ExportSheetScope.CurrentRevision)]
        [InlineData("Current Revision", ExportSheetScope.CurrentRevision)]
        [InlineData("all", ExportSheetScope.All)]
        public void The_param_is_read(string raw, string expected)
            => Assert.Equal(expected, ExportSheetScope.Parse(raw, ExportSheetScope.CurrentRevision, out _));

        [Fact]
        public void An_unknown_param_fails_with_the_reason()
        {
            Assert.Null(ExportSheetScope.Parse("latest", ExportSheetScope.All, out var err));
            Assert.Contains("latest", err);
        }
    }
}
