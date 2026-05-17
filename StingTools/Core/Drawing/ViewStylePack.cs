using System;
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

        // ── Phase 137 — managed template mode ───────────────────────

        /// <summary>Template mode: "managed" (STING owns the view template) or "external" (user-maintained).</summary>
        [JsonProperty("templateMode", NullValueHandling = NullValueHandling.Ignore)]
        public string TemplateMode { get; set; }

        /// <summary>True when TemplateMode == "managed".</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsManaged => string.Equals(TemplateMode, "managed", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>View discipline string for managed template (e.g. "Mechanical", "Electrical").</summary>
        [JsonProperty("discipline", NullValueHandling = NullValueHandling.Ignore)]
        public string Discipline { get; set; }

        /// <summary>Visual style for managed template (e.g. "HiddenLine", "Shaded").</summary>
        [JsonProperty("visualStyle", NullValueHandling = NullValueHandling.Ignore)]
        public string VisualStyle { get; set; }

        /// <summary>Revit phase filter name to apply to the managed template.</summary>
        [JsonProperty("phaseFilter", NullValueHandling = NullValueHandling.Ignore)]
        public string PhaseFilter { get; set; }

        /// <summary>Revit phase name to apply to the managed template.</summary>
        [JsonProperty("phase", NullValueHandling = NullValueHandling.Ignore)]
        public string Phase { get; set; }

        /// <summary>Annotation crop setting for the managed template.</summary>
        [JsonProperty("annotationCrop", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AnnotationCrop { get; set; }

        /// <summary>Far clip distance in mm for the managed template. Null = no override.</summary>
        [JsonProperty("farClipMm", NullValueHandling = NullValueHandling.Ignore)]
        public double? FarClipMm { get; set; }

        /// <summary>View range specification for the managed template (serialised as a sub-object).</summary>
        [JsonProperty("viewRange", NullValueHandling = NullValueHandling.Ignore)]
        public PackViewRange ViewRange { get; set; }

        /// <summary>Underlay level name for the managed template.</summary>
        [JsonProperty("underlay", NullValueHandling = NullValueHandling.Ignore)]
        public string Underlay { get; set; }

        /// <summary>Background colour / setting string for the managed template.</summary>
        [JsonProperty("background", NullValueHandling = NullValueHandling.Ignore)]
        public string Background { get; set; }

        /// <summary>Workset visibility mode for the managed template.</summary>
        [JsonProperty("worksetVisibility", NullValueHandling = NullValueHandling.Ignore)]
        public string WorksetVisibility { get; set; }

        /// <summary>Link overrides specification (serialised as a raw JSON token).</summary>
        [JsonProperty("linkOverrides", NullValueHandling = NullValueHandling.Ignore)]
        public object LinkOverrides { get; set; }

        /// <summary>Color fill scheme references for the managed template.</summary>
        [JsonProperty("colorFillSchemes", NullValueHandling = NullValueHandling.Ignore)]
        public object ColorFillSchemes { get; set; }

        /// <summary>Whether filters are active on the managed template.</summary>
        [JsonProperty("filterEnabled", NullValueHandling = NullValueHandling.Ignore)]
        public bool FilterEnabled { get; set; } = true;

        /// <summary>Fields that the managed template controls. Null = use DefaultManagedFields in ManagedTemplateSyncer.</summary>
        [JsonProperty("managedFields", NullValueHandling = NullValueHandling.Ignore)]
        public System.Collections.Generic.List<string> ManagedFields { get; set; }

        // ── Phase 135 — token profile defaults ──────────────────────

        /// <summary>Default tag colour scheme applied at pack level (e.g. "STING Discipline").</summary>
        [JsonProperty("tagColorScheme", NullValueHandling = NullValueHandling.Ignore)]
        public string TagColorScheme { get; set; }

        /// <summary>Default tag style preset applied at pack level.</summary>
        [JsonProperty("defaultTagStyle", NullValueHandling = NullValueHandling.Ignore)]
        public string DefaultTagStyle { get; set; }

        /// <summary>Per-category tag style overrides. Category name → style preset name.</summary>
        [JsonProperty("categoryTagStyles", NullValueHandling = NullValueHandling.Ignore)]
        public System.Collections.Generic.Dictionary<string, string> CategoryTagStyles { get; set; }

        // ── Phase 177 — per-category paragraph depth ─────────────────

        /// <summary>Per-category paragraph depth overrides. Category name → depth tier (1-10).</summary>
        [JsonProperty("categoryDepths", NullValueHandling = NullValueHandling.Ignore)]
        public System.Collections.Generic.Dictionary<string, int> CategoryDepths { get; set; }

        /// <summary>Per-category TAG7 section visibility flags. Category name → section-visible bool.</summary>
        [JsonProperty("categoryTag7Sections", NullValueHandling = NullValueHandling.Ignore)]
        public System.Collections.Generic.Dictionary<string, bool> CategoryTag7Sections { get; set; }
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

    /// <summary>
    /// Underlay settings carried by a <see cref="ViewStylePack"/>: which level
    /// and whether to show it above or below.
    /// </summary>
    public sealed class PackUnderlay
    {
        [JsonProperty("levelName")]   public string LevelName   { get; set; }
        [JsonProperty("orientation")] public string Orientation { get; set; } = "LookingDown";
    }

    /// <summary>
    /// View-range offsets (in mm) carried by a <see cref="ViewStylePack"/>.
    /// All values are optional; only the supplied offsets are written to the
    /// view template's PlanViewRange.
    /// </summary>
    public sealed class PackViewRange
    {
        [JsonProperty("topOffsetMm",    NullValueHandling = NullValueHandling.Ignore)] public double? TopOffsetMm    { get; set; }
        [JsonProperty("cutOffsetMm",    NullValueHandling = NullValueHandling.Ignore)] public double? CutOffsetMm    { get; set; }
        [JsonProperty("bottomOffsetMm", NullValueHandling = NullValueHandling.Ignore)] public double? BottomOffsetMm { get; set; }
        [JsonProperty("viewDepthMm",    NullValueHandling = NullValueHandling.Ignore)] public double? ViewDepthMm    { get; set; }
    }
}
