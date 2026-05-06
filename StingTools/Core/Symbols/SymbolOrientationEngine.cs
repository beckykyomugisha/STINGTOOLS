// StingTools — orientation engine (Phase 175)
//
// Maps an instance's primary axis vs the view's normal vector to one of
// five orientation states. Used by SymbolConceptRegistry to pick the
// correct family variant for view-dependent symbols.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public enum OrientationState { Horizontal, EndOn, Vertical, VerticalEndOn, Oblique }

    public static class SymbolOrientationEngine
    {
        private const double Tol = 0.15;

        public static OrientationState Compute(FamilyInstance instance, View view)
        {
            if (instance == null || view == null) return OrientationState.Horizontal;
            try
            {
                XYZ axis;
                try { axis = instance.HandOrientation ?? instance.FacingOrientation ?? XYZ.BasisX; }
                catch { axis = XYZ.BasisX; }
                if (axis == null || axis.IsZeroLength()) axis = XYZ.BasisX;
                axis = axis.Normalize();

                XYZ viewNormal = view.ViewDirection ?? XYZ.BasisZ;
                viewNormal = viewNormal.Normalize();

                // Vertical element check first.
                bool axisVertical = Math.Abs(axis.Z) > 1.0 - Tol;
                if (axisVertical)
                {
                    bool viewIsPlan = Math.Abs(viewNormal.Z) > 1.0 - Tol;
                    return viewIsPlan ? OrientationState.Vertical : OrientationState.VerticalEndOn;
                }

                double dot = Math.Abs(axis.DotProduct(viewNormal));
                if (dot < Tol)             return OrientationState.Horizontal;
                if (dot > 1.0 - Tol)       return OrientationState.EndOn;
                return OrientationState.Oblique;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"OrientationEngine: {ex.Message}");
                return OrientationState.Horizontal;
            }
        }

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
    }
}
