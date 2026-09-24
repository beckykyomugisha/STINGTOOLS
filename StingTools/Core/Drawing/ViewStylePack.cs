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

    public sealed partial class ViewStylePack
    {
        [JsonProperty("id")]          public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("origin")]      public string Origin { get; set; } = "corporate";
        [JsonProperty("extends", NullValueHandling = NullValueHandling.Ignore)]
        public string Extends { get; set; }

        /// <summary>
        /// Multiplies every line weight this pack states, clamped to Revit's
        /// 1..16. APPLIED by ViewStylePackApplier.ApplyLineWeightScale, folded
        /// together with DrawingType.Print.LineWeightScale so the two cannot
        /// compound in an order-dependent way.
        ///
        /// It was declared, promoted from the nested "appearance" block, and
        /// carried through the extends fold — and read by nothing, so the
        /// presentation packs' 0.6–0.8 rendered identically to production.
        /// </summary>
        [JsonProperty("lineWeightScale")] public double LineWeightScale { get; set; } = 1.0;

        /// <summary>
        /// DECLARATIVE — the text style a drawing produced with this pack
        /// should use. Revit has no view-level "text style" setting (text
        /// height is a property of each text/tag TYPE), so there is nothing to
        /// apply to a view and this field deliberately does not try. It is a
        /// pre-flight target: the value names a text type the project is
        /// expected to hold, and TemplateManager's style creators author it.
        ///
        /// Documented rather than removed because removing it would lose a
        /// stated intent; documented rather than left bare because an
        /// undocumented unread field reads as a bug.
        /// </summary>
        [JsonProperty("textStyle")]       public string TextStyle { get; set; }

        /// <summary>
        /// APPLIED — resolved by AnnotationRunner via
        /// DimensionStrategy.ResolveType, which prefers this named
        /// DimensionType over the strategy-derived fallback.
        /// </summary>
        [JsonProperty("dimensionStyle")]  public string DimensionStyle { get; set; }

        /// <summary>
        /// DECLARATIVE — the hatch vocabulary a pack's drawings follow
        /// ("ISO 13567 monochrome", "Rich"). Revit fill patterns are assigned
        /// per material and per filter override, not per view, so there is no
        /// single setting to write. Consumed as a selector by the fill-pattern
        /// creators in TemplateManager and by the pack's own filter rules.
        /// See <see cref="TextStyle"/> for why it stays.
        /// </summary>
        [JsonProperty("hatchPalette")]    public string HatchPalette { get; set; }

        // ── Phase 137 managed-template fields ───────────────────────

        // ── Phase 135 TokenProfile tag fields ───────────────────────

        // ── Phase 177 pack-level TAG7 depth + section fields ────────

        // ── Core visual fields ───────────────────────────────────────

        [JsonProperty("filters")] public List<StyleFilterRule> Filters { get; set; } = new List<StyleFilterRule>();

        // Phase 139 alias pattern, applied one level down. The corporate
        // STING_VIEW_STYLE_PACKS.json keys filter rules under "filterRules"
        // on 11 of 35 packs (corp-base, corp-clarification,
        // corp-demolition-phase and all 8 corp-healthcare-*) — 78 rules that
        // the "filters"-only binding dropped silently, so healthcare
        // pressure / MGPS / fire drawings rendered with no filter colour
        // coding at all. No pack declares both keys, so this write-only
        // alias is order-independent. Canonical Filters still serialises
        // under "filters".
        [JsonProperty("filterRules", NullValueHandling = NullValueHandling.Ignore)]
        public List<StyleFilterRule> FilterRulesAlias
        {
            set { if (value != null && value.Count > 0) Filters = value; }
        }

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

        // V-9: nullable so "the pack did not say" is distinguishable from
        // "the pack said true/false". As non-nullable bools an explicit
        // visible:true was identical to unset, so the AEC-filter registry
        // default overrode the pack on every visible rule — the exact
        // inverse of the documented "pack wins" precedence. Null means
        // defer to the registry default, then to Revit's own default.
        [JsonProperty("visible",  NullValueHandling = NullValueHandling.Ignore)] public bool? Visible { get; set; }
        [JsonProperty("halftone", NullValueHandling = NullValueHandling.Ignore)] public bool? Halftone { get; set; }

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
        [JsonIgnore] public string SurfaceFgColor { get; set; }

        // Short-form aliases for the two extended fields the corporate file
        // actually uses: "surfFgColor" (15×) and "projLinePattern" (5×).
        // The Phase 139 pass aliased projColor/projWeight/cutColor/cutWeight
        // but stopped short of these, so they bound nowhere.
        [JsonProperty("surfaceFgColor", NullValueHandling = NullValueHandling.Ignore)]
        public string SurfaceFgColorLong { get => SurfaceFgColor; set { if (!string.IsNullOrEmpty(value)) SurfaceFgColor = value; } }
        [JsonProperty("surfFgColor", NullValueHandling = NullValueHandling.Ignore)]
        public string SurfaceFgColorShort { get => null; set { if (!string.IsNullOrEmpty(value)) SurfaceFgColor = value; } }
        [JsonProperty("projLinePattern", NullValueHandling = NullValueHandling.Ignore)]
        public string ProjLinePatternShort { get => null; set { if (!string.IsNullOrEmpty(value)) ProjectionLinePattern = value; } }
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
        [JsonProperty("transparency",          NullValueHandling = NullValueHandling.Ignore)] public int?    Transparency { get; set; }

        // Phase 139 alias pattern (as on StyleFilterRule). The corporate
        // vgOverrides blocks mix both spellings across packs — long form
        // projectionLineColor/projectionLineWeight/cutLineColor/cutLineWeight
        // (107/107/45/45 occurrences) and short form
        // projColor/projWeight/cutColor/cutWeight (188/209/9/14). Only the
        // long form bound, so ~420 authored line colours/weights were
        // dropped silently and most packs lost their line graphics. No
        // single override object declares both spellings, so these
        // write-only aliases are order-independent. The canonical fields
        // still serialise under the long names.
        [JsonIgnore] public int?   ProjectionLineWeight { get; set; }
        [JsonIgnore] public string ProjectionLineColor  { get; set; }
        [JsonIgnore] public int?   CutLineWeight        { get; set; }
        [JsonIgnore] public string CutLineColor         { get; set; }

        [JsonProperty("projectionLineWeight",  NullValueHandling = NullValueHandling.Ignore)]
        public int?   ProjLineWeightLong  { get => ProjectionLineWeight; set { if (value.HasValue) ProjectionLineWeight = value; } }
        [JsonProperty("projWeight",            NullValueHandling = NullValueHandling.Ignore)]
        public int?   ProjLineWeightShort { get => null; set { if (value.HasValue) ProjectionLineWeight = value; } }

        [JsonProperty("projectionLineColor",   NullValueHandling = NullValueHandling.Ignore)]
        public string ProjLineColorLong  { get => ProjectionLineColor; set { if (!string.IsNullOrEmpty(value)) ProjectionLineColor = value; } }
        [JsonProperty("projColor",             NullValueHandling = NullValueHandling.Ignore)]
        public string ProjLineColorShort { get => null; set { if (!string.IsNullOrEmpty(value)) ProjectionLineColor = value; } }

        [JsonProperty("cutLineWeight",         NullValueHandling = NullValueHandling.Ignore)]
        public int?   CutLineWeightLong  { get => CutLineWeight; set { if (value.HasValue) CutLineWeight = value; } }
        [JsonProperty("cutWeight",             NullValueHandling = NullValueHandling.Ignore)]
        public int?   CutLineWeightShort { get => null; set { if (value.HasValue) CutLineWeight = value; } }

        [JsonProperty("cutLineColor",          NullValueHandling = NullValueHandling.Ignore)]
        public string CutLineColorLong  { get => CutLineColor; set { if (!string.IsNullOrEmpty(value)) CutLineColor = value; } }
        [JsonProperty("cutColor",              NullValueHandling = NullValueHandling.Ignore)]
        public string CutLineColorShort { get => null; set { if (!string.IsNullOrEmpty(value)) CutLineColor = value; } }

        // Optional visibility flag — true = show, false = hide. Null = leave as-is.
        // Phase 113+ presentation packs use this to hide MEP / structural framing
        // / scope boxes etc. on architectural presentation drawings.
        [JsonProperty("visible",               NullValueHandling = NullValueHandling.Ignore)] public bool?   Visible { get; set; }
    }

    /// <summary>
    /// One routing rule from the style-pack library's <c>routing</c> array:
    /// (purpose, discipline, phase) → style pack id. First match wins, "*"
    /// is a wildcard, and a null field behaves as "*".
    ///
    /// This table has shipped in STING_VIEW_STYLE_PACKS.json since the pack
    /// layer was introduced and bound to NOTHING —
    /// <see cref="ViewStylePackLibrary"/> declared only Version and Packs, so
    /// all 13 rules were inert, and no code read a <c>stylePackId</c>. It is
    /// now the documented fallback for a DrawingType that names no pack of its
    /// own: before this, such a profile got no VG overrides and no filters at
    /// all, silently — which is what the four structural profiles, three
    /// schedule profiles and pres-narrative-A1 were doing.
    /// </summary>
    public sealed class ViewStylePackRoutingRule
    {
        /// <summary>DrawingPurpose value ("Plan", "Section", "Coordination", …) or "*".</summary>
        [JsonProperty("purpose")]      public string Purpose { get; set; } = "*";
        /// <summary>ISO discipline code ("A", "S", "M", "E", "P", "H", …) or "*".</summary>
        [JsonProperty("discipline")]   public string Discipline { get; set; } = "*";
        /// <summary>Optional phase narrowing ("PRESENTATION", "FABRICATION", …) or "*".</summary>
        [JsonProperty("phase", NullValueHandling = NullValueHandling.Ignore)]
        public string Phase { get; set; }
        [JsonProperty("stylePackId")]  public string StylePackId { get; set; }

        /// <summary>Wildcard-aware, case-insensitive field match. Null / empty / "*" matches anything.</summary>
        internal static bool Matches(string ruleValue, string actual)
        {
            if (string.IsNullOrWhiteSpace(ruleValue) || ruleValue == "*") return true;
            if (string.IsNullOrWhiteSpace(actual)) return false;
            return string.Equals(ruleValue, actual, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Revit-free predicate, so routing precedence is unit-testable
        /// without a document.
        /// </summary>
        public bool Matches(string purpose, string discipline, string phase)
            => Matches(Purpose, purpose)
            && Matches(Discipline, discipline)
            && Matches(Phase, phase);
    }

    public sealed class ViewStylePackLibrary
    {
        /// <summary>
        /// Legacy integer version. The shipped file has never carried a
        /// "version" key -- it declares <see cref="SchemaVersion"/> -- so this
        /// was always the default 1 and gated nothing (V-12a). Kept bound for
        /// project override files that may still carry it.
        /// </summary>
        [JsonProperty("version")] public int Version { get; set; } = 1;

        /// <summary>
        /// The schema the file was written against ("1.3"). The shipped file
        /// has declared it since v1.3 and nothing bound it, so a pack file from
        /// a newer plugin loaded here with its unknown fields dropped in
        /// silence. <see cref="SchemaGateWarning"/> is the gate.
        /// </summary>
        [JsonProperty("schemaVersion", NullValueHandling = NullValueHandling.Ignore)]
        public string SchemaVersion { get; set; }

        /// <summary>The newest pack-file schema this plugin understands. Bump it
        /// together with the shipped file's "schemaVersion" when a field is added.</summary>
        public const string SupportedSchemaVersion = "1.3";

        /// <summary>
        /// Revit-free gate. Null when the file is loadable as-is; otherwise the
        /// warning to log. A file with no schemaVersion is accepted (project
        /// overrides predate the field); an unparseable one or one NEWER than
        /// <see cref="SupportedSchemaVersion"/> warns, because Newtonsoft will
        /// drop every field this build does not declare without a word.
        /// </summary>
        public static string SchemaGateWarning(string declared, string source)
        {
            if (string.IsNullOrWhiteSpace(declared)) return null;
            if (!System.Version.TryParse(declared.Trim(), out var v))
                return $"{source}: schemaVersion '{declared}' is not a version number -- loaded anyway, but it cannot be gated.";
            var supported = System.Version.Parse(SupportedSchemaVersion);
            if (v > supported)
                return $"{source}: schemaVersion {declared} is newer than this plugin supports ({SupportedSchemaVersion}). "
                     + "Fields added after that are IGNORED -- update StingTools before relying on this file.";
            return null;
        }

        /// <summary>
        /// (purpose, discipline, phase) → pack id, first match wins. See
        /// <see cref="ViewStylePackRoutingRule"/> for why this was dead.
        /// </summary>
        [JsonProperty("routing")] public List<ViewStylePackRoutingRule> Routing { get; set; }
            = new List<ViewStylePackRoutingRule>();

        // Primary list — newer JSON files use "viewStylePacks".
        [JsonProperty("viewStylePacks", NullValueHandling = NullValueHandling.Ignore)]
        public List<ViewStylePack> Packs { get; set; } = new List<ViewStylePack>();

        // Corporate STING_VIEW_STYLE_PACKS.json and every editor-written
        // project override (<project>/_BIM_COORD/view_style_packs.json,
        // see DrawingTypeEditorDialog.ViewStylePackDoc) key the array under
        // "stylePacks", not "viewStylePacks". Without this alias the loader
        // binds nothing and ViewStylePackRegistry falls back to the 3 hard
        // -coded BuildDefaults() packs — i.e. the 31-pack corporate catalogue
        // was runtime-dead. Additive write-only alias; the canonical Packs
        // list still serialises under "viewStylePacks".
        [JsonProperty("stylePacks", NullValueHandling = NullValueHandling.Ignore)]
        public List<ViewStylePack> StylePacksAlias
        {
            set { if (value != null && value.Count > 0) Packs = value; }
        }
    }

}
