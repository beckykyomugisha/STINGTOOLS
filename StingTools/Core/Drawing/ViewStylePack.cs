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

        // Phase 139 — accept both "filters" and "filterRules" (corporate
        // file uses filterRules; we expose Filters for runtime).
        [JsonProperty("filters", NullValueHandling = NullValueHandling.Ignore)]
        public List<StyleFilterRule> FiltersAlias
        {
            get => Filters;
            set { if (value != null && value.Count > 0) Filters = value; }
        }

        [JsonProperty("filterRules", NullValueHandling = NullValueHandling.Ignore)]
        public List<StyleFilterRule> FilterRulesAlias
        {
            get => null;
            set { if (value != null && value.Count > 0) Filters = value; }
        }

        [JsonIgnore]
        public List<StyleFilterRule> Filters { get; set; } = new List<StyleFilterRule>();

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

        // ── Phase 137 — Managed view-template mode ──
        //
        // When TemplateMode == "managed", ManagedTemplateSyncer mints
        // a "STING:{packId}:{ViewType}" template and binds the listed
        // ManagedFields to the template's controlled-parameter set so
        // pack edits propagate to every view assigned to the template.
        // "external" mode (the default) leaves the user's hand-authored
        // Revit view template alone — the pack only writes overrides
        // directly onto each view as it always has.

        [JsonProperty("templateMode")] public string TemplateMode { get; set; } = "external";

        [JsonProperty("managedFields", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> ManagedFields { get; set; }

        [JsonIgnore]
        public bool IsManaged =>
            string.Equals(TemplateMode, "managed", StringComparison.OrdinalIgnoreCase);

        [JsonProperty("discipline",       NullValueHandling = NullValueHandling.Ignore)] public string Discipline { get; set; }
        [JsonProperty("visualStyle",      NullValueHandling = NullValueHandling.Ignore)] public string VisualStyle { get; set; }
        [JsonProperty("phaseFilter",      NullValueHandling = NullValueHandling.Ignore)] public string PhaseFilter { get; set; }
        [JsonProperty("phase",            NullValueHandling = NullValueHandling.Ignore)] public string Phase { get; set; }
        [JsonProperty("annotationCrop",   NullValueHandling = NullValueHandling.Ignore)] public bool?  AnnotationCrop { get; set; }
        [JsonProperty("farClipMm",        NullValueHandling = NullValueHandling.Ignore)] public double? FarClipMm { get; set; }
        [JsonProperty("viewRange",        NullValueHandling = NullValueHandling.Ignore)] public PackViewRange ViewRange { get; set; }
        [JsonProperty("underlay",         NullValueHandling = NullValueHandling.Ignore)] public PackUnderlay Underlay { get; set; }
        [JsonProperty("background",       NullValueHandling = NullValueHandling.Ignore)] public string Background { get; set; }

        [JsonProperty("worksetVisibility", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> WorksetVisibility { get; set; }

        [JsonProperty("linkOverrides", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, PackLinkOverride> LinkOverrides { get; set; }

        [JsonProperty("colorFillSchemes", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> ColorFillSchemes { get; set; }

        [JsonProperty("filterEnabled", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, bool> FilterEnabled { get; set; }

        [JsonProperty("managedChecksum", NullValueHandling = NullValueHandling.Ignore)]
        public string ManagedChecksum { get; set; }
    }

    public sealed class PackViewRange
    {
        [JsonProperty("topOffsetMm",    NullValueHandling = NullValueHandling.Ignore)] public double? TopOffsetMm { get; set; }
        [JsonProperty("cutOffsetMm",    NullValueHandling = NullValueHandling.Ignore)] public double? CutOffsetMm { get; set; }
        [JsonProperty("bottomOffsetMm", NullValueHandling = NullValueHandling.Ignore)] public double? BottomOffsetMm { get; set; }
        [JsonProperty("viewDepthMm",    NullValueHandling = NullValueHandling.Ignore)] public double? ViewDepthMm { get; set; }
    }

    public sealed class PackUnderlay
    {
        [JsonProperty("levelName",   NullValueHandling = NullValueHandling.Ignore)] public string LevelName { get; set; }
        [JsonProperty("rangeBase",   NullValueHandling = NullValueHandling.Ignore)] public string RangeBase { get; set; }
        [JsonProperty("orientation", NullValueHandling = NullValueHandling.Ignore)] public string Orientation { get; set; }
    }

    public sealed class PackLinkOverride
    {
        [JsonProperty("displayStyle", NullValueHandling = NullValueHandling.Ignore)] public string DisplayStyle { get; set; }
        [JsonProperty("halftone",     NullValueHandling = NullValueHandling.Ignore)] public bool?  Halftone { get; set; }
        [JsonProperty("hidden",       NullValueHandling = NullValueHandling.Ignore)] public bool?  Hidden { get; set; }
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
        [JsonProperty("halftone",              NullValueHandling = NullValueHandling.Ignore)] public bool?   Halftone { get; set; }
        [JsonProperty("projectionLineWeight",  NullValueHandling = NullValueHandling.Ignore)] public int?    ProjectionLineWeight { get; set; }
        [JsonProperty("projectionLineColor",   NullValueHandling = NullValueHandling.Ignore)] public string  ProjectionLineColor { get; set; }
        [JsonProperty("cutLineWeight",         NullValueHandling = NullValueHandling.Ignore)] public int?    CutLineWeight { get; set; }
        [JsonProperty("cutLineColor",          NullValueHandling = NullValueHandling.Ignore)] public string  CutLineColor { get; set; }
        [JsonProperty("transparency",          NullValueHandling = NullValueHandling.Ignore)] public int?    Transparency { get; set; }

        // Optional visibility flag — true = show, false = hide. Null = leave as-is.
        // Phase 113+ presentation packs use this to hide MEP / structural framing
        // / scope boxes etc. on architectural presentation drawings.
        [JsonProperty("visible",               NullValueHandling = NullValueHandling.Ignore)] public bool?   Visible { get; set; }
    }

    public sealed class ViewStylePackLibrary
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;

        // Phase 139 — accept both "stylePacks" (corporate file convention used
        // by the editor + Excel round-trip) and "viewStylePacks" (legacy).
        // Whichever key is present populates Packs via the wrapper setters.
        [JsonProperty("viewStylePacks", NullValueHandling = NullValueHandling.Ignore)]
        public List<ViewStylePack> ViewStylePacks
        {
            get => Packs;
            set { if (value != null && value.Count > 0) Packs = value; }
        }

        [JsonProperty("stylePacks", NullValueHandling = NullValueHandling.Ignore)]
        public List<ViewStylePack> StylePacks
        {
            get => null; // serialise via ViewStylePacks; this key is read-only
            set { if (value != null && value.Count > 0) Packs = value; }
        }

        [JsonIgnore]
        public List<ViewStylePack> Packs { get; set; } = new List<ViewStylePack>();
    }
}
