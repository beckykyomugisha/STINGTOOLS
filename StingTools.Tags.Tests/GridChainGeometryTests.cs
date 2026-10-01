using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing.Dimensioning;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-86: grid chains assumed world-axis grids — a grid's position was its
    /// origin's X or Y and the dimension line ran along a world axis. On a rotated
    /// building the positions were nonsense (often all equal: "grids are
    /// coincident", no chain) and the line was not perpendicular to the grids.
    /// The planner groups by direction, measures along each set's normal and runs
    /// the line along that normal.
    /// </summary>
    public class GridChainGeometryTests
    {
        private const double Margin = 10.0;

        private static GridSeg Seg(int i, double ox, double oy, double dx, double dy, double len = 100)
            => new GridSeg(i, ox, oy, ox + dx * len, oy + dy * len);

        /// <summary>Three grids at <paramref name="deg"/>, 6 ft apart across the set, each
        /// slid along its own direction so every origin has the same world Y.</summary>
        private static List<GridSeg> RotatedSet(double deg, int startIndex = 0)
        {
            double r = deg * Math.PI / 180, dx = Math.Cos(r), dy = Math.Sin(r);
            double nx = -dy, ny = dx;
            var list = new List<GridSeg>();
            for (int i = 0; i < 3; i++)
            {
                double s = 6.0 * i;
                double t = -s * ny / dy;           // slide so origin.Y == 0
                list.Add(Seg(startIndex + i, nx * s + dx * t, ny * s + dy * t, dx, dy));
            }
            return list;
        }

        [Fact]
        public void Rotated_grids_are_measured_across_the_set_not_by_world_Y()
        {
            var plans = GridChainGeometry.Plan(RotatedSet(30), Margin);
            var p = Assert.Single(plans);
            Assert.True(p.Placeable, p.Reason);
            Assert.Equal(3, p.Members.Count);
            Assert.Equal(6.0, p.Positions[1] - p.Positions[0], 6);
            Assert.Equal(6.0, p.Positions[2] - p.Positions[1], 6);
            Assert.Equal("angle-30.0", p.Label);
        }

        [Fact]
        public void The_dimension_line_runs_across_the_grids_and_clear_of_their_ends()
        {
            var grids = RotatedSet(30);
            var p = GridChainGeometry.Plan(grids, Margin).Single();
            double lx = p.LineX1 - p.LineX0, ly = p.LineY1 - p.LineY0;
            double len = Math.Sqrt(lx * lx + ly * ly);
            // Perpendicular to the grid direction.
            Assert.Equal(0.0, (lx * p.DirX + ly * p.DirY) / len, 6);
            // Spans the set plus a margin either side.
            Assert.Equal(12.0 + 2 * Margin, len, 6);
            // Beyond the far end of every grid.
            double lineAlong = p.LineX0 * p.DirX + p.LineY0 * p.DirY;
            double maxEnd = grids.Max(g => Math.Max(g.X0 * p.DirX + g.Y0 * p.DirY, g.X1 * p.DirX + g.Y1 * p.DirY));
            Assert.Equal(maxEnd + Margin, lineAlong, 6);
        }

        [Fact]
        public void A_rotated_orthogonal_grid_gets_two_chains()
        {
            var grids = RotatedSet(30).Concat(RotatedSet(120, startIndex: 10)).ToList();
            var plans = GridChainGeometry.Plan(grids, Margin);
            Assert.Equal(2, plans.Count);
            Assert.All(plans, p => Assert.True(p.Placeable, p.Reason));
            Assert.Contains(plans, p => p.Members.All(i => i < 10));
            Assert.Contains(plans, p => p.Members.All(i => i >= 10));
        }

        [Fact]
        public void World_axis_grids_keep_their_labels_and_positions()
        {
            var grids = new List<GridSeg>
            {
                Seg(0, 0, 0, 1, 0), Seg(1, 0, 20, 1, 0),            // east-west at y = 0, 20
                Seg(2, 5, -10, 0, 1), Seg(3, 35, -10, 0, 1),        // north-south at x = 5, 35
            };
            var plans = GridChainGeometry.Plan(grids, Margin);
            var ew = plans.Single(p => p.Label == "east-west");
            var ns = plans.Single(p => p.Label == "north-south");
            Assert.Equal(new[] { 0.0, 20.0 }, ew.Positions.Select(x => Math.Round(x, 6)));
            Assert.Equal(30.0, ns.Positions[1] - ns.Positions[0], 6);
            // East-west chain runs north-south, beyond the eastern extent (x = 100 + margin).
            Assert.Equal(ew.LineX0, ew.LineX1, 6);
            Assert.Equal(100.0 + Margin, ew.LineX0, 6);
            // North-south chain runs east-west, beyond the northern extent (y = 90 + margin).
            Assert.Equal(ns.LineY0, ns.LineY1, 6);
            Assert.Equal(90.0 + Margin, ns.LineY0, 6);
        }

        [Fact]
        public void A_grid_drawn_the_other_way_joins_the_same_set()
        {
            var grids = new List<GridSeg>
            {
                Seg(0, 0, 0, 1, 0),
                new GridSeg(1, 100, 15, 0, 15),   // reversed
            };
            var p = GridChainGeometry.Plan(grids, Margin).Single();
            Assert.True(p.Placeable, p.Reason);
            Assert.Equal(15.0, p.Positions[1] - p.Positions[0], 6);
        }

        [Fact]
        public void A_grid_on_top_of_another_is_dropped_not_dimensioned_to_zero()
        {
            // e.g. a host grid and the same grid in a link.
            var grids = new List<GridSeg> { Seg(0, 0, 0, 1, 0), Seg(1, 3, 0, 1, 0), Seg(2, 0, 20, 1, 0) };
            var p = GridChainGeometry.Plan(grids, Margin).Single();
            Assert.True(p.Placeable);
            Assert.Equal(2, p.Members.Count);
            Assert.Equal(1, p.CoincidentDropped);
        }

        [Fact]
        public void Coincident_or_single_grids_are_not_placeable_and_say_why()
        {
            var coincident = GridChainGeometry.Plan(new List<GridSeg> { Seg(0, 0, 0, 1, 0), Seg(1, 0, 0, 1, 0) }, Margin).Single();
            Assert.False(coincident.Placeable);
            Assert.Equal("grids are coincident", coincident.Reason);

            var single = GridChainGeometry.Plan(new List<GridSeg> { Seg(0, 0, 0, 1, 0) }, Margin).Single();
            Assert.False(single.Placeable);
            Assert.NotNull(single.Reason);

            Assert.Empty(GridChainGeometry.Plan(new List<GridSeg> { new GridSeg(0, 1, 1, 1, 1) }, Margin));
        }

        // DTW-101: column-to-grid reads the host's grids and every loaded link's. Where a
        // linked grid lies on a host grid (copy / monitor), the host one is used.

        [Fact]
        public void A_linked_grid_on_a_host_grid_is_dropped_and_the_host_kept()
        {
            var grids = new List<GridSeg>
            {
                Seg(0, 0, 0, 1, 0),              // host A
                Seg(1, 20, 0.0005, 1, 0, 30),    // linked A: same line, 0.15 mm off, shorter, slid along
                Seg(2, 0, 20, 1, 0),             // linked B: its own line
            };
            Assert.Equal(new[] { 0, 2 }, GridChainGeometry.KeepFirstOfCoincident(grids));
        }

        [Fact]
        public void Grids_apart_or_crossing_are_all_kept()
        {
            var grids = new List<GridSeg>
            {
                Seg(0, 0, 0, 1, 0),
                Seg(1, 0, 10.0 / 304.8, 1, 0),   // 10 mm away: a different grid
                Seg(2, 50, -50, 0, 1),           // crosses grid 0
                Seg(3, 0, 0, -1, 0),             // same line, opposite direction: coincident
            };
            Assert.Equal(new[] { 0, 1, 2 }, GridChainGeometry.KeepFirstOfCoincident(grids));
            Assert.Empty(GridChainGeometry.KeepFirstOfCoincident(new List<GridSeg>()));
            Assert.Empty(GridChainGeometry.KeepFirstOfCoincident(null));
        }
    }
}
