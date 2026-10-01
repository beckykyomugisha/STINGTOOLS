// ============================================================================
// DwgPickGeometry.cs — Revit-free geometry + type-matching helpers for the
// interactive DWG pickers (Pick Wall / Pick Column / Pick Beam) and the
// structural type factory.
//
// Deliberately free of Autodesk.* so StingTools.Tags.Tests can <Compile Include>
// it and exercise the measuring / matching rules without Revit. All inputs are
// plain doubles in whatever unit the caller uses (the pickers pass feet in plan
// XY, the type matchers pass millimetres); outputs come back in the same unit.
// ============================================================================

using System;
using System.Collections.Generic;

namespace StingTools.Model
{
    internal static class DwgPickGeometry
    {
        /// <summary>Result of measuring two picked lines as the faces of one element.</summary>
        public readonly struct ParallelPair
        {
            public ParallelPair(bool isParallel, double dot, double gap,
                double startX, double startY, double endX, double endY)
            {
                IsParallel = isParallel; Dot = dot; Gap = gap;
                StartX = startX; StartY = startY; EndX = endX; EndY = endY;
            }

            /// <summary>|cos| of the angle between the lines met the threshold AND the
            /// two faces overlap along their length.</summary>
            public bool IsParallel { get; }
            /// <summary>The faces share a stretch along A's axis (the centreline has length).</summary>
            public bool Overlaps => Length > 0;
            /// <summary>|cos| of the angle between the two lines (1 = parallel).</summary>
            public double Dot { get; }
            /// <summary>Perpendicular distance between the lines — the element's thickness / width.</summary>
            public double Gap { get; }
            public double StartX { get; }
            public double StartY { get; }
            public double EndX { get; }
            public double EndY { get; }

            public double Length
            {
                get
                {
                    double dx = EndX - StartX, dy = EndY - StartY;
                    return Math.Sqrt(dx * dx + dy * dy);
                }
            }
        }

        /// <summary>Plan size and placement of a rectangular footprint.</summary>
        public readonly struct RectDims
        {
            public RectDims(double width, double depth, double centerX, double centerY, double angleRad)
            {
                Width = width; Depth = depth; CenterX = centerX; CenterY = centerY; AngleRad = angleRad;
            }

            /// <summary>Extent along the direction given by <see cref="AngleRad"/>.</summary>
            public double Width { get; }
            /// <summary>Extent perpendicular to <see cref="AngleRad"/>.</summary>
            public double Depth { get; }
            public double CenterX { get; }
            public double CenterY { get; }
            /// <summary>Rotation of the Width axis from +X, normalised to (-π/4, π/4].</summary>
            public double AngleRad { get; }
        }

        /// <summary>|cos| two picked faces must reach to count as parallel: 0.999 is
        /// about 2.5°. The old 0.95 admitted ~18°, where the "thickness" measured
        /// depends on where along the lines it is taken and means nothing.</summary>
        public const double DefaultMinDot = 0.999;

        /// <summary>
        /// Measures two lines (A: a0→a1, B: b0→b1) as opposite faces of one element.
        /// B is projected onto A's axis; the element exists only where the two faces
        /// OVERLAP along that axis, so the centreline spans the overlap (in A's
        /// direction) and lines that do not overlap are not a pair. Gap is the
        /// perpendicular distance between the faces at the middle of the overlap,
        /// and the centreline runs halfway between them. A pair is parallel only when
        /// |cos| of the angle between the lines is at least <paramref name="minDot"/>
        /// AND the overlap has length.
        /// </summary>
        public static ParallelPair MeasureParallelPair(
            double a0x, double a0y, double a1x, double a1y,
            double b0x, double b0y, double b1x, double b1y,
            double minDot = DefaultMinDot)
        {
            double ax = a1x - a0x, ay = a1y - a0y;
            double bx = b1x - b0x, by = b1y - b0y;
            double la = Math.Sqrt(ax * ax + ay * ay);
            double lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-12 || lb < 1e-12)
                return new ParallelPair(false, 0, 0, 0, 0, 0, 0);

            double ux = ax / la, uy = ay / la;     // A's axis
            double nx = -uy, ny = ux;              // A's normal
            double dot = Math.Abs(ux * (bx / lb) + uy * (by / lb));

