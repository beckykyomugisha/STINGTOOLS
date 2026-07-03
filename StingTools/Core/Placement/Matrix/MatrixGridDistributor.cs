// StingTools — Matrix grid distributor (M2: exact-count, even distribution).
//
// The placement engines already have grid math, but none of them emit EXACTLY N
// evenly-distributed points (LightingGridCalculator derives N from the lumen method
// and overshoots; CoverageGridGenerator is spacing/coverage-driven). Matrix Place
// declares N up front, so this helper produces exactly N (or the most that fit at
// MinSpacing, reporting the shortfall) — reusing FixturePlacementEngine.PointInSpatial
// for polygon clipping and Revit's SpatialElement.GetBoundarySegments for wall runs.
//
//   EvenGrid  : ceiling / room-centre categories -> rows x cols even grid, exactly N.
//   WallRun   : wall devices -> N points spaced along the room's wall-backed boundary,
//               nudged inward so PlacementHostPreflight's nearest-wall search hosts them.
//
// Both return a DistributionResult carrying the points + requested/placed/shortfall so
// the caller reports "placed 3 of 4 (min spacing)" honestly rather than silently dropping.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement.Matrix
{
    public sealed class DistributionResult
    {
        public List<XYZ> Points = new List<XYZ>();
        public int Requested;
        public int Fit;                 // how many the room can hold at MinSpacing
        public int ShortfallBySpacing => Math.Max(0, Requested - Fit);
        public int ShortfallByClip;     // points dropped because they fell outside the polygon
        public string Note = "";
    }

    public static class MatrixGridDistributor
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>Exactly-N even grid over the room, clipped to its polygon. Ceiling / room-centre
        /// categories. anchorZ is the Z assigned to every point (ceiling soffit or FFL+height).</summary>
        public static DistributionResult EvenGrid(
            SpatialElement room, int n, double minSpacingMm, double wallClearanceMm, double anchorZ)
        {
            var res = new DistributionResult { Requested = Math.Max(0, n) };
            if (room == null || n <= 0) { res.Fit = 0; return res; }

            var bb = SafeBBox(room);
            if (bb == null) { res.Note = "no bounding box"; return res; }

            double clearFt = Math.Max(0, wallClearanceMm) * MmToFt;
            double minX = bb.Min.X + clearFt, minY = bb.Min.Y + clearFt;
            double dx = (bb.Max.X - bb.Min.X) - 2 * clearFt;
            double dy = (bb.Max.Y - bb.Min.Y) - 2 * clearFt;
            if (dx <= 0 || dy <= 0) { minX = bb.Min.X; minY = bb.Min.Y; dx = bb.Max.X - bb.Min.X; dy = bb.Max.Y - bb.Min.Y; }
            if (dx <= 0 || dy <= 0) { res.Note = "degenerate bounding box"; return res; }

            // Cap N by how many fit at MinSpacing (M2 shortfall path).
            double minSpFt = Math.Max(0, minSpacingMm) * MmToFt;
            int maxCols = minSpFt > 1e-6 ? Math.Max(1, (int)Math.Floor(dx / minSpFt)) : int.MaxValue / 4;
            int maxRows = minSpFt > 1e-6 ? Math.Max(1, (int)Math.Floor(dy / minSpFt)) : int.MaxValue / 4;
            long maxFit = (long)maxCols * maxRows;
            int effN = (int)Math.Min(n, maxFit);
            res.Fit = effN;
            if (effN <= 0) { res.Note = "room too small for one at min spacing"; return res; }

            // Rows chosen from the room aspect so cells are ~square; each row gets base or base+1
            // columns so the total is EXACTLY effN (classic n-luminaire layout, e.g. 5 -> 2+3).
            int rows = Math.Max(1, (int)Math.Round(Math.Sqrt((double)effN * dy / dx)));
            rows = Math.Min(rows, effN);
            rows = Math.Min(rows, maxRows);
            if (rows < 1) rows = 1;
            int baseCols = effN / rows;
            int extra = effN % rows;

            for (int j = 0; j < rows; j++)
            {
                int colsThisRow = baseCols + (j < extra ? 1 : 0);
                if (colsThisRow < 1) continue;
                colsThisRow = Math.Min(colsThisRow, maxCols);
                double y = minY + (j + 0.5) * dy / rows;
                for (int i = 0; i < colsThisRow; i++)
                {
                    double x = minX + (i + 0.5) * dx / colsThisRow;
                    var p = new XYZ(x, y, anchorZ);
                    if (InRoom(room, x, y, bb)) res.Points.Add(p);
                }
            }

            res.ShortfallByClip = Math.Max(0, effN - res.Points.Count);
            if (res.ShortfallBySpacing > 0)
                res.Note = $"placed {res.Points.Count} of {n} (min spacing {minSpacingMm:F0}mm)";
            else if (res.ShortfallByClip > 0)
                res.Note = $"placed {res.Points.Count} of {n} ({res.ShortfallByClip} fell outside the room outline)";
            return res;
        }

        /// <summary>N points spaced along the room's wall-backed boundary, nudged inward toward the
        /// room centroid so the nearest-wall host search picks up the wall. Wall devices (sockets,
        /// switches, data). anchorZ = FFL + mounting height.</summary>
        public static DistributionResult WallRun(
            SpatialElement room, int n, double minSpacingMm, double insetMm, double anchorZ)
        {
            var res = new DistributionResult { Requested = Math.Max(0, n) };
            if (room == null || n <= 0) { res.Fit = 0; return res; }

            var segs = WallBackedSegments(room);
            if (segs.Count == 0)
            {
                // No wall-backed boundary (e.g. MEP space): fall back to an even grid so the
                // count is still honoured (placed level-based / best-effort hosted).
                var bb = SafeBBox(room);
                double z = anchorZ;
                var g = EvenGrid(room, n, minSpacingMm, insetMm, z);
                g.Note = string.IsNullOrEmpty(g.Note) ? "no wall boundary — distributed as grid" : g.Note + " (no wall boundary)";
                return g;
            }

            double totalLen = segs.Sum(s => s.Length);
            double insetFt = Math.Max(0, insetMm) * MmToFt;
            double minSpFt = Math.Max(0, minSpacingMm) * MmToFt;
            int maxFit = minSpFt > 1e-6 ? Math.Max(1, (int)Math.Floor(totalLen / minSpFt)) : n;
            int effN = Math.Min(n, maxFit);
            res.Fit = effN;
            if (effN <= 0) { res.Note = "wall run too short for one at min spacing"; return res; }

            XYZ centroid = Centroid(room) ?? Mid(segs);
            // Even arc-length positions: place at (k+0.5)/effN of the total wall length.
            for (int k = 0; k < effN; k++)
            {
                double target = (k + 0.5) / effN * totalLen;
                var pt = PointAtArcLength(segs, target);
                if (pt == null) continue;
                var inward = InwardNudge(pt.Value.p, pt.Value.dir, centroid, insetFt);
                res.Points.Add(new XYZ(inward.X, inward.Y, anchorZ));
            }
            res.ShortfallByClip = 0;
            if (res.ShortfallBySpacing > 0)
                res.Note = $"placed {res.Points.Count} of {n} (min spacing {minSpacingMm:F0}mm on {totalLen / MmToFt / 1000.0:F1}m of wall)";
            return res;
        }

        // ── geometry helpers ────────────────────────────────────────────────

        private struct Seg { public XYZ A, B; public double Length; public XYZ Dir; }

        private static BoundingBoxXYZ SafeBBox(SpatialElement room)
        { try { return room.get_BoundingBox(null); } catch { return null; } }

        private static bool InRoom(SpatialElement room, double x, double y, BoundingBoxXYZ bb)
        {
            try
            {
                double z = room.Level?.Elevation ?? bb.Min.Z;
                return FixturePlacementEngine.PointInSpatial(room, new XYZ(x, y, z));
            }
            catch { return true; } // unbounded/odd rooms: accept rather than drop
        }

        private static List<Seg> WallBackedSegments(SpatialElement room)
        {
            var list = new List<Seg>();
            try
            {
                var doc = room.Document;
                var opts = new SpatialElementBoundaryOptions();
                var loops = room.GetBoundarySegments(opts);
                if (loops == null) return list;
                foreach (var loop in loops)
                {
                    foreach (var bs in loop)
                    {
                        bool isWall = false;
                        try
                        {
                            var el = doc.GetElement(bs.ElementId);
                            isWall = el is Wall;
                        }
                        catch { }
                        if (!isWall) continue;
                        Curve c; try { c = bs.GetCurve(); } catch { continue; }
                        if (c == null) continue;
                        var a = c.GetEndPoint(0); var b = c.GetEndPoint(1);
                        double len = a.DistanceTo(b);
                        if (len < 1e-6) continue;
                        list.Add(new Seg { A = a, B = b, Length = len, Dir = (b - a).Normalize() });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixGridDistributor.WallBackedSegments: {ex.Message}"); }
            return list;
        }

        private static (XYZ p, XYZ dir)? PointAtArcLength(List<Seg> segs, double target)
        {
            double acc = 0;
            foreach (var s in segs)
            {
                if (target <= acc + s.Length)
                {
                    double t = (target - acc) / s.Length;
                    var p = s.A + (s.B - s.A) * t;
                    return (p, s.Dir);
                }
                acc += s.Length;
            }
            var last = segs[segs.Count - 1];
            return (last.B, last.Dir);
        }

        private static XYZ InwardNudge(XYZ p, XYZ segDir, XYZ centroid, double insetFt)
        {
            if (insetFt <= 0 || centroid == null) return p;
            var toCentre = centroid - p;
            // Remove the along-wall component so we move perpendicular into the room.
            var along = segDir * toCentre.DotProduct(segDir);
            var perp = toCentre - along;
            double m = perp.GetLength();
            if (m < 1e-9) return p;
            return p + perp * (insetFt / m);
        }

        private static XYZ Centroid(SpatialElement room)
        {
            try { return (room.Location as LocationPoint)?.Point; } catch { return null; }
        }

        private static XYZ Mid(List<Seg> segs)
        {
            if (segs == null || segs.Count == 0) return XYZ.Zero;
            double x = segs.Average(s => (s.A.X + s.B.X) / 2);
            double y = segs.Average(s => (s.A.Y + s.B.Y) / 2);
            double z = segs.Average(s => (s.A.Z + s.B.Z) / 2);
            return new XYZ(x, y, z);
        }
    }
}
