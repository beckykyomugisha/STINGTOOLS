using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Lightweight snapshot of project template/style state — written before
    /// destructive ops (BatchVGReset, SyncTemplateOverrides, AutoFixTemplate)
    /// so users can roll back without relying on Revit's undo stack.
    ///
    /// Snapshots land under <project>/_BIM_COORD/snapshots/<ts>/state.json.
    /// </summary>
    public sealed class TemplateStateSnapshot
    {
        public string SchemaVersion { get; set; } = "1.0";
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
        public string DocumentPath { get; set; } = "";
        public string TriggeringOp { get; set; } = "";
        public List<TemplateSnap> Templates { get; set; } = new();
        public List<FilterSnap> Filters { get; set; } = new();
        public Dictionary<string, string> Counters { get; set; } = new();
    }

    public sealed class TemplateSnap
    {
        public int ElementId { get; set; }
        public string Name { get; set; } = "";
        public int Scale { get; set; }
        public string DetailLevel { get; set; } = "";
        public List<int> FilterIds { get; set; } = new();
        public List<CategorySnap> CategoryOverrides { get; set; } = new();
    }

    public sealed class FilterSnap
    {
        public int ElementId { get; set; }
        public string Name { get; set; } = "";
        public List<int> CategoryIds { get; set; } = new();
    }

    public sealed class CategorySnap
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = "";
        public bool Halftone { get; set; }
        public int Transparency { get; set; }
        public int ProjLineWeight { get; set; }
        public int CutLineWeight { get; set; }
    }

    public static class SnapshotEngine
    {
        private const string DirName = "snapshots";

        /// <summary>Capture + write a snapshot. Returns the file path, or empty on failure.</summary>
        public static string Capture(Document doc, string triggeringOp)
        {
            if (doc == null) return "";
            try
            {
                var snap = new TemplateStateSnapshot
                {
                    DocumentPath = doc.PathName ?? doc.Title,
                    TriggeringOp = triggeringOp
                };

                // Templates
                foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    if (!v.IsTemplate) continue;
                    var ts = new TemplateSnap
                    {
                        ElementId = (int)v.Id.Value,
                        Name = v.Name,
                        Scale = v.Scale,
                        DetailLevel = v.DetailLevel.ToString()
                    };
                    try { ts.FilterIds = v.GetFilters().Select(i => (int)i.Value).ToList(); }
                    catch { /* tolerate */ }
                    // capture a few category-level overrides cheaply
                    foreach (var bic in QuickCategoriesToSnap())
                    {
                        try
                        {
                            Category cat = Category.GetCategory(doc, bic);
                            if (cat == null) continue;
                            var ogs = v.GetCategoryOverrides(cat.Id);
                            ts.CategoryOverrides.Add(new CategorySnap
                            {
                                CategoryId = (int)cat.Id.Value,
                                Name = cat.Name,
                                Halftone = ogs.Halftone,
                                Transparency = ogs.Transparency,
                                ProjLineWeight = ogs.ProjectionLineWeight,
                                CutLineWeight = ogs.CutLineWeight
                            });
                        }
                        catch { /* tolerate */ }
                    }
                    snap.Templates.Add(ts);
                }

                // Filters
                foreach (var f in new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
                {
                    var fs = new FilterSnap { ElementId = (int)f.Id.Value, Name = f.Name };
                    try { fs.CategoryIds = f.GetCategories().Select(i => (int)i.Value).ToList(); }
                    catch { }
                    snap.Filters.Add(fs);
                }
                snap.Counters["templates"] = snap.Templates.Count.ToString();
                snap.Counters["filters"] = snap.Filters.Count.ToString();

                // Persist
                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir)) projDir = Path.GetTempPath();
                string ts2 = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string dir = Path.Combine(projDir, "_BIM_COORD", DirName, ts2);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "state.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(snap, Formatting.Indented));
                StingTools.Core.StingLog.Info($"SnapshotEngine: wrote {path}");
                return path;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SnapshotEngine.Capture: {ex.Message}");
                return "";
            }
        }

        public static List<string> ListSnapshots(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return new();
                string root = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD", DirName);
                if (!Directory.Exists(root)) return new();
                return Directory.GetDirectories(root)
                    .OrderByDescending(d => d)
                    .Select(d => Path.Combine(d, "state.json"))
                    .Where(File.Exists)
                    .ToList();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SnapshotEngine.List: {ex.Message}");
                return new();
            }
        }

        public static TemplateStateSnapshot Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<TemplateStateSnapshot>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SnapshotEngine.Load: {ex.Message}");
                return null;
            }
        }

        private static IEnumerable<BuiltInCategory> QuickCategoriesToSnap()
        {
            yield return BuiltInCategory.OST_Walls;
            yield return BuiltInCategory.OST_Floors;
            yield return BuiltInCategory.OST_Ceilings;
            yield return BuiltInCategory.OST_Roofs;
            yield return BuiltInCategory.OST_Doors;
            yield return BuiltInCategory.OST_Windows;
            yield return BuiltInCategory.OST_StructuralColumns;
            yield return BuiltInCategory.OST_StructuralFraming;
            yield return BuiltInCategory.OST_DuctCurves;
            yield return BuiltInCategory.OST_PipeCurves;
            yield return BuiltInCategory.OST_Conduit;
            yield return BuiltInCategory.OST_CableTray;
            yield return BuiltInCategory.OST_LightingFixtures;
            yield return BuiltInCategory.OST_ElectricalEquipment;
            yield return BuiltInCategory.OST_PlumbingFixtures;
        }
    }
}