            // B's endpoints in A's frame: t along the axis, d across it.
            double tb0 = (b0x - a0x) * ux + (b0y - a0y) * uy;
            double tb1 = (b1x - a0x) * ux + (b1y - a0y) * uy;
            double db0 = (b0x - a0x) * nx + (b0y - a0y) * ny;
            double db1 = (b1x - a0x) * nx + (b1y - a0y) * ny;

            // The stretch both faces cover.
            double lo = Math.Max(0, Math.Min(tb0, tb1));
            double hi = Math.Min(la, Math.Max(tb0, tb1));
            bool overlaps = hi - lo > 1e-9 * Math.Max(la, lb);
            if (!overlaps)
            {
                // Report the angle and a nominal gap, but never a pair.
                double gapNominal = Math.Abs(((b0x + b1x) * 0.5 - a0x) * nx + ((b0y + b1y) * 0.5 - a0y) * ny);
                return new ParallelPair(false, dot, gapNominal, 0, 0, 0, 0);
            }

            // B's offset from A at an axis position (linear along B).
            double OffsetAt(double t)
            {
                double span = tb1 - tb0;
                if (Math.Abs(span) < 1e-12) return (db0 + db1) * 0.5;
                return db0 + (t - tb0) / span * (db1 - db0);
            }

            double dLo = OffsetAt(lo), dHi = OffsetAt(hi);
            double gap = Math.Abs(OffsetAt((lo + hi) * 0.5));

            return new ParallelPair(dot >= minDot, dot, gap,
                a0x + ux * lo + nx * dLo * 0.5, a0y + uy * lo + ny * dLo * 0.5,
                a0x + ux * hi + nx * dHi * 0.5, a0y + uy * hi + ny * dHi * 0.5);
        }

