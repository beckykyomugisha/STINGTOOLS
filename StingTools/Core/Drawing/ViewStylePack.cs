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

        // Phase 136 — pack-level fallbacks. The runtime (DrawingTypePresentation)
        // uses these only when the DrawingType's own field is null/empty;
        // DrawingType always wins when both are set.
        [JsonProperty("viewTemplate", NullValueHandling = NullValueHandling.Ignore)]
        public string ViewTemplate { get; set; }
        [JsonProperty("detailLevel",  NullValueHandling = NullValueHandling.Ignore)]
        public string DetailLevel { get; set; }
        [JsonProperty("scaleHint",    NullValueHandling = NullValueHandling.Ignore)]
        public string ScaleHint { get; set; }
        [JsonProperty("colorScheme",  NullValueHandling = NullValueHandling.Ignore)]
        public string ColorScheme { get; set; }

        // Convenience for the editor — JSON keys the dialog wrote come from
        // the local "appearance" object. These match the shape used in the
        // disk JSON (STING_VIEW_STYLE_PACKS.json) so the runtime sees the
        // same TextStyle / DimensionStyle / LineWeightScale values the
        // editor emitted.
        [JsonProperty("appearance", NullValueHandling = NullValueHandling.Ignore)]
        public PackAppearanceDto Appearance { get; set; }

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

        // ── Phase 137 — STING-Managed View Templates ─────────────────────
        // When TemplateMode == "managed", ManagedTemplateSyncer auto-
        // generates and maintains a Revit view template per ViewType
        // named STING:<pack-id>:<viewType>. The pack JSON is the single
        // source of truth. Default (null or "external") = legacy behaviour
        // where pack.ViewTemplate names a Revit template applied via
        // DrawingTypePresentation.

        /// <summary>
        /// "managed" or "external" (null = "external" — legacy default).
        /// </summary>
        [JsonProperty("templateMode", NullValueHandling = NullValueHandling.Ignore)]
        public string TemplateMode { get; set; }

        /// <summary>
        /// Fields the syncer is allowed to write. Anything not listed
        /// stays user-editable inside Revit's template editor without
        /// STING clobbering it. Recognised values:
        ///   "vg" "filters" "detailLevel" "scale" "discipline"
        ///   "visualStyle" "viewRange" "underlay" "phaseFilter"
        ///   "phaseName" "annotationCrop" "farClip" "displayOptions"
        /// Default (when null/empty): {vg, filters, detailLevel,
        /// discipline, phaseFilter}.
        /// </summary>
        [JsonProperty("managedFields", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> ManagedFields { get; set; }

        // ── Managed-mode template settings ──
        [JsonProperty("discipline", NullValueHandling = NullValueHandling.Ignore)]
        public string Discipline { get; set; }

        [JsonProperty("visualStyle", NullValueHandling = NullValueHandling.Ignore)]
        public string VisualStyle { get; set; }

        [JsonProperty("phaseFilter", NullValueHandling = NullValueHandling.Ignore)]
        public string PhaseFilter { get; set; }

        [JsonProperty("phaseName", NullValueHandling = NullValueHandling.Ignore)]
        public string PhaseName { get; set; }

        [JsonProperty("annotationCrop", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AnnotationCrop { get; set; }

        [JsonProperty("farClipMm", NullValueHandling = NullValueHandling.Ignore)]
        public double? FarClipMm { get; set; }

        [JsonProperty("viewRange", NullValueHandling = NullValueHandling.Ignore)]
        public PackViewRange ViewRange { get; set; }

        [JsonProperty("underlay", NullValueHandling = NullValueHandling.Ignore)]
        public PackUnderlay Underlay { get; set; }

        [JsonProperty("displayOptions", NullValueHandling = NullValueHandling.Ignore)]
        public PackDisplayOptions DisplayOptions { get; set; }

        [JsonProperty("background", NullValueHandling = NullValueHandling.Ignore)]
        public string Background { get; set; }

        /// <summary>
        /// SHA-256 (managed-fields scope) written by the syncer onto the
        /// generated Revit template. Drift detector compares the live
        /// template's STING_PACK_CHECKSUM_TXT against this value.
        /// </summary>
        [JsonProperty("managedChecksum", NullValueHandling = NullValueHandling.Ignore)]
        public string ManagedChecksum { get; set; }

        /// <summary>True when TemplateMode is "managed".</summary>
        [JsonIgnore]
        public bool IsManaged
            => string.Equals(TemplateMode, "managed", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class PackViewRange
    {
        [JsonProperty("topMm")]        public double TopMm        { get; set; } = 2300;
        [JsonProperty("cutPlaneMm")]   public double CutPlaneMm   { get; set; } = 1200;
        [JsonProperty("bottomMm")]     public double BottomMm     { get; set; } = 0;
        [JsonProperty("viewDepthMm")]  public double ViewDepthMm  { get; set; } = -300;
    }

    public sealed class PackUnderlay
    {
        [JsonProperty("baseLevel")]   public string BaseLevel   { get; set; } = "off";
        [JsonProperty("topLevel")]    public string TopLevel    { get; set; } = "above";
        [JsonProperty("orientation")] public string Orientation { get; set; } = "look-down";
        [JsonProperty("halftone")]    public bool   Halftone    { get; set; } = true;
    }

    public sealed class PackDisplayOptions
    {
        [JsonProperty("shadows")]        public bool Shadows        { get; set; } = false;
        [JsonProperty("sketchyLines")]   public bool SketchyLines   { get; set; } = false;
        [JsonProperty("ambientShadows")] public bool AmbientShadows { get; set; } = false;
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
