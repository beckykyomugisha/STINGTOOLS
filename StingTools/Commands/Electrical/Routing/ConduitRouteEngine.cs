using System;
using System.Collections.Generic;
using System.Linq;
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

        public static List<RouteSegment> ComputeRoute(XYZ start, XYZ end,
            double diameterMm, string label)
        {
            var segs = new List<RouteSegment>();
            if (start == null || end == null) return segs;
            // L/Z: horizontal at start elevation → drop to end elevation.
            var mid1 = new XYZ(end.X, start.Y, start.Z);
            var mid2 = new XYZ(end.X, start.Y, end.Z);
            if (mid1.DistanceTo(start) > 0.01) segs.Add(new RouteSegment(start, mid1, diameterMm, label));
            if (mid2.DistanceTo(mid1)  > 0.01) segs.Add(new RouteSegment(mid1,  mid2, diameterMm, label));
            if (end.DistanceTo(mid2)   > 0.01) segs.Add(new RouteSegment(mid2,  end,  diameterMm, label));
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
        /// Advanced routing overload that uses A* on a VoxelGrid obstacle map to
        /// find an obstacle-avoiding path. Falls back to the standard rectilinear
        /// L/Z path when A* returns no solution or VoxelGrid construction fails.
        ///
        /// Obstacles collected: structural framing, structural columns, floors.
        /// Voxel size: 200 mm (VoxelGrid.DefaultSideMm).
        /// </summary>
        public static List<RouteSegment> ComputeRouteAdvanced(
            Document doc, XYZ start, XYZ end, double diameterMm,
            string label = "", double maxFillPct = 0.40)
        {
            try
            {
                // ── Build obstacle outlines from structural elements ──────
                var obstacleOutlines = new List<Outline>();
                try
                {
                    var structCategories = new[]
                    {
                        typeof(Floor),
                        typeof(FamilyInstance)   // structural columns + framing via filter below
                    };
                    // Floors
                    var floors = new FilteredElementCollector(doc)
                        .OfClass(typeof(Floor)).Cast<Floor>();
                    foreach (var f in floors)
                    {
                        try
                        {
                            var bb = f.get_BoundingBox(null);
                            if (bb != null)
                                obstacleOutlines.Add(new Outline(bb.Min, bb.Max));
                        }
                        catch { }
                    }
                    // Structural columns
                    var cols = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .OfCategory(BuiltInCategory.OST_StructuralColumns)
                        .Cast<FamilyInstance>();
                    foreach (var c in cols)
                    {
                        try
                        {
                            var bb = c.get_BoundingBox(null);
                            if (bb != null)
                                obstacleOutlines.Add(new Outline(bb.Min, bb.Max));
                        }
                        catch { }
                    }
                    // Structural framing
                    var framing = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .Cast<FamilyInstance>();
                    foreach (var f in framing)
                    {
                        try
                        {
                            var bb = f.get_BoundingBox(null);
                            if (bb != null)
                                obstacleOutlines.Add(new Outline(bb.Min, bb.Max));
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ComputeRouteAdvanced obstacle collect: {ex.Message}"); }

                // ── Build VoxelGrid ──────────────────────────────────────
                // Envelope is a generous box around start + end with 2 m padding.
                double padFt = 2.0 / 0.3048;
                var minPt = new XYZ(
                    Math.Min(start.X, end.X) - padFt,
                    Math.Min(start.Y, end.Y) - padFt,
                    Math.Min(start.Z, end.Z) - padFt);
                var maxPt = new XYZ(
                    Math.Max(start.X, end.X) + padFt,
                    Math.Max(start.Y, end.Y) + padFt,
                    Math.Max(start.Z, end.Z) + padFt);

                var outline = new BoundingBoxXYZ { Min = minPt, Max = maxPt };
                var grid = new VoxelGrid(outline, obstacleOutlines);
                int cellCount = grid.Build();
                if (cellCount == 0)
                {
                    StingLog.Warn("ComputeRouteAdvanced: VoxelGrid built 0 cells — falling back to rectilinear.");
                    return ComputeRoute(start, end, diameterMm, label);
                }

                // ── Locate start / end cells ─────────────────────────────
                // Walk cells to find the one whose centre is nearest start/end.
                VoxelCell startCell = null, endCell = null;
                double bestStart = double.MaxValue, bestEnd = double.MaxValue;
                foreach (var cell in grid.Cells)
                {
                    double cx = (cell.MinX + cell.MaxX) * 0.5;
                    double cy = (cell.MinY + cell.MaxY) * 0.5;
                    double cz = (cell.MinZ + cell.MaxZ) * 0.5;
                    double ds = (cx - start.X) * (cx - start.X) +
                                (cy - start.Y) * (cy - start.Y) +
                                (cz - start.Z) * (cz - start.Z);
                    double de = (cx - end.X) * (cx - end.X) +
                                (cy - end.Y) * (cy - end.Y) +
                                (cz - end.Z) * (cz - end.Z);
                    if (ds < bestStart) { bestStart = ds; startCell = cell; }
                    if (de < bestEnd)   { bestEnd = de;   endCell   = cell; }
                }

                if (startCell == null || endCell == null)
                {
                    StingLog.Warn("ComputeRouteAdvanced: could not map start/end to voxel cells — falling back.");
                    return ComputeRoute(start, end, diameterMm, label);
                }

                // ── Run A* ───────────────────────────────────────────────
                var astarResult = AStarSolver.FindPath(grid, startCell, endCell);
                if (!astarResult.Success || astarResult.Path == null || astarResult.Path.Count < 2)
                {
                    StingLog.Warn($"ComputeRouteAdvanced: A* {astarResult.FailureReason} — falling back to rectilinear.");
                    return ComputeRoute(start, end, diameterMm, label);
                }

                // ── Convert VoxelCell path → RouteSegments ───────────────
                var waypoints = new List<XYZ>(astarResult.Path.Count + 2);
                waypoints.Add(start);   // exact start
                foreach (var cell in astarResult.Path)
                {
                    waypoints.Add(new XYZ(
                        (cell.MinX + cell.MaxX) * 0.5,
                        (cell.MinY + cell.MaxY) * 0.5,
                        (cell.MinZ + cell.MaxZ) * 0.5));
                }
                waypoints.Add(end);     // exact end

                var segs = new List<RouteSegment>();
                for (int i = 0; i < waypoints.Count - 1; i++)
                {
                    var a = waypoints[i];
                    var b = waypoints[i + 1];
                    if (a.DistanceTo(b) > 0.01)
                        segs.Add(new RouteSegment(a, b, diameterMm, label));
                }
                if (segs.Count == 0)
                    return ComputeRoute(start, end, diameterMm, label);

                StingLog.Info($"ComputeRouteAdvanced: A* path {astarResult.Path.Count} cells → {segs.Count} segments, cost={astarResult.TotalCost:F2}.");
                return segs;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ComputeRouteAdvanced failed, falling back to rectilinear: {ex.Message}");
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
