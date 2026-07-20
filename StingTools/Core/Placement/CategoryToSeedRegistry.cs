// StingTools — CategoryToSeedRegistry.
//
// The missing link that lets "tick a category, run, and place even with
// no manufacturer family loaded" work end-to-end. Maps a placement rule's
// CategoryFilter (Revit Category.Name) to the default STING seed family
// (Data/Seeds/<seedId>.json) the engine should build/load when the project
// has no family for that category. Mirrors the tag-family seed convention.
//
// Corporate baseline: Data/Placement/STING_CATEGORY_TO_SEED_MAP.json.
// Project override:    <project>/_BIM_COORD/category_to_seed_map.json
//                      (merged by category — project entries win; a seed of
//                       null/"" suppresses the corporate seed for a category).
//
// Resolve(categoryName) -> seedId | null. Cached per document like the other
// registries; Reload(doc) drops the cache so an on-disk edit is picked up.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Placement
{
    public static class CategoryToSeedRegistry
    {
        // Per-document cache keyed by the document's path (empty for unsaved).
        private static readonly Dictionary<string, Dictionary<string, string>> _cache
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        private static string DocKey(Autodesk.Revit.DB.Document doc)
        {
            try { return doc?.PathName ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// Resolve the seed id for a Revit category name. Returns null when the
        /// category is unmapped or is intentionally seedless (e.g. Conduits /
        /// Pipes / Stairs, mapped to null).
        /// </summary>
        public static string Resolve(Autodesk.Revit.DB.Document doc, string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return null;
            var map = GetMap(doc);
            if (map.TryGetValue(categoryName, out var seed))
                return string.IsNullOrWhiteSpace(seed) ? null : seed;
            return null;
        }

        /// <summary>Drop the cached map for a document so a JSON edit is re-read.</summary>
        public static void Reload(Autodesk.Revit.DB.Document doc)
        {
            lock (_lock) { _cache.Remove(DocKey(doc)); _entryCache.Remove(DocKey(doc)); }
        }

        /// <summary>The merged category→seed map for the document (corporate + project override).</summary>
        public static Dictionary<string, string> GetMap(Autodesk.Revit.DB.Document doc)
        {
            string key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var map = LoadCorporate();
                MergeProjectOverride(doc, map);
                _cache[key] = map;
                return map;
            }
        }

        private static Dictionary<string, string> LoadCorporate()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = StingToolsApp.FindDataFile("STING_CATEGORY_TO_SEED_MAP.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    ParseInto(File.ReadAllText(path), map);
                else
                    StingLog.Warn("CategoryToSeedRegistry: STING_CATEGORY_TO_SEED_MAP.json not found in data path.");
            }
            catch (Exception ex) { StingLog.Warn($"CategoryToSeedRegistry.LoadCorporate: {ex.Message}"); }
            return map;
        }

        private static void MergeProjectOverride(Autodesk.Revit.DB.Document doc, Dictionary<string, string> map)
        {
            try
            {
                string baseDir = null;
                try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = Path.GetDirectoryName(doc.PathName); }
                catch { }
                if (string.IsNullOrEmpty(baseDir)) return;
                string ovr = Path.Combine(baseDir, "_BIM_COORD", "category_to_seed_map.json");
                if (!File.Exists(ovr)) return;
                // Project entries win (overwrite). A null/empty seed value
                // suppresses the corporate seed for that category.
                ParseInto(File.ReadAllText(ovr), map);
            }
            catch (Exception ex) { StingLog.Warn($"CategoryToSeedRegistry.MergeProjectOverride: {ex.Message}"); }
        }

        // Accepts either the corporate shape { "map": [ { "category", "seed" } ] }
        // or a flat { "category": "seedId", ... } object for project overrides.
        private static void ParseInto(string json, Dictionary<string, string> map)
        {
            var entries = new Dictionary<string, PlacementCategoryEntry>(StringComparer.OrdinalIgnoreCase);
            ParseEntriesInto(json, entries);
            foreach (var kv in entries) map[kv.Key] = kv.Value.Seed;
        }

        // ── v2 placeability contract ─────────────────────────────────
        //
        // The same JSON also declares, per category, whether the placement
        // engine can point-place it (`placeable`) and — when it can't — the
        // `reason` the Centre shows on the disabled checkbox. Parsed here so
        // there is exactly one on-disk source of truth for both the seed
        // lookup and the Auto-place checklist.

        private static readonly Dictionary<string, Dictionary<string, PlacementCategoryEntry>> _entryCache
            = new Dictionary<string, Dictionary<string, PlacementCategoryEntry>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The merged category entries for the document (corporate baseline +
        /// project override), keyed by Revit category name. Carries the seed id
        /// plus the v2 placeability contract.
        /// </summary>
        public static Dictionary<string, PlacementCategoryEntry> GetEntries(Autodesk.Revit.DB.Document doc)
        {
            string key = DocKey(doc);
            lock (_lock)
            {
                if (_entryCache.TryGetValue(key, out var cached)) return cached;

                var entries = new Dictionary<string, PlacementCategoryEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_CATEGORY_TO_SEED_MAP.json");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        ParseEntriesInto(File.ReadAllText(path), entries);
                    else
                        StingLog.Warn("CategoryToSeedRegistry: STING_CATEGORY_TO_SEED_MAP.json not found in data path.");
                }
                catch (Exception ex) { StingLog.Warn($"CategoryToSeedRegistry.GetEntries corporate: {ex.Message}"); }

                try
                {
                    string baseDir = null;
                    try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = Path.GetDirectoryName(doc.PathName); }
                    catch { }
                    if (!string.IsNullOrEmpty(baseDir))
                    {
                        string ovr = Path.Combine(baseDir, "_BIM_COORD", "category_to_seed_map.json");
                        if (File.Exists(ovr)) ParseEntriesInto(File.ReadAllText(ovr), entries);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CategoryToSeedRegistry.GetEntries override: {ex.Message}"); }

                _entryCache[key] = entries;
                return entries;
            }
        }

        private static void ParseEntriesInto(string json, Dictionary<string, PlacementCategoryEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            JToken root;
            try { root = JToken.Parse(json); } catch (Exception ex) { StingLog.Warn($"CategoryToSeedRegistry parse: {ex.Message}"); return; }

            var arr = root["map"] as JArray;
            if (arr != null)
            {
                foreach (var e in arr)
                {
                    string cat = (string)e["category"];
                    if (string.IsNullOrWhiteSpace(cat)) continue;
                    string seed = e["seed"]?.Type == JTokenType.Null ? null : (string)e["seed"];
                    entries[cat] = new PlacementCategoryEntry
                    {
                        Category  = cat,
                        Seed      = seed, // null preserved → seedless
                        // Absent `placeable` defaults to true so a v1 file (or a
                        // project override that only names a seed) keeps working.
                        Placeable = e["placeable"] == null || e["placeable"].Type == JTokenType.Null
                                    || e["placeable"].Value<bool>(),
                        Reason    = (string)e["reason"] ?? "",
                        Group     = (string)e["group"]  ?? "",
                        Order     = e["order"] == null || e["order"].Type == JTokenType.Null
                                    ? int.MaxValue : e["order"].Value<int>(),
                    };
                }
                return;
            }

            if (root is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    // skip metadata keys
                    if (prop.Name.StartsWith("_") || prop.Name == "version"
                        || prop.Name == "description" || prop.Name == "notes") continue;

                    // Flat shape is seed-only; an object value may carry the v2 fields.
                    if (prop.Value is JObject vo)
                    {
                        string s = vo["seed"]?.Type == JTokenType.Null ? null : (string)vo["seed"];
                        entries[prop.Name] = new PlacementCategoryEntry
                        {
                            Category  = prop.Name,
                            Seed      = s,
                            Placeable = vo["placeable"] == null || vo["placeable"].Type == JTokenType.Null
                                        || vo["placeable"].Value<bool>(),
                            Reason    = (string)vo["reason"] ?? "",
                            Group     = (string)vo["group"]  ?? "",
                            Order     = vo["order"] == null || vo["order"].Type == JTokenType.Null
                                        ? int.MaxValue : vo["order"].Value<int>(),
                        };
                        continue;
                    }

                    string seed = prop.Value?.Type == JTokenType.Null ? null : (string)prop.Value;
                    entries[prop.Name] = new PlacementCategoryEntry
                    {
                        Category = prop.Name, Seed = seed, Placeable = true,
                        Reason = "", Group = "", Order = int.MaxValue,
                    };
                }
            }
        }
    }

    /// <summary>
    /// One row of STING_CATEGORY_TO_SEED_MAP.json: the seed family for a Revit
    /// category plus the v2 placeability contract the Placement Centre renders.
    /// </summary>
    public class PlacementCategoryEntry
    {
        /// <summary>Revit category name, as it appears in a rule's CategoryFilter.</summary>
        public string Category { get; set; } = "";

        /// <summary>Seed family id under Data/Seeds/, or null when seedless.</summary>
        public string Seed { get; set; }

        /// <summary>False when the engine cannot point-place this category.</summary>
        public bool Placeable { get; set; } = true;

        /// <summary>Why the category is not placeable. Shown as the disabled checkbox tooltip.</summary>
        public string Reason { get; set; } = "";

        /// <summary>Display grouping for the Auto-place checklist. No engine effect.</summary>
        public string Group { get; set; } = "";

        /// <summary>Display order within the checklist. No engine effect.</summary>
        public int Order { get; set; } = int.MaxValue;
    }
}
