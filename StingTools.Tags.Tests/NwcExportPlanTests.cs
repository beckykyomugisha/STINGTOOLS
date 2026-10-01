// NW-1: the Export Centre's NWC defaults asked Revit for an empty selection in internal
// coordinates. These pin what each profile value now exports.

using StingTools.Docs;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class NwcExportPlanTests
    {
        [Fact]
        public void New_profile_defaults_export_the_whole_model_in_shared_coordinates_with_element_ids()
        {
            var p = NwcExportPlan.For(NwcExportPlan.DefaultScope, NwcExportPlan.DefaultCoordinates, true);
            Assert.Equal("Model", p.Scope);
            Assert.Equal("Shared", p.Coordinates);
            Assert.True(p.ExportElementIds);
            Assert.Empty(p.Notes);
        }

        [Fact]
        public void A_profile_saved_with_the_old_defaults_exports_something_and_says_how_it_was_read()
        {
            var p = NwcExportPlan.For("Selected", "Project", true);
            Assert.Equal("Model", p.Scope);           // never an empty selection
            Assert.Equal("Shared", p.Coordinates);
            Assert.Equal(2, p.Notes.Count);
            Assert.Contains(p.Notes, n => n.Contains("Selected"));
            Assert.Contains(p.Notes, n => n.Contains("'Project'") && n.Contains("Internal"));
        }

        [Theory]
        [InlineData("CurrentView", "View")]
        [InlineData("currentview", "View")]
        [InlineData("Entire", "Model")]
        [InlineData(null, "Model")]
        [InlineData("Whatever", "Model")]
        public void Scope_maps_to_a_NavisworksExportScope_member(string scope, string expected)
            => Assert.Equal(expected, NwcExportPlan.For(scope, "Shared", true).Scope);

        [Fact]
        public void Internal_coordinates_are_kept_when_asked_for()
        {
            var p = NwcExportPlan.For("Entire", "Internal", true);
            Assert.Equal("Internal", p.Coordinates);
            Assert.Empty(p.Notes);
        }

        [Fact]
        public void Element_ids_off_is_allowed_but_said()
            => Assert.Contains(NwcExportPlan.For("Entire", "Shared", false).Notes, n => n.Contains("Element IDs are off"));
    }
}
