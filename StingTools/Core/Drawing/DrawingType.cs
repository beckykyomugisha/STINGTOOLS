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

        // Print / appearance overrides
        [JsonProperty("print")]      public PrintOverride Print { get; set; } = new PrintOverride();

        // Integrity hash used by the corporate-lock mechanism —
        // DrawingTypeRegistry writes it on load for corporate types and
        // compares on save to detect out-of-band edits.
        [JsonProperty("checksum", NullValueHandling = NullValueHandling.Ignore)]
        public string Checksum { get; set; }
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
    //  ANNOTATION RULE PACK — the "what to tag and how" payload
    // ─────────────────────────────────────────────────────────────────────

    public sealed class AnnotationRulePack
    {
        [JsonProperty("autoDimGrids")]   public bool AutoDimGrids { get; set; }
        [JsonProperty("autoDimLevels")]  public bool AutoDimLevels { get; set; }
        [JsonProperty("autoTagRooms")]   public bool AutoTagRooms { get; set; }
        [JsonProperty("autoTagDoors")]   public bool AutoTagDoors { get; set; }
        [JsonProperty("autoTagWindows")] public bool AutoTagWindows { get; set; }
        [JsonProperty("autoTagEquipment")] public bool AutoTagEquipment { get; set; }
        [JsonProperty("autoTagWelds")]   public bool AutoTagWelds { get; set; }
        [JsonProperty("autoTagSupports")] public bool AutoTagSupports { get; set; }
        [JsonProperty("autoTagBends")]   public bool AutoTagBends { get; set; }

        /// <summary>
        /// Linear | Ordinate | Chain — sets the dimensioning strategy
        /// the annotation pass uses when running auto-dim on grids.
        /// </summary>
        [JsonProperty("dimensionStrategy")] public string DimensionStrategy { get; set; } = "Linear";
        [JsonProperty("dimensionStyle")]    public string DimensionStyle { get; set; }

        /// <summary>
        /// Category-name (STING code) to tag family name. Empty map means
        /// "use whatever tag family is currently active in the project",
        /// which is what Revit does by default.
        /// </summary>
        [JsonProperty("tagFamilies")] public Dictionary<string, string> TagFamilies { get; set; }
            = new Dictionary<string, string>();

        /// <summary>
        /// Scale modifier — at scales coarser than this value, annotation
        /// density is automatically reduced (grid dims only, no per-
        /// element tags). At scales finer than this, full annotation
        /// runs. Null = always full.
        /// </summary>
        [JsonProperty("denseUntilScale", NullValueHandling = NullValueHandling.Ignore)]
        public int? DenseUntilScale { get; set; }
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
    }
}
