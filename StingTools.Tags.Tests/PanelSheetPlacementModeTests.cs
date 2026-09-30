// Panel_PlaceOnSheets: the mode a person gets, and the one a workflow preset gets.

using StingTools.Core.Panels;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PanelSheetPlacementModeTests
    {
        [Fact]
        public void A_person_gets_the_panel_choice_or_guided_manual()
        {
            Assert.Equal(PanelSheetPlacementMode.GuidedManual, PanelSheetPlacementMode.Resolve(false, null, null, out _));
            Assert.Equal(PanelSheetPlacementMode.ViewSchedule, PanelSheetPlacementMode.Resolve(false, "AutoSheets", "ViewSchedule", out _));
            Assert.Equal(PanelSheetPlacementMode.AutoSheets, PanelSheetPlacementMode.Resolve(false, null, "AutoSheets", out _));
        }

        [Fact]
        public void A_preset_defaults_to_auto_sheets()
        {
            Assert.Equal(PanelSheetPlacementMode.AutoSheets, PanelSheetPlacementMode.Resolve(true, "", "GuidedManual", out var err));
            Assert.Null(err);
            Assert.Equal(PanelSheetPlacementMode.AutoSheets, PanelSheetPlacementMode.Resolve(true, "auto", null, out _));
        }

        [Theory]
        [InlineData("GuidedManual")]
        [InlineData("ViewSchedule")]
        [InlineData("PDF")]
        [InlineData("bogus")]
        public void A_preset_cannot_run_a_mode_that_needs_a_person_or_does_not_exist(string mode)
        {
            Assert.Null(PanelSheetPlacementMode.Resolve(true, mode, null, out var err));
            Assert.False(string.IsNullOrEmpty(err));
        }
    }
}
