// StingTools — Drawing Template Manager · Match lines: the Revit-free geometry
//
// Where does the match line between two scope boxes go, and which views get one?
//
// MatchLineEngine used to answer this with each box's AABB and a 1 mm "faces
// coincide" test. That only works for boxes drawn square to project north and
// exactly edge to edge — which is not how the Scope Box Planner lays out area
// boxes: STING-AREA:: boxes overlap their neighbours by 2 m and are turned to
// the grid. A turned box's AABB is bigger than the box, and overlapping boxes
// have no coincident face, so area boxes never got a match line.
//
// This file decides in each pair's OWN frame instead:
//   * both boxes must share a frame (same angle within a tolerance, modulo 90°);
//   * along one axis they must touch or overlap without one containing the other;
//   * along the other axis they must share a span long enough to be an edge, and
//     clearly longer than the overlap is deep — two boxes that meet only at a
//     corner (touching, or diagonal neighbours overlapping in a small square) get
//     no line;
//   * the line sits on the touching face, or down the MIDDLE of the overlap strip.
//
// Lengths are in whatever unit the caller uses (the engine passes feet).
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Drawing
{
    /// <summary>A scope box's plan footprint, measured in its own frame.</summary>
    public struct MatchLineRect
    {
        public double CentreX, CentreY;
        /// <summary>Extent along the box's own axis (the one at <see cref="AngleRad"/>).</summary>
        public double Width;
        /// <summary>Extent across it.</summary>
        public double Depth;
        /// <summary>Angle of the box's width axis from project X, radians.</summary>
        public double AngleRad;

        public MatchLineRect(double cx, double cy, double width, double depth, double angleRad = 0)
        { CentreX = cx; CentreY = cy; Width = width; Depth = depth; AngleRad = angleRad; }
    }

    /// <summary>Where the match line between two boxes goes.</summary>
    public sealed class MatchLineSegment
    {
        public double X0, Y0, X1, Y1;
        /// <summary>"vertical" (the boxes sit side by side along the first box's width axis)
        /// or "horizontal" (along its depth axis) — in the first box's frame.</summary>
        public string Direction;
        /// <summary>How deep the overlap strip is; 0 (or a hair below) for boxes that touch.</summary>
        public double OverlapDepth;
        /// <summary>Length of the shared edge.</summary>
        public double Length => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
    }

    public static class MatchLineGeometry
    {
        /// <summary>Two boxes more than this far apart in angle (modulo 90°) are not in one frame.</summary>
        public const double DefaultAngleToleranceRad = 0.5 * Math.PI / 180.0;

        /// <summary>
        /// The match line between <paramref name="a"/> and <paramref name="b"/>, or null
        /// when they do not share an edge. <paramref name="touchTolerance"/> is how far
        /// apart two faces may be and still "touch"; <paramref name="minSharedSpan"/> is
        /// the shortest shared edge that earns a line.
        /// </summary>
        public static MatchLineSegment Find(MatchLineRect a, MatchLineRect b,
            double touchTolerance, double minSharedSpan,
            double angleToleranceRad = DefaultAngleToleranceRad)
        {
            if (a.Width <= 0 || a.Depth <= 0 || b.Width <= 0 || b.Depth <= 0) return null;
            touchTolerance = Math.Abs(touchTolerance);
            minSharedSpan  = Math.Max(0, minSharedSpan);

            // Same frame? Compare the angles modulo a quarter turn: a box turned 90° is
            // the same rectangle frame with width and depth swapped.
            double quarter = Math.PI / 2;
            double diff = b.AngleRad - a.AngleRad;
            double turns = Math.Round(diff / quarter);
            double residual = diff - turns * quarter;
            if (Math.Abs(residual) > Math.Abs(angleToleranceRad)) return null;
            double bW = b.Width, bD = b.Depth;
            if (((long)Math.Abs(turns)) % 2 == 1) { bW = b.Depth; bD = b.Width; }

            // Work in a's frame: u along a's width axis, v along its depth axis.
            double c = Math.Cos(a.AngleRad), s = Math.Sin(a.AngleRad);
            double ua = a.CentreX * c + a.CentreY * s, va = -a.CentreX * s + a.CentreY * c;
            double ub = b.CentreX * c + b.CentreY * s, vb = -b.CentreX * s + b.CentreY * c;

            double ua0 = ua - a.Width / 2, ua1 = ua + a.Width / 2;
            double va0 = va - a.Depth / 2, va1 = va + a.Depth / 2;
            double ub0 = ub - bW / 2,      ub1 = ub + bW / 2;
            double vb0 = vb - bD / 2,      vb1 = vb + bD / 2;

            double ou0 = Math.Max(ua0, ub0), ou1 = Math.Min(ua1, ub1), lu = ou1 - ou0;
            double ov0 = Math.Max(va0, vb0), ov1 = Math.Min(va1, vb1), lv = ov1 - ov0;

            bool alongU = IsSplitAxis(lu, lv, Math.Min(a.Width, bW), touchTolerance, minSharedSpan);
            bool alongV = !alongU && IsSplitAxis(lv, lu, Math.Min(a.Depth, bD), touchTolerance, minSharedSpan);
            if (!alongU && !alongV) return null;

            double p0u, p0v, p1u, p1v;
            if (alongU)
            {
                double u = (ou0 + ou1) / 2;
                p0u = u; p0v = ov0; p1u = u; p1v = ov1;
            }
            else
            {
                double v = (ov0 + ov1) / 2;
                p0u = ou0; p0v = v; p1u = ou1; p1v = v;
            }
            return new MatchLineSegment
            {
                X0 = p0u * c - p0v * s, Y0 = p0u * s + p0v * c,
                X1 = p1u * c - p1v * s, Y1 = p1u * s + p1v * c,
                Direction = alongU ? "vertical" : "horizontal",
                OverlapDepth = alongU ? lu : lv,
            };
        }

        /// <summary>
        /// True when the boxes sit side by side across this axis: they touch or overlap
        /// on it (<paramref name="overlap"/> ≥ −tolerance) without one spanning the other
        /// (<paramref name="overlap"/> short of the narrower extent), and share an edge on
        /// the other axis that is long enough and clearly longer than the strip is deep.
        /// The "twice as long" rule is what keeps diagonal neighbours — which overlap in
        /// a small square at a corner — from drawing a stub line across that corner.
        /// </summary>
        private static bool IsSplitAxis(double overlap, double sharedSpan, double narrowerExtent,
            double tol, double minSharedSpan)
        {
            if (overlap < -tol) return false;                       // a gap
            if (overlap >= narrowerExtent - tol) return false;       // one box spans the other
            if (sharedSpan < minSharedSpan || sharedSpan <= tol) return false;
            return sharedSpan > 2 * Math.Max(overlap, 0);
        }

        /// <summary>
        /// Building (STING-LOC::) and seed (STING-SEED::) boxes are never drawn on a
        /// sheet — a LOC box sets a tag token, a seed is a size to copy — so they never
        /// carry a match line. Everything else can.
        /// </summary>
        public static bool IsMatchLineBox(string scopeBoxName)
        {
            var kind = ScopeBoxNames.Classify(scopeBoxName);
            return kind != ScopeBoxKind.Building && kind != ScopeBoxKind.Seed;
        }

        /// <summary>
        /// Whether a view on one box gets a match line to a view on the other. Only
        /// views of the same drawing type on the same level pair: an M plan on box A
        /// and an E plan on box B are different drawings, and a level-1 plan does not
        /// continue on a level-2 sheet. Views produced outside the drawing-type engine
        /// carry no type id; two of those pair only when their <paramref name="fallbackKeyA"/>
        /// / <paramref name="fallbackKeyB"/> (the caller's stand-in for the drawing —
        /// view type and template) agree. A stamped view never pairs with an unstamped one.
        /// A null level means the view has none (a section); null pairs only with null.
        /// </summary>
        public static bool ShouldPairViews(string drawingTypeA, string drawingTypeB,
            string levelA, string levelB, string fallbackKeyA, string fallbackKeyB)
        {
            if (!string.Equals(levelA ?? "", levelB ?? "", StringComparison.Ordinal)) return false;
            bool stampedA = !string.IsNullOrWhiteSpace(drawingTypeA);
            bool stampedB = !string.IsNullOrWhiteSpace(drawingTypeB);
            if (stampedA != stampedB) return false;
            if (stampedA) return string.Equals(drawingTypeA.Trim(), drawingTypeB.Trim(), StringComparison.OrdinalIgnoreCase);
            return !string.IsNullOrEmpty(fallbackKeyA)
                && string.Equals(fallbackKeyA, fallbackKeyB, StringComparison.Ordinal);
        }
    }
}
