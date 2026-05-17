using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 137
//
// ProductionPresetRegistry persists DrawingProductionPreset rows to
// <project>/_BIM_COORD/production_presets.json. Pure I/O — no Revit
// API beyond Document.PathName. Built-in defaults (GetDefault) are
// returned without any disk read so commands can fall back when no
// project file is present.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public static class ProductionPresetRegistry
    {
        private const string FileName = "production_presets.json";

        public static List<DrawingProductionPreset> Load(Document doc)
        {
            try
            {
                var path = ResolvePath(doc);
                if (path == null || !File.Exists(path))
                    return new List<DrawingProductionPreset>();
                var json = File.ReadAllText(path);
                var list = JsonConvert.DeserializeObject<List<DrawingProductionPreset>>(json);
                return list ?? new List<DrawingProductionPreset>();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ProductionPresetRegistry.Load failed: {ex.Message}");
                return new List<DrawingProductionPreset>();
            }
        }

        public static void Save(Document doc, List<DrawingProductionPreset> presets)
        {
            try
            {
                var path = ResolvePath(doc);
                if (path == null)
                {
                    StingTools.Core.StingLog.Warn(
                        "ProductionPresetRegistry.Save: project has no path on disk yet — preset not saved.");
                    return;
                }
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(presets ?? new List<DrawingProductionPreset>(), Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ProductionPresetRegistry.Save failed: {ex.Message}");
            }
        }

        public static DrawingProductionPreset GetById(Document doc, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var presets = Load(doc);
            return presets.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static DrawingProductionPreset GetDefault(string commandType)
        {
            var preset = new DrawingProductionPreset
            {
                Id = "default-" + (commandType ?? "generic").ToLowerInvariant(),
                CommandType = commandType ?? "Generic",
                CreatedBy = "STING",
                CreatedAt = DateTime.UtcNow.ToString("o"),
                General = new ProductionGeneralSettings(),
                AnnotationOverrides = new Dictionary<string, AnnotationRulePack>(),
                VgOverrides = new Dictionary<string, List<PresetCategoryOverride>>(),
                CreateSheets = true,
                CreatePackage = true
            };

            switch (commandType)
            {
                case "PerLevel":
                    preset.Name = "Per Level Standard";
                    preset.Description = "Per-level production at the DrawingType's native scale + full annotation pass.";
                    preset.General.DuplicateOption = "Duplicate";
                    preset.General.RunAnnotation = true;
                    break;

                case "Sections":
                    preset.Name = "Sections Standard";
                    preset.Description = "North-south sections at 10000mm depth with levels + grids visible.";
                    preset.SectionConfig = new SectionProductionConfig
                    {
                        CuttingDirection = "NorthSouth",
                        DepthMm = 10000,
                        ShowLevels = true,
                        ShowGrids  = true,
                        AutoPlace  = "ManualSelection"
                    };
                    break;

                case "ExteriorElevations":
                    preset.Name = "Exterior Elevations Standard";
                    preset.Description = "Four cardinal exterior elevations on a single 1+4 sheet.";
                    preset.ElevationConfig = new ElevationProductionConfig
                    {
                        FacesTo = new List<string> { "North", "South", "East", "West" },
                        FarClipMm = 30000,
                        UseOneFourViewSheet = true
                    };
                    break;

                default:
                    preset.Name = (commandType ?? "Generic") + " Default";
                    preset.Description = "Default preset — all fields at class defaults.";
                    break;
            }

            return preset;
        }

        private static string ResolvePath(Document doc)
        {
            if (doc == null) return null;
            var projPath = doc.PathName;
            if (string.IsNullOrEmpty(projPath)) return null;
            var dir = Path.GetDirectoryName(projPath);
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", FileName);
        }
    }
}
