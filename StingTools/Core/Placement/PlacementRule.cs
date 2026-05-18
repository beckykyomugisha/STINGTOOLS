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

        // ── Mounting context / chase routing ────────────────────────

        /// <summary>Mounting context: SURFACE / CHASED / SUSPENDED / RECESSED / PENDANT. Used by WallFollowerRouter, InWallChaseRouter, AutoConduitDrop to select routing strategy.</summary>
        public string MountingContext { get; set; } = "";

        /// <summary>When true, WallFollowerRouter invokes RoutingSupportPlacer after placing each run segment.</summary>
        public bool EmitSupports { get; set; } = false;

        /// <summary>Minimum slope in percent (BS EN 12056-2). 0 = no slope check.</summary>
        public double MinSlopePercent { get; set; } = 0.0;

        /// <summary>Nominal conduit / pipe outer diameter in mm. Used for chase depth calculation and stand-off calculation.</summary>
        public double NominalDiameterMm { get; set; } = 0.0;

        /// <summary>Insulation thickness around the pipe / conduit in mm; added to NominalDiameterMm for chase budget. Also acts as insulation fallback in InWallChaseRouter when 0.</summary>
        public double InsulationThicknessMm { get; set; } = 0.0;

        /// <summary>Face from which the route offset is measured: INTERIOR / BOTTOM / EXTERIOR. Controls sign of the WallFollowerRouter offset.</summary>
        public string RouteFace { get; set; } = "INTERIOR";

        /// <summary>Minimum bend radius in mm for conduit / pipe routes. 0 = use type default.</summary>
        public double RouteMinBendRadiusMm { get; set; } = 0.0;

        // ── Manufacturer catalogue (PC-18) ───────────────────────────

        /// <summary>Manufacturer code for catalogue lookup (e.g. "MK"). Empty = use generic family resolution.</summary>
        public string ManufacturerCode { get; set; } = "";

        /// <summary>Catalogue reference number for the specific product variant.</summary>
        public string CatalogueRef { get; set; } = "";

        /// <summary>Box / backbox depth in mm from the manufacturer catalogue. Used as pipe OD fallback in InWallChaseRouter when NominalDiameterMm is 0.</summary>
        public double BoxDepthMm { get; set; } = 0.0;

        /// <summary>Number of gang positions in a socket / switch plate (1 / 2 / 3 / 4). 0 = single.</summary>
        public int GangCount { get; set; } = 0;

        /// <summary>Module pitch in mm for MK / Schneider modular cluster frames.</summary>
        public double ModulePitchMm { get; set; } = 0.0;

        /// <summary>Mount type from catalogue (e.g. SURFACE_BOX / FLUSH_BOX / GRID_SWITCH).</summary>
        public string MountType { get; set; } = "";

        /// <summary>Insertion origin reference from catalogue (e.g. CENTRE / BOTTOM_LEFT).</summary>
        public string InsertionOrigin { get; set; } = "";

        // ── Plaster / finish-face offset (Phase 139.2) ──────────────

        /// <summary>None / Fixed / Auto. Controls how PlasterOffsetResolver derives the finish-face offset.</summary>
        public string PlasterOffsetMode { get; set; } = "None";

        /// <summary>Fixed plaster offset distance in mm. Used when PlasterOffsetMode = Fixed.</summary>
        public double PlasterOffsetFixedMm { get; set; } = 0.0;

        // ── Eurocode exposure / structural cover ─────────────────────

        /// <summary>Eurocode 2 exposure class for concrete cover calculation (e.g. "XC2"). Used by InWallChaseRouter and ConcreteCoverTable.</summary>
        public string ExposureClass { get; set; } = "";

        // ── Coverage grid extensions ─────────────────────────────────

        /// <summary>Wall clearance in mm for coverage-grid candidates; candidates closer than this to a wall face are rejected.</summary>
        public double WallClearanceMm { get; set; } = 0.0;

        /// <summary>Clearance in mm from obstructions (beams, columns) in coverage-grid pass. Also used as insulation fallback in InWallChaseRouter.</summary>
        public double ObstructionClearanceMm { get; set; } = 0.0;

        // ── Lighting grid (ceiling tile) ─────────────────────────────

        /// <summary>When true, LightingGridCalculator snaps candidate points to the nearest ceiling-tile grid intersection.</summary>
        public bool CeilingTileSnap { get; set; } = false;

        /// <summary>When true, LightingGridCalculator checks for structural joists and flags noggin requirements.</summary>
        public bool StructuralFixingCheck { get; set; } = false;

        /// <summary>Ceiling tile grid X spacing in mm (0 = use default 1200mm / 600mm from ceiling type).</summary>
        public double TileGridSpacingXMm { get; set; } = 0.0;

        /// <summary>Ceiling tile grid Y spacing in mm (0 = use default 1200mm / 600mm from ceiling type).</summary>
        public double TileGridSpacingYMm { get; set; } = 0.0;

        /// <summary>Clearance in mm to structural joists for structural fixing check. 0 = use grid default.</summary>
        public double JoistClearanceMm { get; set; } = 0.0;

        /// <summary>When true, LightingGridCalculator flags points that require a noggin in the NogginRequiredPoints output.</summary>
        public bool EmitNogginRequirement { get; set; } = false;

        /// <summary>Minimum uniformity ratio (Emin/Eav) override for the BS EN 12464-1 lighting calculator. 0 = use standard default.</summary>
        public double MinUniformityRatio { get; set; } = 0.0;

        // ── Two-phase construction extensions (PC-16) ────────────────

        /// <summary>Shared parameter name for the two-phase box location id. Defaults to ParamRegistry.BOX_LOCATION_ID.</summary>
        public string BoxLocationIdParam { get; set; } = "";

        /// <summary>FamilySymbol.Name regex for locating the first-fix back-box family in TwoPhaseBoxPlacer.</summary>
        public string BoxFamilyTypeRegex { get; set; } = "";

        /// <summary>Revit phase name for the second-fix placement step in TwoPhaseBoxPlacer. Distinct from ConstructionPhase which controls first-fix targeting.</summary>
        public string CompletionPhase { get; set; } = "";

        // ── Cluster placement extensions (PC-17) ─────────────────────

        /// <summary>Total number of slots in the cluster frame. 0 = auto from catalogue.</summary>
        public int ClusterTotalSlots { get; set; } = 0;

        /// <summary>This rule's slot index within the cluster frame (0-based).</summary>
        public int ClusterSlotIndex { get; set; } = 0;

        /// <summary>Overall width of the cluster frame in mm (used for collision / spacing checks).</summary>
        public double ClusterFrameWidthMm { get; set; } = 0.0;

        // ── Window geometry anchors ──────────────────────────────────

        /// <summary>Window sill height override in mm above FFL. 0 = read from window parameter.</summary>
        public double SillHeightMm { get; set; } = 0.0;

        /// <summary>Window head height override in mm above FFL. 0 = read from window parameter.</summary>
        public double HeadHeightMm { get; set; } = 0.0;

        /// <summary>Window cill-to-floor distance in mm (for glazing specifications / BS 8300 checks).</summary>
        public double CillToFloorMm { get; set; } = 0.0;

        /// <summary>When true, glazing associated with this rule must be toughened (BS 6206 / BS EN 12150).</summary>
        public bool ToughenedGlazingRequired { get; set; } = false;

        /// <summary>Glazing specification code (e.g. "CLEAR_6MM_LAMINATED").</summary>
        public string GlazingSpec { get; set; } = "";

        // ── Height standards / accessibility ─────────────────────────

        /// <summary>Named height standard for accessibility audit (e.g. "BS8300_SWITCH_1200_1400"). Used by HeightStandardsTable.ValidateRulesAgainstStandards.</summary>
        public string HeightStandard { get; set; } = "";

        /// <summary>Alternative reference to a named row in STING_HEIGHT_STANDARDS.json. Used by Excel export for cross-referencing.</summary>
        public string HeightStandardRef { get; set; } = "";

        // ── Wet zone / IP rating ─────────────────────────────────────

        /// <summary>BS 7671 / IEC 60364-7-701 wet zone exclusion level: NONE / Z0 / Z1 / Z2 / Z3 (higher excludes more). NONE = no exclusion.</summary>
        public string WetZoneExclusion { get; set; } = "NONE";

        /// <summary>When true, this rule's placements are excluded from wet zones per WetZoneExclusion.</summary>
        public bool WetZoneExclude { get; set; } = false;

        /// <summary>BS 7671 wet zone class string (e.g. "BS7671_Z1") for fine-grained exclusion matching.</summary>
        public string WetZoneClass { get; set; } = "";

        /// <summary>Minimum IP rating required for this fixture in the installed location (e.g. "IP44"). Empty = no check.</summary>
        public string IpRatingMin { get; set; } = "";

        // ── Standards / compliance ───────────────────────────────────

        /// <summary>Building type this rule applies to (e.g. "RESIDENTIAL" / "COMMERCIAL" / "HEALTHCARE"). Empty = applies to all.</summary>
        public string BuildingType { get; set; } = "";

        /// <summary>Reference to a building-type → standard mapping table (e.g. "BS8300_TABLE_3"). Used by HeightStandardsTable.</summary>
        public string BuildingTypeTable { get; set; } = "";

        /// <summary>Pipe-delimited list of applicable standards (e.g. "BS7671|BS8300|HTM06-01").</summary>
        public string ApplicableStandards { get; set; } = "";

        /// <summary>When true, the placement engine checks BS 8300 / AD M accessibility compliance for this rule's mounting height.</summary>
        public bool AccessibilityCheck { get; set; } = false;

        // ── Post-placement / COBie / IFC ─────────────────────────────

        /// <summary>Tag written to the placed instance after placement for downstream audit (e.g. "ELECTRICAL_SOCKET_V4"). Used by PostPlacementHooks.</summary>
        public string PostAuditTag { get; set; } = "";

        /// <summary>When true, PostPlacementHooks seeds COBIE_COMPONENT_* parameters from this rule.</summary>
        public bool RequiresCOBieFields { get; set; } = false;

        /// <summary>When true, IFC property sets required by this rule must be present on the placed element.</summary>
        public bool RequiresIfcMapping { get; set; } = false;

        /// <summary>Maintenance clearance zone in mm around the placed fixture (used in clash/space validation).</summary>
        public double MaintenanceClearance { get; set; } = 0.0;

        /// <summary>BS 7671 Table 4 cable derating advisory: warn when this many or more same-category fixtures are bundled within 300 mm. 0 = no advisory.</summary>
        public int CableBundleAdvisoryCount { get; set; } = 0;

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
                RuleId                  = this.RuleId,
                RuleKind                = this.RuleKind,
                CategoryFilter          = this.CategoryFilter,
                VariantHint             = this.VariantHint,
                FamilyTypeRegex         = this.FamilyTypeRegex,
                SourcePack              = this.SourcePack,
                RoomFilter              = this.RoomFilter,
                ExcludeRoomFilter       = this.ExcludeRoomFilter,
                RoomDepartmentFilter    = this.RoomDepartmentFilter,
                MinAreaM2               = this.MinAreaM2,
                MaxAreaM2               = this.MaxAreaM2,
                LevelFilter             = this.LevelFilter,
                PhaseFilter             = this.PhaseFilter,
                WorksetFilter           = this.WorksetFilter,
                AnchorType              = this.AnchorType,
                MountingReference       = this.MountingReference,
                OffsetXMm               = this.OffsetXMm,
                OffsetYMm               = this.OffsetYMm,
                OffsetZMm               = this.OffsetZMm,
                RotationDeg             = this.RotationDeg,
                ToleranceMm             = this.ToleranceMm,
                MountingHeightMm        = this.MountingHeightMm,
                SideConstraint          = this.SideConstraint,
                MinSpacingMm            = this.MinSpacingMm,
                MaxSpacingMm            = this.MaxSpacingMm,
                MaxPerRoom              = this.MaxPerRoom,
                PerAreaM2               = this.PerAreaM2,
                PerOccupant             = this.PerOccupant,
                PerBed                  = this.PerBed,
                PerWorkstation          = this.PerWorkstation,
                PerPupil                = this.PerPupil,
                PerToiletCubicle        = this.PerToiletCubicle,
                OccupancyParamName      = this.OccupancyParamName,
                PerLinearMetre          = this.PerLinearMetre,
                CoverageRadiusMm        = this.CoverageRadiusMm,
                GuaranteeCoverage       = this.GuaranteeCoverage,
                RoutingMode             = this.RoutingMode,
                RouteOffsetMm           = this.RouteOffsetMm,
                RouteSegmentCategory    = this.RouteSegmentCategory,
                TwoPhaseEnabled         = this.TwoPhaseEnabled,
                ConstructionPhase       = this.ConstructionPhase,
                IsClusterMember         = this.IsClusterMember,
                ClusterGroupId          = this.ClusterGroupId,
                DependsOn               = this.DependsOn,
                RelativeTo              = this.RelativeTo,
                CoPlaceWith             = new List<string>(this.CoPlaceWith ?? new List<string>()),
                ConflictsWith           = new List<string>(this.ConflictsWith ?? new List<string>()),
                Priority                = this.Priority,
                StandardRef             = this.StandardRef,
                UniclassPr              = this.UniclassPr,
                Notes                   = this.Notes,
                // Extended properties
                MountingContext         = this.MountingContext,
                EmitSupports            = this.EmitSupports,
                MinSlopePercent         = this.MinSlopePercent,
                NominalDiameterMm       = this.NominalDiameterMm,
                InsulationThicknessMm   = this.InsulationThicknessMm,
                RouteFace               = this.RouteFace,
                RouteMinBendRadiusMm    = this.RouteMinBendRadiusMm,
                ManufacturerCode        = this.ManufacturerCode,
                CatalogueRef            = this.CatalogueRef,
                BoxDepthMm              = this.BoxDepthMm,
                GangCount               = this.GangCount,
                ModulePitchMm           = this.ModulePitchMm,
                MountType               = this.MountType,
                InsertionOrigin         = this.InsertionOrigin,
                PlasterOffsetMode       = this.PlasterOffsetMode,
                PlasterOffsetFixedMm    = this.PlasterOffsetFixedMm,
                ExposureClass           = this.ExposureClass,
                WallClearanceMm         = this.WallClearanceMm,
                ObstructionClearanceMm  = this.ObstructionClearanceMm,
                CeilingTileSnap         = this.CeilingTileSnap,
                StructuralFixingCheck   = this.StructuralFixingCheck,
                TileGridSpacingXMm      = this.TileGridSpacingXMm,
                TileGridSpacingYMm      = this.TileGridSpacingYMm,
                JoistClearanceMm        = this.JoistClearanceMm,
                EmitNogginRequirement   = this.EmitNogginRequirement,
                MinUniformityRatio      = this.MinUniformityRatio,
                BoxLocationIdParam      = this.BoxLocationIdParam,
                BoxFamilyTypeRegex      = this.BoxFamilyTypeRegex,
                CompletionPhase         = this.CompletionPhase,
                ClusterTotalSlots       = this.ClusterTotalSlots,
                ClusterSlotIndex        = this.ClusterSlotIndex,
                ClusterFrameWidthMm     = this.ClusterFrameWidthMm,
                SillHeightMm            = this.SillHeightMm,
                HeadHeightMm            = this.HeadHeightMm,
                CillToFloorMm           = this.CillToFloorMm,
                ToughenedGlazingRequired = this.ToughenedGlazingRequired,
                GlazingSpec             = this.GlazingSpec,
                HeightStandard          = this.HeightStandard,
                HeightStandardRef       = this.HeightStandardRef,
                WetZoneExclusion        = this.WetZoneExclusion,
                WetZoneExclude          = this.WetZoneExclude,
                WetZoneClass            = this.WetZoneClass,
                IpRatingMin             = this.IpRatingMin,
                BuildingType            = this.BuildingType,
                BuildingTypeTable       = this.BuildingTypeTable,
                ApplicableStandards     = this.ApplicableStandards,
                AccessibilityCheck      = this.AccessibilityCheck,
                PostAuditTag            = this.PostAuditTag,
                RequiresCOBieFields     = this.RequiresCOBieFields,
                RequiresIfcMapping      = this.RequiresIfcMapping,
                MaintenanceClearance    = this.MaintenanceClearance,
                CableBundleAdvisoryCount = this.CableBundleAdvisoryCount,
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
