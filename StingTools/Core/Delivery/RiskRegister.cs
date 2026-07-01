// ══════════════════════════════════════════════════════════════════════════
//  RiskRegister.cs — project/delivery risk register model + scoring. PM-8.
//
//  The audit (§6 PM-8) called the risk register "the clearest missing PM
//  primitive" — only a narrow Lightning-protection risk model existed. This is
//  the generic delivery risk register: a 5×5 likelihood × impact matrix with RAG
//  banding, residual (post-mitigation) scoring and a portfolio roll-up. A risk
//  can be anchored to a Revit element or zone (the thin Revit hook lives at the
//  command layer and reuses the issue/SLA/audit machinery).
//
//  Scoring (standard 5×5): score = likelihood × impact ∈ [1,25].
//    Green  ≤ 4  · Amber 5–9  · Red ≥ 10  (PMI / ISO 31000-style bands).
//
//  Pure (no Revit / no I/O) — headless xUnit tests in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Delivery
{
    public enum RiskStatus { Open, Mitigating, Closed, Accepted }

    public class RiskItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";        // Design / Cost / Programme / H&S / Quality …
        public string Owner { get; set; } = "";

        public int Likelihood { get; set; } = 1;          // 1..5
        public int Impact { get; set; } = 1;              // 1..5
        public int ResidualLikelihood { get; set; }       // post-mitigation; 0 = "same as inherent"
        public int ResidualImpact { get; set; }

        public string Mitigation { get; set; } = "";
        public RiskStatus Status { get; set; } = RiskStatus.Open;

        // Optional anchor (the thin Revit hook).
        public long ElementId { get; set; } = -1;
        public string Zone { get; set; } = "";

        public string RaisedDate { get; set; } = "";
        public string ReviewDate { get; set; } = "";

        /// <summary>Inherent score = likelihood × impact, clamped to 1..25.</summary>
        public int InherentScore => Clamp(Likelihood) * Clamp(Impact);

        /// <summary>Residual score = residual L × residual I; falls back to the
        /// inherent score when no residual has been assessed.</summary>
        public int ResidualScore =>
            (ResidualLikelihood > 0 && ResidualImpact > 0)
                ? Clamp(ResidualLikelihood) * Clamp(ResidualImpact)
                : InherentScore;

        public string InherentBand => Band(InherentScore);
        public string ResidualBand => Band(ResidualScore);

        private static int Clamp(int v) => Math.Max(1, Math.Min(5, v));
        public static string Band(int score) => score >= 10 ? "Red" : score >= 5 ? "Amber" : "Green";
    }

    public class RiskSummary
    {
        public int Total { get; set; }
        public int OpenCount { get; set; }
        public int RedCount { get; set; }
        public int AmberCount { get; set; }
        public int GreenCount { get; set; }
        public int RedResidualCount { get; set; }
        public double AverageResidualScore { get; set; }
        /// <summary>Open risks ordered most-severe first (by residual score).</summary>
        public List<RiskItem> TopRisks { get; set; } = new List<RiskItem>();
    }

    public static class RiskRegister
    {
        public static RiskSummary Summarise(IEnumerable<RiskItem> risks, int topN = 10)
        {
            var list = (risks ?? Enumerable.Empty<RiskItem>()).Where(r => r != null).ToList();
            var open = list.Where(r => r.Status == RiskStatus.Open || r.Status == RiskStatus.Mitigating).ToList();

            var s = new RiskSummary
            {
                Total = list.Count,
                OpenCount = open.Count,
                // RAG counts use the residual band — the live exposure after mitigation.
                RedCount = list.Count(r => r.ResidualBand == "Red"),
                AmberCount = list.Count(r => r.ResidualBand == "Amber"),
                GreenCount = list.Count(r => r.ResidualBand == "Green"),
                RedResidualCount = open.Count(r => r.ResidualBand == "Red"),
                AverageResidualScore = open.Count > 0
                    ? Math.Round(open.Average(r => r.ResidualScore), 2) : 0,
                TopRisks = open.OrderByDescending(r => r.ResidualScore)
                               .ThenByDescending(r => r.InherentScore)
                               .Take(Math.Max(0, topN)).ToList(),
            };
            return s;
        }
    }
}
