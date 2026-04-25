// StingTools v4 MVP — fixture placement rule.
//
// PC-01 + PC-06..08 + PC-12..13 expand the rule POCO from the original 11
// fields to ~30 fields covering: 3-D offsets and rotation (PC-06), full
// room scoping suite (PC-07), variant fallback chain (PC-08), density and
// linear rule kinds (PC-12), and dependency DAG support (PC-13).
//
// All millimetre-valued properties are in millimetres; the engine
// converts to Revit internal feet at placement time.

using System.Collections.Generic;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Rule kind controls how the engine produces a count per room.
    /// Point: a single placement at the anchor (legacy).
    /// Density: count = ceil(area_m2 / PerAreaM2) or ceil(occupants / PerOccupant).
    /// Linear: count = ceil(perimeter_m / PerLinearMetre).
    /// </summary>
    public enum PlacementRuleKind
    {
        Point = 0,
        Density = 1,
        Linear = 2,
    }

    /// <summary>
    /// Single placement rule. Parameterless constructor required for
    /// Newtonsoft.Json deserialisation. Use Clone() to produce a
    /// mutable copy during merge without aliasing rule library state.
    /// </summary>
    public class PlacementRule
    {
        // ── Identity ────────────────────────────────────────────────

        /// <summary>Stable identifier; defaults to MergeKey when empty.</summary>
        public string RuleId { get; set; } = "";

        /// <summary>Point / Density / Linear (PC-12).</summary>
        public PlacementRuleKind RuleKind { get; set; } = PlacementRuleKind.Point;

        /// <summary>
        /// Revit category this rule applies to, matched against
        /// Element.Category.Name. Wildcards not supported; use one rule
        /// per category. PC-03 validates this against
        /// Document.Settings.Categories on load.
        /// </summary>
        public string CategoryFilter { get; set; } = "";

        /// <summary>
        /// PC-08 — comma-separated fallback chain (FLUSH,SURFACE,RECESSED)
        /// or a single regex (^IP6[5-7]$). Engine tries each in order
        /// and falls back to the first symbol when nothing matches.
        /// Examples: "FLUSH", "FLUSH,SURFACE", "^IP6[5-7]$".
        /// </summary>
        public string VariantHint { get; set; } = "";

        /// <summary>
        /// PC-08 — optional FamilySymbol.Name regex for refining variant
        /// resolution. Applied after VariantHint when both are set.
        /// </summary>
        public string FamilyTypeRegex { get; set; } = "";

        // ── Room scoping (PC-07) ────────────────────────────────────

        /// <summary>
        /// Room-name regex (case-insensitive). Empty string matches any
        /// room. Regex is pre-compiled by PlacementScorer.
        /// </summary>
        public string RoomFilter { get; set; } = "";

        /// <summary>PC-07 — negative-match regex; rooms matching this skip the rule.</summary>
        public string ExcludeRoomFilter { get; set; } = "";

        /// <summary>PC-07 — Department parameter regex.</summary>
        public string RoomDepartmentFilter { get; set; } = "";

        /// <summary>PC-07 — minimum room area in m² (0 = no minimum).</summary>
        public double MinAreaM2 { get; set; } = 0.0;

        /// <summary>PC-07 — maximum room area in m² (0 = no maximum).</summary>
        public double MaxAreaM2 { get; set; } = 0.0;

        /// <summary>PC-07 — Level-name regex.</summary>
        public string LevelFilter { get; set; } = "";

        /// <summary>PC-07 — Phase-name regex (matched against room CreatedPhaseId.Name).</summary>
        public string PhaseFilter { get; set; } = "";

        /// <summary>PC-07 — Workset-name regex.</summary>
        public string WorksetFilter { get; set; } = "";

        // ── Geometry (PC-06) ────────────────────────────────────────

        /// <summary>
        /// Anchor reference (written to ASS_PLACE_ANCHOR_TXT). One of
        /// the values in PlacementScorer.GenerateAnchorPoints.
        /// </summary>
        public string AnchorType { get; set; } = "ROOM_CENTRE";

        /// <summary>PC-06 — FFL / SOFFIT / SLAB / CEILING. Default FFL preserves legacy semantics.</summary>
        public string MountingReference { get; set; } = "FFL";

        /// <summary>Signed horizontal offset from the anchor in millimetres.</summary>
        public double OffsetXMm { get; set; } = 0.0;

        /// <summary>PC-06 — signed Y offset (anchor's +Y direction).</summary>
        public double OffsetYMm { get; set; } = 0.0;

        /// <summary>PC-06 — signed Z offset, separate from MountingHeightMm. Useful for tilt rigs.</summary>
        public double OffsetZMm { get; set; } = 0.0;

        /// <summary>PC-06 — rotation about Z in degrees.</summary>
        public double RotationDeg { get; set; } = 0.0;

        /// <summary>PC-06 — placement tolerance in millimetres; the scorer treats candidates within this radius as equivalent.</summary>
        public double ToleranceMm { get; set; } = 25.0;

        /// <summary>Mounting height above MountingReference in millimetres.</summary>
        public double MountingHeightMm { get; set; } = 300.0;

        /// <summary>Side constraint (LEFT / RIGHT / EITHER / FRONT / BACK / HINGE_SIDE / LATCH_SIDE).</summary>
        public string SideConstraint { get; set; } = "EITHER";

        // ── Spacing & cap ───────────────────────────────────────────

        /// <summary>Minimum centre-to-centre spacing in millimetres between fixtures placed by this rule within the same room.</summary>
        public double MinSpacingMm { get; set; } = 1000.0;

        /// <summary>Hard cap on fixtures placed by this rule per room. 0 = no cap.</summary>
        public int MaxPerRoom { get; set; } = 0;

        // ── Density / linear (PC-12) ────────────────────────────────

        /// <summary>PC-12 — Density rule: 1 fixture per N m² of room floor area.</summary>
        public double PerAreaM2 { get; set; } = 0.0;

        /// <summary>PC-12 — Density rule: 1 fixture per N occupants (read from room's STING_OCC_COUNT_INT).</summary>
        public double PerOccupant { get; set; } = 0.0;

        /// <summary>PC-12 — Linear rule: 1 fixture per N metres of room perimeter.</summary>
        public double PerLinearMetre { get; set; } = 0.0;

        // ── Dependency DAG (PC-13) ──────────────────────────────────

        /// <summary>PC-13 — RuleId of predecessor; this rule fires only after the predecessor produced ≥1 placement.</summary>
        public string DependsOn { get; set; } = "";

        /// <summary>PC-13 — when DependsOn set, "previous" / "first" / "self" controls anchor relative to which predecessor placement.</summary>
        public string RelativeTo { get; set; } = "";

        /// <summary>PC-13 — RuleIds to co-place at the same point.</summary>
        public List<string> CoPlaceWith { get; set; } = new List<string>();

        /// <summary>PC-13 — RuleIds whose placement in the same room suppresses this rule.</summary>
        public List<string> ConflictsWith { get; set; } = new List<string>();

        // ── Reporting ───────────────────────────────────────────────

        /// <summary>Rule priority (0..100); higher wins. Ties broken by candidate score.</summary>
        public int Priority { get; set; } = 50;

        /// <summary>Citation surfaced in result panels.</summary>
        public string StandardRef { get; set; } = "";

        /// <summary>Optional Uniclass 2015 product code.</summary>
        public string UniclassPr { get; set; } = "";

        /// <summary>Free-text note surfaced in the placement result panel.</summary>
        public string Notes { get; set; } = "";

        // ── Methods ─────────────────────────────────────────────────

        /// <summary>Deep-copy the rule.</summary>
        public PlacementRule Clone()
        {
            return new PlacementRule
            {
                RuleId               = this.RuleId,
                RuleKind             = this.RuleKind,
                CategoryFilter       = this.CategoryFilter,
                VariantHint          = this.VariantHint,
                FamilyTypeRegex      = this.FamilyTypeRegex,
                RoomFilter           = this.RoomFilter,
                ExcludeRoomFilter    = this.ExcludeRoomFilter,
                RoomDepartmentFilter = this.RoomDepartmentFilter,
                MinAreaM2            = this.MinAreaM2,
                MaxAreaM2            = this.MaxAreaM2,
                LevelFilter          = this.LevelFilter,
                PhaseFilter          = this.PhaseFilter,
                WorksetFilter        = this.WorksetFilter,
                AnchorType           = this.AnchorType,
                MountingReference    = this.MountingReference,
                OffsetXMm            = this.OffsetXMm,
                OffsetYMm            = this.OffsetYMm,
                OffsetZMm            = this.OffsetZMm,
                RotationDeg          = this.RotationDeg,
                ToleranceMm          = this.ToleranceMm,
                MountingHeightMm     = this.MountingHeightMm,
                SideConstraint       = this.SideConstraint,
                MinSpacingMm         = this.MinSpacingMm,
                MaxPerRoom           = this.MaxPerRoom,
                PerAreaM2            = this.PerAreaM2,
                PerOccupant          = this.PerOccupant,
                PerLinearMetre       = this.PerLinearMetre,
                DependsOn            = this.DependsOn,
                RelativeTo           = this.RelativeTo,
                CoPlaceWith          = new List<string>(this.CoPlaceWith ?? new List<string>()),
                ConflictsWith        = new List<string>(this.ConflictsWith ?? new List<string>()),
                Priority             = this.Priority,
                StandardRef          = this.StandardRef,
                UniclassPr           = this.UniclassPr,
                Notes                = this.Notes,
            };
        }

        /// <summary>
        /// Merge-key tuple used by PlacementRuleLoader to deduplicate
        /// project-level overrides against the default library.
        /// </summary>
        public string MergeKey => string.IsNullOrEmpty(RuleId)
            ? $"{CategoryFilter}::{VariantHint}::{RoomFilter}::{AnchorType}"
            : RuleId;
    }

    /// <summary>
    /// Wrapper used by Newtonsoft.Json to deserialise the top-level
    /// STING_PLACEMENT_RULES.json schema: { "Version": "v4", "Rules": [...] }.
    /// </summary>
    public class PlacementRuleSet
    {
        public string Version { get; set; } = "v4";
        public string Description { get; set; } = "";
        public List<PlacementRule> Rules { get; set; } = new List<PlacementRule>();
    }
}
