// ══════════════════════════════════════════════════════════════════════════
//  BOQModels.cs — Phase 2 of the BOQ & Cost Manager.
//  Pure data model. No Revit API calls, no file I/O, no WPF references.
//  Every other BOQ file depends on these types.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.BOQ
{
    public enum BOQRowSource
    {
        Model,
        Manual,
        ProvisionalSum,
        Dayworks,       // P3.1 — daywork / time-and-material rows
        PCSum          // P3.1 — prime-cost sum (named supplier allowance)
    }

    /// <summary>P3.1 — shared source label / parse helpers so the export label,
    /// import parser and panel agree on one spelling per source.</summary>
    public static class BoqSourceUtil
    {
        public static string Label(BOQRowSource s)
        {
            switch (s)
            {
                case BOQRowSource.Manual:         return "Manual";
                case BOQRowSource.ProvisionalSum: return "Provisional Sum";
                case BOQRowSource.Dayworks:       return "Dayworks";
                case BOQRowSource.PCSum:          return "PC Sum";
                default:                          return "Model";
            }
        }

        /// <summary>Parse a source label (case-insensitive, substring-tolerant).
        /// Returns Model for unrecognised input.</summary>
        public static BOQRowSource Parse(string label)
        {
            string l = (label ?? "").Trim().ToLowerInvariant();
            if (l.Contains("provisional")) return BOQRowSource.ProvisionalSum;
            if (l.Contains("daywork"))     return BOQRowSource.Dayworks;
            if (l.Contains("pc") || l.Contains("prime cost")) return BOQRowSource.PCSum;
            if (l.Contains("manual"))      return BOQRowSource.Manual;
            return BOQRowSource.Model;
        }

        /// <summary>True for QS-authored rows that must never be overwritten by
        /// a model re-takeoff (everything except Model).</summary>
        public static bool IsQsAuthored(BOQRowSource s) => s != BOQRowSource.Model;
    }

    /// <summary>
    /// P2.2 — how a BOQ is grouped into sections. NRM2 supports both elemental
    /// (work-section) and locational (level / zone) bills; this enum selects
    /// the strategy used by BOQCostManager.GroupIntoSections (and feeds the
    /// aggregation key so similar items collapse within the active dimension).
    /// </summary>
    public enum BoqGroupingMode
    {
        WorkSection,            // by NRM2 § + discipline (default — elemental bill)
        Level,                  // by building level (flat locational bill)
        Zone,                   // by ASS_ZONE_TXT zone
        LevelThenWorkSection,   // by level, then NRM2 § within each level
        Location,              // by room / spatial location code
        SourceModel,           // by host vs each linked model
        Wbs,                   // Phase 2E — by user-defined Work Breakdown Structure code
        Cbs                    // Phase 2E — by user-defined Cost Breakdown Structure code
    }

    public enum BOQChangeType
    {
        NoChange,
        RateRevised,
        QtyChanged,
        NewItem,
        ItemRemoved,
        PSAdded,
        SourcePromoted
    }

    // ── BoqMarkupBreakdown / BoqTotals ─────────────────────────────────────

    /// <summary>WP1 — the single canonical markup waterfall for a BOQ. Every
    /// component carries its absolute value so a caller can render the Grand
    /// Summary without re-deriving any base.</summary>
    public struct BoqMarkupBreakdown
    {
        public double Works;        // Σ measured-works line totals (ex any markup)
        public double Prelims;      // preliminaries (itemised Σ or % of works)
        public double Overhead;     // main-contractor OH&P
        public double Contingency;  // design/construction contingency
        public double NetExVat;     // Works + Prelims + Overhead + Contingency (Contract Sum ex-VAT)
        public double Vat;          // VAT on NetExVat
        public double GrandTotal;   // NetExVat + Vat (Contract Sum incl VAT) — rounded
    }

    /// <summary>
    /// WP1 — the ONE markup model. Defines each component's base so the panel
    /// KPI, both exporters' Contract Sum, the snapshot list, the drift hash and
    /// the budget-variance write-back all reconcile to a single number.
    ///
    /// Canonical convention (documented):
    ///   1. Works subtotal  W   = Σ line totals
    ///   2. Preliminaries   P   = itemised schedule Σ, or W × prelim%   (base: works)
    ///   3. Overhead+Profit O   = (W + P) × ohp%                        (base: works + prelims)
    ///   4. Contingency     C   = (W + P + O) × cont%                   (base: works + prelims + OH&P)
    ///   5. Net ex-VAT          = W + P + O + C  (Contract Sum exclusive of tax)
    ///   6. VAT             V   = Net × vat%
    ///   7. Contract Sum        = Net + V        (the canonical GrandTotal, incl VAT)
    ///
    /// Contingency is applied *after* prelims and OH&P per standard practice.
    /// Per-element rate-level OH&P (the opt-in ES override loaded rate, i.e. a
    /// subcontractor's already-loaded quote) is a DIFFERENT layer baked into the
    /// net unit rate — it is part of <c>Works</c>, not re-applied here, so the
    /// document OH&P never double-fires against the project markup.
    /// </summary>
    public static class BoqTotals
    {
        public static BoqMarkupBreakdown Compute(double works, double prelimsAbsolute,
            double overheadPct, double contingencyPct, double vatPct)
        {
            var b = new BoqMarkupBreakdown
            {
                Works = works,
                Prelims = prelimsAbsolute
            };
            double sub1 = works + b.Prelims;
            b.Overhead = sub1 * (overheadPct / 100.0);
            double sub2 = sub1 + b.Overhead;
            b.Contingency = sub2 * (contingencyPct / 100.0);
            b.NetExVat = sub2 + b.Contingency;
            b.Vat = b.NetExVat * (vatPct / 100.0);
            b.GrandTotal = Math.Round(b.NetExVat + b.Vat, 0);
            return b;
        }
    }

    // ── BOQLineItem ────────────────────────────────────────────────────────

    /// <summary>
    /// One row in a BOQ. Built from a modeled element, a manual entry, or a
    /// provisional sum. See BOQCostManager.BuildBOQDocument for the modeled
    /// pipeline and BOQManualStore.ManualRows for the manual/PS source.
    /// </summary>
    public class BOQLineItem
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string NRM2Section;          // "14"
        public string Category;             // Revit category display name
        public string Discipline;           // A/S/M/E/P/FP/PS
        public string ItemName;
        public string FamilyName;
        public string TypeName;
        public double Quantity;             // NET measured quantity — the value used for cost
        public string Unit;                 // "m²" / "m³" / "m" / "each" / "item" / "kg" / "tonne"

        // ── Phase 2A — NRM2 rules-based measurement audit trail ─────────────
        // The gross→net derivation so a QS can see exactly how a modelled
        // geometry became a measured quantity. GrossQuantity is the raw Revit
        // geometry (pre-deduction, pre-wastage); DeductionQuantity is the
        // opening/void area removed per the active standard's measurement rules
        // (NRM2 / CESMM4); WastageQuantity is the cutting/offcut/lap allowance
        // added as a distinct, visible step (never folded silently into the
        // rate). Quantity = GrossQuantity − DeductionQuantity + WastageQuantity.
        // MeasurementNote is the human-readable one-liner (e.g.
        // "Gross 43.0 m² − openings 5.2 m² + wastage 5% = 39.8 m²").
        // GrossQuantity == 0 on a model row means "not measured separately"
        // (manual/PS rows, or pre-2A snapshots) — readers fall back to Quantity.
        public double GrossQuantity;
        public double DeductionQuantity;
        public double WastageQuantity;
        public string MeasurementNote;
        public double RateUGX;
        public double RateUSD;
        public double EmbodiedCarbonKg;     // kgCO2e
        public double LifecycleCostUGX;     // capital + maintenance NPV 25yr
        public string ResolvedNRM2Paragraph;
        public string BOQLineRef;           // e.g. "14.3.2"
        public string Note;
        public string SourceModel;          // "" / null = host; else the linked model Title (Group by Source model)
        public BOQRowSource Source;
        public string SnapshotRef;
        public long RevitElementId = -1;    // -1 for manual/PS rows
        public string UniqueId;             // Revit UniqueId (cross-doc, survives Revit save/reopen)

        /// <summary>
        /// INT-0 — the canonical 22-char IFC GlobalId, derived deterministically
        /// from <see cref="UniqueId"/> via the one shared resolver
        /// (<see cref="StingTools.IfcResults.IfcGuidEncoder"/>). This is the single
        /// cross-platform join key: COBie Component external identifier, Speckle
        /// applicationId, the server's ExternalElementMapping, and the priced-BOQ
        /// round-trip all key off it. Empty for manual / provisional-sum rows with
        /// no modelled element. The COBie writers use the same encoder on the same
        /// UniqueId, so a BOQ row and the COBie Component for one element always
        /// carry an identical GlobalId.
        ///
        /// STAGED FOLLOW-UP (INT-0 server audit): the server-ingest path
        /// (<c>IFC_PushModelCommand</c>) keys ExternalElementMapping off the stored
        /// <c>IFC_GLOBAL_ID_TXT</c> param, which <c>StabilizeIfcGuidsCommand</c>
        /// fills from Revit's built-in IfcGUID. That equals this encoder value in
        /// the default case, but DIVERGES when a user sets an explicit IFC-GUID
        /// override or the element was never IFC-exported. To unify all four
        /// surfaces under overrides, BOQ + COBie should prefer a stored
        /// IFC_GLOBAL_ID_TXT when present and fall back to the encoder — threaded
        /// through BOQ build into this field (the POCO has no live Element here).
        /// </summary>
        public string IfcGlobalId =>
            string.IsNullOrEmpty(UniqueId)
                ? ""
                : StingTools.IfcResults.IfcGuidEncoder.FromRevitUniqueId(UniqueId);
        public string Level;
        public string Location;             // room name or spatial code
        public string Zone;                 // ASS_ZONE_TXT — P2.2 zone grouping key

        // ── Phase 2E — user-defined WBS / CBS. Assigned by the WBS map
        // (boq_wbs_map.json) from element attributes, with a fallback to the
        // linked 4D ScheduleTask's WBS so the programme and the bill share one
        // work-breakdown structure. Empty when no rule matches. Drives the
        // Wbs / Cbs grouping modes + the ERP export columns.
        public string WbsCode;
        public string CbsCode;
        public DateTime LastCosted = DateTime.UtcNow;
        public string RateSource;           // "CSV" | "COBie" | "Default" | "Manual" | "Override" | "Carbon" | "Interpolated" | "QS"
        public int RateConfidence = 60;     // 0-100 (Phase 11A)
        public int SortOrder;               // stable ordering within a section

        // ── G4 — optional labour / plant / material rate split (per-unit, same
        // currency/unit as RateUGX). Null when the rate source carries no split,
        // so the rate stays a single number (no regression). Totals derive by
        // ×Quantity.
        public double? LabourUGX;
        public double? PlantUGX;
        public double? MaterialUGX;
        public bool HasRateSplit => LabourUGX.HasValue || PlantUGX.HasValue || MaterialUGX.HasValue;
        public double LabourTotalUGX => (LabourUGX ?? 0) * Quantity;
        public double PlantTotalUGX => (PlantUGX ?? 0) * Quantity;
        public double MaterialTotalUGX => (MaterialUGX ?? 0) * Quantity;

        // ── G5 — carbon factor provenance + data-quality. CarbonSource is the
        // resolver source ("epd:…" / "material-param" / "material-lookup-csv" /
        // "carbon-factors-csv" / "none"); CarbonQuality is the band the QS reads
        // (Verified-EPD / Database / Missing); CarbonMaterial is the primary
        // material (drives the carbon-gap report).
        public string CarbonSource;
        public string CarbonQuality;
        public string CarbonMaterial;

        // ── P1 aggregation ─────────────────────────────────────────────────
        // When several near-identical modelled elements collapse into one BOQ
        // row, SimilarCount holds the element count and ConstituentElementIds
        // every element id in the group (for drill-down / back-selection in
        // Revit). RevitElementId/UniqueId remain the representative element.
        // Defaults keep old snapshots deserialising unchanged (count = 1,
        // empty list, null key — i.e. an un-aggregated single-element row).
        public int SimilarCount = 1;
        public List<long> ConstituentElementIds = new List<long>();
        public string AggregationKey;       // grouping key used to collapse the row (debug/export)

        public double TotalUGX => Math.Round(Quantity * RateUGX, 0);
        public double TotalUSD => Math.Round(Quantity * RateUSD, 2);

        /// <summary>
        /// Deep clone. Used by snapshot comparison (we never mutate a loaded snapshot).
        /// </summary>
        public BOQLineItem Clone()
        {
            return new BOQLineItem
            {
                Id = this.Id,
                NRM2Section = this.NRM2Section,
                Category = this.Category,
                Discipline = this.Discipline,
                ItemName = this.ItemName,
                FamilyName = this.FamilyName,
                TypeName = this.TypeName,
                Quantity = this.Quantity,
                Unit = this.Unit,
                GrossQuantity = this.GrossQuantity,
                DeductionQuantity = this.DeductionQuantity,
                WastageQuantity = this.WastageQuantity,
                MeasurementNote = this.MeasurementNote,
                RateUGX = this.RateUGX,
                RateUSD = this.RateUSD,
                EmbodiedCarbonKg = this.EmbodiedCarbonKg,
                LifecycleCostUGX = this.LifecycleCostUGX,
                ResolvedNRM2Paragraph = this.ResolvedNRM2Paragraph,
                BOQLineRef = this.BOQLineRef,
                Note = this.Note,
                SourceModel = this.SourceModel,
                Source = this.Source,
                SnapshotRef = this.SnapshotRef,
                RevitElementId = this.RevitElementId,
                UniqueId = this.UniqueId,
                Level = this.Level,
                Location = this.Location,
                Zone = this.Zone,
                WbsCode = this.WbsCode,
                CbsCode = this.CbsCode,
                LastCosted = this.LastCosted,
                RateSource = this.RateSource,
                RateConfidence = this.RateConfidence,
                SortOrder = this.SortOrder,
                LabourUGX = this.LabourUGX,
                PlantUGX = this.PlantUGX,
                MaterialUGX = this.MaterialUGX,
                CarbonSource = this.CarbonSource,
                CarbonQuality = this.CarbonQuality,
                CarbonMaterial = this.CarbonMaterial,
                SimilarCount = this.SimilarCount,
                ConstituentElementIds = this.ConstituentElementIds != null
                    ? new List<long>(this.ConstituentElementIds) : new List<long>(),
                AggregationKey = this.AggregationKey
            };
        }
    }

    // ── BOQSection ─────────────────────────────────────────────────────────

    /// <summary>
    /// A group of line items under one NRM2 section heading. Totals are derived
    /// so a caller can re-assign individual item rates without re-materialising
    /// the parent section.
    /// </summary>
    public class BOQSection
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string NRM2Section;
        public string Name;
        public string Discipline;
        public BOQRowSource DefaultSource = BOQRowSource.Model;
        public List<BOQLineItem> Items = new List<BOQLineItem>();

        public double TotalUGX => Items.Sum(i => i.TotalUGX);
        public double TotalUSD => Items.Sum(i => i.TotalUSD);
        public double ModeledUGX => Items.Where(i => i.Source == BOQRowSource.Model).Sum(i => i.TotalUGX);
        public double ProvUGX => Items.Where(i => i.Source != BOQRowSource.Model).Sum(i => i.TotalUGX);
        public double TotalCarbonKg => Items.Sum(i => i.EmbodiedCarbonKg);
    }

    // ── BOQDocument ────────────────────────────────────────────────────────

    /// <summary>
    /// The complete BOQ for a project at a point in time. Produced by
    /// BOQCostManager.BuildBOQDocument. Serialised to JSON snapshots under
    /// {bimDir}/boq_snapshot_{type}_{label}_{yyyyMMdd_HHmmss}.json.
    /// </summary>
    public class BOQDocument
    {
        public string ProjectName;
        public string DocumentTitle;
        public string SnapshotLabel;
        public DateTime SnapshotDate = DateTime.UtcNow;
        public string SnapshotType;         // "DD" | "Stage" | "Weekly" | "Handover" | "Manual" | "Live"
        /// <summary>WP-FIX — the client's cost limit, captured VAT-INCLUSIVE (the
        /// all-in funding figure), so it compares like-with-like against the
        /// canonical VAT-inclusive <see cref="GrandTotalUGX"/>. Both
        /// BudgetVarianceUGX and BudgetCoveragePct use this same basis.</summary>
        public double ProjectBudgetUGX;
        public double PrelimPct = 12.0;
        public double ContingencyPct = 10.0;
        public double OverheadPct = 8.0;
        public double VatPct = 18.0;                    // WP1 — VAT on the BOQ document (Uganda standard rate)
        public string Currency = "UGX";
        public double ExchangeRateUgxPerUsd = 3700.0;   // Phase 11E multi-currency
        /// <summary>
        /// Measurement standard ID — "nrm2" / "cesmm4" / "pomi" / "icms3" /
        /// "mmhw". Defaults to NRM2 (UK Building Works). Phase 184h / P6.
        /// </summary>
        public string MeasurementStandardId = "nrm2";

        // ── G3 — optional built-up preliminaries schedule ───────────────────
        // When PrelimsItemised is true the grand total uses the itemised prelim
        // total (PrelimsItemisedUGX) instead of the flat PrelimPct. Loaded by
        // BuildBOQDocument from BoqPrelimsStore; flat % stays the default so
        // nothing regresses (Enabled defaults false → these fields stay inert).
        public bool PrelimsItemised = false;
        public List<BoqPrelimLine> PrelimLines = new List<BoqPrelimLine>();

        public List<BOQSection> Sections = new List<BOQSection>();

        public List<BOQLineItem> AllItems => Sections.SelectMany(s => s.Items).ToList();
        public double ModeledTotalUGX => AllItems.Where(i => i.Source == BOQRowSource.Model).Sum(i => i.TotalUGX);
        public double ProvTotalUGX => AllItems.Where(i => i.Source != BOQRowSource.Model).Sum(i => i.TotalUGX);
        public double SubtotalUGX => AllItems.Sum(i => i.TotalUGX);

        /// <summary>Σ of the itemised prelim lines resolved against the works subtotal.</summary>
        public double PrelimsItemisedUGX => PrelimLines?.Sum(l => l.AmountFor(SubtotalUGX)) ?? 0;

        /// <summary>
        /// The preliminaries contribution to the grand total — the itemised
        /// schedule total when active, else the flat PrelimPct of the works
        /// subtotal. Left unrounded in the flat case so GrandTotalUGX matches the
        /// historic single-round formula exactly (zero delivered-number change).
        /// </summary>
        public double PrelimContributionUGX =>
            PrelimsItemised ? PrelimsItemisedUGX : SubtotalUGX * PrelimPct / 100.0;

        /// <summary>
        /// WP1 — the single canonical markup waterfall (see <see cref="BoqTotals"/>).
        /// Replaces the old parallel "% × subtotal for everything, no VAT" formula.
        /// All component properties below and every external surface read from this.
        /// </summary>
        public BoqMarkupBreakdown Markup =>
            BoqTotals.Compute(SubtotalUGX, PrelimContributionUGX, OverheadPct, ContingencyPct, VatPct);

        public double OverheadProfitUGX => Markup.Overhead;
        public double ContingencyUGX    => Markup.Contingency;
        /// <summary>Contract Sum exclusive of VAT (Works + Prelims + OH&P + Contingency).</summary>
        public double NetTotalExVatUGX  => Markup.NetExVat;
        public double VatUGX            => Markup.Vat;

        /// <summary>The canonical, fully-marked-up Contract Sum INCLUSIVE of VAT.
        /// This is the one number the panel KPI, both exporters, the snapshot list,
        /// the budget variance and the server sync all agree on.</summary>
        public double GrandTotalUGX => Markup.GrandTotal;
        public double BudgetVarianceUGX => ProjectBudgetUGX > 0 ? ProjectBudgetUGX - GrandTotalUGX : 0;
        // WP-FIX — coverage now uses the VAT-inclusive GrandTotal (same basis as
        // the budget + the variance), not the bare works subtotal, so the two
        // budget metrics can no longer disagree on what they measure against.
        public double BudgetCoveragePct => ProjectBudgetUGX > 0 ? GrandTotalUGX / ProjectBudgetUGX * 100 : 0;
        public double TotalCarbonKg => AllItems.Sum(i => i.EmbodiedCarbonKg);
        public int ResolvedParagraphCount => AllItems.Count(i => !string.IsNullOrEmpty(i.ResolvedNRM2Paragraph));
        public double ParagraphCoveragePct => AllItems.Count > 0 ? 100.0 * ResolvedParagraphCount / AllItems.Count : 0;

        /// <summary>
        /// N+9 — Number of elements whose ASS_CST_STALE_BOOL = "1" flag was
        /// cleared during the latest BuildBOQDocument run. The BOQ dashboard
        /// surfaces this as a banner — confirms that material-change → stale
        /// → re-cost loop closed cleanly for those rows.
        /// </summary>
        public int StaleRowsRefreshed { get; set; }

        /// <summary>
        /// The average rate confidence (0-100) across all items. Used by
        /// BOQCostManager.ComputeBOQHealth (Phase 11C).
        /// </summary>
        public double AverageRateConfidence
            => AllItems.Count > 0 ? AllItems.Average(i => (double)i.RateConfidence) : 0;
    }

    // ── BOQSnapshotDiff ────────────────────────────────────────────────────

    /// <summary>
    /// Result of BOQCostManager.CompareSnapshots. Two snapshots A (older) and
    /// B (newer), with per-section and per-category deltas plus a human-readable
    /// PlainSummary paragraph.
    /// </summary>
    public class BOQSnapshotDiff
    {
        public string LabelA, LabelB, TypeA, TypeB;
        public DateTime DateA, DateB;
        public double TotalA, TotalB;
        public double ModeledA, ModeledB, ProvA, ProvB;
        public double CarbonA, CarbonB;
        public List<SectionDiff> SectionDiffs = new List<SectionDiff>();
        public List<CategoryDiff> CategoryDiffs = new List<CategoryDiff>();
        public string PlainSummary;

        public double NetChange => TotalB - TotalA;
        public double NetChangePct => TotalA > 0 ? (TotalB - TotalA) / TotalA * 100 : 0;
        public double NetCarbonChange => CarbonB - CarbonA;
    }

    public class SectionDiff
    {
        public string NRM2Section, Name, Discipline;
        public double TotalA, TotalB;
        public double Delta => TotalB - TotalA;
        public double DeltaPct => TotalA > 0 ? Delta / TotalA * 100 : 0;
    }

    public class CategoryDiff : SectionDiff
    {
        public double QtyA, QtyB, RateA, RateB;
        public BOQChangeType ChangeType;
        public string ChangeReason;
    }

    // ── BOQSnapshotMeta ────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight snapshot index entry. Returned by BOQCostManager.ListSnapshots —
    /// avoids parsing the full JSON payload just to populate a dropdown.
    /// </summary>
    public class BOQSnapshotMeta
    {
        public string Path;
        public string Label;
        public string Type;
        public DateTime Date;
        public double GrandTotalUGX;

        // P1: server-sync provenance. Populated by BoqSyncCoordinator
        // after a successful push. SyncState values:
        //   "Local"   — never attempted (default)
        //   "Pending" — push deferred (offline / no config / server error)
        //   "Synced"  — confirmed on server, ServerBaselineId valid
        //   "Conflict" — server checksum mismatched local checksum
        //   "Disabled" — Planscape not configured for this project
        public string Checksum;
        public Guid? ServerBaselineId;
        public string SyncState = "Local";

        public string DisplayText
            => $"{Type,-10} {Label} — {Date:dd MMM yyyy} — UGX {GrandTotalUGX:N0}";
    }

    // ── BOQManualStore ─────────────────────────────────────────────────────

    /// <summary>
    /// Persistence shape for {projectDir}/_bim_manager/project_boq_manual.json.
    /// Covers manual rows, PS rows and the project-level budget — everything
    /// that can't be derived from Revit geometry alone.
    /// </summary>
    public class BOQManualStore
    {
        public string SchemaVersion = "1.1";
        public double ProjectBudgetUGX;
        public DateTime LastSaved = DateTime.UtcNow;
        public string LastSavedBy;
        public double BoqHealthScore;   // cached from last BuildBOQDocument
        public List<BOQLineItem> ManualRows = new List<BOQLineItem>();
    }

    // ── BOQModelOverride / BOQModelOverridesStore ──────────────────────────

    /// <summary>
    /// Phase 108f: per-element override for a row whose Source is Model.
    /// Needed because (a) ResolveRate otherwise re-derives the rate from the
    /// CSV on every BuildBOQDocument, and (b) the shared ExternalEvent that
    /// writes CST_RATE_SOURCE = "Override" can race with a user-triggered
    /// Refresh — the _commandTag field on StingCommandHandler is single-slot
    /// so if two dispatches queue before Execute runs, the first is lost.
    ///
    /// Persisting the edit to a sidecar on the WPF thread (no ExternalEvent
    /// involved) makes the edit survive Refresh, doc re-open and Revit
    /// crashes, irrespective of whether the background parameter write
    /// succeeded.
    /// </summary>
    public class BOQModelOverride
    {
        public string UniqueId;             // Revit UniqueId — stable across save/reopen, preferred key
        public long ElementId;              // Current-session ElementId — fallback key
        public double? RateUGX;
        public double? RateUSD;
        public string NRM2Paragraph;
        public string Note;
        public string RateSource;           // P3 — provenance ("QS" for imported rates); null ⇒ "Override"
        public DateTime Modified = DateTime.UtcNow;
        public string ModifiedBy;
    }

    /// <summary>
    /// Persistence shape for {projectDir}/_bim_manager/project_boq_model_overrides.json.
    /// Schema is independent from BOQManualStore so manual-row persistence
    /// logic stays untouched by model-row edits.
    /// </summary>
    public class BOQModelOverridesStore
    {
        public string SchemaVersion = "1.0";
        public DateTime LastSaved = DateTime.UtcNow;
        public string LastSavedBy;
        public List<BOQModelOverride> Overrides = new List<BOQModelOverride>();
    }

    // ── BOQReconcileMatch ──────────────────────────────────────────────────

    /// <summary>
    /// One candidate match between a provisional sum row and a modeled element
    /// of the same category whose total is within ±30% of the PS. Produced by
    /// BOQCostManager.ReconcileProvisionals; confirmed/rejected by the user
    /// in the reconcile dialog.
    /// </summary>
    public class BOQReconcileMatch
    {
        public BOQLineItem PSRow;
        public BOQLineItem ModeledRow;
        public double ConfidencePct;
        public string Reason;
        // Z-23 (6.6): signed delta = modeled − provisional. POSITIVE = overrun
        // (modeled cost exceeds the PS allowance); NEGATIVE = credit back to the
        // client (modeled is under the PS). Closeness ranking still uses the
        // magnitude — this preserves the direction the abs() previously lost.
        public double SignedDeltaUGX;
    }

    // ── BOQHealthScore ─────────────────────────────────────────────────────

    /// <summary>
    /// Phase 11C. Scored out of 100 across 7 factors (paragraph coverage,
    /// rate confidence, token completeness, line ref assignment, budget
    /// coverage, PS descriptions, carbon data availability).
    /// </summary>
    public class BOQHealthScore
    {
        public double OverallScore;
        public string Grade;                // "Excellent" / "Good" / "Fair" / "Poor"
        public List<string> Issues = new List<string>();
        public List<string> Recommendations = new List<string>();

        // Component scores (for tooltip / drill-down)
        public double ParagraphCoverageScore;
        public double RateConfidenceScore;
        public double TokenCompletenessScore;
        public double LineRefScore;
        public double BudgetScore;
        public double PSDescriptionScore;
        public double CarbonScore;
    }
}
