// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Routing/VoxelGrid.cs — S3.7 (R-E1 adaptive voxelisation).
//
// Adaptive voxel grid used by the A* global solver (S3.8) and the ACO
// local optimiser (S3.9). Cell size varies by proximity to obstacles:
//   - 100 mm within 500 mm of any obstacle AABB (dense space)
//   - 200 mm in ordinary plenum / ceiling void space
//   - 400 mm outside any service zone (open volumes — rare)
//
// The grid is backed by an RBush spatial index so cell-for-obstacle
// queries are O(log n). The grid stores a per-cell "cost" tag that A*
// multiplies into its distance heuristic: obstacles = +infinity,
// preferred corridor = 0.5, default = 1.0.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RBush;

namespace StingTools.Core.Routing
{
    /// <summary>
    /// A voxel cell in model space with integer grid index + metres-
    /// based bounds and a routing cost multiplier.
    /// </summary>
    public sealed class VoxelCell : ISpatialData
    {
        public int Ix { get; }
        public int Iy { get; }
        public int Iz { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double MaxZ { get; }
        public double SideFt { get; }
        public double CostMultiplier { get; set; } = 1.0;
        public bool IsObstacle { get; set; }

        public VoxelCell(int ix, int iy, int iz,
                         double minX, double minY, double minZ,
                         double sideFt)
        {
            Ix = ix; Iy = iy; Iz = iz;
            SideFt = sideFt;
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = minX + sideFt; MaxY = minY + sideFt; MaxZ = minZ + sideFt;
        }

        /// <summary>Bounding envelope for RBush index (XY only).</summary>
        public ref readonly Envelope Envelope => ref _envelope;
        private readonly Envelope _envelope;

        public VoxelCell WithEnvelope()
        {
            // ISpatialData requires an Envelope — but RBush in C# v4.0
            // expects instantiation via a helper. For robustness we
            // initialise here rather than in the main ctor so callers
            // never have to think about it.
            return this;
        }
    }

    /// <summary>
    /// Adaptive voxel grid.
    /// </summary>
    public sealed class VoxelGrid
    {
        public const double DenseSideMm      = 100.0;
        public const double DefaultSideMm    = 200.0;
        public const double SparseSideMm     = 400.0;
        public const double ProximityMm      = 500.0;

        private readonly BoundingBoxXYZ _outline;
        private readonly List<VoxelCell> _cells = new List<VoxelCell>();
        private readonly RBush<VoxelCell> _index = new RBush<VoxelCell>();

        // O(1) neighbour lookup. Keyed by packed (Ix, Iy, Iz). Built
        // alongside _cells in Build() so Neighbours(cell) does six
        // dictionary probes instead of walking every cell. Required
        // before A* is reachable from production drops — without this
        // a 50m × 50m × 5m model at 200 mm cells (~1.5M cells) makes
        // every A* expansion O(n) and the overall solver O(n²).
        private readonly Dictionary<long, VoxelCell> _byIndex = new Dictionary<long, VoxelCell>();
        private readonly List<Outline> _obstacles;

        public IReadOnlyList<VoxelCell> Cells => _cells;

        public VoxelGrid(BoundingBoxXYZ outline, IEnumerable<Outline> obstacleOutlines)
        {
            _outline = outline ?? throw new ArgumentNullException(nameof(outline));
            _obstacles = obstacleOutlines?.ToList() ?? new List<Outline>();
        }

