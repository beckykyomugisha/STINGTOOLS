// ══════════════════════════════════════════════════════════════════════════
//  SeparationGeometry.cs — which separation rule governs a pair of runs.
//  Revit-free (compiled into StingTools.Routing.Tests).
//
//  DSCH-22. STING_SEPARATION_RULES.json gives a pair of services different
//  distances by geometry: power/data 200 mm in enclosed parallel trays, 300 mm
//  parallel with one open, 50 mm where they cross. Taking the largest rule for
//  the pair holds a crossing to 300 mm. A first attempt (reverted) chose by
//  direction alone; drops are vertical, so every horizontal neighbour read as a
//  "crossing", and pairs with no crossing rule then required 0 mm.
//
//  The rules here:
//    * A CROSSING is two near-horizontal straight runs, near-perpendicular in
//      plan, whose plan footprints actually intersect. Nothing else is one.
//    * Crossing  -> rules whose geometry is "crossing" or "any".
//      Otherwise -> every rule except crossing-only ones.
//    * If that leaves no rule but the pair has rules, the largest of ALL its
//      rules applies. A pair with rules never requires 0 mm.
//    * Enclosure (both_enclosed_metal) and shared containment are not known
//      from the model, so among the remaining rules the largest wins - the
//      conservative reading.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Routing
{
    public static class SeparationGeometry
    {
        /// <summary>Cos of the angle beyond which a run counts as horizontal (≈ 17° from level).</summary>
        public const double HorizontalMaxSinZ = 0.3;
        /// <summary>|cos| of the plan angle below which two runs are perpendicular (≈ 73°–107°).</summary>
        public const double PerpendicularMaxCos = 0.3;

        public readonly struct Seg
        {
            public readonly double X0, Y0, Z0, X1, Y1, Z1;
            public Seg(double x0, double y0, double z0, double x1, double y1, double z1)
            { X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1; }
        }

        public static bool IsCrossing(Seg a, Seg b)
        {
            if (!IsHorizontal(a) || !IsHorizontal(b)) return false;
            double ax = a.X1 - a.X0, ay = a.Y1 - a.Y0, bx = b.X1 - b.X0, by = b.Y1 - b.Y0;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-9 || lb < 1e-9) return false;
            if (Math.Abs((ax * bx + ay * by) / (la * lb)) > PerpendicularMaxCos) return false;
            return PlanSegmentsIntersect(a, b);
        }

        private static bool IsHorizontal(Seg s)
        {
            double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, dz = s.Z1 - s.Z0;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return len > 1e-9 && Math.Abs(dz) / len < HorizontalMaxSinZ;
        }

        private static bool PlanSegmentsIntersect(Seg a, Seg b)
        {
            double d1 = Cross(b.X0, b.Y0, b.X1, b.Y1, a.X0, a.Y0);
            double d2 = Cross(b.X0, b.Y0, b.X1, b.Y1, a.X1, a.Y1);
            double d3 = Cross(a.X0, a.Y0, a.X1, a.Y1, b.X0, b.Y0);
            double d4 = Cross(a.X0, a.Y0, a.X1, a.Y1, b.X1, b.Y1);
            return d1 * d2 <= 0 && d3 * d4 <= 0;
        }

        private static double Cross(double x0, double y0, double x1, double y1, double px, double py)
            => (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0);

        /// <summary>
        /// The governing rule among those that apply to the service pair, as
        /// (geometry, mm, id). Returns null when no rule applies to the pair.
        /// </summary>
        public static (string Geometry, double Mm, string Id)? Governing(
            IEnumerable<(string Geometry, double Mm, string Id)> rulesForPair, bool crossing)
        {
            var all = (rulesForPair ?? Enumerable.Empty<(string, double, string)>()).ToList();
            if (all.Count == 0) return null;
            var chosen = all.Where(r => crossing
                    ? Is(r.Geometry, "crossing") || Is(r.Geometry, "any")
                    : !Is(r.Geometry, "crossing")).ToList();
            var pool = chosen.Count > 0 ? chosen : all;
            return pool.OrderByDescending(r => r.Mm).First();
        }

        private static bool Is(string g, string want)
            => string.Equals(string.IsNullOrWhiteSpace(g) ? "any" : g.Trim(), want, StringComparison.OrdinalIgnoreCase);
    }
}
