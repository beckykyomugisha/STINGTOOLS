// HC-23: Computational geometry for observation / CCTV line-of-sight (LOS).
//
// Replaces the placeholder TEXT code in LIG_AREA_OBS_LOS_TXT with a real
// per-room LOS percentage. Algorithm:
//   1. Resample the room interior on a uniform grid (default 200 mm).
//   2. For each observer location (nurse-station / CCTV camera), cast a 2D ray
//      to every grid point and check whether it intersects any in-room wall /
//      partition / column segment.
//   3. LOS percentage = visible grid points / total grid points * 100, with
//      multi-observer union when more than one observation point is configured.
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using System.Linq;

namespace StingTools.Core.Behavioural
{
    public sealed record LosSegment(XYZ Start, XYZ End);

    public sealed record LosResult(
        double PercentVisible,
        int GridPointsTotal,
        int GridPointsVisible,
        IReadOnlyList<XYZ> BlindSpots);

    public static class ObservationLosSolver
    {
        public static LosResult Compute(
            IReadOnlyList<XYZ> roomBoundary,
            IReadOnlyList<LosSegment> obstacles,
            IReadOnlyList<XYZ> observers,
            double gridSpacingFt = 0.656)  // ≈ 200 mm
        {
            if (roomBoundary == null || roomBoundary.Count < 3 || observers == null || observers.Count == 0)
                return new LosResult(0.0, 0, 0, Array.Empty<XYZ>());

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in roomBoundary)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            int total = 0, visible = 0;
            var blindSpots = new List<XYZ>();

            for (double y = minY; y <= maxY; y += gridSpacingFt)
            {
                for (double x = minX; x <= maxX; x += gridSpacingFt)
                {
                    var test = new XYZ(x, y, 0);
                    if (!PointInPolygon(test, roomBoundary)) continue;
                    total++;
                    bool seen = false;
                    foreach (var obs in observers)
                    {
                        if (!RayBlocked(obs, test, obstacles))
                        {
                            seen = true;
                            break;
                        }
                    }
                    if (seen) visible++;
                    else blindSpots.Add(test);
                }
            }

            double pct = total == 0 ? 0.0 : (100.0 * visible / total);
            return new LosResult(pct, total, visible, blindSpots);
        }

        private static bool PointInPolygon(XYZ p, IReadOnlyList<XYZ> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                    (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y)
                         / ((poly[j].Y - poly[i].Y) + 1e-12) + poly[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool RayBlocked(XYZ from, XYZ to, IReadOnlyList<LosSegment> obstacles)
        {
            if (obstacles == null) return false;
            foreach (var seg in obstacles)
            {
                if (SegmentsIntersect(from, to, seg.Start, seg.End)) return true;
            }
            return false;
        }

        private static bool SegmentsIntersect(XYZ p1, XYZ p2, XYZ p3, XYZ p4)
        {
            double d1 = Cross(p4.X - p3.X, p4.Y - p3.Y, p1.X - p3.X, p1.Y - p3.Y);
            double d2 = Cross(p4.X - p3.X, p4.Y - p3.Y, p2.X - p3.X, p2.Y - p3.Y);
            double d3 = Cross(p2.X - p1.X, p2.Y - p1.Y, p3.X - p1.X, p3.Y - p1.Y);
            double d4 = Cross(p2.X - p1.X, p2.Y - p1.Y, p4.X - p1.X, p4.Y - p1.Y);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;

        public static string ToLosCode(double percentVisible)
        {
            if (percentVisible >= 95.0) return "LOS-A";
            if (percentVisible >= 80.0) return "LOS-B";
            if (percentVisible >= 60.0) return "LOS-C";
            if (percentVisible >= 40.0) return "LOS-D";
            return "LOS-E";
        }
    }
}
