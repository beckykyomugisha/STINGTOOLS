using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.Core.Routing;

namespace StingTools.Commands.Electrical.Routing
{
    /// <summary>
    /// Pure rectilinear routing engine — no Revit transactions, no model
    /// writes. Computes a Manhattan-style L/Z path from one XYZ to another,
    /// staying at the source's elevation until the final drop. Conduit
    /// diameter selection follows BS 7671 Appendix E + IEC 61386 by sizing
    /// to ≤40 % cross-section fill. The MEP Routing API isn't used (it
    /// isn't enabled on every Revit configuration); production hardening
    /// could swap in NavMesh / ray-casting clash avoidance — Phase 179
    /// honestly delivers the simple rectilinear path.
    /// </summary>
    public class RouteSegment
    {
        public XYZ Start { get; set; }
        public XYZ End   { get; set; }
        public double DiameterMm { get; set; }
        public string Label { get; set; } = "";
        public RouteSegment() { }
        public RouteSegment(XYZ start, XYZ end, double diameterMm, string label)
        { Start = start; End = end; DiameterMm = diameterMm; Label = label ?? ""; }
    }

    public static class ConduitRouteEngine
    {
        private static readonly double[] StandardConduitMm =
            { 16, 20, 25, 32, 40, 50, 63, 75, 100 };

        /// <summary>Shortest leg ComputeRoute will emit (feet, ≈3 mm).</summary>
        public const double MinLegFt = 0.01;

        public static List<RouteSegment> ComputeRoute(XYZ start, XYZ end,
            double diameterMm, string label)
        {
            var segs = new List<RouteSegment>();
            if (start == null || end == null) return segs;
            if (start.DistanceTo(end) <= MinLegFt) return segs;

            // L/Z: horizontal at start elevation → drop to end elevation.
            var mid1 = new XYZ(end.X, start.Y, start.Z);
            var mid2 = new XYZ(end.X, start.Y, end.Z);

            // The route must begin at `start` and finish at `end` — the very
            // points the caller resolved from the load and panel connectors.
            // It used to emit each leg independently and drop any leg under
            // the 0.01 ft tolerance, which had two consequences (#597):
            //   * a collinear route ended on the intermediate waypoint object
            //     instead of the goal, and
            //   * when the LAST leg was sub-tolerance (goal a few mm off the
            //     start's Y), that leg was dropped and the run stopped short
            //     of the panel, leaving a gap nobody was told about.
            // Waypoints under tolerance are now merged into their neighbour
            // and the goal itself always closes the path.
            var pts = new List<XYZ> { start };
            foreach (var p in new[] { mid1, mid2 })
                if (p.DistanceTo(pts[pts.Count - 1]) > MinLegFt) pts.Add(p);
            if (end.DistanceTo(pts[pts.Count - 1]) > MinLegFt)
                pts.Add(end);
            else
                pts[pts.Count - 1] = end;   // snap the sub-tolerance tail onto the goal
            // Snapping can leave the previous waypoint within tolerance of the
            // goal; merge it so no zero-length leg is emitted. Never drop start.
            while (pts.Count > 2 && pts[pts.Count - 2].DistanceTo(end) <= MinLegFt)
                pts.RemoveAt(pts.Count - 2);

            for (int i = 0; i < pts.Count - 1; i++)
                segs.Add(new RouteSegment(pts[i], pts[i + 1], diameterMm, label));
            return segs;
        }

        /// <summary>
        /// Count direction changes along a route. A direction change is
        /// a bend the contractor will have to fabricate as a fitting.
        /// Used by ConduitAutoRouteCommand to pre-flight against the
        /// BS 7671 §522.8.5 max-3-bends-per-draw-in rule before any
        /// Conduit elements are created.
        /// </summary>
        public static int CountBends(IList<RouteSegment> segs)
        {
            if (segs == null || segs.Count < 2) return 0;
            int bends = 0;
            XYZ prevDir = (segs[0].End - segs[0].Start).Normalize();
            for (int i = 1; i < segs.Count; i++)
            {
                XYZ d = (segs[i].End - segs[i].Start);
                double len = d.GetLength();
                if (len < 1e-6) continue;
                XYZ dir = d.Normalize();
                // Treat any deviation > 5° as a bend so colinear sub-
                // segments inserted by smoothers don't double-count.
                if (dir.DotProduct(prevDir) < Math.Cos(5.0 * Math.PI / 180.0))
                    bends++;
                prevDir = dir;
            }
            return bends;
        }

