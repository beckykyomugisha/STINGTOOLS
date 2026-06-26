// StingTools — Savings-% arithmetic + display (Phase 195, WS F).
//
// Pure POCO / Revit-free + unit-tested. One guarded place for the
// (baseline − design)/baseline savings %, so every estimator (energy / water /
// materials) is protected against NaN / divide-by-zero, and an over-baseline
// design reads clearly ("design X vs baseline Y") instead of a confusing
// large-negative percentage.

using System;

namespace StingTools.Core.Sustainability
{
    public static class SustainSavings
    {
        /// <summary>(baseline − design)/baseline × 100. Returns 0 ("not meaningful")
        /// when the baseline is ≤ 0 or any input is NaN/∞. Negative ⇒ over baseline.</summary>
        public static double Pct(double baseline, double design)
        {
            if (double.IsNaN(baseline) || double.IsInfinity(baseline) ||
                double.IsNaN(design)   || double.IsInfinity(design)   || baseline <= 0)
                return 0;
            return (baseline - design) / baseline * 100.0;
        }

        /// <summary>True when a meaningful savings % can be computed (finite inputs,
        /// positive baseline).</summary>
        public static bool IsMeaningful(double baseline, double design)
            => !(double.IsNaN(baseline) || double.IsInfinity(baseline) ||
                 double.IsNaN(design)   || double.IsInfinity(design)   || baseline <= 0);

        /// <summary>Clear one-line description that distinguishes "not computable"
        /// from a real reduction vs an over-baseline design — so a large negative
        /// savings reads plainly rather than as "−812%".</summary>
        public static string Describe(double baseline, double design, string unit = "")
        {
            if (!IsMeaningful(baseline, design))
                return "— baseline not available";
            string u = string.IsNullOrEmpty(unit) ? "" : " " + unit;
            double pct = (baseline - design) / baseline * 100.0;
            if (design <= baseline)
                return $"{pct:0.#}% reduction (design {design:0.#}{u} vs baseline {baseline:0.#}{u})";
            return $"over baseline — design {design:0.#}{u} vs baseline {baseline:0.#}{u} (+{-pct:0.#}%)";
        }
    }
}
