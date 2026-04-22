// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Routing/AStarSolver.cs — S3.8.
//
// A* global solver over a VoxelGrid (S3.7). Returns a list of
// VoxelCells from start to goal minimising manhattan distance
// weighted by cell CostMultiplier. Uses .NET 8 PriorityQueue.
//
// The output path is the raw grid path — ACO refiner (S3.9), 3-opt
// smoother (S3.10), and Bezier fitting-snap (S3.11) operate on this
// path in turn to produce final routing geometry.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Routing
{
    /// <summary>
    /// Result of a single A* search.
    /// </summary>
    public sealed class AStarResult
    {
        public List<VoxelCell> Path { get; set; } = new List<VoxelCell>();
        public bool Success { get; set; }
        public string FailureReason { get; set; } = string.Empty;
        public int NodesExpanded { get; set; }
        public double TotalCost { get; set; }
    }

    public static class AStarSolver
    {
        /// <summary>
        /// Find shortest weighted path on the voxel grid from the cell
        /// nearest to <paramref name="start"/> to the cell nearest to
        /// <paramref name="goal"/>. Returns the path in grid cells.
        /// </summary>
        public static AStarResult FindPath(VoxelGrid grid, VoxelCell start, VoxelCell goal, int maxExpansions = 200_000)
        {
            var result = new AStarResult();
            if (grid == null || start == null || goal == null)
            {
                result.FailureReason = "Null grid / start / goal";
                return result;
            }
            if (start.IsObstacle || goal.IsObstacle)
            {
                result.FailureReason = "Start or goal cell is inside an obstacle";
                return result;
            }

            var cameFrom = new Dictionary<VoxelCell, VoxelCell>();
            var gScore   = new Dictionary<VoxelCell, double> { [start] = 0.0 };
            var fScore   = new Dictionary<VoxelCell, double> { [start] = Heuristic(start, goal) };
            var open     = new PriorityQueue<VoxelCell, double>();
            open.Enqueue(start, fScore[start]);
            var inOpen   = new HashSet<VoxelCell> { start };

            int expanded = 0;
            while (open.Count > 0 && expanded < maxExpansions)
            {
                var current = open.Dequeue();
                inOpen.Remove(current);
                expanded++;

                if (current == goal)
                {
                    result.Path = Reconstruct(cameFrom, current);
                    result.Success = true;
                    result.NodesExpanded = expanded;
                    result.TotalCost = gScore[current];
                    return result;
                }

                foreach (var n in grid.Neighbours(current))
                {
                    if (n.IsObstacle) continue;
                    double step = StepCost(current, n);
                    double tentative = gScore[current] + step;

                    if (!gScore.TryGetValue(n, out double prev) || tentative < prev)
                    {
                        cameFrom[n] = current;
                        gScore[n]   = tentative;
                        fScore[n]   = tentative + Heuristic(n, goal);
                        if (!inOpen.Contains(n))
                        {
                            open.Enqueue(n, fScore[n]);
                            inOpen.Add(n);
                        }
                    }
                }
            }

            result.FailureReason = expanded >= maxExpansions
                ? $"A* exceeded {maxExpansions} node expansions without reaching goal"
                : "A* open set exhausted — goal unreachable";
            result.NodesExpanded = expanded;
            return result;
        }

        private static double Heuristic(VoxelCell a, VoxelCell b)
        {
            // Manhattan distance in cell indices × average cell size.
            double cells = Math.Abs(a.Ix - b.Ix) + Math.Abs(a.Iy - b.Iy) + Math.Abs(a.Iz - b.Iz);
            double avg = (a.SideFt + b.SideFt) * 0.5;
            return cells * avg;
        }

        private static double StepCost(VoxelCell from, VoxelCell to)
        {
            double d = Heuristic(from, to);
            double mult = double.IsPositiveInfinity(to.CostMultiplier) ? 1e12 : to.CostMultiplier;
            return d * mult;
        }

        private static List<VoxelCell> Reconstruct(Dictionary<VoxelCell, VoxelCell> cameFrom, VoxelCell end)
        {
            var path = new List<VoxelCell> { end };
            var cur = end;
            while (cameFrom.TryGetValue(cur, out var prev))
            {
                path.Add(prev);
                cur = prev;
            }
            path.Reverse();
            return path;
        }
    }
}
