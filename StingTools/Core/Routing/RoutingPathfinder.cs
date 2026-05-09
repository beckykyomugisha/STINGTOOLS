// StingTools v4 MVP — RoutingPathfinder façade.
//
// One-call wrapper around VoxelGrid + AStarSolver (+ future ACO
// refiner / 3-opt smoother / Bezier fitting-snap). Accepts two XYZ
// points in Revit internal units and an obstacle list, builds an
// adaptive voxel grid that contains both endpoints, runs A*, and
// returns the resulting polyline as a List<XYZ>.
//
// This wraps the S3.7-S3.11 dead-code path that landed with the v4
// MVP but was never invoked. Phase A wires it here so that
// GenerateLayoutCommand + downstream tooling can opt into proper 3D
// pathfinding instead of the plumb-vertical drops produced by
// DropEngineBase.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public class RoutingPath
    {
        public List<XYZ> Points { get; } = new List<XYZ>();
        public bool Success { get; set; }
        public string FailureReason { get; set; } = "";
        public int NodesExpanded { get; set; }
        public double TotalCost { get; set; }
        public int CellsBuilt { get; set; }
    }

    public static class RoutingPathfinder
    {
        /// <summary>
        /// Expand an AABB outline in feet. Used to ensure the
        /// pathfinding volume contains both endpoints with some
        /// margin for A* to find a path around obstacles.
        /// </summary>
        private const double OutlinePaddingFt = 2.0;

        /// <summary>
        /// Find a 6-connected Manhattan path from <paramref name="start"/>
        /// to <paramref name="goal"/> through the axis-aligned obstacle
        /// outlines. Coordinates are Revit internal feet.
        /// </summary>
        public static RoutingPath FindPath(
            XYZ start,
            XYZ goal,
            IEnumerable<Outline> obstacles,
            int maxExpansions = 200_000)
        {
            var result = new RoutingPath();
            if (start == null || goal == null)
            {
                result.FailureReason = "start/goal null";
                return result;
            }

            // Bounding volume: the AABB containing both endpoints,
            // padded so the solver has room to manoeuvre.
            var minX = Math.Min(start.X, goal.X) - OutlinePaddingFt;
            var minY = Math.Min(start.Y, goal.Y) - OutlinePaddingFt;
            var minZ = Math.Min(start.Z, goal.Z) - OutlinePaddingFt;
            var maxX = Math.Max(start.X, goal.X) + OutlinePaddingFt;
            var maxY = Math.Max(start.Y, goal.Y) + OutlinePaddingFt;
            var maxZ = Math.Max(start.Z, goal.Z) + OutlinePaddingFt;
            var outline = new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };

            VoxelGrid grid;
            try
            {
                grid = new VoxelGrid(outline, obstacles ?? Enumerable.Empty<Outline>());
                result.CellsBuilt = grid.Build();
            }
            catch (Exception ex)
            {
                result.FailureReason = $"VoxelGrid.Build: {ex.Message}";
                return result;
            }

            if (result.CellsBuilt == 0)
            {
                result.FailureReason = "VoxelGrid built zero cells (degenerate outline)";
                return result;
            }

            var startCell = NearestCell(grid, start);
            var goalCell  = NearestCell(grid, goal);
            if (startCell == null || goalCell == null)
            {
                result.FailureReason = "No non-obstacle cell near endpoint";
                return result;
            }

            var astar = AStarSolver.FindPath(grid, startCell, goalCell, maxExpansions);
            result.Success       = astar.Success;
            result.FailureReason = astar.FailureReason;
            result.NodesExpanded = astar.NodesExpanded;
            result.TotalCost     = astar.TotalCost;

            if (astar.Success && astar.Path != null)
            {
                foreach (var cell in astar.Path)
                {
                    result.Points.Add(CellCentre(cell));
                }
                // Replace first/last grid-centres with the exact start/
                // goal XYZs so the polyline terminates exactly on the
                // fixture / outlet.
                if (result.Points.Count > 0)
                {
                    result.Points[0] = start;
                    result.Points[result.Points.Count - 1] = goal;
                }
            }
            return result;
        }

        private static VoxelCell NearestCell(VoxelGrid grid, XYZ p)
        {
            VoxelCell best = null;
            double bestDist = double.MaxValue;
            foreach (var c in grid.Cells)
            {
                if (c.IsObstacle) continue;
                var cx = 0.5 * (c.MinX + c.MaxX);
                var cy = 0.5 * (c.MinY + c.MaxY);
                var cz = 0.5 * (c.MinZ + c.MaxZ);
                double dx = cx - p.X, dy = cy - p.Y, dz = cz - p.Z;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        private static XYZ CellCentre(VoxelCell c)
        {
            return new XYZ(
                0.5 * (c.MinX + c.MaxX),
                0.5 * (c.MinY + c.MaxY),
                0.5 * (c.MinZ + c.MaxZ));
        }

        /// <summary>
        /// Collect obstacle outlines from the active view for feeding
        /// into FindPath. Pulls bounding boxes of walls, floors, roofs,
        /// ceilings, columns, beams, and structural framing that
        /// intersect the (paddedStart, paddedGoal) AABB.
        /// </summary>
        public static List<Outline> CollectObstaclesInAABB(
            Document doc,
            XYZ start,
            XYZ goal,
            double paddingFt = 4.0)
        {
            var list = new List<Outline>();
            if (doc == null || start == null || goal == null) return list;

            var minX = Math.Min(start.X, goal.X) - paddingFt;
            var minY = Math.Min(start.Y, goal.Y) - paddingFt;
            var minZ = Math.Min(start.Z, goal.Z) - paddingFt;
            var maxX = Math.Max(start.X, goal.X) + paddingFt;
            var maxY = Math.Max(start.Y, goal.Y) + paddingFt;
            var maxZ = Math.Max(start.Z, goal.Z) + paddingFt;
            var probe = new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
            var bboxFilter = new BoundingBoxIntersectsFilter(probe);

            var cats = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralFraming,
            };

            foreach (var cat in cats)
            {
                try
                {
                    var col = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .WherePasses(bboxFilter);
                    foreach (var el in col)
                    {
                        // STING_WALL_ROUTING_FLAG / STING_SLAB_ROUTING_FLAG
                        // — when set to ALLOW, the wall / slab is removed
                        // from the obstacle list so A* can route THROUGH
                        // it (chase, penetration, removable cover). Type-
                        // bound parameter so a single edit to the wall
                        // type drives the policy across every instance.
                        // Stay defensive: read both instance + type just
                        // in case a project has bound it as instance.
                        if (cat == BuiltInCategory.OST_Walls ||
                            cat == BuiltInCategory.OST_Floors)
                        {
                            string flagName = cat == BuiltInCategory.OST_Walls
                                ? "STING_WALL_ROUTING_FLAG"
                                : "STING_SLAB_ROUTING_FLAG";
                            string flag = ReadFlag(el, flagName)
                                ?? ReadFlag(doc.GetElement(el.GetTypeId()), flagName);
                            if (string.Equals(flag, "ALLOW",
                                StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        var bb = el.get_BoundingBox(null);
                        if (bb == null) continue;
                        list.Add(new Outline(bb.Min, bb.Max));
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RoutingPathfinder: collector for {cat} failed: {ex.Message}");
                }
            }
            return list;
        }

        private static string ReadFlag(Element el, string paramName)
        {
            if (el == null) return null;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null) return null;
                if (p.StorageType == StorageType.String) return p.AsString();
                if (p.StorageType == StorageType.Integer) return p.AsInteger() == 1 ? "ALLOW" : "DENY";
            }
            catch { }
            return null;
        }
    }
}
