// The production context tag, and why a style re-sync keeps a scope-box crop.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ViewContextTagTests
    {
        [Theory]
        [InlineData("L01", "", "A01", "STING-AREA::A01::L01")]
        [InlineData("L01", "", "Z01", "STING::mep-plan-A1-1to100::L01::Z01")]
        [InlineData("", "", "", "Scope Box 3")]
        [InlineData("L02", "12345", "", "B")]
        public void The_box_name_comes_back_whole_even_when_it_contains_separators(string lvl, string room, string tag, string box)
        {
            var composed = ViewContextTag.Compose(lvl, room, tag, box);
            Assert.Equal(box, ViewContextTag.ScopeBoxName(composed));
        }

        [Theory]
        [InlineData("L01::::")]                          // per-level, no box
        [InlineData("L01::::STING-DEPENDENT-PARENT")]    // a dependent's parent
        [InlineData("exterior::face::N")]                // exterior elevations
        [InlineData("")]
        [InlineData(null)]
        public void A_tag_with_no_box_names_none(string tag)
        {
            Assert.Null(ViewContextTag.ScopeBoxName(tag));
        }

        [Fact]
        public void Compose_without_a_box_is_the_three_part_tag_existing_views_carry()
        {
            Assert.Equal("L01::::", ViewContextTag.Compose("L01", "", null, null));
        }

        [Theory]
        [InlineData(null, false, false, CropRecovery.UseProfile)]
        [InlineData(null, true, true, CropRecovery.UseProfile)]
        [InlineData("STING-AREA::A01", true, true, CropRecovery.KeepAssigned)]
        [InlineData("STING-AREA::A01", true, false, CropRecovery.KeepAssigned)]
        [InlineData("STING-AREA::A01", false, true, CropRecovery.RestoreFromTag)]
        [InlineData("STING-AREA::A01", false, false, CropRecovery.LeaveAlone)]
        public void A_resync_never_replaces_a_scope_box_crop_with_the_profile_crop(string box, bool assigned, bool exists, CropRecovery expected)
        {
            Assert.Equal(expected, ViewContextTag.Decide(box, assigned, exists));
        }
    }
}
