// StingTools — Drawing Template Manager · Phase 168 — Match-line subsystem
//
// Configuration POCO for the Match-Line engine. Loaded from
// `Data/STING_MATCH_LINES.json` at first call per document and merged
// with optional project override at
// `<project>/_BIM_COORD/match_lines.json` (project entries win).
//
// Defaults are conservative — coplanar tolerance 1 mm, 100 mm minimum
// face overlap (so corner-touches don't register as match lines), 25 mm
// extension beyond the crop edge, captions on both ends.
//
// MatchLineEngine reads this once at run start; runtime mutation should
// go through MatchLineConfigRegistry.Reload(doc) so the registered
// IUpdater + the active engine instance see the change atomically.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public sealed class MatchLineConfig
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("name")]          public string Name { get; set; }
        [JsonProperty("description")]   public string Description { get; set; }
        [JsonProperty("lastUpdated")]   public string LastUpdated { get; set; }

        [JsonProperty("adjacency")]  public AdjacencyCfg  Adjacency  { get; set; } = new AdjacencyCfg();
        [JsonProperty("geometry")]   public GeometryCfg   Geometry   { get; set; } = new GeometryCfg();
        [JsonProperty("captions")]   public CaptionsCfg   Captions   { get; set; } = new CaptionsCfg();
        [JsonProperty("stamping")]   public StampingCfg   Stamping   { get; set; } = new StampingCfg();
        [JsonProperty("validation")] public ValidationCfg Validation { get; set; } = new ValidationCfg();
        [JsonProperty("discipline")] public DisciplineCfg Discipline { get; set; } = new DisciplineCfg();
    }

    public sealed class AdjacencyCfg
    {
        [JsonProperty("coplanarToleranceMm")] public double CoplanarToleranceMm { get; set; } = 1.0;
        [JsonProperty("minOverlapMm")]        public double MinOverlapMm { get; set; } = 100.0;
        [JsonProperty("ignoreCornerTouches")] public bool   IgnoreCornerTouches { get; set; } = true;
        [JsonProperty("considerLevelMatch")]  public bool   ConsiderLevelMatch { get; set; } = true;
    }

    public sealed class GeometryCfg
    {
        [JsonProperty("lineStyleName")]         public string LineStyleName { get; set; } = "STING - Match Line";
        [JsonProperty("fallbackLineStyleName")] public string FallbackLineStyleName { get; set; } = "Medium Lines";
        [JsonProperty("marginFromCropMm")]      public double MarginFromCropMm { get; set; } = 0.0;
        [JsonProperty("extendBeyondCropMm")]    public double ExtendBeyondCropMm { get; set; } = 25.0;
        [JsonProperty("drawOnBothSides")]       public bool   DrawOnBothSides { get; set; } = true;
    }

    public sealed class CaptionsCfg
    {
        [JsonProperty("tagFamilyName")]              public string TagFamilyName { get; set; } = "STING_TAG_MATCHLINE";
        [JsonProperty("fallbackTextNoteTypeName")]   public string FallbackTextNoteTypeName { get; set; } = "STING - 2.5mm";
        [JsonProperty("tipPlacement")]               public string TipPlacement { get; set; } = "BothEnds";
        [JsonProperty("tipFormat")]                  public string TipFormat { get; set; } = "see {paired_ref} →";
        [JsonProperty("midPlacement")]               public string MidPlacement { get; set; } = "Centre";
        [JsonProperty("midFormat")]                  public string MidFormat { get; set; } = "MATCH LINE — see {paired_ref}";
    }

    public sealed class StampingCfg
    {
        [JsonProperty("writePairGuid")]                 public bool WritePairGuid { get; set; } = true;
        [JsonProperty("writeDirection")]                public bool WriteDirection { get; set; } = true;
        [JsonProperty("writePairedRef")]                public bool WritePairedRef { get; set; } = true;
        [JsonProperty("stampViewWithMatchLineCount")]   public bool StampViewWithMatchLineCount { get; set; } = true;
    }

    public sealed class ValidationCfg
    {
        [JsonProperty("warnIfRefMissingInBundle")]   public bool WarnIfRefMissingInBundle { get; set; } = true;
        [JsonProperty("warnIfPairedSheetRenumbered")] public bool WarnIfPairedSheetRenumbered { get; set; } = true;
        [JsonProperty("warnIfScopeBoxMoved")]        public bool WarnIfScopeBoxMoved { get; set; } = true;
    }

    public sealed class DisciplineCfg
    {
        [JsonProperty("tintByDiscipline")] public bool TintByDiscipline { get; set; } = true;
        [JsonProperty("colorMap")]
        public Dictionary<string, string> ColorMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class MatchLineConfigRegistry
    {
        private static MatchLineConfig _cached;
        private static readonly object _lock = new object();

        public static MatchLineConfig Get(Autodesk.Revit.DB.Document doc)
        {
            lock (_lock)
            {
                if (_cached != null) return _cached;
                _cached = LoadFromDisk(doc) ?? new MatchLineConfig
                {
                    Name = "STING Match-Line config (defaults)",
                };
                return _cached;
            }
        }

        public static void Reload(Autodesk.Revit.DB.Document doc)
        {
            lock (_lock) { _cached = null; }
        }

        private static MatchLineConfig LoadFromDisk(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                // 1. Corporate baseline shipped alongside the DLL.
                var corp = StingToolsApp.FindDataFile("STING_MATCH_LINES.json");
                MatchLineConfig cfg = null;
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    cfg = JsonConvert.DeserializeObject<MatchLineConfig>(File.ReadAllText(corp));

                // 2. Project override at <project>/_BIM_COORD/match_lines.json
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    var dir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "_BIM_COORD");
                    var proj = Path.Combine(dir, "match_lines.json");
                    if (File.Exists(proj))
                    {
                        var over = JsonConvert.DeserializeObject<MatchLineConfig>(File.ReadAllText(proj));
                        if (over != null) cfg = over;   // wholesale override; granular merge is a Phase II refinement.
                    }
                }
                return cfg;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MatchLineConfigRegistry.LoadFromDisk: {ex.Message}");
                return null;
            }
        }
    }
}
