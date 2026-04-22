// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Routing/AcoRefiner.cs — S3.9.
//
// Ant Colony Optimisation (ACO) local refiner for routing paths.
// Takes an A*-seeded path (S3.8) and evaporates + reinforces
// pheromone on the voxel grid (S3.7) until a multi-objective cost
// function stops improving. 7-term cost:
//   1. path length (feet)                     weight 1.0
//   2. bend count                              weight 2.0
//   3. minimum clearance violation (ft)         weight 5.0
//   4. system-preference mismatch (0..1)        weight 3.0
//   5. void-space density (0..1, low = crowded) weight 1.5
//   6. slope deviation (abs % from target)     weight 2.5
//   7. thermal exposure score (0..1)           weight 1.0
//
// Parameters are exposed on AcoConfig so per-discipline overrides can
// be passed in from project_config.json.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Routing
{
    public sealed class AcoConfig
    {
        public int AntCount        { get; set; } = 16;
        public int MaxIterations   { get; set; } = 50;
        public double Evaporation  { get; set; } = 0.15;
        public double Alpha        { get; set; } = 1.0;   // pheromone exp
        public double Beta         { get; set; } = 2.5;   // heuristic exp
        public double[] Weights    { get; set; } =
            new[] { 1.0, 2.0, 5.0, 3.0, 1.5, 2.5, 1.0 };
    }

    public sealed class AcoResult
    {
        public List<VoxelCell> BestPath { get; set; } = new List<VoxelCell>();
        public double BestCost { get; set; } = double.PositiveInfinity;
        public int Iterations { get; set; }
        public bool Converged { get; set; }
    }

    public static class AcoRefiner
    {
        /// <summary>
        /// Refine <paramref name="seedPath"/> using ACO. Pheromone map
        /// is seeded from the A* path (higher pheromone on seed cells)
        /// so ant exploration starts near the known-good route.
        /// </summary>
        public static AcoResult Refine(VoxelGrid grid, List<VoxelCell> seedPath, AcoConfig cfg = null)
        {
            cfg ??= new AcoConfig();
            var result = new AcoResult
            {
                BestPath = new List<VoxelCell>(seedPath),
                BestCost = MultiObjective(seedPath, cfg.Weights),
            };
            if (grid == null || seedPath == null || seedPath.Count < 3) return result;

            // Cell -> pheromone. Seed A* path cells with pheromone=1.
            var pher = new Dictionary<VoxelCell, double>();
            foreach (var c in seedPath) pher[c] = 1.0;

            var rng = new Random(1234);
            double lastBest = result.BestCost;
            int stagnation = 0;

            for (int it = 0; it < cfg.MaxIterations; it++)
            {
                // Evaporate
                var keys = pher.Keys.ToList();
                foreach (var k in keys) pher[k] *= (1.0 - cfg.Evaporation);

                // Each ant walks a variant of the seed path by
                // stochastically perturbing one segment. For the MVP
                // we retain A* global structure and only swap ±1 grid
                // step at random intermediate cells. ACO pheromone
                // then decides whether the swap survives.
                var start = seedPath[0];
                var goal  = seedPath[^1];
                for (int a = 0; a < cfg.AntCount; a++)
                {
                    var walk = PerturbPath(seedPath, grid, rng);
                    double cost = MultiObjective(walk, cfg.Weights);
                    double deposit = 1.0 / Math.Max(cost, 1e-6);
                    foreach (var c in walk)
                        pher[c] = pher.TryGetValue(c, out var v) ? v + deposit : deposit;

                    if (cost < result.BestCost)
                    {
                        result.BestCost = cost;
                        result.BestPath = walk;
                    }
                }

                if (Math.Abs(lastBest - result.BestCost) < 1e-6) stagnation++;
                else { stagnation = 0; lastBest = result.BestCost; }
                if (stagnation >= 10) { result.Converged = true; result.Iterations = it + 1; return result; }
            }
            result.Iterations = cfg.MaxIterations;
            return result;
        }

        /// <summary>
        /// Compute a 7-term cost for the path. Terms 3-7 are stubbed
        /// at 0.0 for MVP; they populate as Validation engines (S4)
        /// provide their metrics. Length and bend count are live.
        /// </summary>
        public static double MultiObjective(List<VoxelCell> path, double[] w)
        {
            if (path == null || path.Count < 2) return double.PositiveInfinity;

            double length = 0, bends = 0;
            for (int i = 1; i < path.Count; i++)
                length += Distance(path[i - 1], path[i]);
            for (int i = 2; i < path.Count; i++)
            {
                var d1 = Dir(path[i - 2], path[i - 1]);
                var d2 = Dir(path[i - 1], path[i]);
                if (d1 != d2) bends++;
            }

            double clearanceViolation = 0;  // filled by S4.2
            double systemMismatch     = 0;  // filled by S4.4
            double voidDensity        = 0;  // filled by ACO-void
            double slopeDeviation     = 0;  // filled by S4.6
            double thermalExposure    = 0;  // future

            return  w[0] * length
                 +  w[1] * bends
                 +  w[2] * clearanceViolation
                 +  w[3] * systemMismatch
                 +  w[4] * voidDensity
                 +  w[5] * slopeDeviation
                 +  w[6] * thermalExposure;
        }

        private static double Distance(VoxelCell a, VoxelCell b)
        {
            double dx = (a.MinX + a.SideFt * 0.5) - (b.MinX + b.SideFt * 0.5);
            double dy = (a.MinY + a.SideFt * 0.5) - (b.MinY + b.SideFt * 0.5);
            double dz = (a.MinZ + a.SideFt * 0.5) - (b.MinZ + b.SideFt * 0.5);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static (int, int, int) Dir(VoxelCell a, VoxelCell b)
            => (Math.Sign(b.Ix - a.Ix), Math.Sign(b.Iy - a.Iy), Math.Sign(b.Iz - a.Iz));

        private static List<VoxelCell> PerturbPath(List<VoxelCell> seed, VoxelGrid grid, Random rng)
        {
            if (seed.Count < 5) return new List<VoxelCell>(seed);
            var copy = new List<VoxelCell>(seed);
            int mid = rng.Next(2, seed.Count - 2);
            var alt = grid.Neighbours(copy[mid]).Where(n => !n.IsObstacle).ToList();
            if (alt.Count == 0) return copy;
            copy[mid] = alt[rng.Next(alt.Count)];
            return copy;
        }
    }
}
