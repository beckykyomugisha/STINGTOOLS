// ══════════════════════════════════════════════════════════════════════════
//  BOQModels.cs — Phase 2 of the BOQ & Cost Manager.
//  Pure data model. No Revit API calls, no file I/O, no WPF references.
//  Every other BOQ file depends on these types.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.BOQ
{
    public enum BOQRowSource
    {
        Model,
        Manual,
        ProvisionalSum
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
        public double Quantity;
        public string Unit;                 // "m²" / "m³" / "m" / "each" / "item" / "kg" / "tonne"
        public double RateUGX;
        public double RateUSD;
        public double EmbodiedCarbonKg;     // kgCO2e
        public double LifecycleCostUGX;     // capital + maintenance NPV 25yr
        public string ResolvedNRM2Paragraph;
        public string BOQLineRef;           // e.g. "14.3.2"
        public string Note;
        public BOQRowSource Source;
        public string SnapshotRef;
        public long RevitElementId = -1;    // -1 for manual/PS rows
        public string UniqueId;             // Revit UniqueId (cross-doc, survives Revit save/reopen)
        public string Level;
        public string Location;             // room name or spatial code
        public DateTime LastCosted = DateTime.UtcNow;
        public string RateSource;           // "CSV" | "COBie" | "Default" | "Manual" | "Override" | "Carbon" | "Interpolated"
        public int RateConfidence = 60;     // 0-100 (Phase 11A)
        public int SortOrder;               // stable ordering within a section

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
                RateUGX = this.RateUGX,
                RateUSD = this.RateUSD,
                EmbodiedCarbonKg = this.EmbodiedCarbonKg,
                LifecycleCostUGX = this.LifecycleCostUGX,
                ResolvedNRM2Paragraph = this.ResolvedNRM2Paragraph,
                BOQLineRef = this.BOQLineRef,
                Note = this.Note,
                Source = this.Source,
                SnapshotRef = this.SnapshotRef,
                RevitElementId = this.RevitElementId,
                UniqueId = this.UniqueId,
                Level = this.Level,
                Location = this.Location,
                LastCosted = this.LastCosted,
                RateSource = this.RateSource,
                RateConfidence = this.RateConfidence,
                SortOrder = this.SortOrder
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
        public double ProjectBudgetUGX;
        public double PrelimPct = 12.0;
        public double ContingencyPct = 10.0;
        public double OverheadPct = 8.0;
        public string Currency = "UGX";
        public double ExchangeRateUgxPerUsd = 3700.0;   // Phase 11E multi-currency
        public List<BOQSection> Sections = new List<BOQSection>();

        public List<BOQLineItem> AllItems => Sections.SelectMany(s => s.Items).ToList();
        public double ModeledTotalUGX => AllItems.Where(i => i.Source == BOQRowSource.Model).Sum(i => i.TotalUGX);
        public double ProvTotalUGX => AllItems.Where(i => i.Source != BOQRowSource.Model).Sum(i => i.TotalUGX);
        public double SubtotalUGX => AllItems.Sum(i => i.TotalUGX);
        public double GrandTotalUGX =>
            Math.Round(SubtotalUGX * (1 + PrelimPct / 100.0 + ContingencyPct / 100.0 + OverheadPct / 100.0), 0);
        public double BudgetVarianceUGX => ProjectBudgetUGX > 0 ? ProjectBudgetUGX - GrandTotalUGX : 0;
        public double BudgetCoveragePct => ProjectBudgetUGX > 0 ? SubtotalUGX / ProjectBudgetUGX * 100 : 0;
        public double TotalCarbonKg => AllItems.Sum(i => i.EmbodiedCarbonKg);
        public int ResolvedParagraphCount => AllItems.Count(i => !string.IsNullOrEmpty(i.ResolvedNRM2Paragraph));
        public double ParagraphCoveragePct => AllItems.Count > 0 ? 100.0 * ResolvedParagraphCount / AllItems.Count : 0;

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
