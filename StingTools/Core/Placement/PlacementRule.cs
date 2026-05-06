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

        /// <summary>Phase 139 — discipline pack of origin (Baseline/Electrical/Windows/...).</summary>
        public string SourcePack { get; set; } = "";

        // ── Phase 139 A1 — Building & Standards Context ─────────────

        /// <summary>Office|Residential|Healthcare|Education|Hospitality|Industrial|Retail|Mixed (empty = all).</summary>
        public string BuildingType { get; set; } = "";

        /// <summary>Standards this rule is anchored to (e.g. ["BS7671","BS5839","NFPA13"]).</summary>
        public string[] ApplicableStandards { get; set; } = new string[0];

        /// <summary>Minimum IP rating required on placed family (e.g. "IP44"); empty = no check.</summary>
        public string IpRatingMin { get; set; } = "";

        /// <summary>"NONE"|"BS7671_Z1"|"BS7671_Z2"|"IEC60364_Z0"|"IEC60364_Z1".</summary>
        public string WetZoneExclusion { get; set; } = "NONE";

        /// <summary>When true, validate placed height against Part M / BS8300 reach range.</summary>
        public bool AccessibilityCheck { get; set; } = false;

        /// <summary>Key into HeightStandardsTable (e.g. "BS8300_SWITCH_1200_1400").</summary>
        public string HeightStandard { get; set; } = "";

        // ── Phase 139 A2 — Coverage & Spacing Standards ────────────

        /// <summary>Coverage radius per device (mm) — engine fills room to 100% coverage when GuaranteeCoverage=true.</summary>
        public double CoverageRadiusMm { get; set; } = 0.0;

        /// <summary>Upper bound centre-to-centre spacing (mm). Complements MinSpacingMm.</summary>
        public double MaxSpacingMm { get; set; } = 0.0;

        /// <summary>Minimum clearance from any room boundary wall (mm).</summary>
        public double WallClearanceMm { get; set; } = 0.0;

        /// <summary>Minimum clearance from ceiling obstructions (mm).</summary>
        public double ObstructionClearanceMm { get; set; } = 0.0;

        /// <summary>When true, reject run if CoveragePercent &lt; 100% and report uncovered zones.</summary>
        public bool GuaranteeCoverage { get; set; } = false;

        // ── Phase 139 A3 — Routing & Containment ───────────────────

        /// <summary>"NONE"|"WALL_FOLLOW"|"CEILING_FOLLOW"|"FLOOR_FOLLOW"|"CONDUIT_RUN"|"TRAY_RUN".</summary>
        public string RoutingMode { get; set; } = "NONE";

        /// <summary>Offset from face (mm); positive = into room, negative = into structure.</summary>
        public double RouteOffsetMm { get; set; } = 0.0;

        /// <summary>"INTERIOR"|"EXTERIOR"|"TOP"|"BOTTOM" — host face to offset from.</summary>
        public string RouteFace { get; set; } = "INTERIOR";

        /// <summary>Minimum bend radius for conduit/pipe routing (mm).</summary>
        public double RouteMinBendRadiusMm { get; set; } = 0.0;

        /// <summary>Revit category for route segments: "Conduit"|"CableTray"|"Pipe"|"Duct"|"GenericModel".</summary>
        public string RouteSegmentCategory { get; set; } = "";

        // ── Phase 139 A4 — Window/Sill Variants ────────────────────

        /// <summary>Sill height from FFL (mm) — overrides MountingHeightMm for window rules.</summary>
        public double SillHeightMm { get; set; } = 0.0;

        /// <summary>Top of window opening from FFL (mm).</summary>
        public double HeadHeightMm { get; set; } = 0.0;

        /// <summary>Clear drop from sill to floor (mm); &lt;800mm triggers safety glazing.</summary>
        public double CillToFloorMm { get; set; } = 0.0;

        /// <summary>BS 6206 / Approved Doc N — toughened glazing required when CillToFloorMm &lt; 800.</summary>
        public bool ToughenedGlazingRequired { get; set; } = false;

        /// <summary>"CLEAR"|"OBSCURED"|"TOUGHENED"|"LAMINATED"|"ACOUSTIC"|"FIRE_RATED"|"UV_FILTER"|"SEALED".</summary>
        public string GlazingSpec { get; set; } = "";

        // ── Phase 139 A5 — Density Extensions ──────────────────────

        /// <summary>Healthcare: 1 fixture per N beds (reads STING_BED_COUNT_INT).</summary>
        public double PerBed { get; set; } = 0.0;

        /// <summary>Office: 1 per N workstations (reads STING_WORKSTATION_COUNT_INT).</summary>
        public double PerWorkstation { get; set; } = 0.0;

        /// <summary>Education: 1 per N pupils (reads STING_PUPIL_COUNT_INT).</summary>
        public double PerPupil { get; set; } = 0.0;

        /// <summary>Sanitary: N wash basins per WC cubicle.</summary>
        public double PerToiletCubicle { get; set; } = 0.0;

        /// <summary>Override default occupancy param; empty = STING_OCC_COUNT_INT.</summary>
        public string OccupancyParamName { get; set; } = "";

        /// <summary>"WORKPLACE1992"|"BS6465_OFFICE"|"BS6465_SCHOOL"|"BS6465_HEALTHCARE"|"HTM0201".</summary>
        public string BuildingTypeTable { get; set; } = "";

        // ── Phase 139 A6 — Post-Placement Audit ────────────────────

        /// <summary>If non-empty, write to STING_PLACE_AUDIT_TXT after placement.</summary>
        public string PostAuditTag { get; set; } = "";

        /// <summary>When true, preflight checks placed family for COBie.Component shared parameters.</summary>
        public bool RequiresCOBieFields { get; set; } = false;

        /// <summary>When true, preflight checks IfcExportAs parameter on placed family type.</summary>
        public bool RequiresIfcMapping { get; set; } = false;

        /// <summary>"FRONT_600"|"FRONT_1000"|"SIDES_300"|"TOP_900" — BS4422 / HTM clearance class.</summary>
        public string MaintenanceClearance { get; set; } = "";

        // ── Phase 139.2 A1 — Manufacturer hint ──────────────────────

        public string ManufacturerCode  { get; set; } = "";
        public string CatalogueRef      { get; set; } = "";
        public int    BoxDepthMm        { get; set; } = 0;
        public double ModulePitchMm     { get; set; } = 0.0;
        public int    GangCount         { get; set; } = 0;
        public string MountType         { get; set; } = "";
        public string InsertionOrigin   { get; set; } = "";

        // ── Phase 139.2 A2 — Two-phase conduiting ───────────────────

        public string ConstructionPhase  { get; set; } = "";
        public string CompletionPhase    { get; set; } = "";
        public string BoxFamilyTypeRegex { get; set; } = "";
        public string BoxLocationIdParam { get; set; } = "";
        public bool   TwoPhaseEnabled    { get; set; } = false;

        // ── Phase 139.2 A3 — Compound cluster ───────────────────────

        public bool   IsClusterMember     { get; set; } = false;
        public string ClusterGroupId      { get; set; } = "";
        public int    ClusterSlotIndex    { get; set; } = 0;
        public int    ClusterTotalSlots   { get; set; } = 0;
        public double ClusterFrameWidthMm { get; set; } = 0.0;

        // ── Phase 139.2 A4 — Plaster / finish-face offset ───────────

        public string PlasterOffsetMode    { get; set; } = "None";
        public double PlasterOffsetFixedMm { get; set; } = 0.0;

        // ── Phase 139.2 A5 — Ceiling tile snap ──────────────────────

        public bool   CeilingTileSnap     { get; set; } = false;
        public double TileGridSpacingXMm  { get; set; } = 0.0;
        public double TileGridSpacingYMm  { get; set; } = 0.0;

        // ── Phase 139.2 A6 — Structural fixing check ────────────────

        public bool   StructuralFixingCheck { get; set; } = false;
        public double JoistClearanceMm      { get; set; } = 0.0;
        public bool   EmitNogginRequirement { get; set; } = false;

        // ── Phase 139.2 A7 — BS 7671 wet zone exclusion ─────────────

        public bool   WetZoneExclude { get; set; } = false;
        public string WetZoneClass   { get; set; } = "";

        // ── Phase 139.2 A8 — Standards aliases ──────────────────────

        public string HeightStandardRef { get; set; } = "";

        // ── Phase 139.27 (X-02) — Per-rule lighting uniformity gate ─

        /// <summary>
        /// Minimum acceptable BS EN 12464-1 / CIBSE LG7 uniformity ratio
        /// (Uo = Emin / Eavg). 0 = use calculator default (0.40 — general).
        /// 0.60 typical for offices; 0.70 for healthcare / classrooms.
        /// </summary>
        public double MinUniformityRatio { get; set; } = 0.0;

        // ── Phase 139.27 (X-04) — Cable-derating advisory ───────────

        /// <summary>
        /// When > 0, the placement engine emits an advisory warning if
        /// more than this many same-system cables / conduits land within
        /// the rule's bundle clearance — BS 7671 Table 4 derating
        /// (e.g. 0.80× at 4 cables, 0.50× at ≥ 9). 0 = no advisory.
        /// </summary>
        public int CableBundleAdvisoryCount { get; set; } = 0;

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
                SourcePack           = this.SourcePack,
                BuildingType         = this.BuildingType,
                ApplicableStandards  = (string[])(this.ApplicableStandards?.Clone() ?? new string[0]),
                IpRatingMin          = this.IpRatingMin,
                WetZoneExclusion     = this.WetZoneExclusion,
                AccessibilityCheck   = this.AccessibilityCheck,
                HeightStandard       = this.HeightStandard,
                CoverageRadiusMm     = this.CoverageRadiusMm,
                MaxSpacingMm         = this.MaxSpacingMm,
                WallClearanceMm      = this.WallClearanceMm,
                ObstructionClearanceMm = this.ObstructionClearanceMm,
                GuaranteeCoverage    = this.GuaranteeCoverage,
                RoutingMode          = this.RoutingMode,
                RouteOffsetMm        = this.RouteOffsetMm,
                RouteFace            = this.RouteFace,
                RouteMinBendRadiusMm = this.RouteMinBendRadiusMm,
                RouteSegmentCategory = this.RouteSegmentCategory,
                SillHeightMm         = this.SillHeightMm,
                HeadHeightMm         = this.HeadHeightMm,
                CillToFloorMm        = this.CillToFloorMm,
                ToughenedGlazingRequired = this.ToughenedGlazingRequired,
                GlazingSpec          = this.GlazingSpec,
                PerBed               = this.PerBed,
                PerWorkstation       = this.PerWorkstation,
                PerPupil             = this.PerPupil,
                PerToiletCubicle     = this.PerToiletCubicle,
                OccupancyParamName   = this.OccupancyParamName,
                BuildingTypeTable    = this.BuildingTypeTable,
                PostAuditTag         = this.PostAuditTag,
                RequiresCOBieFields  = this.RequiresCOBieFields,
                RequiresIfcMapping   = this.RequiresIfcMapping,
                MaintenanceClearance = this.MaintenanceClearance,
                ManufacturerCode     = this.ManufacturerCode,
                CatalogueRef         = this.CatalogueRef,
                BoxDepthMm           = this.BoxDepthMm,
                ModulePitchMm        = this.ModulePitchMm,
                GangCount            = this.GangCount,
                MountType            = this.MountType,
                InsertionOrigin      = this.InsertionOrigin,
                ConstructionPhase    = this.ConstructionPhase,
                CompletionPhase      = this.CompletionPhase,
                BoxFamilyTypeRegex   = this.BoxFamilyTypeRegex,
                BoxLocationIdParam   = this.BoxLocationIdParam,
                TwoPhaseEnabled      = this.TwoPhaseEnabled,
                IsClusterMember      = this.IsClusterMember,
                ClusterGroupId       = this.ClusterGroupId,
                ClusterSlotIndex     = this.ClusterSlotIndex,
                ClusterTotalSlots    = this.ClusterTotalSlots,
                ClusterFrameWidthMm  = this.ClusterFrameWidthMm,
                PlasterOffsetMode    = this.PlasterOffsetMode,
                PlasterOffsetFixedMm = this.PlasterOffsetFixedMm,
                CeilingTileSnap      = this.CeilingTileSnap,
                TileGridSpacingXMm   = this.TileGridSpacingXMm,
                TileGridSpacingYMm   = this.TileGridSpacingYMm,
                StructuralFixingCheck = this.StructuralFixingCheck,
                JoistClearanceMm     = this.JoistClearanceMm,
                EmitNogginRequirement = this.EmitNogginRequirement,
                WetZoneExclude       = this.WetZoneExclude,
                WetZoneClass         = this.WetZoneClass,
                HeightStandardRef    = this.HeightStandardRef,
                MinUniformityRatio        = this.MinUniformityRatio,
                CableBundleAdvisoryCount  = this.CableBundleAdvisoryCount,
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