        /// <summary>Index of the value nearest <paramref name="target"/>; -1 when there is none.
        /// Non-positive and non-finite values are ignored (a type with no measurable size).</summary>
        public static int NearestIndex(IReadOnlyList<double> values, double target)
        {
            if (values == null) return -1;
            int best = -1;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) continue;
                double diff = Math.Abs(v - target);
                if (diff < bestDiff) { bestDiff = diff; best = i; }
            }
            return best;
        }

        /// <summary>Index of the nearest value when it lies within ±<paramref name="tolerance"/>
        /// of the target (inclusive); -1 when nothing is close enough.</summary>
        public static int IndexWithinTolerance(IReadOnlyList<double> values, double target, double tolerance)
        {
            int i = NearestIndex(values, target);
            if (i < 0) return -1;
            return Math.Abs(values[i] - target) <= tolerance ? i : -1;
        }

        /// <summary>
        /// Recognises a closed rectangle from its vertices (a closing duplicate of the
        /// first point, repeated points and collinear mid-edge points are tolerated).
        /// Every corner must be within <paramref name="angleTolDeg"/> of 90°.
        /// </summary>
        public static bool TryParseRectangle(IReadOnlyList<double> xs, IReadOnlyList<double> ys,
            out RectDims rect, double angleTolDeg = 2.0)
        {
            rect = default;
            if (xs == null || ys == null || xs.Count != ys.Count || xs.Count < 4) return false;

            // Scale-relative tolerance for "same point".
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < xs.Count; i++)
            {
                minX = Math.Min(minX, xs[i]); maxX = Math.Max(maxX, xs[i]);
                minY = Math.Min(minY, ys[i]); maxY = Math.Max(maxY, ys[i]);
            }
            double span = Math.Max(maxX - minX, maxY - minY);
            if (span <= 0) return false;
            double eps = span * 1e-6;

            // Drop consecutive duplicates and the closing point.
            var px = new List<double>(); var py = new List<double>();
            for (int i = 0; i < xs.Count; i++)
            {
                if (px.Count > 0 && Dist(px[px.Count - 1], py[py.Count - 1], xs[i], ys[i]) <= eps) continue;
                px.Add(xs[i]); py.Add(ys[i]);
            }
            if (px.Count > 1 && Dist(px[0], py[0], px[px.Count - 1], py[py.Count - 1]) <= eps)
            {
                px.RemoveAt(px.Count - 1); py.RemoveAt(py.Count - 1);
            }

            // Drop collinear vertices (a rectangle drawn with a mid-edge vertex).
            bool removed = true;
            while (removed && px.Count > 4)
            {
                removed = false;
                for (int i = 0; i < px.Count; i++)
                {
                    int p = (i - 1 + px.Count) % px.Count, n = (i + 1) % px.Count;
                    double cross = (px[i] - px[p]) * (py[n] - py[i]) - (py[i] - py[p]) * (px[n] - px[i]);
                    double l1 = Dist(px[p], py[p], px[i], py[i]), l2 = Dist(px[i], py[i], px[n], py[n]);
                    if (l1 > 0 && l2 > 0 && Math.Abs(cross) / (l1 * l2) < 1e-6)
                    {
                        px.RemoveAt(i); py.RemoveAt(i); removed = true; break;
                    }
                }
            }
            if (px.Count != 4) return false;

            double maxCos = Math.Sin(angleTolDeg * Math.PI / 180.0);
            for (int i = 0; i < 4; i++)
            {
                int p = (i + 3) % 4, n = (i + 1) % 4;
                double e1x = px[i] - px[p], e1y = py[i] - py[p];
                double e2x = px[n] - px[i], e2y = py[n] - py[i];
                double l1 = Math.Sqrt(e1x * e1x + e1y * e1y), l2 = Math.Sqrt(e2x * e2x + e2y * e2y);
                if (l1 <= eps || l2 <= eps) return false;
                double cos = (e1x * e2x + e1y * e2y) / (l1 * l2);
                if (Math.Abs(cos) > maxCos) return false;
            }

            double wx = px[1] - px[0], wy = py[1] - py[0];
            double width = Math.Sqrt(wx * wx + wy * wy);
            double depth = Dist(px[1], py[1], px[2], py[2]);
            double angle = Math.Atan2(wy, wx);
            double cx = (px[0] + px[1] + px[2] + px[3]) / 4.0;
            double cy = (py[0] + py[1] + py[2] + py[3]) / 4.0;
            NormalizeOrientation(ref width, ref depth, ref angle);
            rect = new RectDims(width, depth, cx, cy, angle);
            return true;
        }

        /// <summary>
        /// Footprint from two opposite edges: the along-edge length is the Width
        /// (on the edges' direction), the gap between them is the Depth.
        /// </summary>
        public static RectDims RectFromParallelEdges(ParallelPair pair)
        {
            double width = pair.Length;
            double depth = pair.Gap;
            double angle = Math.Atan2(pair.EndY - pair.StartY, pair.EndX - pair.StartX);
            double cx = (pair.StartX + pair.EndX) * 0.5, cy = (pair.StartY + pair.EndY) * 0.5;
            NormalizeOrientation(ref width, ref depth, ref angle);
            return new RectDims(width, depth, cx, cy, angle);
        }

        /// <summary>
        /// Brings a rotation into (-π/4, π/4], rotating by quarter turns and swapping
        /// width/depth on each odd quarter turn so the footprint is unchanged. A
        /// rectangle drawn at 90° is then placed unrotated with its sizes swapped,
        /// instead of rotated a quarter turn.
        /// </summary>
        public static void NormalizeOrientation(ref double width, ref double depth, ref double angleRad)
        {
            const double Quarter = Math.PI / 2.0;
            // Map into (-π/4, π/4] by removing whole quarter turns.
            double k = Math.Round(angleRad / Quarter);
            double a = angleRad - k * Quarter;
            if (a <= -Math.PI / 4.0 + 1e-12) { a += Quarter; k -= 1; }
            if (((long)Math.Abs(k)) % 2 == 1)
            {
                double t = width; width = depth; depth = t;
            }
            angleRad = a;
        }

        /// <summary>2D distance from a point to the segment (x0,y0)-(x1,y1).</summary>
        public static double DistanceToSegment2D(double x0, double y0, double x1, double y1, double px, double py)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double len2 = dx * dx + dy * dy;
            if (len2 <= 0) return Dist(x0, y0, px, py);
            double t = ((px - x0) * dx + (py - y0) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return Dist(x0 + t * dx, y0 + t * dy, px, py);
        }

        /// <summary>2D distance from a point to an open polyline.</summary>
        public static double DistanceToPolyline2D(IReadOnlyList<double> xs, IReadOnlyList<double> ys, double px, double py)
        {
            if (xs == null || ys == null || xs.Count == 0 || xs.Count != ys.Count) return double.MaxValue;
            if (xs.Count == 1) return Dist(xs[0], ys[0], px, py);
            double best = double.MaxValue;
            for (int i = 0; i + 1 < xs.Count; i++)
                best = Math.Min(best, DistanceToSegment2D(xs[i], ys[i], xs[i + 1], ys[i + 1], px, py));
            return best;
        }

        private static double Dist(double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