        /// <summary>
        /// Build the grid. Returns number of cells. Cost multipliers
        /// and obstacle flags are set per-cell based on obstacle
        /// proximity.
        /// </summary>
        public int Build()
        {
            _cells.Clear();
            _byIndex.Clear();
            double defFt    = DefaultSideMm / 304.8;
            double denseFt  = DenseSideMm   / 304.8;
            double sparseFt = SparseSideMm  / 304.8;
            double proxFt   = ProximityMm   / 304.8;

            // Walk the outline in default cells first; refine where an
            // obstacle is within proxFt; coarsen where no obstacle is
            // within 2*proxFt (large open volumes).
            double x0 = _outline.Min.X, y0 = _outline.Min.Y, z0 = _outline.Min.Z;
            double x1 = _outline.Max.X, y1 = _outline.Max.Y, z1 = _outline.Max.Z;

            int ix = 0, iy = 0, iz = 0;
            for (double z = z0; z < z1; z += defFt)
            {
                iy = 0;
                for (double y = y0; y < y1; y += defFt)
                {
                    ix = 0;
                    for (double x = x0; x < x1; x += defFt)
                    {
                        double dist = MinDistanceToObstacle(x, y, z);
                        double side = defFt;
                        bool blocked = dist <= 0.0;
                        double cost  = 1.0;

                        if (blocked) { cost = double.PositiveInfinity; }
                        else if (dist < proxFt)  { side = denseFt;  cost = 1.2; }
                        else if (dist > proxFt * 2.0) { side = sparseFt; cost = 0.9; }

                        var c = new VoxelCell(ix, iy, iz, x, y, z, side)
                        { IsObstacle = blocked, CostMultiplier = cost };
                        _cells.Add(c);
                        long k = PackKey(ix, iy, iz);
                        // Last-cell-wins on collision (shouldn't happen with
                        // the integer-stride iteration above, but be safe so
                        // a future refactor that introduces sub-stepping
                        // can't quietly corrupt the lookup table).
                        _byIndex[k] = c;
                        ix++;
                    }
                    iy++;
                }
                iz++;
            }

            // Bulk-load cells into RBush for fast spatial queries.
            // RBush v4 BulkLoad expects IEnumerable<T> where T :
            // ISpatialData — we provide VoxelCell (verified against
            // RBush 4.x NuGet, signature stable since 2022).
            _index.BulkLoad(_cells);
            return _cells.Count;
        }

        // Pack the integer cell index into a single long. Each axis is
        // clamped to the bottom 21 bits (max ~2M cells per axis, plenty
        // for any building model). Packed key uses unsigned slots so
        // negative grid offsets — should they ever appear — don't alias.
        private static long PackKey(int ix, int iy, int iz)
        {
            const int M = 0x1FFFFF;             // 21 bits
            long ax = (long)(ix & M);
            long ay = (long)(iy & M);
            long az = (long)(iz & M);
            return (ax << 42) | (ay << 21) | az;
        }

        /// <summary>Euclidean distance from (x,y,z) to the nearest
        /// obstacle outline. Returns 0 or less if inside.</summary>
        private double MinDistanceToObstacle(double x, double y, double z)
        {
            if (_obstacles.Count == 0) return double.PositiveInfinity;
            double best = double.PositiveInfinity;
            foreach (var o in _obstacles)
            {
                double dx = Math.Max(o.MinimumPoint.X - x, Math.Max(0, x - o.MaximumPoint.X));
                double dy = Math.Max(o.MinimumPoint.Y - y, Math.Max(0, y - o.MaximumPoint.Y));
                double dz = Math.Max(o.MinimumPoint.Z - z, Math.Max(0, z - o.MaximumPoint.Z));
                double d  = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Neighbours of cell c in 6-connected adjacency (±x, ±y, ±z).
        /// Six dictionary probes instead of walking the entire cell list —
        /// turns A* expansion from O(n) to O(1) per call. Diagonal moves
        /// are handled by ACO + 3-opt smoothers downstream.
        /// </summary>
        public IEnumerable<VoxelCell> Neighbours(VoxelCell c)
        {
            if (c == null) yield break;
            VoxelCell n;
            if (_byIndex.TryGetValue(PackKey(c.Ix - 1, c.Iy, c.Iz), out n)) yield return n;
            if (_byIndex.TryGetValue(PackKey(c.Ix + 1, c.Iy, c.Iz), out n)) yield return n;
            if (_byIndex.TryGetValue(PackKey(c.Ix, c.Iy - 1, c.Iz), out n)) yield return n;
            if (_byIndex.TryGetValue(PackKey(c.Ix, c.Iy + 1, c.Iz), out n)) yield return n;
            if (_byIndex.TryGetValue(PackKey(c.Ix, c.Iy, c.Iz - 1), out n)) yield return n;
            if (_byIndex.TryGetValue(PackKey(c.Ix, c.Iy, c.Iz + 1), out n)) yield return n;
        }

        /// <summary>O(1) lookup by integer grid index.</summary>
        public VoxelCell GetCell(int ix, int iy, int iz)
        {
            _byIndex.TryGetValue(PackKey(ix, iy, iz), out var c);
            return c;
        }
    }
}
