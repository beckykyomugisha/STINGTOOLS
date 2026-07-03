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
        public double AngleDeg;         // dominant-axis rotation to apply to the fixtures (0 = none)
        public string Note = "";
    }

    public static class MatrixGridDistributor
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>Exactly-N even grid ORIENTED to the room's dominant axis (its longest wall), so
        /// a rotated rectangular room gets a grid parallel to its walls and the fixtures are rotated
        /// to match (DistributionResult.AngleDeg). Ceiling / room-centre categories. anchorZ is the Z
        /// assigned to every point (ceiling soffit or FFL+height). For a convex room the aligned
        /// rows x cols grid is used verbatim; for an L-shaped room (where that grid would clip and
        /// leave a gap) it densifies and farthest-point-selects exactly N inside the actual outline.</summary>
        public static DistributionResult EvenGrid(
            SpatialElement room, int n, double minSpacingMm, double wallClearanceMm, double anchorZ)
        {
            var res = new DistributionResult { Requested = Math.Max(0, n) };
            if (room == null || n <= 0) { res.Fit = 0; return res; }

            var bb = SafeBBox(room);
            if (bb == null) { res.Note = "no bounding box"; return res; }

            var frame = ComputeFrame(room, bb);
            res.AngleDeg = frame.AngleRad * 180.0 / Math.PI;

            double clearFt = Math.Max(0, wallClearanceMm) * MmToFt;
            double da = (frame.AMax - frame.AMin);   // oriented extent along the room's long axis
            double db = (frame.BMax - frame.BMin);   // oriented extent across it
            if (da <= 0 || db <= 0) { res.Note = "degenerate room extent"; return res; }

            // Cap N by how many fit at MinSpacing in the ORIENTED frame (M2 shortfall path).
            double minSpFt = Math.Max(0, minSpacingMm) * MmToFt;
            int maxCols = minSpFt > 1e-6 ? Math.Max(1, (int)Math.Floor(da / minSpFt)) : int.MaxValue / 4;
            int maxRows = minSpFt > 1e-6 ? Math.Max(1, (int)Math.Floor(db / minSpFt)) : int.MaxValue / 4;
            long maxFit = (long)maxCols * maxRows;
            int effN = (int)Math.Min(n, maxFit);
            res.Fit = effN;
            if (effN <= 0) { res.Note = "room too small for one at min spacing"; return res; }

            // 1) Clean aligned rows x cols grid (exactly effN), transformed to world + clipped.
            var cleanWorld = LayOrientedGrid(frame, effN, clearFt, anchorZ, maxCols, maxRows);
            var cleanIn = cleanWorld.Where(p => InRoom(room, p.X, p.Y, bb)).ToList();
            if (cleanIn.Count == effN)
            {
                res.Points = cleanIn;   // convex / rectangular — the aligned grid fits entirely
            }
            else
            {
                // 2) L-shaped / concave — the aligned grid clipped. Densify (a finer aligned grid),
                //    keep only in-room candidates, then farthest-point-select exactly effN so the
                //    ACTUAL area fills evenly instead of leaving the clipped gap.
                int denseTarget = (int)Math.Min(maxFit, (long)Math.Max(effN + 1, effN * 3));
                var dense = LayOrientedGrid(frame, denseTarget, clearFt, anchorZ, maxCols, maxRows)
                    .Where(p => InRoom(room, p.X, p.Y, bb)).ToList();
                res.Points = dense.Count <= effN ? dense : FarthestPointSelect(dense, effN, bb);
            }

            res.ShortfallByClip = Math.Max(0, effN - res.Points.Count);
            if (res.ShortfallBySpacing > 0)
                res.Note = $"placed {res.Points.Count} of {n} (min spacing {minSpacingMm:F0}mm)";
            else if (res.ShortfallByClip > 0)
                res.Note = $"placed {res.Points.Count} of {n} ({res.ShortfallByClip} could not fit inside the room outline)";
            return res;
        }

        // ── oriented-frame grid ─────────────────────────────────────────────

        // The room's local frame: origin at centroid C, unit axes U (dominant/long) and V (perp),
        // and the oriented-bounding-box extents [AMin,AMax] x [BMin,BMax] along U/V.
        private struct RoomFrame
        {
            public XYZ C, U, V;
            public double AMin, AMax, BMin, BMax, AngleRad;
        }

        // Derive the room's orientation: dominant axis = longest wall-backed boundary segment (else
        // longest boundary segment, else world X). U is forced to be the LONGER oriented extent so
        // linear luminaires align with the room's long axis.
        private static RoomFrame ComputeFrame(SpatialElement room, BoundingBoxXYZ bb)
        {
            XYZ dir = null;
            var walls = WallBackedSegments(room);
            if (walls.Count > 0) dir = walls.OrderByDescending(s => s.Length).First().Dir;
            else
            {
                var segs = AllBoundarySegments(room);
                if (segs.Count > 0) dir = segs.OrderByDescending(s => s.Length).First().Dir;
            }
            if (dir == null) dir = XYZ.BasisX;
            dir = new XYZ(dir.X, dir.Y, 0);
            if (dir.GetLength() < 1e-9) dir = XYZ.BasisX;
            dir = dir.Normalize();

            XYZ c = Centroid(room) ?? new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, bb.Min.Z);
            var pts = AllBoundaryPoints(room);
            if (pts.Count == 0)
                pts = new List<XYZ> { bb.Min, new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z), bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z) };

            XYZ u = dir, v = new XYZ(-dir.Y, dir.X, 0);
            Extents(pts, c, u, v, out double aMin, out double aMax, out double bMin, out double bMax);
            // Force U to be the longer axis (rotate the frame 90 deg if the perpendicular is longer).
            if ((bMax - bMin) > (aMax - aMin))
            {
                u = v; v = new XYZ(-u.Y, u.X, 0);
                Extents(pts, c, u, v, out aMin, out aMax, out bMin, out bMax);
            }
            return new RoomFrame
            {
                C = c, U = u, V = v, AMin = aMin, AMax = aMax, BMin = bMin, BMax = bMax,
                AngleRad = Math.Atan2(u.Y, u.X)
            };
        }

        private static void Extents(List<XYZ> pts, XYZ c, XYZ u, XYZ v,
            out double aMin, out double aMax, out double bMin, out double bMax)
        {
            aMin = double.MaxValue; aMax = double.MinValue; bMin = double.MaxValue; bMax = double.MinValue;
            foreach (var p in pts)
            {
                var d = p - c;
                double a = d.DotProduct(u), b = d.DotProduct(v);
                if (a < aMin) aMin = a; if (a > aMax) aMax = a;
                if (b < bMin) bMin = b; if (b > bMax) bMax = b;
            }
        }

        // Lay an exactly-count rows x cols grid in the oriented frame; return world points at anchorZ.
        private static List<XYZ> LayOrientedGrid(
            RoomFrame f, int count, double clearFt, double anchorZ, int maxCols, int maxRows)
        {
            var outPts = new List<XYZ>();
            if (count <= 0) return outPts;
            double da = (f.AMax - f.AMin) - 2 * clearFt;
            double db = (f.BMax - f.BMin) - 2 * clearFt;
            double a0 = f.AMin + clearFt, b0 = f.BMin + clearFt;
            if (da <= 0 || db <= 0) { da = f.AMax - f.AMin; db = f.BMax - f.BMin; a0 = f.AMin; b0 = f.BMin; }
            if (da <= 0 || db <= 0) return outPts;

            int rows = Math.Max(1, (int)Math.Round(Math.Sqrt((double)count * db / da)));
            rows = Math.Min(rows, count);
            rows = Math.Min(rows, Math.Max(1, maxRows));
            if (rows < 1) rows = 1;
            int baseCols = count / rows;
            int extra = count % rows;

            for (int j = 0; j < rows; j++)
            {
                int colsThisRow = baseCols + (j < extra ? 1 : 0);
                if (colsThisRow < 1) continue;
                colsThisRow = Math.Min(colsThisRow, Math.Max(1, maxCols));
                double b = b0 + (j + 0.5) * db / rows;
                for (int i = 0; i < colsThisRow; i++)
                {
                    double a = a0 + (i + 0.5) * da / colsThisRow;
                    var w = f.C + f.U * a + f.V * b;         // oriented -> world
                    outPts.Add(new XYZ(w.X, w.Y, anchorZ));
                }
            }
            return outPts;
        }

        // Even fill of exactly k points from candidates: seed near the centre, then greedily add the
        // candidate farthest from the chosen set (maximises spread inside the actual room outline).
        private static List<XYZ> FarthestPointSelect(List<XYZ> cands, int k, BoundingBoxXYZ bb)
        {
            var sel = new List<XYZ>();
            if (cands == null || cands.Count == 0 || k <= 0) return sel;
            var centre = new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, cands[0].Z);
            var pool = new List<XYZ>(cands);
            // seed: most central candidate
            int seed = 0; double best = double.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            { double d = pool[i].DistanceTo(centre); if (d < best) { best = d; seed = i; } }
            sel.Add(pool[seed]); pool.RemoveAt(seed);

            while (sel.Count < k && pool.Count > 0)
            {
                int bestIdx = -1; double bestMin = -1;
                for (int i = 0; i < pool.Count; i++)
                {
                    double dmin = double.MaxValue;
                    foreach (var s in sel) { double d = s.DistanceTo(pool[i]); if (d < dmin) dmin = d; }
                    if (dmin > bestMin) { bestMin = dmin; bestIdx = i; }
                }
                if (bestIdx < 0) break;
                sel.Add(pool[bestIdx]); pool.RemoveAt(bestIdx);
            }
            return sel;
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

        // All boundary segments (wall-backed or not) — used for the oriented extent / long axis.
        private static List<Seg> AllBoundarySegments(SpatialElement room)
        {
            var list = new List<Seg>();
            try
            {
                var opts = new SpatialElementBoundaryOptions();
                var loops = room.GetBoundarySegments(opts);
                if (loops == null) return list;
                foreach (var loop in loops)
                    foreach (var bs in loop)
                    {
                        Curve c; try { c = bs.GetCurve(); } catch { continue; }
                        if (c == null) continue;
                        var a = c.GetEndPoint(0); var b = c.GetEndPoint(1);
                        double len = a.DistanceTo(b);
                        if (len < 1e-6) continue;
                        list.Add(new Seg { A = a, B = b, Length = len, Dir = (b - a).Normalize() });
                    }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixGridDistributor.AllBoundarySegments: {ex.Message}"); }
            return list;
        }

        // Ordered boundary vertices — used to compute the oriented bounding box extents.
        private static List<XYZ> AllBoundaryPoints(SpatialElement room)
        {
            var pts = new List<XYZ>();
            try
            {
                var opts = new SpatialElementBoundaryOptions();
                var loops = room.GetBoundarySegments(opts);
                if (loops == null) return pts;
                foreach (var loop in loops)
                    foreach (var bs in loop)
                    {
                        Curve c; try { c = bs.GetCurve(); } catch { continue; }
                        if (c == null) continue;
                        pts.Add(c.GetEndPoint(0));
                    }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixGridDistributor.AllBoundaryPoints: {ex.Message}"); }
            return pts;
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
