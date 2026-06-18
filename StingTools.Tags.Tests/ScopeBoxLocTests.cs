using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the scope-box LOC plan-rectangle containment + most-specific
    /// (smallest-area) selection used for site-element LOC detection.
    /// </summary>
    public class ScopeBoxLocTests
    {
        private static ScopeBoxLoc Box(string loc, double minX, double minY, double maxX, double maxY)
            => new ScopeBoxLoc { Loc = loc, MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };

        [Fact]
        public void Contains_is_inclusive_rectangle()
        {
            var b = Box("BLD1", 0, 0, 10, 10);
            Assert.True(b.Contains(5, 5));
            Assert.True(b.Contains(0, 0));   // edge inclusive
            Assert.True(b.Contains(10, 10));
            Assert.False(b.Contains(11, 5));
            Assert.False(b.Contains(5, -1));
        }

        [Fact]
        public void Area_is_width_times_height()
            => Assert.Equal(200.0, Box("X", 0, 0, 20, 10).Area, 6);

        [Fact]
        public void SmallestContaining_picks_most_specific_overlapping_box()
        {
            // A big campus box and a smaller building box both contain (5,5).
            var big = Box("BLD0", 0, 0, 100, 100);      // area 10000
            var small = Box("BLD2", 0, 0, 10, 10);      // area 100, nested
            var boxes = new List<ScopeBoxLoc> { big, small };
            Assert.Equal("BLD2", ScopeBoxLoc.SmallestContaining(boxes, 5, 5).Loc);
            // order-independent
            var reversed = new List<ScopeBoxLoc> { small, big };
            Assert.Equal("BLD2", ScopeBoxLoc.SmallestContaining(reversed, 5, 5).Loc);
        }

        [Fact]
        public void SmallestContaining_point_only_in_big_box_returns_big()
        {
            var big = Box("BLD0", 0, 0, 100, 100);
            var small = Box("BLD2", 0, 0, 10, 10);
            Assert.Equal("BLD0", ScopeBoxLoc.SmallestContaining(new[] { big, small }, 50, 50).Loc);
        }

        [Fact]
        public void SmallestContaining_returns_null_when_no_box_contains_point()
        {
            var boxes = new List<ScopeBoxLoc> { Box("A", 0, 0, 1, 1) };
            Assert.Null(ScopeBoxLoc.SmallestContaining(boxes, 5, 5));
            Assert.Null(ScopeBoxLoc.SmallestContaining(null, 0, 0));
        }

        [Fact]
        public void SmallestContaining_tie_resolves_to_first_in_order()
        {
            var a = Box("A", 0, 0, 10, 10);   // area 100
            var b = Box("B", 0, 0, 10, 10);   // identical area, contains point
            Assert.Equal("A", ScopeBoxLoc.SmallestContaining(new[] { a, b }, 5, 5).Loc);
        }
    }
}
