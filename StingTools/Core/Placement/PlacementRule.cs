using System;
// StingTools v4 MVP — fixture placement rule.
//
// PC-01 + PC-06..08 + PC-12..16 expand the rule POCO from the original 11
// fields to ~50 fields covering: 3-D offsets / rotation (PC-06), full
// room scoping suite (PC-07), variant fallback chain (PC-08), density and
// linear rule kinds (PC-12), dependency DAG support (PC-13), coverage-grid
// guarantee (PC-14), integrated routing (PC-15), two-phase construction
// phasing (PC-16), and cluster placement (PC-17).
//
// All millimetre-valued properties are in millimetres; the engine
// converts to Revit internal feet at placement time.

using System.Collections.Generic;
using StingTools.Core.Routing;

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

        /// <summary>
        /// Source discipline pack filename (set by PlacementRuleLoader).
        /// Read-only for the UI; used in merge-conflict warnings and Excel export.
        /// </summary>
        public string SourcePack { get; set; } = "";

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

        /// <summary>Material specification string written to PLM_PPE_MAT_TXT / HVC_DCT_MAT_TXT on placed elements.</summary>
        public string Material { get; set; } = "";

        // ── Spacing & cap ───────────────────────────────────────────

        /// <summary>Minimum centre-to-centre spacing in millimetres between fixtures placed by this rule within the same room.</summary>
        public double MinSpacingMm { get; set; } = 1000.0;

        /// <summary>Maximum allowable spacing in mm between coverage-grid fixtures (BS EN 12464 / BS 5839). 0 = no maximum.</summary>
        public double MaxSpacingMm { get; set; } = 0.0;

        /// <summary>Hard cap on fixtures placed by this rule per room. 0 = no cap.</summary>
        public int MaxPerRoom { get; set; } = 0;

        // ── Density / linear (PC-12) ────────────────────────────────

        /// <summary>PC-12 — Density rule: 1 fixture per N m² of room floor area.</summary>
        public double PerAreaM2 { get; set; } = 0.0;

        /// <summary>PC-12 — Density rule: 1 fixture per N occupants (read from room's STING_OCC_COUNT_INT).</summary>
        public double PerOccupant { get; set; } = 0.0;

        /// <summary>PC-12 — Density rule: 1 fixture per N beds (healthcare; read from room's STING_BED_COUNT_INT).</summary>
        public double PerBed { get; set; } = 0.0;

        /// <summary>PC-12 — Density rule: 1 fixture per N workstations (office; read from room's STING_WS_COUNT_INT).</summary>
        public double PerWorkstation { get; set; } = 0.0;

        /// <summary>PC-12 — Density rule: 1 fixture per N pupils (education; read from room's STING_PUPIL_COUNT_INT).</summary>
        public double PerPupil { get; set; } = 0.0;

        /// <summary>PC-12 — Density rule: 1 fixture per N toilet cubicles (BS 6465 accessory rules).</summary>
        public double PerToiletCubicle { get; set; } = 0.0;

        /// <summary>PC-12 — Name of a room integer parameter to read occupancy from when PerOccupant is set. Defaults to STING_OCC_COUNT_INT.</summary>
        public string OccupancyParamName { get; set; } = "";

        /// <summary>PC-12 — Linear rule: 1 fixture per N metres of room perimeter.</summary>
        public double PerLinearMetre { get; set; } = 0.0;

        // ── Coverage grid (PC-14) ────────────────────────────────────

        /// <summary>PC-14 — effective coverage radius in mm (BS EN 12464-1 / BS 5839-1 / BS EN 14604). 0 = no coverage grid.</summary>
        public double CoverageRadiusMm { get; set; } = 0.0;

        /// <summary>PC-14 — when true, engine adds fixtures until CoveragePercent ≥ 99 % even if MaxPerRoom would cap earlier.</summary>
        public bool GuaranteeCoverage { get; set; } = false;

        // ── Integrated routing (PC-15) ───────────────────────────────

        /// <summary>PC-15 — NONE / AUTO_CONDUIT / AUTO_PIPE / AUTO_DUCT / WALL_FOLLOWER. If set, the engine routes from each placed fixture after placement.</summary>
        public string RoutingMode { get; set; } = "NONE";

        /// <summary>PC-15 — vertical offset in mm from the fixture connector to where the route begins (negative = below connector).</summary>
        public double RouteOffsetMm { get; set; } = 0.0;

        /// <summary>PC-15 — PIPE / CONDUIT / CABLE_TRAY / DUCT. Must match an available Revit system category when RoutingMode ≠ NONE.</summary>
        public string RouteSegmentCategory { get; set; } = "";

        // ── Two-phase construction (PC-16) ───────────────────────────

        /// <summary>PC-16 — when true, the TwoPhaseBoxPlacer runs first-fix box before second-fix fixture placement.</summary>
        public bool TwoPhaseEnabled { get; set; } = false;

        /// <summary>PC-16 — FIRST_FIX / SECOND_FIX / FINISHED. Controls which construction phase this rule targets.</summary>
        public string ConstructionPhase { get; set; } = "FINISHED";

        // ── Cluster placement (PC-17) ────────────────────────────────

        /// <summary>PC-17 — when true, this rule participates in cluster placement with other rules sharing ClusterGroupId.</summary>
        public bool IsClusterMember { get; set; } = false;

        /// <summary>PC-17 — group identifier for co-located clusters (e.g. "SWITCH_PLATE_CLUSTER"). Rules in the same group are placed as a single compound element.</summary>
        public string ClusterGroupId { get; set; } = "";

        // ── Dependency DAG (PC-13) ──────────────────────────────────

        /// <summary>PC-13 — RuleId of predecessor; this rule fires only after the predecessor produced ≥1 placement.</summary>
        public string DependsOn { get; set; } = "";

        /// <summary>PC-13 — when DependsOn set, "previous" / "first" / "self" controls anchor relative to which predecessor placement.</summary>
        public string RelativeTo { get; set; } = "";

        /// <summary>PC-13 — RuleIds to co-place at the same point.</summary>
        public List<string> CoPlaceWith { get; set; } = new List<string>();

        /// <summary>PC-13 — RuleIds whose placement in the same room suppresses this rule.</summary>
        public List<string> ConflictsWith { get; set; } = new List<string>();

        // ── Coverage / routing ──────────────────────────────────────

        /// <summary>Minimum clearance from wall face in millimetres.</summary>
        public double WallClearanceMm { get; set; } = 0.0;

        /// <summary>Minimum clearance from any obstruction in millimetres.</summary>
        public double ObstructionClearanceMm { get; set; } = 0.0;

        /// <summary>Wall face to route on — INTERIOR / EXTERIOR / THROUGH / AUTO.</summary>
        public string RouteFace { get; set; } = "";

        /// <summary>Minimum bend radius for conduit / cable routing (mm).</summary>
        public double RouteMinBendRadiusMm { get; set; } = 0.0;

        // ── Glazing / fenestration ───────────────────────────────────

        /// <summary>Sill height above floor finish level in millimetres.</summary>
        public double SillHeightMm { get; set; } = 0.0;

        /// <summary>Head height above floor finish level in millimetres.</summary>
        public double HeadHeightMm { get; set; } = 0.0;

        /// <summary>Cill / sill-to-floor dimension used by some standards (mm).</summary>
        public double CillToFloorMm { get; set; } = 0.0;

        /// <summary>Require toughened glazing flag (BS EN 12150).</summary>
        public bool ToughenedGlazingRequired { get; set; } = false;

        /// <summary>Glazing specification reference (e.g. "BS EN 12150 Class A").</summary>
        public string GlazingSpec { get; set; } = "";

        // ── Compliance / audit ───────────────────────────────────────

        /// <summary>Audit tag written to element on last compliance run.</summary>
        public string PostAuditTag { get; set; } = "";

        /// <summary>When true the engine checks and populates mandatory COBie fields.</summary>
        public bool RequiresCOBieFields { get; set; } = false;

        /// <summary>When true the engine validates mandatory IFC property mappings.</summary>
        public bool RequiresIfcMapping { get; set; } = false;

        /// <summary>Minimum maintenance access clearance in millimetres.</summary>
        public double MaintenanceClearance { get; set; } = 0.0;

        // ── Manufacturer / catalogue ─────────────────────────────────

        /// <summary>Manufacturer code cross-referenced against ManufacturerCatalogueRegistry.</summary>
        public string ManufacturerCode { get; set; } = "";

        /// <summary>Manufacturer catalogue reference / part number.</summary>
        public string CatalogueRef { get; set; } = "";

        // ── Footprint-aware spacing (real-size 3D families) ─────────

        /// <summary>
        /// When true, FixturePlacementEngine reads the resolved FamilySymbol's
        /// bounding box at placement time and scales MinSpacingMm /
        /// CoverageRadiusMm / OffsetXMm / ObstructionClearanceMm /
        /// WallClearanceMm proportionally to <see cref="ReferenceFootprintMm"/>.
        /// Lets one rule serve multiple manufacturers — a 1200 mm AHU lands
        /// with appropriately scaled spacing without per-vendor JSON edits.
        /// Defaults to false: legacy rules retain hard-coded values.
        /// </summary>
        public bool FamilyBboxAware { get; set; } = false;

        /// <summary>
        /// Reference footprint in millimetres against which the rule's
        /// spacing fields were tuned. The engine computes
        /// scale = max(symbolFootprintMm, MinSymbolFootprintMm) / ReferenceFootprintMm
        /// and multiplies the spacing fields by scale. Default 150 mm matches
        /// the small-fixture defaults the legacy rules were authored to.
        /// </summary>
        public double ReferenceFootprintMm { get; set; } = 150.0;

        /// <summary>
        /// Floor on the measured family footprint when FamilyBboxAware is set —
        /// prevents pathological 0-bbox families from collapsing spacing to 0.
        /// Default 100 mm.
        /// </summary>
        public double MinSymbolFootprintMm { get; set; } = 100.0;

        /// <summary>
        /// Cap on the bbox-derived scale factor. Default 8.0 — a 1200 mm
        /// family against a 150 mm reference scales spacings 8× and stops.
        /// Without a cap a 12 m AHU shell would scale spacings to 80 m and
        /// silently break room-area coverage rules.
        /// </summary>
        public double MaxFootprintScale { get; set; } = 8.0;

        // ── Type-catalog (.txt sidecar) ─────────────────────────────

        /// <summary>
        /// When set, the engine treats this rule's family as a Revit type
        /// catalog (`YourFamily.txt` sidecar next to `YourFamily.rfa`).
        /// The value is matched (case-insensitively, exact or regex) against
        /// the type names in the catalog. Only the matching type is loaded —
        /// avoids bloating the project with 200-type valve / fittings libraries.
        /// Empty (default) ⇒ legacy behaviour: all types load.
        /// </summary>
        public string TypeCatalogKey { get; set; } = "";

        /// <summary>Nominal back-box or enclosure depth in millimetres.</summary>
        public double BoxDepthMm { get; set; } = 0.0;

        /// <summary>Number of gangs (for socket / switch outlets).</summary>
        public int GangCount { get; set; } = 0;

        /// <summary>Module pitch for multi-module socket / switch ranges (mm).</summary>
        public double ModulePitchMm { get; set; } = 0.0;

        /// <summary>Mounting type string — FLUSH / SURFACE / PENDANT / TRACK / etc.</summary>
        public string MountType { get; set; } = "";

        /// <summary>Family insertion origin identifier — CENTRE / TOP_LEFT / BOTTOM_CENTRE / etc.</summary>
        public string InsertionOrigin { get; set; } = "";

        /// <summary>Plaster offset mode — NONE / FIXED / LAYER.</summary>
        public string PlasterOffsetMode { get; set; } = "";

        /// <summary>Fixed plaster offset dimension (mm) when PlasterOffsetMode = FIXED.</summary>
        public double PlasterOffsetFixedMm { get; set; } = 0.0;

        // ── Two-phase / construction sequence ────────────────────────

        /// <summary>Phase name for second-fix / completion-phase placement.</summary>
        public string CompletionPhase { get; set; } = "";

        /// <summary>FamilySymbol name regex for the first-fix box / back-box.</summary>
        public string BoxFamilyTypeRegex { get; set; } = "";

        /// <summary>Parameter name on the first-fix box that receives the final-fix element's Id.</summary>
        public string BoxLocationIdParam { get; set; } = "";

        // ── Cluster / gang ───────────────────────────────────────────

        /// <summary>Slot index within the cluster gang (0-based).</summary>
        public int ClusterSlotIndex { get; set; } = 0;

        /// <summary>Total number of slots in the cluster frame.</summary>
        public int ClusterTotalSlots { get; set; } = 0;

        /// <summary>Overall width of the cluster frame in millimetres.</summary>
        public double ClusterFrameWidthMm { get; set; } = 0.0;

        // ── Ceiling tile snap / structural fixing ─────────────────────

        /// <summary>When true snap placed points to the ceiling tile grid (CeilingGridSnap).</summary>
        public bool CeilingTileSnap { get; set; } = false;

        /// <summary>Tile grid X dimension override for ceiling snap (mm). 0 = use 1200mm default.</summary>
        public double TileGridSpacingXMm { get; set; } = 0.0;

        /// <summary>Tile grid Y dimension override for ceiling snap (mm). 0 = use 600mm default.</summary>
        public double TileGridSpacingYMm { get; set; } = 0.0;

        /// <summary>When true the engine checks for structural fixing points (joists / noggins).</summary>
        public bool StructuralFixingCheck { get; set; } = false;

        /// <summary>Minimum clearance from a joist centreline to allow direct fixing (mm).</summary>
        public double JoistClearanceMm { get; set; } = 0.0;

        /// <summary>When true the engine emits noggin-required markers at points without structural support.</summary>
        public bool EmitNogginRequirement { get; set; } = false;

        // ── Wet zone / accessibility / height ─────────────────────────

        /// <summary>When true exclude this fixture from wet zone areas.</summary>
        public bool WetZoneExclude { get; set; } = false;

        /// <summary>Wet zone class this fixture is rated for (BS EN 60529 / IEC 60364-7-701).</summary>
        public string WetZoneClass { get; set; } = "";

        /// <summary>Height standard reference applied by AccessibilityAuditor (e.g. "BS 8300:2018 §9.5").</summary>
        public string HeightStandardRef { get; set; } = "";

        // ── Density extensions ───────────────────────────────────────

        /// <summary>Name of the building-type lookup table for occupancy-based density rules.</summary>
        public string BuildingTypeTable { get; set; } = "";

        // ── Building type / standards ─────────────────────────────────

        /// <summary>Building occupancy / use class this rule targets (e.g. "EDUCATION", "HEALTHCARE", "OFFICE").</summary>
        public string BuildingType { get; set; } = "";

        /// <summary>Semicolon-separated list of standards this rule enforces (e.g. "BS 8300;BB101;HTM 08-03").</summary>
        public string ApplicableStandards { get; set; } = "";

        /// <summary>Minimum IP rating required (numeric, e.g. 44 for IP44).</summary>
        public int IpRatingMin { get; set; } = 0;

        /// <summary>Wet zone exclusion zone class string (e.g. "ZONE_1", "ZONE_2").</summary>
        public string WetZoneExclusion { get; set; } = "";

        /// <summary>When true the AccessibilityAuditor checks this placement against accessibility standards.</summary>
        public bool AccessibilityCheck { get; set; } = false;

        /// <summary>Height standard code used by the accessibility checker (e.g. "BS_8300_TABLE_6").</summary>
        public string HeightStandard { get; set; } = "";

        /// <summary>Maximum number of slots / circuits this rule allocates in a distribution board.</summary>
        public int MaxSlotsMm { get; set; } = 0;

        /// <summary>When true a two-part audit (install + commissioning) is required.</summary>
        public bool TwoPartAudit { get; set; } = false;

        // ── Slope / insulation ────────────────────────────────────────

        /// <summary>Minimum slope percentage for gravity drainage segments (BS EN 12056-2).</summary>
        public double MinSlopePercent { get; set; } = 0.0;

        /// <summary>Insulation thickness added around the pipe / conduit (mm).</summary>
        public double InsulationThicknessMm { get; set; } = 0.0;

        // ── Lighting ─────────────────────────────────────────────────

        /// <summary>Minimum acceptable BS EN 12464-1 uniformity ratio (Uo = Emin / Eavg). 0 = use calculator default.</summary>
        public double MinUniformityRatio { get; set; } = 0.0;

        // ── Electrical ───────────────────────────────────────────────

        /// <summary>Advisory maximum number of cables / conductors bundled in this trunking / conduit run.</summary>
        public int CableBundleAdvisoryCount { get; set; } = 0;

        // ── Context ───────────────────────────────────────────────────

        /// <summary>Nominal pipe / conduit outer diameter in millimetres for chase-depth calculations.</summary>
        public double NominalDiameterMm { get; set; } = 0.0;

        /// <summary>Mounting context identifier — SURFACE / FLUSH / CHASED / PENDANT / TRACK.</summary>
        public string MountingContext { get; set; } = "";

        /// <summary>Eurocode 2 / BS EN 1992-1-1 exposure class for concrete cover calculations (XC1 / XC2 / XS1 etc.).</summary>
        public string ExposureClass { get; set; } = "";

        /// <summary>When true the engine emits support-bracket requirements alongside the placed fixture.</summary>
        public bool EmitSupports { get; set; } = false;

        // ── Source / metadata ────────────────────────────────────────

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
                // Extended properties
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
                PostAuditTag         = this.PostAuditTag,
                RequiresCOBieFields  = this.RequiresCOBieFields,
                RequiresIfcMapping   = this.RequiresIfcMapping,
                MaintenanceClearance = this.MaintenanceClearance,
                ManufacturerCode     = this.ManufacturerCode,
                CatalogueRef         = this.CatalogueRef,
                BoxDepthMm           = this.BoxDepthMm,
                GangCount            = this.GangCount,
                ModulePitchMm        = this.ModulePitchMm,
                MountType            = this.MountType,
                InsertionOrigin      = this.InsertionOrigin,
                PlasterOffsetMode    = this.PlasterOffsetMode,
                PlasterOffsetFixedMm = this.PlasterOffsetFixedMm,
                TwoPhaseEnabled      = this.TwoPhaseEnabled,
                ConstructionPhase    = this.ConstructionPhase,
                CompletionPhase      = this.CompletionPhase,
                BoxFamilyTypeRegex   = this.BoxFamilyTypeRegex,
                BoxLocationIdParam   = this.BoxLocationIdParam,
                IsClusterMember      = this.IsClusterMember,
                ClusterGroupId       = this.ClusterGroupId,
                ClusterSlotIndex     = this.ClusterSlotIndex,
                ClusterTotalSlots    = this.ClusterTotalSlots,
                ClusterFrameWidthMm  = this.ClusterFrameWidthMm,
                CeilingTileSnap      = this.CeilingTileSnap,
                TileGridSpacingXMm   = this.TileGridSpacingXMm,
                TileGridSpacingYMm   = this.TileGridSpacingYMm,
                StructuralFixingCheck = this.StructuralFixingCheck,
                JoistClearanceMm     = this.JoistClearanceMm,
                EmitNogginRequirement = this.EmitNogginRequirement,
                WetZoneExclude       = this.WetZoneExclude,
                WetZoneClass         = this.WetZoneClass,
                HeightStandardRef    = this.HeightStandardRef,
                PerPupil             = this.PerPupil,
                PerToiletCubicle     = this.PerToiletCubicle,
                PerBed               = this.PerBed,
                PerWorkstation       = this.PerWorkstation,
                OccupancyParamName   = this.OccupancyParamName,
                BuildingTypeTable    = this.BuildingTypeTable,
                BuildingType         = this.BuildingType,
                ApplicableStandards  = this.ApplicableStandards,
                IpRatingMin          = this.IpRatingMin,
                WetZoneExclusion     = this.WetZoneExclusion,
                AccessibilityCheck   = this.AccessibilityCheck,
                HeightStandard       = this.HeightStandard,
                MaxSlotsMm           = this.MaxSlotsMm,
                TwoPartAudit         = this.TwoPartAudit,
                MinSlopePercent      = this.MinSlopePercent,
                InsulationThicknessMm = this.InsulationThicknessMm,
                MinUniformityRatio   = this.MinUniformityRatio,
                CableBundleAdvisoryCount = this.CableBundleAdvisoryCount,
                NominalDiameterMm    = this.NominalDiameterMm,
                MountingContext      = this.MountingContext,
                ExposureClass        = this.ExposureClass,
                EmitSupports         = this.EmitSupports,
                SourcePack           = this.SourcePack,
                Material             = this.Material,
                // Footprint-aware spacing
                FamilyBboxAware      = this.FamilyBboxAware,
                ReferenceFootprintMm = this.ReferenceFootprintMm,
                MinSymbolFootprintMm = this.MinSymbolFootprintMm,
                MaxFootprintScale    = this.MaxFootprintScale,
                // Type catalog
                TypeCatalogKey       = this.TypeCatalogKey,
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
