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

using System;
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

        // ── Phase 135 TokenProfile tag fields ───────────────────────

        // ── Phase 177 pack-level TAG7 depth + section fields ────────

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

        // ── Phase 135 — Tag Appearance pack-level defaults ──
        // Resolved by TokenProfileApplier whenever the per-DrawingType
        // AnnotationTokenProfile leaves a slot empty. DrawingType always
        // wins when both set the same field.

        /// <summary>
        /// Pack-level default colour scheme. Variable-driven scheme
        /// name (e.g. "System", "Status", "Discipline") written into
        /// STING_VIEW_TAG_STYLE for every view this pack is applied to.
        /// Null = no pack default.
        /// </summary>
        [JsonProperty("tagColorScheme", NullValueHandling = NullValueHandling.Ignore)]
        public string TagColorScheme { get; set; }

        /// <summary>
        /// Pack-level default tag style preset — "{size}{style}_{colour}"
        /// canonical name (e.g. "2.5BOLD_RED"). Used when the
        /// DrawingType profile's TagSize / TagStyle / TagColor are all
        /// null. Null = no pack default.
        /// </summary>
        [JsonProperty("defaultTagStyle", NullValueHandling = NullValueHandling.Ignore)]
        public string DefaultTagStyle { get; set; }

        /// <summary>
        /// Per-category tag style override (canonical preset name).
        /// Loosely wins over DefaultTagStyle, loses to the
        /// DrawingType.TokenProfile.TagSize/Style/Color triple.
        /// </summary>
        [JsonProperty("categoryTagStyles", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> CategoryTagStyles { get; set; }
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
        // Phase 139 — accept both schema variants. Long form (filterName /
        // projectionLineColor / cutLineWeight / …) is the canonical POCO
        // shape; short form (name / projColor / cutWeight / …) is the
        // STING_VIEW_STYLE_PACKS.json corporate file convention. Wrapper
        // setters route either into the underlying field.
        [JsonProperty("filterName", NullValueHandling = NullValueHandling.Ignore)]
        public string FilterNameLong { get => FilterName; set { if (!string.IsNullOrEmpty(value)) FilterName = value; } }
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string FilterNameShort { get => null; set { if (!string.IsNullOrEmpty(value)) FilterName = value; } }
        [JsonIgnore] public string FilterName { get; set; }

        [JsonProperty("visible")]             public bool Visible { get; set; } = true;
        [JsonProperty("halftone")]            public bool Halftone { get; set; } = false;

        [JsonProperty("projectionLineColor",  NullValueHandling = NullValueHandling.Ignore)]
        public string ProjLineColorLong { get => ProjectionLineColor; set { if (!string.IsNullOrEmpty(value)) ProjectionLineColor = value; } }
        [JsonProperty("projColor", NullValueHandling = NullValueHandling.Ignore)]
        public string ProjLineColorShort { get => null; set { if (!string.IsNullOrEmpty(value)) ProjectionLineColor = value; } }
        [JsonIgnore] public string ProjectionLineColor { get; set; }

        [JsonProperty("projectionLineWeight", NullValueHandling = NullValueHandling.Ignore)]
        public int? ProjLineWeightLong { get => ProjectionLineWeight; set { if (value.HasValue) ProjectionLineWeight = value; } }
        [JsonProperty("projWeight", NullValueHandling = NullValueHandling.Ignore)]
        public int? ProjLineWeightShort { get => null; set { if (value.HasValue) ProjectionLineWeight = value; } }
        [JsonIgnore] public int? ProjectionLineWeight { get; set; }

        [JsonProperty("cutLineColor",         NullValueHandling = NullValueHandling.Ignore)]
        public string CutLineColorLong { get => CutLineColor; set { if (!string.IsNullOrEmpty(value)) CutLineColor = value; } }
        [JsonProperty("cutColor", NullValueHandling = NullValueHandling.Ignore)]
        public string CutLineColorShort { get => null; set { if (!string.IsNullOrEmpty(value)) CutLineColor = value; } }
        [JsonIgnore] public string CutLineColor { get; set; }

        [JsonProperty("cutLineWeight",        NullValueHandling = NullValueHandling.Ignore)]
        public int? CutLineWeightLong { get => CutLineWeight; set { if (value.HasValue) CutLineWeight = value; } }
        [JsonProperty("cutWeight", NullValueHandling = NullValueHandling.Ignore)]
        public int? CutLineWeightShort { get => null; set { if (value.HasValue) CutLineWeight = value; } }
        [JsonIgnore] public int? CutLineWeight { get; set; }

        [JsonProperty("transparency",         NullValueHandling = NullValueHandling.Ignore)] public int?   Transparency { get; set; }  // 0..100

        // ── Phase 139 — extended override fields ──
        // Mirrors FilterDefaultOverride so packs can express surface fills,
        // line patterns, and detail-level overrides for filter-driven rules
        // (fire compartments, system colour washes, escape-route highlights).
        [JsonProperty("projectionLinePattern", NullValueHandling = NullValueHandling.Ignore)] public string ProjectionLinePattern { get; set; }
        [JsonProperty("cutLinePattern",        NullValueHandling = NullValueHandling.Ignore)] public string CutLinePattern { get; set; }
        [JsonProperty("surfaceFgColor",        NullValueHandling = NullValueHandling.Ignore)] public string SurfaceFgColor { get; set; }
        [JsonProperty("surfaceFgPattern",      NullValueHandling = NullValueHandling.Ignore)] public string SurfaceFgPattern { get; set; }
        [JsonProperty("surfaceBgColor",        NullValueHandling = NullValueHandling.Ignore)] public string SurfaceBgColor { get; set; }
        [JsonProperty("surfaceBgPattern",      NullValueHandling = NullValueHandling.Ignore)] public string SurfaceBgPattern { get; set; }
        [JsonProperty("cutFgColor",            NullValueHandling = NullValueHandling.Ignore)] public string CutFgColor { get; set; }
        [JsonProperty("cutFgPattern",          NullValueHandling = NullValueHandling.Ignore)] public string CutFgPattern { get; set; }
        [JsonProperty("cutBgColor",            NullValueHandling = NullValueHandling.Ignore)] public string CutBgColor { get; set; }
        [JsonProperty("cutBgPattern",          NullValueHandling = NullValueHandling.Ignore)] public string CutBgPattern { get; set; }
        [JsonProperty("detailLevel",           NullValueHandling = NullValueHandling.Ignore)] public string DetailLevel { get; set; }

        /// <summary>
        /// When true, the applier merges defaults from
        /// AecFilterRegistry.GetByName(filterName) before writing — useful
        /// for packs that just say {"filterName":"STING - Fire 60 min Walls"}
        /// and want the corporate-baseline override recipe applied.
        /// Default true. Set false to leave Revit defaults in place where
        /// this rule is silent.
        /// </summary>
        [JsonProperty("inheritDefaults", NullValueHandling = NullValueHandling.Ignore)] public bool? InheritDefaults { get; set; }
    }

    public sealed class StyleVgOverride
    {
        // ── Visibility ─────────────────────────────────────────────────────────
        [JsonProperty("visible", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Visible { get; set; }                  // null = inherited / not set

        [JsonProperty("halftone", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Halftone { get; set; }

        [JsonProperty("detailLevel", NullValueHandling = NullValueHandling.Ignore)]
        public string DetailLevel { get; set; }             // "ByView"|"Coarse"|"Medium"|"Fine"

        // ── Projection / Surface ────────────────────────────────────────────────
        [JsonProperty("projectionLineColor",   NullValueHandling = NullValueHandling.Ignore)]
        public string ProjectionLineColor { get; set; }     // "#RRGGBB"

        [JsonProperty("projectionLineWeight",  NullValueHandling = NullValueHandling.Ignore)]
        public int? ProjectionLineWeight { get; set; }      // 1–16

        [JsonProperty("projectionLinePattern", NullValueHandling = NullValueHandling.Ignore)]
        public string ProjectionLinePattern { get; set; }   // LinePatternElement.Name or "Solid"

        [JsonProperty("surfaceFgPatternName",  NullValueHandling = NullValueHandling.Ignore)]
        public string SurfaceFgPatternName { get; set; }    // FillPatternElement.Name

        [JsonProperty("surfaceFgPatternColor", NullValueHandling = NullValueHandling.Ignore)]
        public string SurfaceFgPatternColor { get; set; }

        [JsonProperty("surfaceFgPatternVisible", NullValueHandling = NullValueHandling.Ignore)]
        public bool? SurfaceFgPatternVisible { get; set; }

        [JsonProperty("surfaceBgPatternName",  NullValueHandling = NullValueHandling.Ignore)]
        public string SurfaceBgPatternName { get; set; }

        [JsonProperty("surfaceBgPatternColor", NullValueHandling = NullValueHandling.Ignore)]
        public string SurfaceBgPatternColor { get; set; }

        [JsonProperty("surfaceBgPatternVisible", NullValueHandling = NullValueHandling.Ignore)]
        public bool? SurfaceBgPatternVisible { get; set; }

        [JsonProperty("transparency", NullValueHandling = NullValueHandling.Ignore)]
        public int? Transparency { get; set; }              // 0–100

        // ── Cut ────────────────────────────────────────────────────────────────
        [JsonProperty("cutLineColor",   NullValueHandling = NullValueHandling.Ignore)]
        public string CutLineColor { get; set; }

        [JsonProperty("cutLineWeight",  NullValueHandling = NullValueHandling.Ignore)]
        public int? CutLineWeight { get; set; }

        [JsonProperty("cutLinePattern", NullValueHandling = NullValueHandling.Ignore)]
        public string CutLinePattern { get; set; }

        [JsonProperty("cutFgPatternName",  NullValueHandling = NullValueHandling.Ignore)]
        public string CutFgPatternName { get; set; }

        [JsonProperty("cutFgPatternColor", NullValueHandling = NullValueHandling.Ignore)]
        public string CutFgPatternColor { get; set; }

        [JsonProperty("cutFgPatternVisible", NullValueHandling = NullValueHandling.Ignore)]
        public bool? CutFgPatternVisible { get; set; }

        [JsonProperty("cutBgPatternName",  NullValueHandling = NullValueHandling.Ignore)]
        public string CutBgPatternName { get; set; }

        [JsonProperty("cutBgPatternColor", NullValueHandling = NullValueHandling.Ignore)]
        public string CutBgPatternColor { get; set; }

        [JsonProperty("cutBgPatternVisible", NullValueHandling = NullValueHandling.Ignore)]
        public bool? CutBgPatternVisible { get; set; }
    }

    /// <summary>
    /// Mirrors the dialog's "appearance" object inside each pack so the
    /// runtime can deserialize the same JSON the editor writes. Promoted
    /// fields are normalised onto ViewStylePack at load time by
    /// <see cref="ViewStylePackRegistry"/> (see Promote).
    /// </summary>
    public sealed class PackAppearanceDto
    {
        [JsonProperty("lineWeightScale", NullValueHandling = NullValueHandling.Ignore)] public double? LineWeightScale { get; set; }
        [JsonProperty("textStyleName",   NullValueHandling = NullValueHandling.Ignore)] public string TextStyleName { get; set; }
        [JsonProperty("dimensionStyleName", NullValueHandling = NullValueHandling.Ignore)] public string DimensionStyleName { get; set; }
        [JsonProperty("hatchPalette",    NullValueHandling = NullValueHandling.Ignore)] public string HatchPalette { get; set; }
    }

    public sealed class ViewStylePackLibrary
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;

        // Primary list — newer JSON files use "viewStylePacks".
        [JsonProperty("viewStylePacks", NullValueHandling = NullValueHandling.Ignore)]
        public List<ViewStylePack> Packs { get; set; } = new List<ViewStylePack>();

        // Phase 136 — alias so the existing on-disk
        // STING_VIEW_STYLE_PACKS.json (which uses "stylePacks") also
        // deserializes correctly. Newtonsoft.Json calls the setter when
        // it sees the legacy key; we forward to Packs so the runtime
        // sees the same list either way.
        [JsonProperty("stylePacks", NullValueHandling = NullValueHandling.Ignore)]
        public List<ViewStylePack> StylePacksLegacy
        {
            get => null;     // never re-serialised — the canonical key wins on save
            set { if (value != null && value.Count > 0) Packs = value; }
        }
    }

}
