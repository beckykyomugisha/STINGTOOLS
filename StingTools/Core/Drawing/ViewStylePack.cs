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
}
