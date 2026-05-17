// StingTools — Drawing Template Manager · AEC/FM Filter Library
//
// AecFilterDefinition is the JSON-backed POCO for STING_AEC_FILTERS.json.
// Each entry describes one corporate-baseline ParameterFilterElement —
// a name, the categories it binds to, a rule tree (leaf or AND/OR
// compound), and a default OverrideGraphicSettings recipe so the filter
// renders out-of-the-box without per-pack tuning. Packs that want to
// override a colour or weight can still do so via StyleFilterRule.
//
// Rule grammar:
//   leaf       = { param, kind?, op, value, type? }
//   compound   = { logic, rules[] }   // logic = "and" | "or"
//
//   kind   = "builtin" (default) | "shared" | "phase" | "workset" | "level"
//   op     = equals | notEquals | greater | greaterOrEqual | less |
//            lessOrEqual | contains | notContains | beginsWith |
//            notBeginsWith | endsWith | notEndsWith | hasValue | hasNoValue
//   type   = "string" (default) | "int" | "double" | "elementId" | "yesno"
//
// AecFilterFactory consumes this POCO and emits an ElementFilter +
// ParameterFilterElement. ViewStylePackApplier consumes the override
// recipe via FilterDefaultOverride.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public sealed class AecFilterLibrary
    {
        [JsonProperty("version")]      public int Version { get; set; } = 1;
        [JsonProperty("schemaUri")]    public string SchemaUri { get; set; }
        [JsonProperty("namespace")]    public string Namespace { get; set; } = "STING";
        [JsonProperty("description")]  public string Description { get; set; }

        [JsonProperty("colorSchemes", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> ColorSchemes { get; set; }

        [JsonProperty("filters")]      public List<AecFilterDefinition> Filters { get; set; }
            = new List<AecFilterDefinition>();
    }

    public sealed class AecFilterDefinition
    {
        [JsonProperty("id")]           public string Id { get; set; }
        [JsonProperty("name")]         public string Name { get; set; }

        /// <summary>BuiltInCategory enum names ("OST_Walls") or localised category names.</summary>
        [JsonProperty("categories")]   public List<string> Categories { get; set; } = new List<string>();

        /// <summary>Rule tree — single leaf or { logic, rules[] } compound.</summary>
        [JsonProperty("rule")]         public AecFilterRule Rule { get; set; }

        /// <summary>Default OverrideGraphicSettings recipe applied when packs reference
        /// this filter without their own override values. Pack-level fields win when set.</summary>
        [JsonProperty("override", NullValueHandling = NullValueHandling.Ignore)]
        public FilterDefaultOverride DefaultOverride { get; set; }

        [JsonProperty("tags",     NullValueHandling = NullValueHandling.Ignore)] public List<string> Tags { get; set; }
        [JsonProperty("standard", NullValueHandling = NullValueHandling.Ignore)] public string Standard { get; set; }
        [JsonProperty("notes",    NullValueHandling = NullValueHandling.Ignore)] public string Notes { get; set; }
        [JsonProperty("origin",   NullValueHandling = NullValueHandling.Ignore)] public string Origin { get; set; } = "corporate";
    }

    /// <summary>
    /// Polymorphic rule node — either a leaf (param + op + value) or a
    /// compound (logic + rules[]). Newtonsoft serialises both shapes via
    /// the same POCO; null fields signal which shape this node is.
    /// </summary>
    public sealed class AecFilterRule
    {
        // Leaf fields
        [JsonProperty("param", NullValueHandling = NullValueHandling.Ignore)] public string Param { get; set; }
        [JsonProperty("kind",  NullValueHandling = NullValueHandling.Ignore)] public string Kind  { get; set; }
        [JsonProperty("op",    NullValueHandling = NullValueHandling.Ignore)] public string Op    { get; set; }
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)] public string Value { get; set; }
        [JsonProperty("type",  NullValueHandling = NullValueHandling.Ignore)] public string Type  { get; set; }

        // Compound fields
        [JsonProperty("logic", NullValueHandling = NullValueHandling.Ignore)] public string Logic { get; set; }
        [JsonProperty("rules", NullValueHandling = NullValueHandling.Ignore)] public List<AecFilterRule> Rules { get; set; }

        [JsonIgnore]
        public bool IsLeaf => !string.IsNullOrEmpty(Param);

        [JsonIgnore]
        public bool IsCompound => !string.IsNullOrEmpty(Logic) && Rules != null && Rules.Count > 0;
    }

    /// <summary>
    /// Default OverrideGraphicSettings recipe for a filter. Mirrors the
    /// extended StyleFilterRule shape (Phase 139) — any field left null
    /// means "no override at this layer". ViewStylePackApplier merges
    /// with pack-level overrides, where pack wins.
    /// </summary>
    public sealed class FilterDefaultOverride
    {
        [JsonProperty("visible",          NullValueHandling = NullValueHandling.Ignore)] public bool?   Visible { get; set; }
        [JsonProperty("halftone",         NullValueHandling = NullValueHandling.Ignore)] public bool?   Halftone { get; set; }

        [JsonProperty("projColor",        NullValueHandling = NullValueHandling.Ignore)] public string  ProjColor { get; set; }
        [JsonProperty("projWeight",       NullValueHandling = NullValueHandling.Ignore)] public int?    ProjWeight { get; set; }
        [JsonProperty("projLinePattern",  NullValueHandling = NullValueHandling.Ignore)] public string  ProjLinePattern { get; set; }

        [JsonProperty("cutColor",         NullValueHandling = NullValueHandling.Ignore)] public string  CutColor { get; set; }
        [JsonProperty("cutWeight",        NullValueHandling = NullValueHandling.Ignore)] public int?    CutWeight { get; set; }
        [JsonProperty("cutLinePattern",   NullValueHandling = NullValueHandling.Ignore)] public string  CutLinePattern { get; set; }

        [JsonProperty("surfFgColor",      NullValueHandling = NullValueHandling.Ignore)] public string  SurfFgColor { get; set; }
        [JsonProperty("surfFgPattern",    NullValueHandling = NullValueHandling.Ignore)] public string  SurfFgPattern { get; set; }
        [JsonProperty("surfBgColor",      NullValueHandling = NullValueHandling.Ignore)] public string  SurfBgColor { get; set; }
        [JsonProperty("surfBgPattern",    NullValueHandling = NullValueHandling.Ignore)] public string  SurfBgPattern { get; set; }

        [JsonProperty("cutFgColor",       NullValueHandling = NullValueHandling.Ignore)] public string  CutFgColor { get; set; }
        [JsonProperty("cutFgPattern",     NullValueHandling = NullValueHandling.Ignore)] public string  CutFgPattern { get; set; }
        [JsonProperty("cutBgColor",       NullValueHandling = NullValueHandling.Ignore)] public string  CutBgColor { get; set; }
        [JsonProperty("cutBgPattern",     NullValueHandling = NullValueHandling.Ignore)] public string  CutBgPattern { get; set; }

        [JsonProperty("transparency",     NullValueHandling = NullValueHandling.Ignore)] public int?    Transparency { get; set; }
        [JsonProperty("detailLevel",      NullValueHandling = NullValueHandling.Ignore)] public string  DetailLevel { get; set; }
    }
}
