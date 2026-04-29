// StingTools — Drawing Template Manager
//
// DrawingType is the single source of truth for "how a drawing should
// look". It bundles every knob a generation command needs — sheet size,
// title block, scale, view template, slot layout, annotation rule pack,
// crop strategy, section-marker family, numbering patterns, viewport
// type — so that any command that produces drawings (fabrication,
// batch sections/elevations, sheet manager, doc automation) can resolve
// one profile and get a perfectly-presented, consistently-numbered
// drawing out the other end.
//
// See Core/Drawing/DrawingTypeRegistry.cs for loader + 15 built-in
// defaults, Core/Drawing/DrawingDispatcher.cs for (discipline, phase,
// docType) routing, and Core/Drawing/DrawingTypeValidator.cs for the
// pre-flight check that runs before any batch generation.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    // ─────────────────────────────────────────────────────────────────────
    //  ROOT LIBRARY
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Root JSON document loaded from Data/STING_DRAWING_TYPES.json and,
    /// when present, a project-scoped override at
    /// &lt;project&gt;/_BIM_COORD/drawing_types.json. The registry merges
    /// the two, with project entries winning on the "id" key.
    /// </summary>
    public sealed class DrawingTypeLibrary
    {
        [JsonProperty("version")]       public int Version { get; set; } = 1;
        [JsonProperty("drawingTypes")]  public List<DrawingType> DrawingTypes { get; set; } = new List<DrawingType>();
        [JsonProperty("routing")]       public List<DrawingRoutingRule> Routing { get; set; } = new List<DrawingRoutingRule>();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PURPOSE — used both as a fast filter and to drive the
    //  Viewport-type + Annotation-rule defaults that ship with each
    //  built-in. Values are case-insensitive on the way in.
    // ─────────────────────────────────────────────────────────────────────

    public static class DrawingPurpose
    {
        public const string Plan         = "Plan";
        public const string Rcp          = "RCP";
        public const string Section      = "Section";
        public const string Elevation    = "Elevation";
        public const string Detail       = "Detail";
        public const string Schedule     = "Schedule";
        public const string Spool        = "Spool";
        public const string Coordination = "Coordination";
        public const string Legend       = "Legend";
        public const string ThreeD       = "3D";
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DRAWING TYPE — the main record
    // ─────────────────────────────────────────────────────────────────────

    public sealed class DrawingType
    {
        // Identity
        [JsonProperty("id")]          public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("origin")]      public string Origin { get; set; } = "corporate"; // corporate | project
        [JsonProperty("purpose")]     public string Purpose { get; set; } = DrawingPurpose.Plan;

        // Routing / selection
        [JsonProperty("discipline")]  public string Discipline { get; set; } = "*";
        [JsonProperty("phase")]       public string Phase { get; set; } = "*";

        // Sheet
        [JsonProperty("paperSize")]        public string PaperSize { get; set; } = "A1";
        [JsonProperty("titleBlockFamily")] public string TitleBlockFamily { get; set; }
        [JsonProperty("orientation")]      public string Orientation { get; set; } = "Landscape";

        // Views
        [JsonProperty("scale")]            public int Scale { get; set; } = 100; // 1:N
        [JsonProperty("detailLevel")]      public string DetailLevel { get; set; } = "Medium"; // Coarse | Medium | Fine
        [JsonProperty("viewTemplateName")] public string ViewTemplateName { get; set; }
        [JsonProperty("viewportTypeName")] public string ViewportTypeName { get; set; }

        /// <summary>
        /// References a <see cref="ViewStylePack"/> by id. The pack
        /// supplies shared graphic overrides / filters / VG overrides
        /// / text + dim styles. Null = no pack applied (profile carries
        /// its own appearance).
        /// </summary>
        [JsonProperty("viewStylePackId", NullValueHandling = NullValueHandling.Ignore)]
        public string ViewStylePackId { get; set; }

        /// <summary>
        /// Profile inheritance — a child DrawingType's Extends names a
        /// parent id; the registry walks the chain at load-time so
        /// resolvers see a merged snapshot. Mirrors ViewStylePack.Extends.
        /// </summary>
        [JsonProperty("extends", NullValueHandling = NullValueHandling.Ignore)]
        public string Extends { get; set; }

        /// <summary>
        /// Map of title-block instance parameter name → value template.
        /// Applied to the title-block FamilyInstance at sheet-creation
        /// time by <see cref="TitleBlockParamApplier"/>. Value template
        /// supports <c>${ProjectInfoParam}</c> substitution and the
        /// standard <c>{disc}/{lvl}/{seq:Dn}/{mark}</c> token set from
        /// the caller's pattern context.
        /// </summary>
        [JsonProperty("titleBlockParams", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> TitleBlockParams { get; set; }

        /// <summary>
        /// ISO 19650 naming — optional per-profile convention payload.
        /// When set, the editor's Numbering card's "Generate ISO
        /// pattern" button assembles SheetNumberPattern from these
        /// fields using the standard
        ///   {project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}-{suit}-{rev}
        /// template. Values also flow through into the
        /// TitleBlockParams map so title-block cells read the same
        /// codes the sheet number does.
        /// </summary>
        [JsonProperty("isoNaming", NullValueHandling = NullValueHandling.Ignore)]
        public IsoNaming IsoNaming { get; set; }

        // Numbering
        [JsonProperty("sheetNumberPattern")] public string SheetNumberPattern { get; set; } = "{disc}-{seq:D3}";
        [JsonProperty("sheetNamePattern")]   public string SheetNamePattern { get; set; }   = "{discipline} {purpose} - {lvl}";

        // Crop
        [JsonProperty("crop")]       public DrawingCropStrategy Crop { get; set; } = new DrawingCropStrategy();

        // Section / callout markers (only used by Section / Elevation / Detail purposes)
        [JsonProperty("sectionMarker")] public SectionMarkerSpec SectionMarker { get; set; } = new SectionMarkerSpec();

        // Slot layout (where views land on the sheet, normalised 0..1 over the drawable zone)
        [JsonProperty("slots")]      public List<DrawingSlot> Slots { get; set; } = new List<DrawingSlot>();

        // Annotation rule pack
        [JsonProperty("annotation")] public AnnotationRulePack Annotation { get; set; } = new AnnotationRulePack();

        /// <summary>
        /// Phase 135 — token-level annotation profile. Controls TAG7
        /// presentation mode, paragraph depth (global + per-category),
        /// TAG7 section A–F visibility, tag size/style/colour preset,
        /// per-view variable colour scheme, and 8-segment tag mask.
        /// Null = inherit from <see cref="ViewStylePack"/> defaults
        /// (tagColorScheme / defaultTagStyle / categoryTagStyles).
        /// Applied between style-pack (step 7) and annotation (step 8)
        /// via <c>TokenProfileApplier</c>.
        /// </summary>
        [JsonProperty("tokenProfile", NullValueHandling = NullValueHandling.Ignore)]
        public AnnotationTokenProfile TokenProfile { get; set; }

        // Print / appearance overrides
        [JsonProperty("print")]      public PrintOverride Print { get; set; } = new PrintOverride();

        /// <summary>
        /// Phase 137 — production rules. One DrawingType can produce
        /// multiple companion views (e.g. one plan + one RCP + one
        /// section). Each rule yields one view with optional per-rule
        /// overrides, slotting into the parent profile's slot[idx]
        /// when SlotIndex is non-negative.
        /// </summary>
        [JsonProperty("productionRules", NullValueHandling = NullValueHandling.Ignore)]
        public List<ProductionRule> ProductionRules { get; set; }

        /// <summary>
        /// Phase 137 — drawing package this profile belongs to. The
        /// production engine groups views/sheets onto a sheet by
        /// package id (e.g. "Issue-A-Architectural").
        /// </summary>
        [JsonProperty("packageId", NullValueHandling = NullValueHandling.Ignore)]
        public string PackageId { get; set; }

        // Integrity hash used by the corporate-lock mechanism —
        // DrawingTypeRegistry writes it on load for corporate types and
        // compares on save to detect out-of-band edits.
        [JsonProperty("checksum", NullValueHandling = NullValueHandling.Ignore)]
        public string Checksum { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ISO 19650 NAMING — per-profile convention payload used to
    //  build a compliant SheetNumberPattern and flow the matching
    //  codes into TitleBlockParams.
    // ─────────────────────────────────────────────────────────────────────

    public sealed class IsoNaming
    {
        [JsonProperty("volume",      NullValueHandling = NullValueHandling.Ignore)] public string Volume { get; set; }       // e.g. "01", "ZZ"
        [JsonProperty("type",        NullValueHandling = NullValueHandling.Ignore)] public string Type { get; set; }         // DR / SH / M3 / VS / CA / SP
        [JsonProperty("role",        NullValueHandling = NullValueHandling.Ignore)] public string Role { get; set; }         // A / S / M / E / P / FP
        [JsonProperty("suitability", NullValueHandling = NullValueHandling.Ignore)] public string Suitability { get; set; }  // S0..S7 / A1..A5 / B1..B5 / C1..C3
        [JsonProperty("revision",    NullValueHandling = NullValueHandling.Ignore)] public string Revision { get; set; }     // P01, P02, C01
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SLOT — where a view of a given ViewType lands on the sheet.
    //  Coordinates are fractions of the drawable zone (0..1) so the same
    //  DrawingType works across A1 / A2 / A3 title blocks without edits.
    //  DrawingSlot is a superset of the existing Docs/TemplateViewSlot;
    //  future work folds SheetTemplateEngine into this engine.
    // ─────────────────────────────────────────────────────────────────────

    public sealed class DrawingSlot
    {
        [JsonProperty("label")]    public string Label { get; set; }
        [JsonProperty("viewType")] public string ViewType { get; set; } // Plan | Section | Elevation | 3D | ISO | Schedule | Legend | RCP | Detail
        [JsonProperty("normX")]    public double NormX { get; set; }
        [JsonProperty("normY")]    public double NormY { get; set; }
        [JsonProperty("normW")]    public double NormW { get; set; }
        [JsonProperty("normH")]    public double NormH { get; set; }

        // Per-slot overrides (defaults to the DrawingType's top-level values)
        [JsonProperty("scale",           NullValueHandling = NullValueHandling.Ignore)] public int?    Scale { get; set; }
        [JsonProperty("detailLevel",     NullValueHandling = NullValueHandling.Ignore)] public string DetailLevel { get; set; }
        [JsonProperty("viewTemplate",    NullValueHandling = NullValueHandling.Ignore)] public string ViewTemplate { get; set; }
        [JsonProperty("viewportType",    NullValueHandling = NullValueHandling.Ignore)] public string ViewportType { get; set; }

        [JsonProperty("required")] public bool Required { get; set; } = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CROP STRATEGY
    // ─────────────────────────────────────────────────────────────────────

    public sealed class DrawingCropStrategy
    {
        /// <summary>
        /// ScopeBox       – use the named scope box, error if missing
        /// ScopeBoxOrBbox – use scope box if present, else tight bbox + margin
        /// TightBbox      – bounding box of elements + margin
        /// RoomBoundary   – union of room boundaries + margin (plans only)
        /// None           – leave the view's default crop alone
        /// </summary>
        [JsonProperty("kind")]        public string Kind { get; set; } = "ScopeBoxOrBbox";
        [JsonProperty("scopeBoxName")] public string ScopeBoxName { get; set; }
        [JsonProperty("marginMm")]    public double MarginMm { get; set; } = 150.0;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SECTION / ELEVATION MARKER
    // ─────────────────────────────────────────────────────────────────────

    public sealed class SectionMarkerSpec
    {
        [JsonProperty("family")]      public string Family { get; set; }
        [JsonProperty("markPrefix")]  public string MarkPrefix { get; set; } = "S";
        [JsonProperty("bubbleStyle")] public string BubbleStyle { get; set; } = "Filled";
        [JsonProperty("farClipMm")]   public double FarClipMm { get; set; } = 3000.0;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ANNOTATION RULE PACK + AUTO-ANNOTATION RULE — moved to
    //  Core/Drawing/AnnotationRulePack.cs in Phase 137.
    // ─────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────
    //  ANNOTATION TOKEN PROFILE — Phase 135
    //
    //  Per-DrawingType token-level presentation knobs. Distinct from the
    //  rule-pack (which decides WHAT to tag) — this one decides HOW the
    //  resulting tags read on the sheet: how many paragraph tiers are
    //  visible, which TAG7 sub-sections show, what size/style/colour the
    //  tag text is, and which 8-segment tokens the displayed tag carries.
    //
    //  Every field is optional. Null means "inherit / don't override":
    //    * Pack-level defaults on the resolved ViewStylePack apply when
    //      this profile leaves a slot empty.
    //    * Where neither profile nor pack sets a value, the engine
    //      leaves the underlying parameter alone.
    //
    //  Applied by <c>TokenProfileApplier</c> in step 7.5 of the pipeline,
    //  between ViewStylePack (step 7) and Annotation (step 8).
    // ─────────────────────────────────────────────────────────────────────

    public sealed class AnnotationTokenProfile
    {
        /// <summary>
        /// Presentation mode preset — Compact / Technical / FullSpec /
        /// Presentation / BOQ. Drives the global TAG_PARA_STATE_*
        /// pattern and TAG_WARN_VISIBLE flag through the same engine
        /// the existing SetPresentationModeCommand uses. Null = leave
        /// preset alone, write the explicit fields below instead.
        /// </summary>
        [JsonProperty("presentationMode", NullValueHandling = NullValueHandling.Ignore)]
        public string PresentationMode { get; set; }

        /// <summary>
        /// Global TAG7 paragraph depth (1..10). Sets PARA_STATE_1..N to
        /// Yes and the rest to No. Null = leave as-is. Per-category
        /// overrides (<see cref="CategoryDepths"/>) win over this when
        /// present.
        /// </summary>
        [JsonProperty("paraDepth", NullValueHandling = NullValueHandling.Ignore)]
        public int? ParaDepth { get; set; }

        /// <summary>
        /// Per-category TAG7 paragraph depth override (1..10). Lookup
        /// by category display name. Empty / missing key falls back to
        /// <see cref="ParaDepth"/>.
        /// </summary>
        [JsonProperty("categoryDepths", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, int> CategoryDepths { get; set; }

        /// <summary>
        /// TAG7 sub-section visibility map. Keys: "A".."F" (case-
        /// insensitive). Values: true = visible, false = hidden. Writes
        /// TAG_7_SECTION_VISIBLE_{A..F}_BOOL on every element in scope.
        /// Missing key = leave as-is.
        /// </summary>
        [JsonProperty("sectionVisibility", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, bool> SectionVisibility { get; set; }

        /// <summary>
        /// Tag size preset — "2", "2.5", "3", "3.5". Combined with
        /// <see cref="TagStyle"/> + <see cref="TagColor"/> to pick the
        /// active TAG_{size}{style}_{color}_BOOL. Null = leave as-is.
        /// </summary>
        [JsonProperty("tagSize", NullValueHandling = NullValueHandling.Ignore)]
        public string TagSize { get; set; }

        /// <summary>
        /// Tag style preset — "NOM" / "BOLD" / "ITALIC" / "BOLDITALIC".
        /// Null = leave as-is.
        /// </summary>
        [JsonProperty("tagStyle", NullValueHandling = NullValueHandling.Ignore)]
        public string TagStyle { get; set; }

        /// <summary>
        /// Tag colour preset — "BLACK" / "BLUE" / "GREEN" / "RED" /
        /// "ORANGE" / "GREY" / "PURPLE" / "YELLOW". Null = leave as-is.
        /// </summary>
        [JsonProperty("tagColor", NullValueHandling = NullValueHandling.Ignore)]
        public string TagColor { get; set; }

        /// <summary>
        /// Per-view variable colour scheme to write into
        /// STING_VIEW_TAG_STYLE — picks up TagStyleEngine's
        /// VariableSchemes (System / Status / Zone / Level / Location
        /// / Function) or BuiltInSchemes (Discipline / Warm / Cool /
        /// Red / Yellow / Blue / Mono / Dark). Null = leave the view
        /// param alone.
        /// </summary>
        [JsonProperty("colorScheme", NullValueHandling = NullValueHandling.Ignore)]
        public string ColorScheme { get; set; }

        /// <summary>
        /// 8-segment tag mask string of 1/0 chars selecting which
        /// DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ tokens render in the
        /// displayed tag. Example "10000001" = DISC + SEQ only. Writes
        /// <c>TAG_SEG_MASK_TXT</c>. Null = leave as-is. Length must be
        /// 8; shorter / longer values are ignored with a warning.
        /// </summary>
        [JsonProperty("segmentMask", NullValueHandling = NullValueHandling.Ignore)]
        public string SegmentMask { get; set; }

        /// <summary>
        /// Display mode integer (1..5) written to
        /// <c>STING_DISPLAY_MODE</c>. 1=SEQ, 2=PROD-SEQ, 3=DISC-SYS-SEQ,
        /// 4=DISC-PROD-SEQ, 5=Full 8-segment. Null = leave as-is.
        /// </summary>
        [JsonProperty("displayMode", NullValueHandling = NullValueHandling.Ignore)]
        public int? DisplayMode { get; set; }

        /// <summary>
        /// Phase 165 — T4-T10 payload pattern mode. One of "HANDOVER" /
        /// "DC" / "CUSTOM" (case-insensitive). Drives which payload set
        /// renders for tier 4-10 by writing the
        /// <c>HANDOVER_MODE_HANDOVER_BOOL</c> /
        /// <c>HANDOVER_MODE_DC_BOOL</c> /
        /// <c>HANDOVER_MODE_CUSTOM_BOOL</c> trio mutually exclusively on
        /// every element type used in the view. DC is the default at the
        /// pipeline level (TagConfig.ResolveActivePatternMode), so leaving
        /// this null means "use whatever the project / type already says",
        /// which is DC unless explicitly overridden elsewhere. Most
        /// production drawings will pin "DC" here so the live design &amp;
        /// construction T4-T10 payload is forced regardless of any leftover
        /// HANDOVER toggles from prior workflows. Set to "HANDOVER" for
        /// post-construction handover packages, "CUSTOM" for project-
        /// specific tier content.
        /// </summary>
        [JsonProperty("patternMode", NullValueHandling = NullValueHandling.Ignore)]
        public string PatternMode { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PRODUCTION RULE — Phase 137
    //
    //  One rule per companion view a single DrawingType produces. The
    //  parent profile defines slot geometry, scale, etc; each rule may
    //  optionally override scale / detail level / view template / pack
    //  / annotation / phase per produced view, and pin itself to a
    //  specific slot index.
    // ─────────────────────────────────────────────────────────────────────

    public sealed class ProductionRule
    {
        [JsonProperty("idx")]       public int    Idx { get; set; }
        [JsonProperty("viewType")]  public string ViewType { get; set; }
        [JsonProperty("nameSuffix",            NullValueHandling = NullValueHandling.Ignore)] public string NameSuffix { get; set; }
        [JsonProperty("scaleOverride",         NullValueHandling = NullValueHandling.Ignore)] public int?   ScaleOverride { get; set; }
        [JsonProperty("detailLevelOverride",   NullValueHandling = NullValueHandling.Ignore)] public string DetailLevelOverride { get; set; }
        [JsonProperty("viewTemplateOverride",  NullValueHandling = NullValueHandling.Ignore)] public string ViewTemplateOverride { get; set; }
        [JsonProperty("viewStylePackOverride", NullValueHandling = NullValueHandling.Ignore)] public string ViewStylePackOverride { get; set; }
        [JsonProperty("annotationOverride",    NullValueHandling = NullValueHandling.Ignore)] public AnnotationRulePack AnnotationOverride { get; set; }
        [JsonProperty("phaseOverride",         NullValueHandling = NullValueHandling.Ignore)] public string PhaseOverride { get; set; }
        [JsonProperty("required")]  public bool Required { get; set; } = true;
        [JsonProperty("slotIndex")] public int  SlotIndex { get; set; } = -1;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PRINT OVERRIDE
    // ─────────────────────────────────────────────────────────────────────

    public sealed class PrintOverride
    {
        [JsonProperty("colourScheme",    NullValueHandling = NullValueHandling.Ignore)] public string ColourScheme { get; set; }
        [JsonProperty("lineWeightScale", NullValueHandling = NullValueHandling.Ignore)] public double? LineWeightScale { get; set; }
        [JsonProperty("halftoneLinks")]  public bool HalftoneLinks { get; set; } = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ROUTING RULE — resolves (discipline, phase, docType) → DrawingType
    //  Rules are evaluated in order, first match wins. "*" wildcards.
    // ─────────────────────────────────────────────────────────────────────

    public sealed class DrawingRoutingRule
    {
        [JsonProperty("discipline")]    public string Discipline { get; set; } = "*";
        [JsonProperty("phase")]         public string Phase { get; set; } = "*";
        [JsonProperty("docType")]       public string DocType { get; set; } = "*"; // matches DrawingPurpose values or user codes
        [JsonProperty("drawingTypeId")] public string DrawingTypeId { get; set; }

        // Week 6 — predicate extensions. Each optional field narrows
        // the match further. When null the field does not participate
        // in matching. All set predicates must match for the rule to
        // fire (logical AND). Field formats:
        //
        //   disciplineMatches / phaseMatches / docTypeMatches
        //       regex — alternative to exact disc/phase/docType above
        //   levelMatches
        //       regex evaluated against the caller's level code, e.g.
        //       "^B\d+" to match any basement level
        //   projectCodeMatches
        //       regex evaluated against doc's PRJ_ORG_PROJECT_CODE
        //   hasScopeBox
        //       true = only fires when the caller passes a scope box
        //       with a matching discipline / doc type
        [JsonProperty("disciplineMatches", NullValueHandling = NullValueHandling.Ignore)] public string DisciplineMatches { get; set; }
        [JsonProperty("phaseMatches",      NullValueHandling = NullValueHandling.Ignore)] public string PhaseMatches { get; set; }
        [JsonProperty("docTypeMatches",    NullValueHandling = NullValueHandling.Ignore)] public string DocTypeMatches { get; set; }
        [JsonProperty("levelMatches",      NullValueHandling = NullValueHandling.Ignore)] public string LevelMatches { get; set; }
        [JsonProperty("projectCodeMatches",NullValueHandling = NullValueHandling.Ignore)] public string ProjectCodeMatches { get; set; }
    }
}
