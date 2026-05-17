using StingTools.Core;
// StingTools — orientation engine (Phase 175)
//
// Maps an element's primary axis vs the view's normal vector to one of
// five orientation states. Used by SymbolConceptRegistry to pick the
// correct family variant for view-dependent symbols.
//
// Hosts are detected in priority order:
//   1. FamilyInstance       — HandOrientation / FacingOrientation
//   2. MEPCurve              — Pipe / Duct / Conduit / CableTray axis
//                              (LocationCurve direction)
//   3. Any LocationCurve     — generic fallback
//   4. BoundingBox dominant  — last-resort axis from bbox aspect ratio
//   5. None                  — Horizontal default

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Symbols
{
    public enum OrientationState { Horizontal, EndOn, Vertical, VerticalEndOn, Oblique }

    public static class SymbolOrientationEngine
    {
        private const double Tol = 0.15;
        private const double VerticalTol = 0.95;  // |axis.Z| ≥ this → vertical

        public static OrientationState Compute(Element host, View view)
        {
            if (host == null || view == null) return OrientationState.Horizontal;
            try
            {
                XYZ axis = ResolvePrimaryAxis(host);
                if (axis == null || axis.IsZeroLength()) return OrientationState.Horizontal;
                axis = axis.Normalize();

                XYZ viewNormal = (view.ViewDirection ?? XYZ.BasisZ);
                viewNormal = viewNormal.IsZeroLength() ? XYZ.BasisZ : viewNormal.Normalize();

                bool axisVertical = Math.Abs(axis.Z) > VerticalTol;
                bool viewIsPlan   = Math.Abs(viewNormal.Z) > VerticalTol;

                if (axisVertical)
                    return viewIsPlan ? OrientationState.Vertical : OrientationState.VerticalEndOn;

                double dot = Math.Abs(axis.DotProduct(viewNormal));
                if (dot < Tol)         return OrientationState.Horizontal;
                if (dot > 1.0 - Tol)   return OrientationState.EndOn;
                return OrientationState.Oblique;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"OrientationEngine: {ex.Message}");
                return OrientationState.Horizontal;
            }
        }

        /// <summary>
        /// Backwards-compatible overload — many call sites already pass a
        /// FamilyInstance. Delegates to the Element version.
        /// </summary>
        public static OrientationState Compute(FamilyInstance instance, View view)
            => Compute((Element)instance, view);

        public static string GetOrientationStateKey(OrientationState state)
        {
            switch (state)
            {
                case OrientationState.Horizontal:    return "PIPE_HORIZONTAL_VIEW_PLAN";
                case OrientationState.EndOn:         return "PIPE_HORIZONTAL_ENDVIEW";
                case OrientationState.Vertical:      return "PIPE_VERTICAL_VIEW_PLAN";
                case OrientationState.VerticalEndOn: return "PIPE_VERTICAL_ENDVIEW";
                case OrientationState.Oblique:
                default:                             return "PIPE_HORIZONTAL_VIEW_PLAN";
            }
        }

        // ── Primary-axis resolution ─────────────────────────────────────

        private static XYZ ResolvePrimaryAxis(Element host)
        {
            // 1. FamilyInstance — best signal: the family author's intent
            //    encoded as HandOrientation (perpendicular to facing).
            if (host is FamilyInstance fi)
            {
                try
                {
                    XYZ hand = fi.HandOrientation;
                    if (hand != null && !hand.IsZeroLength()) return hand;
                    XYZ facing = fi.FacingOrientation;
                    if (facing != null && !facing.IsZeroLength()) return facing;
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"FamilyInstance axis: {ex.Message}"); }
            }

            // 2. MEPCurve — Pipe / Duct / Conduit / CableTray. Curve direction
            //    is the canonical axis. Vertical pipes / risers light up
            //    cleanly here.
            if (host is Pipe || host is Duct || host is Conduit || host is CableTray
                || host is FlexPipe || host is FlexDuct)
            {
                try
                {
                    if (host.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ p0 = lc.Curve.GetEndPoint(0);
                        XYZ p1 = lc.Curve.GetEndPoint(1);
                        XYZ dir = p1 - p0;
                        if (!dir.IsZeroLength()) return dir;
                    }
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"MEPCurve axis: {ex.Message}"); }
            }

            // 3. Generic LocationCurve fallback (e.g. linked-model proxies,
            //    detail components placed along a line, structural framing).
            try
            {
                if (host?.Location is LocationCurve loc && loc.Curve != null)
                {
                    XYZ p0 = loc.Curve.GetEndPoint(0);
                    XYZ p1 = loc.Curve.GetEndPoint(1);
                    XYZ dir = p1 - p0;
                    if (!dir.IsZeroLength()) return dir;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LocationCurve fallback: {ex.Message}"); }

            // 4. Bounding-box aspect ratio — last-resort heuristic for
            //    elongated linked geometry that doesn't expose an axis.
            try
            {
                BoundingBoxXYZ bb = host?.get_BoundingBox(null);
                if (bb != null)
                {
                    double dx = Math.Abs(bb.Max.X - bb.Min.X);
                    double dy = Math.Abs(bb.Max.Y - bb.Min.Y);
                    double dz = Math.Abs(bb.Max.Z - bb.Min.Z);
                    // Use longest dimension as axis only if it dominates.
                    if (dz > dx * 1.5 && dz > dy * 1.5) return XYZ.BasisZ;
                    if (dx > dy * 1.5) return XYZ.BasisX;
                    if (dy > dx * 1.5) return XYZ.BasisY;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"BBox axis fallback: {ex.Message}"); }

            return XYZ.BasisX;
        }
    }
}
