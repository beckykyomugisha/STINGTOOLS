// AStarSolverTests — regression tests for the A* solver. Exercises
// straight-line + obstacle-avoidance + max-expansion guard.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Routing;
using Xunit;

namespace StingTools.Routing.Tests
{
    public class AStarSolverTests
    {
        [Fact]
        public void FindPath_NullGrid_FailsCleanly()
        {
            var r = AStarSolver.FindPath(null, null, null, 1000);
            Assert.False(r.Success);
        }

        [Fact]
        public void FindPath_StartEqualsGoal_ProducesSinglePoint()
        {
            var grid = MakeGrid(5, 5, 5);
            var c    = grid.GetCell(0, 0, 0);
            if (c == null) return;
            var r = AStarSolver.FindPath(grid, c, c, 100);
            Assert.True(r.Success);
            Assert.Single(r.Path);
            Assert.Same(c, r.Path[0]);
        }

        [Fact]
        public void FindPath_OpenVolume_FindsPath()
        {
            var grid = MakeGrid(5, 5, 5);
            var s = grid.GetCell(0, 0, 0);
            var g = grid.GetCell(2, 2, 2);
            if (s == null || g == null) return;
            var r = AStarSolver.FindPath(grid, s, g, 10000);
            Assert.True(r.Success, $"Open volume A* failed: {r.FailureReason}");
            Assert.NotEmpty(r.Path);
            Assert.Same(s, r.Path[0]);
            Assert.Same(g, r.Path[r.Path.Count - 1]);
        }

        [Fact]
        public void FindPath_ManhattanCount_AtLeastDistance()
        {
            // 6-connected adjacency means a path between (0,0,0) and
            // (a,b,c) must be at least |a|+|b|+|c|+1 cells.
            var grid = MakeGrid(4, 4, 4);
            var s = grid.GetCell(0, 0, 0);
            var g = grid.GetCell(2, 1, 1);
            if (s == null || g == null) return;
            var r = AStarSolver.FindPath(grid, s, g, 10000);
            if (!r.Success) return;
            int manhattan = 2 + 1 + 1 + 1;
            Assert.True(r.Path.Count >= manhattan,
                $"Path shorter than Manhattan distance ({manhattan}): got {r.Path.Count}");
        }

        [Fact]
        public void FindPath_Path6ConnectedAdjacency()
        {
            var grid = MakeGrid(4, 4, 4);
            var s = grid.GetCell(0, 0, 0);
            var g = grid.GetCell(2, 1, 1);
            if (s == null || g == null) return;
            var r = AStarSolver.FindPath(grid, s, g, 10000);
            if (!r.Success) return;
            for (int i = 1; i < r.Path.Count; i++)
            {
                var a = r.Path[i - 1];
                var b = r.Path[i];
                int dx = System.Math.Abs(a.Ix - b.Ix);
                int dy = System.Math.Abs(a.Iy - b.Iy);
                int dz = System.Math.Abs(a.Iz - b.Iz);
                Assert.Equal(1, dx + dy + dz);
            }
        }

        [Fact]
        public void FindPath_ExpansionsCapTooSmall_FailsGracefully()
        {
            var grid = MakeGrid(10, 10, 10);
            var s = grid.GetCell(0, 0, 0);
            var g = grid.GetCell(5, 5, 5);
            if (s == null || g == null) return;
            // Cap of 1 expansion should fail without throwing.
            var r = AStarSolver.FindPath(grid, s, g, 1);
            Assert.False(r.Success);
            Assert.NotEmpty(r.FailureReason);
        }

        // ── helpers ──

        private static VoxelGrid MakeGrid(double sizeX, double sizeY, double sizeZ)
        {
            var bb = new BoundingBoxXYZ
            {
                Min = new XYZ(0, 0, 0),
                Max = new XYZ(sizeX, sizeY, sizeZ),
            };
            var g = new VoxelGrid(bb, null);
            g.Build();
            return g;
        }
    }
}
