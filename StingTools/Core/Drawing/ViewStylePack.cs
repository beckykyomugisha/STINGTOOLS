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
