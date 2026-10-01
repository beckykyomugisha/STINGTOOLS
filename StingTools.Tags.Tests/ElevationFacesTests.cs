using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-54 / DTW-27: which elevation faces a type asks for, and which way a produced elevation looks. The plugin calls the same file.</summary>
    public class ElevationFacesTests
    {

        [Fact]
        public void A_type_with_four_elevation_slots_gets_four_faces_one_slot_each()
        {
            var plan = ElevationFaces.Plan(null, new[] { "Elevation", "Elevation", "Elevation", "Elevation" });
            Assert.Equal(new[] { 0, 1, 2, 3 }, plan.Select(p => p.Face));
            Assert.Equal(new[] { 0, 1, 2, 3 }, plan.Select(p => p.SlotIndex));
        }

        [Fact]
        public void A_type_with_one_elevation_slot_gets_one_face()
            => Assert.Single(ElevationFaces.Plan(null, new[] { "Elevation", "Legend" }));

        [Fact]
        public void Rules_choose_their_faces()
        {
            var rules = new List<ProductionRule>
            {
                new ProductionRule { Idx = 0, ViewType = "Elevation", ElevationFace = 2 },
                new ProductionRule { Idx = 1, ViewType = "Elevation", ElevationFace = 3 },
                new ProductionRule { Idx = 2, ViewType = "FloorPlan" },
            };
            var plan = ElevationFaces.Plan(rules, null);
            Assert.Equal(new[] { 2, 3 }, plan.Select(p => p.Face));
            Assert.Equal(new[] { 0, 1 }, plan.Select(p => p.RuleIdx));
        }

        [Fact]
        public void The_compass_names_the_way_the_view_looks()
        {
            Assert.Equal("North", ElevationFaces.Compass(0, 1));
            Assert.Equal("West", ElevationFaces.Compass(-1, 0.1));
            Assert.True(ElevationFaces.LooksToward(0, -1, 0.1, -1));
            Assert.False(ElevationFaces.LooksToward(0, 1, 0, -1));
            var n = ElevationFaces.ExteriorStation("North", 0, 0, 10, 20, 3).Value;
            Assert.Equal(23, n.Y, 6); Assert.Equal(-1, n.LookY, 6);
        }
    }
}
