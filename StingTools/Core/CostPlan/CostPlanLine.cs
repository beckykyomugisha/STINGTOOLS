// ══════════════════════════════════════════════════════════════════════════
//  CostPlanLine.cs — Element × benchmark × low/likely/high.
//
//  Each line ties an NRM1 element code to a £/m² GIFA benchmark with
//  uncertainty bands. Three-point estimating (PERT-style) — low/likely/
//  high — supports Monte-Carlo cost forecasting in later phases.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.CostPlan
{
    public class CostPlanLine
    {
        /// <summary>NRM1 element code — e.g. "1", "2.1", "5.6".</summary>
        public string ElementCode { get; set; } = "";

        public string ElementName { get; set; } = "";

        /// <summary>Building-type key the benchmark applies to (e.g. "office_cat_B").</summary>
        public string BuildingType { get; set; } = "";

        public string Unit { get; set; } = "m² GIFA";

        /// <summary>Optimistic / lower bound (£/unit).</summary>
        public double LowRate { get; set; }

        /// <summary>Most-likely (£/unit) — the headline benchmark.</summary>
        public double LikelyRate { get; set; }

        /// <summary>Pessimistic / upper bound (£/unit).</summary>
        public double HighRate { get; set; }

        /// <summary>Source of the benchmark (e.g. "BCIS Q1-2026", "internal").</summary>
        public string Source { get; set; } = "";

        /// <summary>Override quantity in GIFA m² — defaults to the plan target.</summary>
        public double Quantity { get; set; }

        public double TotalLow => Math.Round(Quantity * LowRate, 0);
        public double TotalLikely => Math.Round(Quantity * LikelyRate, 0);
        public double TotalHigh => Math.Round(Quantity * HighRate, 0);

        /// <summary>PERT expected value — (low + 4*likely + high) / 6.</summary>
        public double TotalExpected => Math.Round((TotalLow + 4 * TotalLikely + TotalHigh) / 6.0, 0);

        /// <summary>Per-element user override note (free text).</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// A complete NRM1 cost plan for one project. Persisted as JSON
    /// alongside the BOQ snapshots under <project>/_bim_manager/cost_plans/.
    /// </summary>
    public class CostPlanDocument
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string ProjectName { get; set; } = "";
        public string BuildingType { get; set; } = "";
        public string Label { get; set; } = "Concept";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = Environment.UserName ?? "";
        public string Currency { get; set; } = "GBP";

        /// <summary>Target gross internal floor area in m².</summary>
        public double GifaM2 { get; set; }

        /// <summary>Risk allowance as a percentage of the subtotal.</summary>
        public double RiskAllowancePct { get; set; } = 7.5;

        /// <summary>Inflation allowance (% of subtotal at tender).</summary>
        public double InflationAllowancePct { get; set; } = 4.0;

        /// <summary>Design contingency (% of subtotal).</summary>
        public double DesignContingencyPct { get; set; } = 5.0;

        public List<CostPlanLine> Lines { get; set; } = new List<CostPlanLine>();

        public double SubtotalLow => Lines.Sum(l => l.TotalLow);
        public double SubtotalLikely => Lines.Sum(l => l.TotalLikely);
        public double SubtotalHigh => Lines.Sum(l => l.TotalHigh);
        public double SubtotalExpected => Lines.Sum(l => l.TotalExpected);

        public double GrandTotalLow =>
            Math.Round(SubtotalLow * (1 + (RiskAllowancePct + InflationAllowancePct + DesignContingencyPct) / 100.0), 0);
        public double GrandTotalLikely =>
            Math.Round(SubtotalLikely * (1 + (RiskAllowancePct + InflationAllowancePct + DesignContingencyPct) / 100.0), 0);
        public double GrandTotalHigh =>
            Math.Round(SubtotalHigh * (1 + (RiskAllowancePct + InflationAllowancePct + DesignContingencyPct) / 100.0), 0);

        /// <summary>£/m² GIFA at the likely rate — the headline cost-plan benchmark.</summary>
        public double CostPerSqmLikely => GifaM2 > 0 ? Math.Round(GrandTotalLikely / GifaM2, 0) : 0;
    }
}
