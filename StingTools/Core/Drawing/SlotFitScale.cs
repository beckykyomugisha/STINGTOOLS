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
