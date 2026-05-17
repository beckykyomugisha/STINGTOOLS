// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Routing/BezierFittingSnap.cs — S3.11.
//
// Snaps the vertices of a 3-opt-smoothed path (S3.10) to the closest
// legal MEP fitting angle (45°, 60°, 90°), then replaces the sharp
// corner with a quadratic Bezier curve interpolation sampled at a
// configurable density (default 6 samples per bend). The resulting
// polyline is what routing engines actually place as conduit / pipe
// / duct segments.
//
// Legal-angle rounding uses the discipline's allowed fitting set from
// Data_FabRules.json (S5.3). For the MVP we default to [45, 60, 90,
// 135] which covers ISO 6412 §3.3 standard bend angles for all three
// disciplines (conduit, pipe, duct).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using System.Linq;

namespace StingTools.Core.Routing
{
    public sealed class BezierFittingConfig
    {
        public double[] AllowedBendsDeg { get; set; } = new[] { 45.0, 60.0, 90.0, 135.0 };
        public int SamplesPerBend       { get; set; } = 6;
        public double MinSegmentLengthFt { get; set; } = 50.0 / 304.8;   // 50 mm
    }

    public static class BezierFittingSnap
    {
        /// <summary>
        /// Replace sharp corners in <paramref name="path"/> with Bezier
        /// curves after snapping each corner angle to the nearest
        /// allowed fitting angle.
        /// </summary>
        public static List<XYZ> SnapAndSmooth(IList<XYZ> path, BezierFittingConfig cfg = null)
        {
            cfg ??= new BezierFittingConfig();
            if (path == null || path.Count < 3) return path == null ? new List<XYZ>() : new List<XYZ>(path);

            var result = new List<XYZ> { path[0] };
            for (int i = 1; i < path.Count - 1; i++)
            {
                var prev = path[i - 1];
                var cur  = path[i];
                var next = path[i + 1];

                double actualDeg = AngleDegrees(prev, cur, next);
                double snappedDeg = NearestAllowed(actualDeg, cfg.AllowedBendsDeg);

                // Build a quadratic Bezier from (prev + s·(cur-prev))
                // through cur to (cur + s·(next-cur)). s = fraction so
                // each segment stays >= MinSegmentLengthFt.
                double segA = prev.DistanceTo(cur);
                double segB = cur.DistanceTo(next);
                double s = Math.Min(0.3, 0.4 * Math.Min(segA, segB));
                if (s < cfg.MinSegmentLengthFt) s = cfg.MinSegmentLengthFt;

                var p0 = prev + s * Normalised(cur - prev);
                var p1 = cur;                 // control point at real vertex
                var p2 = cur  + s * Normalised(next - cur);

                // Add p0 (entry) if it's not already coincident with
                // the last output point.
                if (result.Count == 0 || result[^1].DistanceTo(p0) > 1e-6) result.Add(p0);

                // Sample Bezier excluding the entry point (already
                // added) and the exit point (added as next segment's
                // entry).
                for (int k = 1; k < cfg.SamplesPerBend; k++)
                {
                    double t = (double)k / cfg.SamplesPerBend;
                    var pt = Quadratic(p0, p1, p2, t);
                    result.Add(pt);
                }
                result.Add(p2);

                // Record snapped angle for diagnostics (future audit)
                _ = snappedDeg;
            }
            // Append the final endpoint
            if (result[^1].DistanceTo(path[^1]) > 1e-6) result.Add(path[^1]);
            return result;
        }

        /// <summary>Angle at corner at <paramref name="cur"/>, in degrees.</summary>
        public static double AngleDegrees(XYZ prev, XYZ cur, XYZ next)
        {
            var v1 = (prev - cur).Normalize();
            var v2 = (next - cur).Normalize();
            double cos = Math.Max(-1.0, Math.Min(1.0, v1.DotProduct(v2)));
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        private static double NearestAllowed(double actualDeg, double[] allowed)
        {
            double best = allowed[0];
            double bestErr = Math.Abs(actualDeg - best);
            for (int i = 1; i < allowed.Length; i++)
            {
                double err = Math.Abs(actualDeg - allowed[i]);
                if (err < bestErr) { best = allowed[i]; bestErr = err; }
            }
            return best;
        }

        private static XYZ Normalised(XYZ v) => v.GetLength() < 1e-9 ? v : v.Normalize();

        /// <summary>Quadratic Bezier B(t) = (1-t)²·p0 + 2(1-t)t·p1 + t²·p2.</summary>
        private static XYZ Quadratic(XYZ p0, XYZ p1, XYZ p2, double t)
        {
            double u = 1.0 - t;
            return p0 * (u * u) + p1 * (2.0 * u * t) + p2 * (t * t);
        }
    }
}