        /// <summary>
        /// Insert draw-in waypoints so no draw-in segment exceeds the
        /// supplied bend cap. Each call produces at most maxBends
        /// direction changes per output sub-route — the boundary cells
        /// become "natural" draw-in box locations the user can visit
        /// in Revit and replace with junction-box families. Empty when
        /// bend count already satisfies the cap.
        /// </summary>
        public static List<List<RouteSegment>> SplitAtBendCap(IList<RouteSegment> segs, int maxBends)
        {
            var groups = new List<List<RouteSegment>>();
            if (segs == null || segs.Count == 0) return groups;
            if (maxBends <= 0)
            {
                groups.Add(new List<RouteSegment>(segs));
                return groups;
            }
            var current = new List<RouteSegment> { segs[0] };
            int bends = 0;
            XYZ prevDir = (segs[0].End - segs[0].Start).Normalize();
            for (int i = 1; i < segs.Count; i++)
            {
                XYZ d = (segs[i].End - segs[i].Start);
                if (d.GetLength() < 1e-6) continue;
                XYZ dir = d.Normalize();
                bool bendHere = dir.DotProduct(prevDir) < Math.Cos(5.0 * Math.PI / 180.0);
                if (bendHere) bends++;
                if (bends > maxBends)
                {
                    groups.Add(current);
                    current = new List<RouteSegment>();
                    bends = 0;
                }
                current.Add(segs[i]);
                prevDir = dir;
            }
            if (current.Count > 0) groups.Add(current);
            return groups;
        }

        public static double SelectConduitDiameterMm(IEnumerable<StingCable> cables)
        {
            if (cables == null) return 20;
            var list = cables.ToList();
            if (list.Count == 0) return 20;
            double totalAreaMm2 = list.Sum(c =>
            {
                double od = c.OuterDiameterMm > 0 ? c.OuterDiameterMm : EstimateCableOdMm(c.CsaMm2);
                return Math.PI * od * od * 0.25 * Math.Max(1, c.CoreCount);
            });
            double requiredAreaMm2 = totalAreaMm2 / 0.40;     // ≤40 % fill
            double requiredDiamMm  = 2.0 * Math.Sqrt(requiredAreaMm2 / Math.PI);
            foreach (var d in StandardConduitMm)
                if (d >= requiredDiamMm) return d;
            return StandardConduitMm[StandardConduitMm.Length - 1];
        }

        /// <summary>
        /// Turn an A* cell path into a conduit route a fitter can build:
        ///   * every leg axis-aligned — the exact start and end are joined to
        ///     the first and last cell centres by X, then Y, then Z moves,
        ///     never by a diagonal;
        ///   * straight runs through consecutive cells merged into one
        ///     segment (A* returns one cell per 200 mm);
        ///   * sub-tolerance legs dropped, the goal always closing the path.
        /// Revit-free apart from XYZ, so it is unit-tested.
        /// </summary>
        public static List<RouteSegment> OrthogonalRoute(XYZ start, IList<XYZ> cellCentres, XYZ end,
            double diameterMm, string label)
        {
            var raw = new List<XYZ> { start };
            if (cellCentres != null) raw.AddRange(cellCentres);
            raw.Add(end);

            // 1. Axis-aligned: split any leg that moves on more than one axis.
            var pts = new List<XYZ> { raw[0] };
            for (int i = 1; i < raw.Count; i++)
            {
                var a = pts[pts.Count - 1];
                var b = raw[i];
                var p1 = new XYZ(b.X, a.Y, a.Z);
                var p2 = new XYZ(b.X, b.Y, a.Z);
                foreach (var p in new[] { p1, p2, b })
                    if (p.DistanceTo(pts[pts.Count - 1]) > MinLegFt) pts.Add(p);
            }
            if (pts[pts.Count - 1].DistanceTo(end) > 1e-9)
            {
                if (pts.Count > 1 && pts[pts.Count - 1].DistanceTo(end) <= MinLegFt) pts[pts.Count - 1] = end;
                else pts.Add(end);
            }

            // 2. Merge colinear runs.
            var merged = new List<XYZ> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var d1 = (pts[i] - merged[merged.Count - 1]).Normalize();
                var d2 = (pts[i + 1] - pts[i]).Normalize();
                if (d1.DotProduct(d2) > 1 - 1e-9) continue;       // same direction: no corner here
                merged.Add(pts[i]);
            }
            if (pts.Count > 1) merged.Add(pts[pts.Count - 1]);

            var segs = new List<RouteSegment>();
            for (int i = 0; i < merged.Count - 1; i++)
                if (merged[i].DistanceTo(merged[i + 1]) > MinLegFt)
                    segs.Add(new RouteSegment(merged[i], merged[i + 1], diameterMm, label));
            return segs;
        }

