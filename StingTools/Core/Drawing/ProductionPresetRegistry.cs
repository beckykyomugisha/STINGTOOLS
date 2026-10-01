using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 137
//
// ProductionPresetRegistry persists DrawingProductionPreset rows to
// <project>/_BIM_COORD/production_presets.json. Pure I/O — no Revit
// API beyond Document.PathName.

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

        public static List<DrawingProductionPreset> Load(Document doc) => Load(doc, out _);

        /// <summary>
        /// The project's presets. DTW-188: <paramref name="error"/> is set when
        /// the file EXISTS but cannot be read — an empty list then means "could
        /// not read", not "none saved", and nothing may write the file.
        /// </summary>
        public static List<DrawingProductionPreset> Load(Document doc, out string error)
        {
            error = null;
            try
            {
                var path = ResolvePath(doc);
                if (path == null || !File.Exists(path))
                    return new List<DrawingProductionPreset>();
                var json = File.ReadAllText(path);
                var list = JsonConvert.DeserializeObject<List<DrawingProductionPreset>>(json);
                if (list == null && !string.IsNullOrWhiteSpace(json))
                    throw new InvalidDataException("the file deserialised to nothing");
                return list ?? new List<DrawingProductionPreset>();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ProductionPresetRegistry.Load failed: {ex.Message}");
                error = $"{FileName} could not be read: {ex.Message}";
                return new List<DrawingProductionPreset>();
            }
        }

        /// <summary>
        /// Write the presets. Returns false with <paramref name="error"/> set
        /// when nothing was written. DTW-188: this used to return void and
        /// swallow every failure — an unsaved model and a write error both
        /// logged a warning while the dialog said "Saved" — and it overwrote a
        /// presets file that had failed to load, erasing every preset in it.
        /// </summary>
        public static bool Save(Document doc, List<DrawingProductionPreset> presets, out string error)
        {
            error = null;
            try
            {
                var path = ResolvePath(doc);
                if (path == null)
                {
                    error = "the project has not been saved to disk yet — save the Revit model first.";
                    StingTools.Core.StingLog.Warn("ProductionPresetRegistry.Save: " + error);
                    return false;
                }
                Load(doc, out var loadError);
                if (loadError != null)
                {
                    error = loadError + " Saving would replace every preset in it; repair or move the file first.";
                    StingTools.Core.StingLog.Warn("ProductionPresetRegistry.Save refused: " + error);
                    return false;
                }
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(presets ?? new List<DrawingProductionPreset>(), Formatting.Indented);
                OutputLocationHelper.WriteAllTextAtomic(path, json);
                return true;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ProductionPresetRegistry.Save failed: {ex.Message}");
                error = ex.Message;
                return false;
            }
        }

        public static DrawingProductionPreset GetById(Document doc, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var presets = Load(doc);
            return presets.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolvePath(Document doc)
        {
            if (doc == null) return null;
            var projPath = doc.PathName;
            if (string.IsNullOrEmpty(projPath)) return null;
            var dir = Path.GetDirectoryName(projPath);
            if (string.IsNullOrEmpty(dir)) return null;
            return StingPaths.MetaFile(doc, "_BIM_COORD", FileName);
        }
    }
}
