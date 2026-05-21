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
    /// A6 — Material packs (named sets of materials bound to a
    /// DrawingType). When the Drawing Type engine stamps a sheet with
    /// a profile that declares <c>materialPack: "arch-baseline"</c>,
    /// the MAT tab offers to mint that pack's materials into the
    /// project so every arch plan ships with the same concrete /
    /// steel / glass spec.
    ///
    /// JSON shape (corporate baseline at
    /// <c>Data/STING_MATERIAL_PACKS.json</c>; project override at
    /// <c>&lt;project&gt;/_BIM_COORD/material_packs.json</c>):
    /// <code>
    /// {
    ///   "packs": {
    ///     "arch-baseline": {
    ///       "name": "Architectural baseline",
    ///       "description": "Concrete + Steel + Glass + Plaster",
    ///       "materials": [
    ///         "BLE_Concrete_C40",
    ///         "BLE_Steel_S355",
    ///         "BLE_Glass_Glazing",
    ///         "BLE_Plasterboard_Standard"
    ///       ]
    ///     }
    ///   }
    /// }
    /// </code>
    /// </summary>
    public class MaterialPackFile
    {
        [JsonProperty("packs")]
        public Dictionary<string, MaterialPack> Packs { get; set; }
            = new Dictionary<string, MaterialPack>(StringComparer.OrdinalIgnoreCase);

        public static MaterialPackFile Empty => new MaterialPackFile();
    }

    public class MaterialPack
    {
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("materials")]   public List<string> Materials { get; set; } = new List<string>();
    }

    public static class MaterialPackRegistry
    {
        private static readonly Dictionary<string, MaterialPackFile> _cache =
            new Dictionary<string, MaterialPackFile>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        public const string FileName = "material_packs.json";

        public static MaterialPackFile GetOrLoad(Document doc)
        {
            if (doc == null) return MaterialPackFile.Empty;
            string key = doc.PathName ?? doc.Title ?? "(untitled)";
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var hit)) return hit;
                var merged = MergeCorporateAndProject(doc);
                _cache[key] = merged;
                return merged;
            }
        }

        public static void Reload(Document doc)
        {
            if (doc == null) return;
            string key = doc.PathName ?? doc.Title ?? "(untitled)";
            lock (_lock) { _cache.Remove(key); }
        }

        public static MaterialPack Get(Document doc, string packId)
        {
            if (string.IsNullOrEmpty(packId)) return null;
            var file = GetOrLoad(doc);
            return file?.Packs != null && file.Packs.TryGetValue(packId, out var p) ? p : null;
        }

        private static MaterialPackFile MergeCorporateAndProject(Document doc)
        {
            var merged = new MaterialPackFile();
            // Corporate baseline first.
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_MATERIAL_PACKS.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                {
                    var f = JsonConvert.DeserializeObject<MaterialPackFile>(File.ReadAllText(corp));
                    if (f?.Packs != null) foreach (var kv in f.Packs) merged.Packs[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialPackRegistry corporate: {ex.Message}"); }
            // Project override second — wins on key clash.
            try
            {
                string proj = Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "", FileName);
                if (File.Exists(proj))
                {
                    var f = JsonConvert.DeserializeObject<MaterialPackFile>(File.ReadAllText(proj));
                    if (f?.Packs != null) foreach (var kv in f.Packs) merged.Packs[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialPackRegistry project: {ex.Message}"); }
            return merged;
        }

        /// <summary>
        /// Mint every material in the pack that's missing from the
        /// project. Existing materials are left alone so the user
        /// doesn't lose project-specific tuning. Returns the count of
        /// freshly-minted materials.
        /// </summary>
        public static int LoadPack(Document doc, MaterialPack pack)
        {
            if (doc == null || pack == null || pack.Materials == null || pack.Materials.Count == 0) return 0;
            int created = 0;
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>().Select(m => m.Name ?? ""),
                StringComparer.OrdinalIgnoreCase);
            using (var t = new Transaction(doc, "STING Load Material Pack"))
            {
                t.Start();
                foreach (var name in pack.Materials)
                {
                    if (string.IsNullOrWhiteSpace(name) || existing.Contains(name)) continue;
                    try
                    {
                        ElementId newId = Material.Create(doc, name);
                        var m = doc.GetElement(newId) as Material;
                        if (m != null)
                        {
                            // Auto-fill the new material via the lookup CSV +
                            // any project override so the freshly-minted row
                            // doesn't show up missing cost / carbon.
                            ApplyDefaults(doc, m, name);
                            created++;
                            MaterialAuditLogger.Log(doc, "MAT_PackLoad", name,
                                new Dictionary<string, object> { ["pack"] = pack.Name });
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"LoadPack create '{name}': {ex.Message}"); }
                }
                t.Commit();
            }
            return created;
        }

        private static void ApplyDefaults(Document doc, Material m, string name)
        {
            var ov = MaterialOverrideRegistry.ResolveOverride(doc, name);
            try
            {
                var cp = m.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                if (cp != null && !cp.IsReadOnly && cp.StorageType == StorageType.Double && cp.AsDouble() == 0)
                {
                    double cost = ov?.Cost ?? MaterialLookupCsv.GetCost(name);
                    if (cost > 0) cp.Set(cost);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ApplyDefaults cost '{name}': {ex.Message}"); }
            try
            {
                var lp = m.LookupParameter("STING_EMB_CARBON_NR");
                if (lp != null && !lp.IsReadOnly && lp.StorageType == StorageType.Double && lp.AsDouble() == 0)
                {
                    double c = ov?.CarbonKgCo2e ?? MaterialLookupCsv.GetCarbon(name);
                    if (c > 0) lp.Set(c);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ApplyDefaults carbon '{name}': {ex.Message}"); }
            try
            {
                if (!string.IsNullOrEmpty(ov?.Class)) m.MaterialClass = ov.Class;
            }
            catch (Exception ex) { StingLog.Warn($"ApplyDefaults class '{name}': {ex.Message}"); }
        }
    }
}