        /// <summary>
        /// Obstacle-avoiding route: A* over a VoxelGrid built from structural
        /// columns and framing. Falls back to the rectilinear L/Z path (and
        /// says so through <paramref name="method"/>) when the grid is empty
        /// or A* finds no path.
        ///
        /// What changed from the first version (ROADMAP ELEC-7 / MEPG-11):
        /// the path is post-processed by <see cref="OrthogonalRoute"/> (one
        /// segment per straight run, no diagonal end legs, not one conduit per
        /// voxel); the start and goal cells are released when an obstacle's
        /// clearance covers them (the load and the panel sit next to walls and
        /// columns); cell centres are taken on the regular grid stride so
        /// refined and coarse cells line up; floors are not obstacles, because
        /// a floor's bounding box fills its whole storey and blocked every
        /// riser — slab penetrations are detected and stamped separately.
        /// </summary>
        public static List<RouteSegment> ComputeRouteAdvanced(
            Document doc, XYZ start, XYZ end, double diameterMm,
            string label, out string method)
        {
            method = "rectilinear L/Z";
            try
            {
                var obstacleOutlines = new List<Outline>();
                foreach (var cat in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming })
                {
                    try
                    {
                        foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).OfCategory(cat))
                        {
                            var bb = el.get_BoundingBox(null);
                            if (bb != null) obstacleOutlines.Add(new Outline(bb.Min, bb.Max));
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"ComputeRouteAdvanced obstacles ({cat}): {ex.Message}"); }
                }

                double padFt = 2.0 / 0.3048;
                var outline = new BoundingBoxXYZ
                {
                    Min = new XYZ(Math.Min(start.X, end.X) - padFt, Math.Min(start.Y, end.Y) - padFt, Math.Min(start.Z, end.Z) - padFt),
                    Max = new XYZ(Math.Max(start.X, end.X) + padFt, Math.Max(start.Y, end.Y) + padFt, Math.Max(start.Z, end.Z) + padFt)
                };
                var grid = new VoxelGrid(outline, obstacleOutlines);
                if (grid.Build() == 0)
                    return ComputeRoute(start, end, diameterMm, label);

                double stride = VoxelGrid.DefaultSideMm / 304.8;
                XYZ Centre(VoxelCell c) => new XYZ(c.MinX + stride / 2, c.MinY + stride / 2, c.MinZ + stride / 2);
                VoxelCell Nearest(XYZ p) => grid.Cells.OrderBy(c => Centre(c).DistanceTo(p)).FirstOrDefault();
                var startCell = Nearest(start);
                var endCell = Nearest(end);
                if (startCell == null || endCell == null)
                    return ComputeRoute(start, end, diameterMm, label);
                // The endpoints are equipment connectors: the conduit must be
                // allowed to leave them even when a column's clearance covers them.
                startCell.IsObstacle = false;
                endCell.IsObstacle = false;

                var astar = AStarSolver.FindPath(grid, startCell, endCell);
                if (!astar.Success || astar.Path == null || astar.Path.Count < 1)
                {
                    StingLog.Warn($"ComputeRouteAdvanced: A* {astar.FailureReason} — falling back to rectilinear.");
                    method = "rectilinear L/Z (A* found no path)";
                    return ComputeRoute(start, end, diameterMm, label);
                }
                var segs = OrthogonalRoute(start, astar.Path.Select(Centre).ToList(), end, diameterMm, label);
                if (segs.Count == 0) return ComputeRoute(start, end, diameterMm, label);
                method = "A* obstacle-avoiding";
                return segs;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ComputeRouteAdvanced failed, falling back to rectilinear: {ex.Message}");
                method = "rectilinear L/Z (A* failed)";
                return ComputeRoute(start, end, diameterMm, label);
            }
        }

        public static double EstimateCableOdMm(double csaMm2)
        {
            if (csaMm2 <= 1.5)   return 6.5;
            if (csaMm2 <= 2.5)   return 7.5;
            if (csaMm2 <= 4)     return 8.5;
            if (csaMm2 <= 6)     return 9.5;
            if (csaMm2 <= 10)    return 11.5;
            if (csaMm2 <= 16)    return 13.5;
            if (csaMm2 <= 25)    return 16.5;
            if (csaMm2 <= 35)    return 19.0;
            if (csaMm2 <= 50)    return 22.0;
            if (csaMm2 <= 70)    return 26.0;
            if (csaMm2 <= 95)    return 30.0;
            if (csaMm2 <= 120)   return 34.0;
            if (csaMm2 <= 150)   return 38.0;
            if (csaMm2 <= 185)   return 42.0;
            return 50.0;
        }
    }
}
