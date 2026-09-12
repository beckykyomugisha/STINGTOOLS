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

        /// <summary>
        /// The Revit categories this commodity was measured FROM, sorted.
        ///
        /// Aggregation groups by commodity key and had been dropping this, so a
        /// row reading "Generic - 225mm, 610 m2, unpriced" gave a QS no way to
        /// tell it was a ROOF — the one fact needed to price it. Cement comes
        /// from several categories at once, so it is a set rather than a single
        /// value; collapsing it to "the first one" would name a source that is
        /// only part of the truth.
        /// </summary>
        public List<string> Categories = new List<string>();

        /// <summary>
        /// The model TYPE names behind this row, sorted, capped at a few.
        ///
        /// For an unmatched row the key IS the type name and this adds nothing;
        /// for cement it says which walls and slabs produced it, which is what
        /// makes a rate checkable rather than merely enterable.
        /// </summary>
        public List<string> TypeNames = new List<string>();

        /// <summary>
        /// True when a supplier-unit rule matched this row's CATEGORY but not its
        /// type, so the quantity stayed in measured units rather than being
        /// converted on a guess. Surfaced by reconciler rule R5 — a wrong trade
        /// quantity is worse than an honest measured one.
        /// </summary>
        public bool ConversionBlocked;
        public string ConversionNote = "";

        /// <summary>The constituent kind this row came from, or "" for a row that
        /// carried none. Used to decide whether the row is an intermediate.</summary>
        public string SourceKind = "";

        /// <summary>
        /// True when this row is an INTERMEDIATE MEASURE whose purchasable
        /// constituents are separately listed in the same document — blockwork
        /// area against the block count derived from it, mortar volume against
        /// its cement and sand.
        ///
        /// The first real export listed all four alongside their own children
        /// and flagged each with R3 ("has no rate. It will total zero"), which
        /// reads as an instruction to price them. Rating 175 m2 of blockwork
        /// next to 2,292 blocks pays for the same wall twice.
        ///
        /// A memorandum row keeps its quantity — it is the audit trail that
        /// makes the derived counts checkable — but it can never carry money:
        /// AmountUGX is hard-zero, so the double-count is unrepresentable
        /// rather than merely discouraged.
        /// </summary>
        public bool IsMemorandum;
        public string MemorandumNote = "";

        /// <summary>Derived — see the file header. Rounded to whole UGX.</summary>
        public double AmountUGX => IsMemorandum ? 0 : Math.Round(OrderQuantity * RateUGX, 0);

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

        /// <summary>
        /// Everything the export needed to say about itself: the compound-takeoff
        /// gate, excluded rows, the tiling and room-finish scans, site-tool
        /// heuristics.
        ///
        /// These used to exist ONLY in the post-export dialog. The tiling scan
        /// was added precisely so that "no tiling appeared" stopped being
        /// ambiguous — and then its answer vanished the moment the dialog was
        /// closed, so a workbook reviewed later could not say why it looked the
        /// way it did. A deliverable has to carry its own explanation.
        /// </summary>
        /// <summary>
        /// The project rate file this run resolved, absolute.
        ///
        /// Carried on the document because the reconciler is Revit-free and
        /// cannot ask StingPaths. It matters: every message used to name
        /// "_BIM_COORD/commodity_rates.csv", which is the ALIAS. The live
        /// folder is _data/coord, and a user following the text literally
        /// landed in a stale legacy folder and concluded the file was missing.
        /// </summary>
        public string ProjectRatesPath = "";

        /// <summary>
        /// Measured source per (commodityKey, typeName), for the by-type sheet.
        /// Carried on the document because the breakdown must be computed from
        /// the SAME numerator the order lines were, not from a second pass.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> SourceByType =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        public List<string> Warnings = new List<string>();

        /// <summary>MAT-SCHED-8 — model rows dropped as not-a-material, by category.
        /// Reported so an exclusion is a stated decision, never a silent loss.</summary>
        public Dictionary<string, int> ExcludedByCategory = new Dictionary<string, int>();
        public int ExcludedRowCount => ExcludedByCategory.Values.Sum();

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
