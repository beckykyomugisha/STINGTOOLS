// StingTools — Drawing Template Manager · Week 2
//
// ViewStylePack factors the graphic-override payload out of
// DrawingType so multiple profiles share the same visual style. A
// typical corporate catalogue has ~40 profiles but only ~8 distinct
// visual styles — without this layer every profile would inline its
// own filters / VG overrides / text + dim style references, and a
// single tweak to "corp standard plan" would require editing 12 JSON
// entries.
//
// DrawingType.ViewStylePackId references a pack by id;
// DrawingTypePresentation.Apply resolves the pack and applies its
// settings after the profile-level scale / template / annotation.
//
// Inheritance: a pack may set Extends = "<parent-id>"; the registry
// walks the chain at load-time so resolvers see a merged snapshot.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// View range offsets for a managed view template pack (all values in mm).
    /// Null fields are not written to the template.
    /// </summary>
    public sealed class PackViewRange
    {
        [JsonProperty("topOffsetMm",    NullValueHandling = NullValueHandling.Ignore)] public double? TopOffsetMm    { get; set; }
        [JsonProperty("cutOffsetMm",    NullValueHandling = NullValueHandling.Ignore)] public double? CutOffsetMm    { get; set; }
        [JsonProperty("bottomOffsetMm", NullValueHandling = NullValueHandling.Ignore)] public double? BottomOffsetMm { get; set; }
        [JsonProperty("viewDepthMm",    NullValueHandling = NullValueHandling.Ignore)] public double? ViewDepthMm    { get; set; }
    }

    /// <summary>
    /// Underlay configuration for a managed view template pack.
    /// </summary>
    public sealed class PackUnderlay
    {
        [JsonProperty("levelName",   NullValueHandling = NullValueHandling.Ignore)] public string LevelName   { get; set; }
        [JsonProperty("orientation", NullValueHandling = NullValueHandling.Ignore)] public string Orientation { get; set; }
    }

    public sealed class ViewStylePack
    {
        [JsonProperty("id")]          public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("origin")]      public string Origin { get; set; } = "corporate";
        [JsonProperty("extends", NullValueHandling = NullValueHandling.Ignore)]
        public string Extends { get; set; }

        [JsonProperty("lineWeightScale")] public double LineWeightScale { get; set; } = 1.0;
        [JsonProperty("textStyle")]       public string TextStyle { get; set; }
        [JsonProperty("dimensionStyle")]  public string DimensionStyle { get; set; }
        [JsonProperty("hatchPalette")]    public string HatchPalette { get; set; }

        // ── Phase 137 managed-template fields ───────────────────────

        /// <summary>
        /// templateMode: "managed" or "external" (default).
        /// When "managed" STING auto-generates and maintains a Revit view
        /// template named "STING:{id}:{ViewType}" from this pack's JSON.
        /// </summary>
        [JsonProperty("templateMode", NullValueHandling = NullValueHandling.Ignore)]
        public string TemplateMode { get; set; }

        /// <summary>
        /// true when TemplateMode == "managed". Derived property for
        /// convenience; callers can also test TemplateMode directly.
        /// </summary>
        [JsonIgnore]
        public bool IsManaged =>
            string.Equals(TemplateMode, "managed", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whitelist of managed field names. Null = use engine default
        /// (scale, detailLevel, discipline, visualStyle, phaseFilter,
        /// tagColorScheme, defaultTagStyle).
        /// </summary>
        [JsonProperty("managedFields", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> ManagedFields { get; set; }

        /// <summary>Revit view discipline code string (Architectural / Structural / Mechanical / Electrical / Coordination).</summary>
        [JsonProperty("discipline", NullValueHandling = NullValueHandling.Ignore)]
        public string Discipline { get; set; }

        /// <summary>Revit display style / visual style string (HLR / Shaded / Consistent Colors / Realistic / Wireframe / etc.).</summary>
        [JsonProperty("visualStyle", NullValueHandling = NullValueHandling.Ignore)]
        public string VisualStyle { get; set; }

        /// <summary>Phase filter name to apply to the managed template.</summary>
        [JsonProperty("phaseFilter", NullValueHandling = NullValueHandling.Ignore)]
        public string PhaseFilter { get; set; }

        /// <summary>Phase name to apply to the managed template.</summary>
        [JsonProperty("phase", NullValueHandling = NullValueHandling.Ignore)]
        public string Phase { get; set; }

        /// <summary>Annotation crop active flag. Null = leave unchanged.</summary>
        [JsonProperty("annotationCrop", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AnnotationCrop { get; set; }

        /// <summary>Far clip offset in millimetres. Null = leave unchanged.</summary>
        [JsonProperty("farClipMm", NullValueHandling = NullValueHandling.Ignore)]
        public double? FarClipMm { get; set; }

        /// <summary>View range offsets (plan views only). Null = leave unchanged.</summary>
        [JsonProperty("viewRange", NullValueHandling = NullValueHandling.Ignore)]
        public PackViewRange ViewRange { get; set; }

        /// <summary>Underlay level and orientation (plan views only). Null = leave unchanged.</summary>
        [JsonProperty("underlay", NullValueHandling = NullValueHandling.Ignore)]
        public PackUnderlay Underlay { get; set; }

        /// <summary>Background colour override string ("#RRGGBB" or "White" / "Sky" / "Black"). Null = leave unchanged.</summary>
        [JsonProperty("background", NullValueHandling = NullValueHandling.Ignore)]
        public string Background { get; set; }

        /// <summary>Workset visibility preset string — ALL_VISIBLE / ALL_HIDDEN / USE_GLOBAL. Null = leave unchanged.</summary>
        [JsonProperty("worksetVisibility", NullValueHandling = NullValueHandling.Ignore)]
        public string WorksetVisibility { get; set; }

        /// <summary>Link visibility overrides — dictionary of link file name → override mode string.</summary>
        [JsonProperty("linkOverrides", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> LinkOverrides { get; set; }

        /// <summary>Color fill scheme — dictionary of category name → scheme name to apply.</summary>
        [JsonProperty("colorFillSchemes", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> ColorFillSchemes { get; set; }

        /// <summary>Filter enabled override — dictionary of filter name → enabled flag.</summary>
        [JsonProperty("filterEnabled", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, bool> FilterEnabled { get; set; }

        // ── Phase 135 TokenProfile tag fields ───────────────────────

        /// <summary>Tag color scheme name applied via TokenProfileApplier (e.g. "Discipline" / "Warm" / "Cool").</summary>
        [JsonProperty("tagColorScheme", NullValueHandling = NullValueHandling.Ignore)]
        public string TagColorScheme { get; set; }

        /// <summary>Default tag style string applied to all elements in views using this pack.</summary>
        [JsonProperty("defaultTagStyle", NullValueHandling = NullValueHandling.Ignore)]
        public string DefaultTagStyle { get; set; }

        /// <summary>Per-category tag style overrides. Dictionary of Revit category name → tag style string.</summary>
        [JsonProperty("categoryTagStyles", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> CategoryTagStyles { get; set; }

        // ── Core visual fields ───────────────────────────────────────

        [JsonProperty("filters")] public List<StyleFilterRule> Filters { get; set; } = new List<StyleFilterRule>();

        /// <summary>
        /// Category-name → graphic override. Keys match Revit Category
        /// localised names (Walls / Grids / Rooms / etc.) or BIC strings.
        /// </summary>
        [JsonProperty("vgOverrides")] public Dictionary<string, StyleVgOverride> VgOverrides { get; set; }
            = new Dictionary<string, StyleVgOverride>();

        /// <summary>
        /// Category → tag family mapping. Mirrors
        /// AnnotationRulePack.TagFamilies but lives at the pack level
        /// so many profiles share one table. Profile-level map wins
        /// when both declare the same key.
        /// </summary>
        [JsonProperty("tagFamilies")] public Dictionary<string, string> TagFamilies { get; set; }
            = new Dictionary<string, string>();

        [JsonProperty("checksum", NullValueHandling = NullValueHandling.Ignore)]
        public string Checksum { get; set; }
    }

    public sealed class StyleFilterRule
    {
        [JsonProperty("filterName")]          public string FilterName { get; set; }
        [JsonProperty("visible")]             public bool Visible { get; set; } = true;
        [JsonProperty("halftone")]            public bool Halftone { get; set; } = false;
        [JsonProperty("projectionLineColor",  NullValueHandling = NullValueHandling.Ignore)] public string ProjectionLineColor { get; set; }  // "#RRGGBB"
        [JsonProperty("projectionLineWeight", NullValueHandling = NullValueHandling.Ignore)] public int?   ProjectionLineWeight { get; set; }
        [JsonProperty("cutLineColor",         NullValueHandling = NullValueHandling.Ignore)] public string CutLineColor { get; set; }
        [JsonProperty("cutLineWeight",        NullValueHandling = NullValueHandling.Ignore)] public int?   CutLineWeight { get; set; }
        [JsonProperty("transparency",         NullValueHandling = NullValueHandling.Ignore)] public int?   Transparency { get; set; }  // 0..100
    }

    public sealed class StyleVgOverride
    {
        [JsonProperty("halftone",              NullValueHandling = NullValueHandling.Ignore)] public bool?   Halftone { get; set; }
        [JsonProperty("projectionLineWeight",  NullValueHandling = NullValueHandling.Ignore)] public int?    ProjectionLineWeight { get; set; }
        [JsonProperty("projectionLineColor",   NullValueHandling = NullValueHandling.Ignore)] public string  ProjectionLineColor { get; set; }
        [JsonProperty("cutLineWeight",         NullValueHandling = NullValueHandling.Ignore)] public int?    CutLineWeight { get; set; }
        [JsonProperty("cutLineColor",          NullValueHandling = NullValueHandling.Ignore)] public string  CutLineColor { get; set; }
        [JsonProperty("transparency",          NullValueHandling = NullValueHandling.Ignore)] public int?    Transparency { get; set; }
    }

    public sealed class ViewStylePackLibrary
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;
        [JsonProperty("viewStylePacks")] public List<ViewStylePack> Packs { get; set; } = new List<ViewStylePack>();
    }
}
