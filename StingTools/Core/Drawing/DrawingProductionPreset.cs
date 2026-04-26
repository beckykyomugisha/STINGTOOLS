// StingTools — Drawing Template Manager · Phase 137
//
// DrawingProductionPreset is a reusable bundle of options for the
// drawing-production commands (per-level plans, sections,
// elevations). One preset answers "how do I want this command to
// behave?": general settings, per-discipline annotation overrides,
// VG overrides, level/phase filters, sheet creation, package id,
// and command-specific config blocks (sections / elevations).
//
// Persisted to <project>/_BIM_COORD/production_presets.json by
// ProductionPresetRegistry. Read by the production commands.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public sealed class DrawingProductionPreset
    {
        [JsonProperty("id")]          public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("createdBy")]   public string CreatedBy { get; set; }
        [JsonProperty("createdAt")]   public string CreatedAt { get; set; }

        /// <summary>"PerLevel" / "Sections" / "ExteriorElevations".</summary>
        [JsonProperty("commandType")] public string CommandType { get; set; }

        [JsonProperty("general")] public ProductionGeneralSettings General { get; set; }
            = new ProductionGeneralSettings();

        /// <summary>Per-DrawingType-id annotation overrides.</summary>
        [JsonProperty("annotationOverrides")]
        public Dictionary<string, AnnotationRulePack> AnnotationOverrides { get; set; }
            = new Dictionary<string, AnnotationRulePack>();

        /// <summary>Per-DrawingType-id VG override list.</summary>
        [JsonProperty("vgOverrides")]
        public Dictionary<string, List<PresetCategoryOverride>> VgOverrides { get; set; }
            = new Dictionary<string, List<PresetCategoryOverride>>();

        [JsonProperty("levelFilter", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> LevelFilter { get; set; }

        [JsonProperty("phaseFilter", NullValueHandling = NullValueHandling.Ignore)]
        public string PhaseFilter { get; set; }

        [JsonProperty("createSheets")]   public bool CreateSheets  { get; set; } = true;
        [JsonProperty("createPackage")]  public bool CreatePackage { get; set; } = true;

        [JsonProperty("packageId", NullValueHandling = NullValueHandling.Ignore)]
        public string PackageId { get; set; }

        [JsonProperty("sectionConfig", NullValueHandling = NullValueHandling.Ignore)]
        public SectionProductionConfig SectionConfig { get; set; }

        [JsonProperty("elevationConfig", NullValueHandling = NullValueHandling.Ignore)]
        public ElevationProductionConfig ElevationConfig { get; set; }
    }

    public sealed class ProductionGeneralSettings
    {
        /// <summary>Duplicate / DuplicateWithDetailing / DuplicateAsDependent.</summary>
        [JsonProperty("duplicateOption")] public string DuplicateOption { get; set; } = "Duplicate";

        /// <summary>When true, repeated runs match existing views by name + tag and skip creation.</summary>
        [JsonProperty("idempotent")]      public bool   Idempotent { get; set; } = true;
        [JsonProperty("runAnnotation")]   public bool   RunAnnotation { get; set; } = true;
        [JsonProperty("hideUnwantedCats")] public bool  HideUnwantedCats { get; set; } = false;

        /// <summary>When true, only create views the active DrawingType marks Required.</summary>
        [JsonProperty("generateOnlyDefault")] public bool GenerateOnlyDefault { get; set; } = false;

        [JsonProperty("scaleOverride",       NullValueHandling = NullValueHandling.Ignore)] public int?   ScaleOverride { get; set; }
        [JsonProperty("detailLevelOverride", NullValueHandling = NullValueHandling.Ignore)] public string DetailLevelOverride { get; set; }
    }

    public sealed class PresetCategoryOverride
    {
        [JsonProperty("category")]        public string Category { get; set; }
        [JsonProperty("subCategory",      NullValueHandling = NullValueHandling.Ignore)] public string SubCategory { get; set; }
        [JsonProperty("visible",          NullValueHandling = NullValueHandling.Ignore)] public bool?  Visible { get; set; }

        [JsonProperty("projLineColor",    NullValueHandling = NullValueHandling.Ignore)] public string ProjLineColor { get; set; }
        [JsonProperty("projLineWeight",   NullValueHandling = NullValueHandling.Ignore)] public int?   ProjLineWeight { get; set; }
        [JsonProperty("projLinePattern",  NullValueHandling = NullValueHandling.Ignore)] public string ProjLinePattern { get; set; }

        [JsonProperty("surfFgColor",      NullValueHandling = NullValueHandling.Ignore)] public string SurfFgColor { get; set; }
        [JsonProperty("surfFgPattern",    NullValueHandling = NullValueHandling.Ignore)] public string SurfFgPattern { get; set; }
        [JsonProperty("surfFgVisible",    NullValueHandling = NullValueHandling.Ignore)] public bool?  SurfFgVisible { get; set; }
        [JsonProperty("surfBgColor",      NullValueHandling = NullValueHandling.Ignore)] public string SurfBgColor { get; set; }
        [JsonProperty("surfBgPattern",    NullValueHandling = NullValueHandling.Ignore)] public string SurfBgPattern { get; set; }
        [JsonProperty("surfBgVisible",    NullValueHandling = NullValueHandling.Ignore)] public bool?  SurfBgVisible { get; set; }
        [JsonProperty("transparency",     NullValueHandling = NullValueHandling.Ignore)] public int?   Transparency { get; set; }

        [JsonProperty("cutLineColor",     NullValueHandling = NullValueHandling.Ignore)] public string CutLineColor { get; set; }
        [JsonProperty("cutLineWeight",    NullValueHandling = NullValueHandling.Ignore)] public int?   CutLineWeight { get; set; }
        [JsonProperty("cutLinePattern",   NullValueHandling = NullValueHandling.Ignore)] public string CutLinePattern { get; set; }
        [JsonProperty("cutFgColor",       NullValueHandling = NullValueHandling.Ignore)] public string CutFgColor { get; set; }
        [JsonProperty("cutFgPattern",     NullValueHandling = NullValueHandling.Ignore)] public string CutFgPattern { get; set; }
        [JsonProperty("cutFgVisible",     NullValueHandling = NullValueHandling.Ignore)] public bool?  CutFgVisible { get; set; }
        [JsonProperty("cutBgColor",       NullValueHandling = NullValueHandling.Ignore)] public string CutBgColor { get; set; }
        [JsonProperty("cutBgPattern",     NullValueHandling = NullValueHandling.Ignore)] public string CutBgPattern { get; set; }

        [JsonProperty("halftone",         NullValueHandling = NullValueHandling.Ignore)] public bool?  Halftone { get; set; }
        [JsonProperty("detailLevel",      NullValueHandling = NullValueHandling.Ignore)] public string DetailLevel { get; set; }
    }

    public sealed class SectionProductionConfig
    {
        /// <summary>NorthSouth / EastWest / Perpendicular / CustomAngle.</summary>
        [JsonProperty("cuttingDirection")] public string CuttingDirection { get; set; } = "Perpendicular";

        [JsonProperty("customAngleDeg", NullValueHandling = NullValueHandling.Ignore)]
        public double? CustomAngleDeg { get; set; }

        [JsonProperty("spacingMm")]   public double SpacingMm { get; set; } = 5000;
        [JsonProperty("depthMm")]     public double DepthMm   { get; set; } = 10000;
        [JsonProperty("farClipMm")]   public double FarClipMm { get; set; } = 10000;
        [JsonProperty("showLevels")]  public bool   ShowLevels { get; set; } = true;
        [JsonProperty("showGrids")]   public bool   ShowGrids  { get; set; } = true;

        /// <summary>ManualSelection / AlongGridLines / PerRoom.</summary>
        [JsonProperty("autoPlace")]   public string AutoPlace { get; set; } = "ManualSelection";

        [JsonProperty("segmentedView")] public bool   SegmentedView { get; set; } = false;

        /// <summary>Section / Callout / Both.</summary>
        [JsonProperty("calloutMode")]   public string CalloutMode { get; set; } = "Section";
    }

    public sealed class ElevationProductionConfig
    {
        [JsonProperty("facesTo")] public List<string> FacesTo { get; set; }
            = new List<string> { "North", "South", "East", "West" };

        [JsonProperty("offsetMm")]    public double OffsetMm { get; set; } = 3000;
        [JsonProperty("farClipMm")]   public double FarClipMm { get; set; } = 30000;
        [JsonProperty("showLevels")]  public bool   ShowLevels { get; set; } = true;
        [JsonProperty("showGrids")]   public bool   ShowGrids { get; set; } = true;

        /// <summary>When true, the four exterior elevations land on a single 1+4-view sheet.</summary>
        [JsonProperty("useOneFourViewSheet")] public bool UseOneFourViewSheet { get; set; } = true;

        [JsonProperty("markerFamily", NullValueHandling = NullValueHandling.Ignore)]
        public string MarkerFamily { get; set; }

        [JsonProperty("interiorFacesCount")] public int InteriorFacesCount { get; set; } = 4;

        [JsonProperty("interiorRoomFilter", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> InteriorRoomFilter { get; set; }
    }
}
