// StingTools — Matrix defaults: category -> anchor heuristic, area auto-suggest,
// and the indicative load table (M7). Single small helper so the dialog and the
// placement/load engines agree on sensible per-category starting points.
//
// Load + suggest values come from Data/Placement/STING_CATEGORY_LOAD_DEFAULTS.json
// (corporate baseline) layered with <project>/_BIM_COORD/category_load_defaults.json.
// Cached per document like the sibling placement registries.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement.Matrix
{
    public sealed class CategoryLoadDefault
    {
        public double LoadVaPerUnit { get; set; }
        public string LoadType { get; set; } = "none";     // lighting | power | mech | none
        public double SuggestPerAreaM2 { get; set; }
    }

    public static class MatrixDefaults
    {
        private const string FileName = "STING_CATEGORY_LOAD_DEFAULTS.json";

        // ── Anchor heuristic ────────────────────────────────────────────────
        // Ceiling-mounted fixture categories host to the ceiling; wall devices host to
        // a wall; equipment sits room-centre. The auto-grid anchor for ceiling categories
        // is LIGHTING_GRID (even ceiling grid); single/wall use the plain anchor.
        private static readonly HashSet<string> CeilingCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Lighting Fixtures", "Lighting Devices", "Sprinklers", "Air Terminals",
            "Duct Accessories", "Fire Protection", "Fire Alarm Devices", "Communication Devices"
        };
        private static readonly HashSet<string> EquipmentCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Electrical Equipment", "Mechanical Equipment", "Plumbing Equipment", "Specialty Equipment"
        };

        public static bool IsCeiling(string category) => category != null && CeilingCats.Contains(category);
        public static bool IsEquipment(string category) => category != null && EquipmentCats.Contains(category);

        /// <summary>Default host anchor for a category. autoGrid=true + ceiling ⇒ LIGHTING_GRID
        /// so a count &gt; 1 lands as an even ceiling grid.</summary>
        public static string DefaultAnchor(string category, bool autoGrid)
        {
            if (IsCeiling(category)) return autoGrid ? "LIGHTING_GRID" : "CEILING_CENTRE";
            if (IsEquipment(category)) return "ROOM_CENTRE";
            return "WALL_MIDPOINT";
        }

        /// <summary>All anchors offered in the column dropdown.</summary>
        public static readonly string[] Anchors =
        {
            "LIGHTING_GRID", "CEILING_CENTRE", "WALL_MIDPOINT", "WALL_FACE_OFFSET",
            "ROOM_CENTRE", "WALL_CORNER"
        };

        // ── Load / suggest table ────────────────────────────────────────────
        private static readonly Dictionary<string, Dictionary<string, CategoryLoadDefault>> _cache
            = new Dictionary<string, Dictionary<string, CategoryLoadDefault>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        private static string DocKey(Document doc) { try { return doc?.PathName ?? ""; } catch { return ""; } }

        public static void Reload(Document doc) { lock (_lock) { _cache.Remove(DocKey(doc)); } }

        public static CategoryLoadDefault Load(Document doc, string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            var map = GetMap(doc);
            return map.TryGetValue(category, out var d) ? d : null;
        }

        /// <summary>Indicative VA for one unit of a category (0 for non-electrical / sources).</summary>
        public static double LoadVa(Document doc, string category)
            => Load(doc, category)?.LoadVaPerUnit ?? 0.0;

        public static string LoadType(Document doc, string category)
            => Load(doc, category)?.LoadType ?? "none";

        /// <summary>Auto-suggest a starting cell count for a room of areaM2, from the density hint
        /// in the load-defaults file, else from any loaded PlacementRule PerAreaM2 for the category.
        /// Returns 0 when no density is known (the user types a count from scratch is avoided by
        /// the caller only when this returns &gt; 0).</summary>
        public static int SuggestCount(Document doc, string category, double areaM2)
        {
            if (string.IsNullOrWhiteSpace(category) || areaM2 <= 0) return 0;
            double perArea = Load(doc, category)?.SuggestPerAreaM2 ?? 0.0;
            if (perArea <= 0) perArea = RulePerAreaM2(doc, category);
            if (perArea <= 0) return 0;
            return Math.Max(1, (int)Math.Ceiling(areaM2 / perArea));
        }

        // Secondary suggest source: the smallest PerAreaM2 across loaded placement rules for
        // the category (densest wins so we don't under-suggest). Best-effort; never throws.
        private static double RulePerAreaM2(Document doc, string category)
        {
            try
            {
                var rules = PlacementRuleLoader.Load(doc?.PathName);
                var vals = rules?
                    .Where(r => string.Equals(r.CategoryFilter, category, StringComparison.OrdinalIgnoreCase)
                                && r.PerAreaM2 > 0)
                    .Select(r => r.PerAreaM2).ToList();
                return (vals != null && vals.Count > 0) ? vals.Min() : 0.0;
            }
            catch { return 0.0; }
        }

        private static Dictionary<string, CategoryLoadDefault> GetMap(Document doc)
        {
            string key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var data = new Dictionary<string, CategoryLoadDefault>(StringComparer.OrdinalIgnoreCase);
                LoadCorporate(data);
                MergeOverride(doc, data);
                _cache[key] = data;
                return data;
            }
        }

        private static void LoadCorporate(Dictionary<string, CategoryLoadDefault> data)
        {
            try
            {
                string path = StingToolsApp.FindDataFile(FileName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    ParseInto(File.ReadAllText(path), data);
                else StingLog.Warn($"MatrixDefaults: {FileName} not found in data path.");
            }
            catch (Exception ex) { StingLog.Warn($"MatrixDefaults.LoadCorporate: {ex.Message}"); }
        }

        private static void MergeOverride(Document doc, Dictionary<string, CategoryLoadDefault> data)
        {
            try
            {
                string baseDir = null;
                try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = Path.GetDirectoryName(doc.PathName); }
                catch { }
                if (string.IsNullOrEmpty(baseDir)) return;
                string ovr = Path.Combine(baseDir, "_BIM_COORD", "category_load_defaults.json");
                if (File.Exists(ovr)) ParseInto(File.ReadAllText(ovr), data);
            }
            catch (Exception ex) { StingLog.Warn($"MatrixDefaults.MergeOverride: {ex.Message}"); }
        }

        private static void ParseInto(string json, Dictionary<string, CategoryLoadDefault> data)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var root = JToken.Parse(json);
                var obj = root["categoryLoadDefaults"] as JObject ?? root as JObject;
                if (obj == null) return;
                foreach (var p in obj.Properties())
                {
                    if (!(p.Value is JObject v)) continue;
                    data[p.Name] = new CategoryLoadDefault
                    {
                        LoadVaPerUnit = v.Value<double?>("loadVaPerUnit") ?? 0.0,
                        LoadType = v.Value<string>("loadType") ?? "none",
                        SuggestPerAreaM2 = v.Value<double?>("suggestPerAreaM2") ?? 0.0
                    };
                }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixDefaults.ParseInto: {ex.Message}"); }
        }
    }
}
