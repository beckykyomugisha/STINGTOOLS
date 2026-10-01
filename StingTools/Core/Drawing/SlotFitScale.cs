// StingTools — Drawing Template Manager · fit-to-slot scale decision
//
// Revit-free so the decision can be unit-tested. SheetPlacementBridge measures
// the view and the slot; this decides what scale the view ends up at.

using System;

namespace StingTools.Core.Drawing
{
    internal static class SlotFitScale
    {
        /// <summary>Standard 1:N scales, finest first.</summary>
        internal static readonly int[] StandardScales =
            { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 1250, 2000, 2500, 5000, 10000 };

        /// <summary>DTW-157 — used when STING_VIEWPORT_PLACEMENT_RULES.json carries no
        /// <c>annotationMarginFactor</c>. The fit measures the model crop only; grid
        /// and level heads, the annotation crop and the viewport title extend the
        /// viewport past it, so the measured extent is grown by 10 % before fitting.</summary>
        internal const double DefaultAnnotationMarginFactor = 1.10;

        /// <summary>A factor below 1 would shrink the measured extent and one above 2
        /// is almost certainly a typo; both are clamped.</summary>
        internal static double ClampMarginFactor(double f)
            => double.IsNaN(f) || f < 1.0 ? 1.0 : f > 2.0 ? 2.0 : f;

        /// <summary>DTW-157 — true when a placed viewport's box runs past its slot by
        /// more than <paramref name="tol"/> on any side. All values in one unit.</summary>
        internal static bool Overflows(double boxMinX, double boxMinY, double boxMaxX, double boxMaxY,
            double slotCx, double slotCy, double slotW, double slotH, double tol)
            => boxMinX < slotCx - slotW / 2 - tol || boxMaxX > slotCx + slotW / 2 + tol
            || boxMinY < slotCy - slotH / 2 - tol || boxMaxY > slotCy + slotH / 2 + tol;

        internal static int RoundUpToStandardScale(double v)
        {
            if (v <= 1) return 1;
            foreach (var s in StandardScales) if (s >= v - 1e-9) return s;
            return (int)(Math.Ceiling(v / 1000.0) * 1000);
        }

        /// <summary>
        /// DTW-150 — the scale a view placed into a slot should end up at.
        ///
        /// <paramref name="requiredScale"/> is the 1:N at which the view's extent
        /// just fits the slot.
        /// <paramref name="typeScale"/> is the drawing type's own scale (0 = none,
        /// use <paramref name="currentScale"/>). <paramref name="scaleHint"/> is
        /// the family slot's floor.
        ///
        /// Fit-to-slot only ever COARSENS: a small plan on a 1:100 type stays at
        /// 1:100, it does not become 1:20 because it would fit. It used to replace
        /// the type's scale in both directions.
        /// </summary>
        internal static int Decide(double requiredScale, int typeScale, int currentScale, int? scaleHint, out bool coarsened)
        {
            int baseline = typeScale > 0 ? typeScale : (currentScale > 0 ? currentScale : 100);
            int fit = RoundUpToStandardScale(requiredScale);
            int target = Math.Max(fit, baseline);
            if (scaleHint.HasValue && scaleHint.Value > 0) target = Math.Max(target, scaleHint.Value);
            coarsened = target > baseline;
            return target;
        }
    }
}
