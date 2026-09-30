// Match lines between scope boxes: the Revit-free geometry MatchLineEngine uses.
//
// The engine used to test AABB faces for coincidence within 1 mm. The Scope Box
// Planner's area boxes overlap by 2 m and may be turned to the grid, so they
// never matched. These pin the replacement: touching, overlapping (line down the
// middle of the strip), rotated, non-adjacent and corner-only cases, plus which
// boxes and which views take part at all.

using System;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MatchLineGeometryTests
    {
        private const double Tol = 1.0 / 304.8;      // 1 mm in feet, as the engine passes it
        private const double MinSpan = 100.0 / 304.8; // 100 mm

        private static MatchLineRect Box(double minX, double minY, double maxX, double maxY)
            => new MatchLineRect((minX + maxX) / 2, (minY + maxY) / 2, maxX - minX, maxY - minY);

        [Fact]
        public void Touching_boxes_get_a_line_on_the_shared_face()
        {
            var seg = MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(60, 0, 120, 40), Tol, MinSpan);
            Assert.NotNull(seg);
            Assert.Equal("vertical", seg.Direction);
            Assert.Equal(60, seg.X0, 6);
            Assert.Equal(60, seg.X1, 6);
            Assert.Equal(40, seg.Length, 6);
        }

        [Fact]
        public void Touching_boxes_stacked_north_south_get_a_horizontal_line()
        {
            var seg = MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(10, 40, 50, 90), Tol, MinSpan);
            Assert.NotNull(seg);
            Assert.Equal("horizontal", seg.Direction);
            Assert.Equal(40, seg.Y0, 6);
            Assert.Equal(10, Math.Min(seg.X0, seg.X1), 6);
            Assert.Equal(50, Math.Max(seg.X0, seg.X1), 6);
        }

        [Fact]
        public void Overlapping_boxes_get_a_line_down_the_middle_of_the_strip()
        {
            // The planner's layout: 2 m overlap between neighbours.
            var seg = MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(58, 0, 118, 40), Tol, MinSpan);
            Assert.NotNull(seg);
            Assert.Equal("vertical", seg.Direction);
            Assert.Equal(59, seg.X0, 6);
            Assert.Equal(59, seg.X1, 6);
            Assert.Equal(2, seg.OverlapDepth, 6);
            Assert.Equal(40, seg.Length, 6);
        }

        [Fact]
        public void Rotated_overlapping_boxes_are_matched_in_their_own_frame()
        {
            double ang = 30 * Math.PI / 180, c = Math.Cos(ang), s = Math.Sin(ang);
            // Two 60 x 40 boxes turned 30°, centres 58 apart along the turned axis (2 overlap).
            var a = new MatchLineRect(0, 0, 60, 40, ang);
            var b = new MatchLineRect(58 * c, 58 * s, 60, 40, ang);
            var seg = MatchLineGeometry.Find(a, b, Tol, MinSpan);
            Assert.NotNull(seg);
            Assert.Equal(2, seg.OverlapDepth, 6);
            Assert.Equal(40, seg.Length, 6);
            // Mid-strip at u = 29 in the box frame; the line runs across the axis.
            double midX = (seg.X0 + seg.X1) / 2, midY = (seg.Y0 + seg.Y1) / 2;
            Assert.Equal(29 * c, midX, 6);
            Assert.Equal(29 * s, midY, 6);
            double dirDot = ((seg.X1 - seg.X0) * c + (seg.Y1 - seg.Y0) * s) / seg.Length;
            Assert.Equal(0, dirDot, 6);
        }

        [Fact]
        public void A_box_turned_a_quarter_turn_further_is_the_same_frame()
        {
            // 60 x 40 at 0° beside a 40 x 60 described at 90°: the same rectangle frame.
            var seg = MatchLineGeometry.Find(new MatchLineRect(30, 20, 60, 40, 0),
                                             new MatchLineRect(89, 20, 40, 60, Math.PI / 2), Tol, MinSpan);
            Assert.NotNull(seg);
            Assert.Equal(59.5, seg.X0, 6);
        }

        [Fact]
        public void Boxes_at_different_angles_get_no_line()
        {
            var seg = MatchLineGeometry.Find(new MatchLineRect(30, 20, 60, 40, 0),
                                             new MatchLineRect(88, 20, 60, 40, 10 * Math.PI / 180), Tol, MinSpan);
            Assert.Null(seg);
        }

        [Fact]
        public void Non_adjacent_boxes_get_no_line()
            => Assert.Null(MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(61, 0, 121, 40), Tol, MinSpan));

        [Fact]
        public void Boxes_touching_only_at_a_corner_get_no_line()
            => Assert.Null(MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(60, 40, 120, 80), Tol, MinSpan));

        [Fact]
        public void Diagonal_neighbours_overlapping_at_a_corner_get_no_line()
            => Assert.Null(MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(58, 38, 118, 78), Tol, MinSpan));

        [Fact]
        public void A_box_inside_another_gets_no_line()
            => Assert.Null(MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(10, 10, 20, 20), Tol, MinSpan));

        [Fact]
        public void A_shared_edge_shorter_than_the_minimum_gets_no_line()
            => Assert.Null(MatchLineGeometry.Find(Box(0, 0, 60, 40), Box(60, 39.99, 120, 80), Tol, MinSpan));

        [Theory]
        [InlineData("STING-LOC::BLD1", false)]
        [InlineData("STING-SEED::60x42", false)]
        [InlineData("STING-AREA::A1-01", true)]
        [InlineData("STING::mep-plan-A1-1to100::L01::Z01", true)]
        [InlineData("Scope Box 1", true)]
        public void Building_and_seed_boxes_never_carry_match_lines(string name, bool expected)
            => Assert.Equal(expected, MatchLineGeometry.IsMatchLineBox(name));

        [Fact]
        public void Views_pair_only_with_the_same_drawing_type_on_the_same_level()
        {
            Assert.True(MatchLineGeometry.ShouldPairViews("elec-power-A1-1to100", "ELEC-POWER-A1-1to100", "11", "11", "a", "b"));
            Assert.False(MatchLineGeometry.ShouldPairViews("elec-power-A1-1to100", "mep-hvac-duct-A1-1to100", "11", "11", "a", "a"));
            Assert.False(MatchLineGeometry.ShouldPairViews("elec-power-A1-1to100", "elec-power-A1-1to100", "11", "12", "a", "a"));
        }

        [Fact]
        public void Unstamped_views_pair_by_their_fallback_key_and_never_with_stamped_ones()
        {
            Assert.True(MatchLineGeometry.ShouldPairViews(null, "", "11", "11", "FloorPlan|7", "FloorPlan|7"));
            Assert.False(MatchLineGeometry.ShouldPairViews(null, null, "11", "11", "FloorPlan|7", "FloorPlan|8"));
            Assert.False(MatchLineGeometry.ShouldPairViews("elec-power-A1-1to100", null, "11", "11", "FloorPlan|7", "FloorPlan|7"));
        }
    }
}
