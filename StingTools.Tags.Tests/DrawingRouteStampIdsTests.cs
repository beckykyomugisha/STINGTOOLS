// A sheet or view made for a routing key carries the drawing-type id that key routed
// to when it was made. When a project override later re-routes the key, the sheets
// stamped with the shipped id are still that request's sheets: looking up only the
// routed id would miss them and mint duplicates (the panel-schedule sheets are found
// again by stamp). DrawingRouteRequests.StampIds is the list every lookup matches.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingRouteStampIdsTests
    {
        [Fact]
        public void Routed_to_the_shipped_type_the_lookup_matches_that_one_id()
        {
            var ids = DrawingRouteRequests.StampIds("elec-panel-schedule-A3", DrawingRouteRequests.PanelSchedule);
            Assert.Equal(new[] { "elec-panel-schedule-A3" }, ids);
        }

        [Fact]
        public void Re_routed_the_lookup_matches_the_routed_id_first_then_the_shipped_one()
        {
            var ids = DrawingRouteRequests.StampIds("prj-panel-schedule-A2", DrawingRouteRequests.PanelSchedule);
            Assert.Equal(new[] { "prj-panel-schedule-A2", "elec-panel-schedule-A3" }, ids);
        }

        [Fact]
        public void The_shipped_id_is_matched_once_whatever_its_case()
        {
            var ids = DrawingRouteRequests.StampIds("ELEC-PANEL-SCHEDULE-A3", DrawingRouteRequests.PanelSchedule);
            Assert.Single(ids);
        }

        [Fact]
        public void Nothing_routed_leaves_the_shipped_id()
        {
            Assert.Equal(new[] { "plumb-drainage-schematic-A1" },
                DrawingRouteRequests.StampIds(null, DrawingRouteRequests.DrainageSchematic));
        }

        [Fact]
        public void An_earlier_view_under_the_shipped_id_makes_way_on_a_re_routed_sheet()
        {
            var d = ExistingViewPlacement.Decide(new[]
                {
                    new PlacedView { ViewId = 1, DrawingTypeId = "elec-panel-schedule-A3" },
                    new PlacedView { ViewId = 2, DrawingTypeId = "legend-A3" },
                }, 9, "prj-panel-schedule-A2",
                DrawingRouteRequests.StampIds("prj-panel-schedule-A2", DrawingRouteRequests.PanelSchedule));
            Assert.Equal(new long[] { 1 }, d.RemoveViewIds);
        }
    }
}
