// ══════════════════════════════════════════════════════════════════════════
//  OwnerKpiHealth.cs — the Owner KPI model-health score, and what it is based on.
//
//  KUT deep review ACC-10. The score was 40% compliance + 25% clash + 20% warnings
//  + 15% stale, with "no clash run" counted as 0 open clashes — so a model never
//  clash-tested earned the full 25 clash points, and an empty model scored on the
//  other terms. Now: no clash run → the clash term is left out and the rest
//  re-weighted, and the basis says so; nothing in scope → no score at all.
//
//  Revit-free (unit-tested in StingTools.Tags.Tests).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Commands.Kpi
{
    public static class OwnerKpiHealth
    {
        // Normalisation caps (documented, tunable).
        public const double ClashCap = 200.0, WarningCap = 500.0, StaleCap = 100.0;
        public const double WCompliance = 0.40, WClash = 0.25, WWarnings = 0.20, WStale = 0.15;

        public sealed class Result
        {
            /// <summary>0–100, or null when there is nothing to score.</summary>
            public double? Score;
            public string Basis = "";
        }

        public static Result Compute(int totalElements, double compliancePct, bool clashRunFound,
                                     int openClashes, int warnings, int stale)
        {
            if (totalElements <= 0)
                return new Result { Score = null, Basis = "n/a — no taggable elements in the model" };

            double Clean(double v, double cap) => 100.0 * (1.0 - Math.Min(1.0, Math.Max(0, v) / cap));

            var terms = new List<(double w, double v, string name)>
            {
                (WCompliance, compliancePct, "compliance"),
                (WWarnings, Clean(warnings, WarningCap), "warnings"),
                (WStale, Clean(stale, StaleCap), "stale"),
            };
            if (clashRunFound) terms.Insert(1, (WClash, Clean(openClashes, ClashCap), "clash"));

            double wSum = 0, acc = 0;
            foreach (var t in terms) { wSum += t.w; acc += t.w * t.v; }
            double score = Math.Round(Math.Max(0, Math.Min(100, acc / wSum)), 1);

            string basis = clashRunFound
                ? "compliance 40% · clash 25% · warnings 20% · stale 15%"
                : "NO STING clash run — clash term excluded; compliance 53% · warnings 27% · stale 20%";
            return new Result { Score = score, Basis = basis };
        }
    }
}
