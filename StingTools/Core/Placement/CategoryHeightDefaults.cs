// StingTools — CategoryHeightDefaults.
//
// Resolves a STING fixture category (e.g. "Electrical Fixtures") to a default
// standards-based mounting height, for the DWG->seed->swap bridge
// (DwgFixtureBridge). When the bridge builds its synthetic PlacementRule for a
// captured fixture it calls Resolve(doc, category) and stamps HeightStandard +
// MountingHeightMm onto the rule, so a DWG-placed socket lands at 450mm, a
// switch at 1350mm, an MCP at 1400mm etc. with zero user input.
//
// A category maps to EITHER a HeightStandard key (resolved to that standard's
// PreferredMm via HeightStandardsTable) OR an explicit mountingHeightMm. The
// dialog also reads QuickHeightsMm() for its raw-height quick-list.
//
// Corporate baseline: Data/Placement/STING_CATEGORY_HEIGHT_DEFAULTS.json.
// Project override:    <project>/_BIM_COORD/category_height_defaults.json (same
//                      shape; entries layer over the baseline by category name,
//                      quickHeightsMm replaces the list when present).
//
// Cached per document like the other placement registries; Reload(doc) drops it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Placement
{
    /// <summary>A resolved category mounting-height default.</summary>
    public sealed class CategoryHeightDefault
    {
        /// <summary>HeightStandard key (a STING_HEIGHT_STANDARDS.json entry), or "" when the
        /// default is a raw height.</summary>
        public string HeightStandard { get; set; } = "";
        /// <summary>Resolved mounting height in mm above the host level / FFL. When
        /// HeightStandard is set this is the standard's PreferredMm.</summary>
        public double MountingHeightMm { get; set; }
    }

    public static class CategoryHeightDefaults
    {
        private const string FileName = "STING_CATEGORY_HEIGHT_DEFAULTS.json";

        // Hard fallback quick-list, used only when the JSON is missing entirely.
        private static readonly double[] DefaultQuickHeightsMm =
            { 0, 150, 300, 450, 900, 1200, 1350, 1400, 2200, 2500, 3150 };

        private sealed class MapData
        {
            public Dictionary<string, CategoryHeightDefault> ByCategory
                = new Dictionary<string, CategoryHeightDefault>(StringComparer.OrdinalIgnoreCase);
            public List<double> QuickHeightsMm = new List<double>();
        }

        private static readonly Dictionary<string, MapData> _cache
            = new Dictionary<string, MapData>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        private static string DocKey(Autodesk.Revit.DB.Document doc)
        { try { return doc?.PathName ?? ""; } catch { return ""; } }

        public static void Reload(Autodesk.Revit.DB.Document doc)
        { lock (_lock) { _cache.Remove(DocKey(doc)); } }

        /// <summary>Resolve a category to its default mounting height. Returns null when the
        /// category has no entry (caller keeps the rule's built-in default). A HeightStandard
        /// entry resolves its PreferredMm via HeightStandardsTable; if that lookup fails the
        /// entry is skipped (returns null) so a stale key never silently drops to 0.</summary>
        public static CategoryHeightDefault Resolve(Autodesk.Revit.DB.Document doc, string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            var data = GetMap(doc);
            if (!data.ByCategory.TryGetValue(category, out var d) || d == null) return null;

            if (!string.IsNullOrWhiteSpace(d.HeightStandard))
            {
                var entry = HeightStandardsTable.Get(d.HeightStandard);
                if (entry == null)
                {
                    StingLog.Warn($"CategoryHeightDefaults: category '{category}' references unknown " +
                                  $"HeightStandard '{d.HeightStandard}' — not in STING_HEIGHT_STANDARDS.json.");
                    return null;
                }
                return new CategoryHeightDefault
                {
                    HeightStandard = d.HeightStandard,
                    MountingHeightMm = entry.PreferredMm > 0 ? entry.PreferredMm : d.MountingHeightMm
                };
            }
            return new CategoryHeightDefault { HeightStandard = "", MountingHeightMm = d.MountingHeightMm };
        }

        /// <summary>The raw mounting-height quick-list (mm) the Map-DWG-Layers dialog offers
        /// alongside the named standards.</summary>
        public static IReadOnlyList<double> QuickHeightsMm(Autodesk.Revit.DB.Document doc)
        {
            var data = GetMap(doc);
            return (data.QuickHeightsMm != null && data.QuickHeightsMm.Count > 0)
                ? data.QuickHeightsMm
                : DefaultQuickHeightsMm.ToList();
        }

        private static MapData GetMap(Autodesk.Revit.DB.Document doc)
        {
            string key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var data = new MapData();
                LoadCorporate(data);
                MergeProjectOverride(doc, data);
                _cache[key] = data;
                return data;
            }
        }

        private static void LoadCorporate(MapData data)
        {
            try
            {
                string path = StingToolsApp.FindDataFile(FileName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    ParseInto(File.ReadAllText(path), data);
                else
                    StingLog.Warn($"CategoryHeightDefaults: {FileName} not found in data path.");
            }
            catch (Exception ex) { StingLog.Warn($"CategoryHeightDefaults.LoadCorporate: {ex.Message}"); }
        }

        private static void MergeProjectOverride(Autodesk.Revit.DB.Document doc, MapData data)
        {
            try
            {
                string baseDir = null;
                try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = Path.GetDirectoryName(doc.PathName); }
                catch { }
                if (string.IsNullOrEmpty(baseDir)) return;
                string ovr = Path.Combine(baseDir, "_BIM_COORD", "category_height_defaults.json");
                if (!File.Exists(ovr)) return;
                ParseInto(File.ReadAllText(ovr), data); // project entries replace by category name
            }
            catch (Exception ex) { StingLog.Warn($"CategoryHeightDefaults.MergeProjectOverride: {ex.Message}"); }
        }

        private static void ParseInto(string json, MapData data)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex) { StingLog.Warn($"CategoryHeightDefaults parse: {ex.Message}"); return; }

            if (root["categoryHeightDefaults"] is JObject map)
                foreach (var p in map.Properties())
                {
                    if (!(p.Value is JObject o)) continue;
                    string std = (string)o["heightStandard"] ?? "";
                    double mm = o["mountingHeightMm"] != null ? (double)o["mountingHeightMm"] : 0.0;
                    data.ByCategory[p.Name] = new CategoryHeightDefault
                    {
                        HeightStandard = std,
                        MountingHeightMm = mm
                    };
                }

            if (root["quickHeightsMm"] is JArray q)
            {
                var list = new List<double>();
                foreach (var v in q)
                {
                    try { list.Add((double)v); } catch { }
                }
                if (list.Count > 0) data.QuickHeightsMm = list; // override replaces the list
            }
        }
    }
}
