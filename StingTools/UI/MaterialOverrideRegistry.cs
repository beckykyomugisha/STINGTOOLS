using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Project-scoped material override file.
    /// Path: <c>&lt;project&gt;/_BIM_COORD/materials.json</c>
    ///
    /// Layered on top of the corporate <c>BLE_MATERIALS.csv</c> +
    /// <c>MEP_MATERIALS.csv</c> + <c>MATERIAL_LOOKUP.csv</c>: a project
    /// can override one row without forking the whole catalogue. Same
    /// pattern Drawing Types use for <c>_BIM_COORD/drawing_types.json</c>.
    ///
    /// The override JSON shape:
    /// <code>
    /// {
    ///   "schemaVersion": 1,
    ///   "materials": {
    ///     "BLE_Concrete_C40": {
    ///       "cost":          85.00,
    ///       "carbonKgCo2e":  410.0,
    ///       "thermalConductivityWmK": 2.30,
    ///       "epdSource":     "EC3-12345",
    ///       "epdDate":       "2024-09-15",
    ///       "class":         "Concrete",
    ///       "notes":         "Project-specific GGBS-blended mix"
    ///     }
    ///   }
    /// }
    /// </code>
    /// Empty / missing entries fall through to the corporate baseline.
    /// </summary>
    public static class MaterialOverrideRegistry
    {
        // ── Per-document cache ──
        private static readonly Dictionary<string, MaterialOverrideFile> _cache =
            new Dictionary<string, MaterialOverrideFile>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        public const string FileName = "materials.json";

        public static string GetOverrideFilePath(Document doc)
        {
            string root = Core.ProjectFolderEngine.GetDataPath(doc, "");
            if (string.IsNullOrEmpty(root)) root = ".";
            return Path.Combine(root, FileName);
        }

        public static MaterialOverrideFile GetOrLoad(Document doc)
        {
            if (doc == null) return MaterialOverrideFile.Empty;
            string key = doc.PathName ?? doc.Title ?? "(untitled)";
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var fresh = LoadFromDisk(GetOverrideFilePath(doc));
                _cache[key] = fresh;
                return fresh;
            }
        }

        public static void Reload(Document doc)
        {
            if (doc == null) return;
            string key = doc.PathName ?? doc.Title ?? "(untitled)";
            lock (_lock) { _cache.Remove(key); }
            GetOrLoad(doc);
        }

        public static void EnsureOverrideFile(Document doc)
        {
            string path = GetOverrideFilePath(doc);
            if (File.Exists(path)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var seed = new MaterialOverrideFile
                {
                    SchemaVersion = 1,
                    Materials = new Dictionary<string, MaterialOverrideRow>(StringComparer.OrdinalIgnoreCase),
                };
                // Seed with a worked example so admins see the shape.
                seed.Materials["EXAMPLE_BLE_Concrete_C40"] = new MaterialOverrideRow
                {
                    Cost = 85.0,
                    CarbonKgCo2e = 410.0,
                    EpdSource = "EC3-xxxxx",
                    EpdDate = "YYYY-MM-DD",
                    Notes = "Replace EXAMPLE_* keys with the material name to override.",
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(seed, Formatting.Indented));
                StingLog.Info($"MaterialOverrideRegistry: seeded {path}");
            }
            catch (Exception ex) { StingLog.Warn($"EnsureOverrideFile: {ex.Message}"); }
        }

        public static MaterialOverrideRow ResolveOverride(Document doc, string materialName)
        {
            if (doc == null || string.IsNullOrEmpty(materialName)) return null;
            var file = GetOrLoad(doc);
            if (file?.Materials == null) return null;
            return file.Materials.TryGetValue(materialName, out var row) ? row : null;
        }

        private static MaterialOverrideFile LoadFromDisk(string path)
        {
            if (!File.Exists(path)) return MaterialOverrideFile.Empty;
            try
            {
                var raw = File.ReadAllText(path);
                var parsed = JsonConvert.DeserializeObject<MaterialOverrideFile>(raw)
                             ?? MaterialOverrideFile.Empty;
                if (parsed.Materials == null)
                    parsed.Materials = new Dictionary<string, MaterialOverrideRow>(StringComparer.OrdinalIgnoreCase);
                return parsed;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaterialOverrideRegistry parse '{path}': {ex.Message}");
                return MaterialOverrideFile.Empty;
            }
        }
    }

    public class MaterialOverrideFile
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, MaterialOverrideRow> Materials { get; set; }
            = new Dictionary<string, MaterialOverrideRow>(StringComparer.OrdinalIgnoreCase);

        public static MaterialOverrideFile Empty => new MaterialOverrideFile();
    }

    public class MaterialOverrideRow
    {
        // Identity
        [JsonProperty("class",     NullValueHandling = NullValueHandling.Ignore)] public string Class { get; set; }
        [JsonProperty("notes",     NullValueHandling = NullValueHandling.Ignore)] public string Notes { get; set; }
        // Cost
        [JsonProperty("cost",      NullValueHandling = NullValueHandling.Ignore)] public double? Cost { get; set; }
        [JsonProperty("costCurrency", NullValueHandling = NullValueHandling.Ignore)] public string CostCurrency { get; set; }
        // Sustainability
        [JsonProperty("carbonKgCo2e", NullValueHandling = NullValueHandling.Ignore)] public double? CarbonKgCo2e { get; set; }
        [JsonProperty("epdSource", NullValueHandling = NullValueHandling.Ignore)] public string EpdSource { get; set; }
        [JsonProperty("epdDate",   NullValueHandling = NullValueHandling.Ignore)] public string EpdDate { get; set; }
        // Thermal
        [JsonProperty("thermalConductivityWmK", NullValueHandling = NullValueHandling.Ignore)] public double? ThermalConductivityWmK { get; set; }
        [JsonProperty("densityKgM3", NullValueHandling = NullValueHandling.Ignore)] public double? DensityKgM3 { get; set; }
        [JsonProperty("specificHeatJkgK", NullValueHandling = NullValueHandling.Ignore)] public double? SpecificHeatJkgK { get; set; }
    }
}
