// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleModel.cs — MAT-SCHED pure data model.
//  No Revit API, no file I/O, no WPF. Every other MaterialSchedule file
//  depends on these types.
//
//  Money is DERIVED, never stored: the PATMAC reference sample shipped a row
//  reading "1 Roll x 300,000 = 150,000", and a stored Amount field is what
//  makes that representable. Deriving it removes the defect class instead of
//  detecting it.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One buyable commodity within one construction stage.</summary>
    public sealed class MaterialCommodity
    {
        public string CommodityKey = "";     // canonical merge key — "cement"
        public string Description = "";      // "Cement (OPC 42.5N)"
        public string Spec = "";             // "T16" / "8\" hollow" / "1000g"
        public string SupplierUnit = "";     // "Bags" / "Trips (Sino Truck)" / "No."
        public double NetQuantity;           // supplier units, PRE-wastage
        public double WastagePct;            // visible; never folded into the quantity
        public double OrderQuantity;         // post-wastage, rounded per the unit rule
        public double RateUGX;
        public string RateSource = "";       // "baseline" / "project" / "unpriced"
        public List<string> TraceRefs = new List<string>();

        /// <summary>Derived — see the file header. Rounded to whole UGX.</summary>
        public double AmountUGX => Math.Round(OrderQuantity * RateUGX, 0);

        /// <summary>True when no rate could be resolved for this commodity.</summary>
        public bool IsUnpriced => RateUGX <= 0;
    }

    /// <summary>
    /// A stage's labour lump. QS-entered: the BOQ's LabourUGX rate split is nulled
    /// on manual override and on modal-rate aggregation, so deriving from it would
    /// silently under-report. The derived figure is offered as a SUGGESTION only,
    /// and only when every contributing row carries a split.
    /// </summary>
    public sealed class LabourLine
    {
        public string Description = "Labour";
        public double AmountUGX;
        public double? SuggestedUGX;
        public string SuggestionBasis = "";
    }

    /// <summary>A provisional sum carried from the BOQ manual store.</summary>
    public sealed class ProvisionalSumLine
    {
        public string Description = "";
        public double AmountUGX;
        public string SourceRef = "";
    }

    /// <summary>One lettered section of the schedule.</summary>
    public sealed class StageSection
    {
        public string StageId = "";
        public string Letter = "";           // assigned by StageMapper — never authored
        public string Title = "";
        public string Preamble = "";
        public List<MaterialCommodity> Commodities = new List<MaterialCommodity>();
        public List<LabourLine> Labour = new List<LabourLine>();
        public List<ProvisionalSumLine> ProvisionalSums = new List<ProvisionalSumLine>();

        public double SubTotalUGX =>
            Commodities.Sum(c => c.AmountUGX)
            + Labour.Sum(l => l.AmountUGX)
            + ProvisionalSums.Sum(p => p.AmountUGX);
    }

    public sealed class MaterialScheduleOptions
    {
        /// <summary>Renderer flag only — the engine computes identically either way.</summary>
        public bool ShowPrices = true;
        public double ContingencyPct = 5.0;
    }

    public sealed class ReconciliationIssue
    {
        public string Code = "";             // "R1".."R4"
        public string Message = "";
        public string StageId = "";
        public string CommodityKey = "";
    }

    public sealed class MaterialScheduleReconciliation
    {
        public List<ReconciliationIssue> Issues = new List<ReconciliationIssue>();
        public bool IsClean => Issues.Count == 0;
    }

    public sealed class MaterialScheduleDocument
    {
        public string ProjectName = "";
        public string ProjectCode = "";
        public DateTime GeneratedUtc = DateTime.UtcNow;
        public string Currency = "UGX";
        public List<StageSection> Stages = new List<StageSection>();
        public MaterialScheduleOptions Options = new MaterialScheduleOptions();
        public MaterialScheduleReconciliation Reconciliation = new MaterialScheduleReconciliation();

        public double WorksSubtotalUGX => Stages.Sum(s => s.SubTotalUGX);
        public double ContingencyUGX => Math.Round(WorksSubtotalUGX * Options.ContingencyPct / 100.0, 0);
        public double GrandTotalUGX => WorksSubtotalUGX + ContingencyUGX;

        /// <summary>
        /// The summary is PROJECTED from the body, never authored alongside it —
        /// so the PATMAC defects D1 (duplicate letters) and D2 (summary order not
        /// matching body order) are unrepresentable.
        /// </summary>
        public IEnumerable<(string Letter, string Title, double SubTotalUGX)> Summary =>
            Stages.Select(s => (s.Letter, s.Title, s.SubTotalUGX));
    }
}
