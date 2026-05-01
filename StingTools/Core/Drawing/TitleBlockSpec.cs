// StingTools — Drawing Template Manager · Phase 170 — Title-block factory POCOs
//
// Spec model loaded from Data/STING_TITLE_BLOCKS.json. Each
// TitleBlockSpec describes one .rfa family the generator will mint:
// the .rft template to start from, the shared parameters to add,
// the geometry (lines / labels / static text / filled regions)
// to draw, and the parametric reflow groups that implement the
// hybrid BIM-mode toggle (per docs/TITLE_BLOCK_FAMILY_DESIGN.md
// §3.1.2).
//
// All coordinates in mm relative to the title-block sheet's
// bottom-left (consistent with Revit family-doc convention). The
// generator converts to internal feet at place time.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    /// <summary>Root JSON document — list of families to mint.</summary>
    public sealed class TitleBlockLibrary
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("name")]          public string Name { get; set; }
        [JsonProperty("description")]   public string Description { get; set; }
        [JsonProperty("lastUpdated")]   public string LastUpdated { get; set; }
        [JsonProperty("families")]      public List<TitleBlockSpec> Families { get; set; }
            = new List<TitleBlockSpec>();
    }

    /// <summary>Single .rfa family.</summary>
    public sealed class TitleBlockSpec
    {
        [JsonProperty("id")]                   public string Id { get; set; }
        [JsonProperty("description")]          public string Description { get; set; }

        /// <summary>Path to the .rft Revit family template, relative to
        /// Revit's family-template root, OR absolute. Generator searches
        /// both paths; first existing wins.</summary>
        [JsonProperty("templateRft")]          public string TemplateRft { get; set; }

        /// <summary>Output path for the .rfa, relative to the project
        /// directory or absolute. Generator creates directories as
        /// needed.</summary>
        [JsonProperty("saveAs")]               public string SaveAs { get; set; }

        [JsonProperty("category")]             public string Category { get; set; }
            = "OST_TitleBlocks";

        /// <summary>Family-internal Yes/No instance parameter that
        /// drives BIM-mode visibility. When the user toggles this
        /// to 0 on a sheet, every cell flagged bimOnly hides via the
        /// hybrid Strategy A / B / two-label-trick.</summary>
        [JsonProperty("bimModeParameter")]     public string BimModeParameter { get; set; }
            = "STING_BIM_MODE_BOOL";

        [JsonProperty("parameters")]   public List<ParamSpec>        Parameters    { get; set; } = new List<ParamSpec>();
        [JsonProperty("lines")]        public List<LineSpec>         Lines         { get; set; } = new List<LineSpec>();
        [JsonProperty("staticText")]   public List<StaticTextSpec>   StaticText    { get; set; } = new List<StaticTextSpec>();
        [JsonProperty("labels")]       public List<LabelSpec>        Labels        { get; set; } = new List<LabelSpec>();
        [JsonProperty("labelPairs")]   public List<LabelPairSpec>    LabelPairs    { get; set; } = new List<LabelPairSpec>();
        [JsonProperty("filledRegions")]public List<FilledRegionSpec> FilledRegions { get; set; } = new List<FilledRegionSpec>();
        [JsonProperty("reflowGroups")] public List<ReflowGroupSpec>  ReflowGroups  { get; set; } = new List<ReflowGroupSpec>();
    }

    /// <summary>One shared / family parameter to add via FamilyManager.</summary>
    public sealed class ParamSpec
    {
        [JsonProperty("name")]      public string Name { get; set; }
        [JsonProperty("instance")]  public bool   Instance { get; set; } = true;

        /// <summary>"shared" pulls from the shared parameter file by
        /// name; "internal" creates a family-internal parameter (used
        /// for STING_BIM_MODE_BOOL itself + the calculated NOT_BIM
        /// inverse). Default "shared".</summary>
        [JsonProperty("kind")]      public string Kind { get; set; } = "shared";

        /// <summary>Used only when kind = "internal". One of
        /// "Text", "YesNo", "Length", "Number", "Integer". Default
        /// "Text".</summary>
        [JsonProperty("type")]      public string Type { get; set; } = "Text";

        /// <summary>Parameter group — "IdentityData", "General",
        /// "Geometry", "Constraints". Default "IdentityData".</summary>
        [JsonProperty("group")]     public string Group { get; set; } = "IdentityData";

        /// <summary>Optional formula. Family-internal calculated
        /// parameters (e.g. "if(STING_BIM_MODE_BOOL, 110, 70)" for
        /// strip height) and inverse booleans
        /// ("not(STING_BIM_MODE_BOOL)") are set via this field.</summary>
        [JsonProperty("formula")]   public string Formula { get; set; }

        /// <summary>Optional default value (used when no formula).
        /// "1" / "0" for booleans, plain number for length, free
        /// string for text.</summary>
        [JsonProperty("default")]   public string Default { get; set; }
    }

    /// <summary>Detail line on the title-block view.</summary>
    public sealed class LineSpec
    {
        [JsonProperty("from")]    public double[] From { get; set; }   // [x, y] mm
        [JsonProperty("to")]      public double[] To   { get; set; }
        [JsonProperty("style")]   public string   Style { get; set; } = "Medium Lines";
        [JsonProperty("bimOnly")] public bool     BimOnly { get; set; }

        /// <summary>"always" (default), "bimOnly" (legacy alias for
        /// bimOnly:true), or "nonBimOnly" (Phase 171 — visible only
        /// when BIM=0; lets spec authors do Strategy A strip
        /// auto-shrink: pair a `bimOnly` line at the full-mode strip
        /// top with a `nonBimOnly` line at the collapsed strip top).
        /// When set, takes precedence over bimOnly.</summary>
        [JsonProperty("visibility")] public string Visibility { get; set; }
    }

    /// <summary>Static text — cell label like "CLIENT", not bound
    /// to a parameter.</summary>
    public sealed class StaticTextSpec
    {
        [JsonProperty("text")]    public string   Text { get; set; }
        [JsonProperty("anchor")]  public double[] Anchor { get; set; }   // [x, y] mm
        [JsonProperty("size")]    public double   Size { get; set; } = 1.8;  // mm text height
        [JsonProperty("hAlign")]  public string   HAlign { get; set; } = "Left";
        [JsonProperty("vAlign")]  public string   VAlign { get; set; } = "Middle";
        [JsonProperty("bimOnly")] public bool     BimOnly { get; set; }
        [JsonProperty("textTypeName")] public string TextTypeName { get; set; }
        [JsonProperty("visibility")] public string Visibility { get; set; }
    }

    /// <summary>Label bound to a single family parameter.</summary>
    public sealed class LabelSpec
    {
        [JsonProperty("param")]   public string   Param { get; set; }
        [JsonProperty("anchor")]  public double[] Anchor { get; set; }
        [JsonProperty("size")]    public double   Size { get; set; } = 2.5;
        [JsonProperty("hAlign")]  public string   HAlign { get; set; } = "Left";
        [JsonProperty("vAlign")]  public string   VAlign { get; set; } = "Middle";
        [JsonProperty("prefix")]  public string   Prefix { get; set; } = "";
        [JsonProperty("suffix")]  public string   Suffix { get; set; } = "";
        [JsonProperty("bimOnly")] public bool     BimOnly { get; set; }
        [JsonProperty("visibility")] public string Visibility { get; set; }
    }

    /// <summary>Two labels at the same anchor with reciprocal
    /// visibility — implements the SHEET_FULL_REF / Sheet Number
    /// switch. paramA shows when BIM=1, paramB shows when BIM=0.</summary>
    public sealed class LabelPairSpec
    {
        [JsonProperty("paramBim")]    public string   ParamBim { get; set; }       // visible when BIM=1
        [JsonProperty("paramNonBim")] public string   ParamNonBim { get; set; }    // visible when BIM=0
        [JsonProperty("anchor")]      public double[] Anchor { get; set; }
        [JsonProperty("size")]        public double   Size { get; set; } = 5.0;
        [JsonProperty("hAlign")]      public string   HAlign { get; set; } = "Center";
        [JsonProperty("vAlign")]      public string   VAlign { get; set; } = "Middle";
        [JsonProperty("paramNonBimIsBuiltIn")] public bool ParamNonBimIsBuiltIn { get; set; } = false;
    }

    /// <summary>Filled region — typically the suitability chip / status
    /// band background. fill = solid colour name; for solid fills the
    /// generator picks the project's "Solid Fill" pattern.</summary>
    public sealed class FilledRegionSpec
    {
        [JsonProperty("topLeft")]     public double[] TopLeft { get; set; }      // [x, y] mm
        [JsonProperty("bottomRight")] public double[] BottomRight { get; set; }
        [JsonProperty("fillTypeName")] public string  FillTypeName { get; set; } = "Solid fill - Light Grey";
        [JsonProperty("color")]       public string   Color { get; set; }        // "#RRGGBB", optional
        [JsonProperty("bimOnly")]     public bool     BimOnly { get; set; }
        [JsonProperty("visibility")]  public string   Visibility { get; set; }
    }

    /// <summary>Reflow group — Strategy B from §3.1. The group is a
    /// stripe of cells whose vertical extent is driven by a length
    /// parameter that's formula-bound to BIM_MODE. When BIM=0 the
    /// length collapses to 0 and every cell inside the group hides.
    ///
    /// Generator authoring: places two reference planes (top/bottom
    /// of the group), creates a labelled dimension between them,
    /// adds a length parameter, sets the formula, then for every
    /// element placed by the children Lines/Labels/Text/FilledRegions
    /// inside the group also binds the element's Visible to the
    /// group's gate-parameter (because hiding via length=0 alone
    /// doesn't always remove the element — defence in depth).</summary>
    public sealed class ReflowGroupSpec
    {
        [JsonProperty("id")]            public string Id { get; set; }            // "row7Seg" / "rowTrCdeMidp" / etc.
        [JsonProperty("description")]   public string Description { get; set; }

        /// <summary>The length parameter the group exposes. Generator
        /// adds it as a family-internal Length instance parameter
        /// with formula = `if(STING_BIM_MODE_BOOL, fullHeightMm,
        /// collapsedHeightMm)`. If collapsedHeightMm = 0 (default)
        /// the group disappears entirely in non-BIM mode.</summary>
        [JsonProperty("heightParam")]   public string HeightParam { get; set; }

        [JsonProperty("fullHeightMm")]      public double FullHeightMm { get; set; }
        [JsonProperty("collapsedHeightMm")] public double CollapsedHeightMm { get; set; } = 0.0;

        /// <summary>Y coordinate of the group's BOTTOM edge (anchored
        /// reference). Top edge = bottomY + heightParam.</summary>
        [JsonProperty("bottomY")]       public double BottomY { get; set; }

        /// <summary>Children — same shape as the family-level
        /// collections, but their (x, y) coords are taken AS-IS;
        /// the generator ALSO binds each child's Visible to the
        /// group's gate parameter so the element hides when
        /// heightParam = 0.</summary>
        [JsonProperty("lines")]         public List<LineSpec>         Lines         { get; set; } = new List<LineSpec>();
        [JsonProperty("staticText")]    public List<StaticTextSpec>   StaticText    { get; set; } = new List<StaticTextSpec>();
        [JsonProperty("labels")]        public List<LabelSpec>        Labels        { get; set; } = new List<LabelSpec>();
        [JsonProperty("filledRegions")] public List<FilledRegionSpec> FilledRegions { get; set; } = new List<FilledRegionSpec>();
    }

    /// <summary>Loader — corporate baseline at Data/STING_TITLE_BLOCKS.json,
    /// no project override yet (Phase 171).</summary>
    public static class TitleBlockSpecRegistry
    {
        public static TitleBlockLibrary Load()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("STING_TITLE_BLOCKS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("TitleBlockSpecRegistry: STING_TITLE_BLOCKS.json not found");
                    return null;
                }
                return JsonConvert.DeserializeObject<TitleBlockLibrary>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Error("TitleBlockSpecRegistry.Load", ex);
                return null;
            }
        }
    }
}
