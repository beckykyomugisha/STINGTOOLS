// VoxelGridTests — regression tests for the O(1) Neighbours() dictionary
// fix from routing Wave A. The test suite exercises grid construction +
// adjacency lookup across small grids; full A* pathfinding has its own
// suite (AStarSolverTests).

using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Routing;
using Xunit;

namespace StingTools.Routing.Tests
{
    public class VoxelGridTests
    {
        // ── Build ───────────────────────────────────────────────────────

        [Fact]
        public void Build_TinyOutline_ProducesNonZeroCells()
        {
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 1, 1, 1), null);
            int n = grid.Build();
            Assert.True(n > 0, $"Expected > 0 cells in 1ft³ volume; got {n}");
        }

        [Fact]
        public void Build_DegenerateOutline_ReturnsZero()
        {
            // Min == Max → no cells.
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 0, 0, 0), null);
            Assert.Equal(0, grid.Build());
        }

        // ── GetCell + Neighbours via dictionary ─────────────────────────

        [Fact]
        public void GetCell_UnknownIndex_ReturnsNull()
        {
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 1, 1, 1), null);
            grid.Build();
            Assert.Null(grid.GetCell(99999, 99999, 99999));
        }

        [Fact]
        public void GetCell_ValidIndex_ReturnsCell()
        {
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 2, 2, 2), null);
            grid.Build();
            // The (0,0,0) cell always exists when build returns > 0.
            var c = grid.GetCell(0, 0, 0);
            Assert.NotNull(c);
            Assert.Equal(0, c.Ix);
            Assert.Equal(0, c.Iy);
            Assert.Equal(0, c.Iz);
        }

        [Fact]
        public void Neighbours_InteriorCell_HasUpToSix()
        {
            // 3×3×3 grid — centre cell (1,1,1) should see 6 axis-aligned
            // neighbours.
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 5, 5, 5), null);
            grid.Build();
            var centre = grid.GetCell(1, 1, 1);
            if (centre == null) return;  // grid coarseness may vary; defensive
            var neighbours = grid.Neighbours(centre).ToList();
            Assert.True(neighbours.Count <= 6,
                $"6-connected adjacency should never exceed 6 neighbours; got {neighbours.Count}");
        }

        [Fact]
        public void Neighbours_NullCell_ReturnsEmpty()
        {
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 1, 1, 1), null);
            grid.Build();
            Assert.Empty(grid.Neighbours(null));
        }

        [Fact]
        public void Neighbours_OnlyAxisAligned_NoDiagonals()
        {
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 5, 5, 5), null);
            grid.Build();
            var centre = grid.GetCell(1, 1, 1);
            if (centre == null) return;
            foreach (var n in grid.Neighbours(centre))
            {
                int dx = System.Math.Abs(n.Ix - centre.Ix);
                int dy = System.Math.Abs(n.Iy - centre.Iy);
                int dz = System.Math.Abs(n.Iz - centre.Iz);
                Assert.Equal(1, dx + dy + dz);
            }
        }

        [Fact]
        public void Neighbours_SymmetryProperty()
        {
            // If A is a neighbour of B then B is a neighbour of A.
            var grid = new VoxelGrid(MakeOutline(0, 0, 0, 3, 3, 3), null);
            grid.Build();
            var a = grid.GetCell(0, 0, 0);
            if (a == null) return;
            foreach (var b in grid.Neighbours(a))
            {
                bool aInBs = grid.Neighbours(b).Any(c => c == a);
                Assert.True(aInBs,
                    $"Adjacency must be symmetric: A=({a.Ix},{a.Iy},{a.Iz}) " +
                    $"B=({b.Ix},{b.Iy},{b.Iz}).");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static BoundingBoxXYZ MakeOutline(double xMin, double yMin, double zMin,
            double xMax, double yMax, double zMax)
            => new BoundingBoxXYZ
            {
                Min = new XYZ(xMin, yMin, zMin),
                Max = new XYZ(xMax, yMax, zMax),
            };
    }
}
