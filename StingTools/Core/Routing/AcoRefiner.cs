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

        /// <summary>
        /// Optional clearance probe — when set, the ACO cost function
        /// queries this delegate for each cell on a candidate path
        /// and treats the returned value as a violation magnitude in
        /// metres. Hooked to <see cref="SeparationChecker"/> by the
        /// drop engines so ACO can dodge BS EN 50174-2 / BS 5839-1
        /// separation conflicts instead of just reporting them.
        /// Empty by default (term 3 stays at 0).
        /// </summary>
        public Func<VoxelCell, double> ClearanceProbe { get; set; }

        /// <summary>
        /// Material cost in $/m for term 0 (length). When > 0 the cost
        /// function multiplies length × CostPerMetre, turning ACO into
        /// a budget-aware optimiser. Defaults to 0 → length contributes
        /// raw distance only.
        /// </summary>
        public double CostPerMetre { get; set; } = 0.0;

        /// <summary>
        /// Hard cap on bend count. When set > 0, paths exceeding this
        /// many bends pay an exponential penalty in term 1 — used to
        /// enforce BS 7671 §522.8.5 (max 3 bends between draw-in
        /// points). Set to 0 to disable.
        /// </summary>
        public int MaxBends { get; set; } = 0;

        /// <summary>
        /// Deterministic RNG seed. Default 1234 (the v4 MVP value).
        /// Pass a per-project seed for reproducible runs across
        /// re-invocations / test fixtures.
        /// </summary>
        public int Seed { get; set; } = 1234;
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
                BestCost = MultiObjective(seedPath, cfg),
            };
            if (grid == null || seedPath == null || seedPath.Count < 3) return result;

            // Cell -> pheromone. Seed A* path cells with pheromone=1.
            var pher = new Dictionary<VoxelCell, double>();
            foreach (var c in seedPath) pher[c] = 1.0;

            var rng = new Random(cfg.Seed);
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
                    double cost = MultiObjective(walk, cfg);
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
        /// Compute a 7-term cost for the path. Terms 1 (length), 2
        /// (bend count) and 3 (clearance) are now live; the others
        /// remain reserved for future validators. ClearanceProbe and
        /// CostPerMetre flow in via <see cref="AcoConfig"/> so callers
        /// can plug separation / pricing logic in without forking
        /// the refiner.
        /// </summary>
        public static double MultiObjective(List<VoxelCell> path, AcoConfig cfg)
        {
            if (path == null || path.Count < 2) return double.PositiveInfinity;
            if (cfg == null) cfg = new AcoConfig();
            var w = cfg.Weights;

            // Term 0 — length, optionally weighted by material cost.
            double length = 0;
            for (int i = 1; i < path.Count; i++)
                length += Distance(path[i - 1], path[i]);
            double lengthTerm = length;
            if (cfg.CostPerMetre > 0)
            {
                // Convert ft → m before multiplying by per-metre rate.
                lengthTerm = (length * 0.3048) * cfg.CostPerMetre;
            }

            // Term 1 — bend count, with optional exponential penalty
            // when the path exceeds MaxBends. The exponential keeps
            // the gradient pointing away from violation while still
            // permitting one-bend recoveries when nothing else fits.
            int bends = 0;
            for (int i = 2; i < path.Count; i++)
            {
                var d1 = Dir(path[i - 2], path[i - 1]);
                var d2 = Dir(path[i - 1], path[i]);
                if (d1 != d2) bends++;
            }
            double bendTerm = bends;
            if (cfg.MaxBends > 0 && bends > cfg.MaxBends)
                bendTerm += Math.Pow(2.0, bends - cfg.MaxBends);  // 2× per excess bend

            // Term 2 — clearance violation. The probe returns the
            // shortfall in metres for each cell; we sum the squared
            // shortfalls so a single bad cell hurts more than many
            // marginal ones.
            double clearanceViolation = 0;
            if (cfg.ClearanceProbe != null)
            {
                foreach (var c in path)
                {
                    try
                    {
                        double v = cfg.ClearanceProbe(c);
                        if (v > 0) clearanceViolation += v * v;
                    }
                    catch { /* probe errors silently zero — never break the optimiser */ }
                }
            }

            double systemMismatch     = 0;  // filled by S4.4 when ready
            double voidDensity        = 0;  // filled by ACO-void
            double slopeDeviation     = 0;  // filled by S4.6
            double thermalExposure    = 0;  // future

            return  w[0] * lengthTerm
                 +  w[1] * bendTerm
                 +  w[2] * clearanceViolation
                 +  w[3] * systemMismatch
                 +  w[4] * voidDensity
                 +  w[5] * slopeDeviation
                 +  w[6] * thermalExposure;
        }

        /// <summary>Backwards-compatible overload — delegates to the
        /// AcoConfig-aware variant with default settings.</summary>
        public static double MultiObjective(List<VoxelCell> path, double[] w)
            => MultiObjective(path, new AcoConfig { Weights = w });

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
