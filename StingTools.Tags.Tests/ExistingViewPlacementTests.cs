// An engine-made view (SLD, riser, panel schedule) on its drawing type's sheet: what a re-run takes off.

using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ExistingViewPlacementTests
    {
        private static PlacedView P(long id, string dt) => new PlacedView { ViewId = id, DrawingTypeId = dt };

        [Fact]
        public void An_earlier_run_of_the_same_type_makes_way_and_other_views_stay()
        {
            var d = ExistingViewPlacement.Decide(new[] { P(1, "elec-sld-A1-NTS"), P(2, "legend-A3"), P(3, null) },
                viewId: 9, drawingTypeId: "elec-sld-A1-NTS");
            Assert.False(d.AlreadyPlaced);
            Assert.Equal(new long[] { 1 }, d.RemoveViewIds.ToArray());
        }

        [Fact]
        public void The_view_already_on_the_sheet_is_not_placed_again()
        {
            var d = ExistingViewPlacement.Decide(new[] { P(9, "elec-sld-A1-NTS") }, 9, "ELEC-SLD-A1-NTS");
            Assert.True(d.AlreadyPlaced);
            Assert.Empty(d.RemoveViewIds);
        }

        [Fact]
        public void An_empty_sheet_places_and_removes_nothing()
        {
            var d = ExistingViewPlacement.Decide(null, 9, "x");
            Assert.False(d.AlreadyPlaced);
            Assert.Empty(d.RemoveViewIds);
        }
    }
}
