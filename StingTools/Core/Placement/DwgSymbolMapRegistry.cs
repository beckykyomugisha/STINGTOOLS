// StingTools — DwgSymbolMapRegistry.
//
// Maps a captured DWG MEP fixture block (blockName + layer + coarse
// InferredCategory from CADToModelEngine) to a STING placement category +
// type-variant hint + a host anchor, for the DWG->seed->swap bridge
// (DwgFixtureBridge). The resolved category feeds CategoryToSeedRegistry, the
// variant hint drives the seed type lookup, and the anchor seeds
// PlacementHostPreflight's wall/ceiling host search.
//
// Corporate baseline: Data/Placement/DWG_SYMBOL_MAP.json.
// Project override:    <project>/_BIM_COORD/dwg_symbol_map.json (same shape; its
//                      rules are PREPENDED so they win, fallbacks/skip merged).
//
// Resolve(doc, blockName, layer, inferredCategory) -> DwgSymbolMapping | null
// (null = skip: a run/structure block or an unmapped one). Cached per document
// like the other placement registries; Reload(doc) drops the cache.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Placement
{
    /// <summary>A resolved DWG-block → STING placement mapping.</summary>
    public sealed class DwgSymbolMapping
    {
        public string Category    { get; set; } = "";
        public string VariantHint { get; set; } = "";
        public string Anchor      { get; set; } = "WALL_MIDPOINT";
    }

    public static class DwgSymbolMapRegistry
    {
        private sealed class Rule
        {
            public bool   ByLayer;        // match against layer name when true, else block name
            public string Pattern = "";   // case-insensitive substring
            public string Category = "";
            public string VariantHint = "";
            public string Anchor = "WALL_MIDPOINT";
        }

        private sealed class MapData
        {
            public List<Rule> Rules = new List<Rule>();
            public Dictionary<string, DwgSymbolMapping> CategoryFallback
                = new Dictionary<string, DwgSymbolMapping>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> SkipCategories
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // D1 — the authoritative allowlist of categories this FIXTURE bridge may place.
            // A resolution to any category NOT in here returns null (not a fixture).
            public HashSet<string> FixtureCategories
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, MapData> _cache
            = new Dictionary<string, MapData>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        private static string DocKey(Autodesk.Revit.DB.Document doc)
        { try { return doc?.PathName ?? ""; } catch { return ""; } }

        public static void Reload(Autodesk.Revit.DB.Document doc)
        { lock (_lock) { _cache.Remove(DocKey(doc)); } }

        /// <summary>A user-assigned layer → STING mapping, for writing the project override
        /// from the Map-DWG-Layers UI. A blank/null Category clears (unmaps) the layer.</summary>
        public sealed class LayerRuleInput
        {
            public string Layer { get; set; } = "";
            public string Category { get; set; } = "";
            public string VariantHint { get; set; } = "";
            public string Anchor { get; set; } = "WALL_MIDPOINT";
        }

        /// <summary>Resolve a LAYER (no block name) to its STING FIXTURE mapping. D1 — this
        /// does NOT use the structural CADToModelEngine.LayerMapper (whose loose substrings
        /// map door->Doors, "light"->Electrical so "Lightning protection" matches, etc.).
        /// It matches ONLY the explicit DWG-symbol-map LAYER rules (fixture-only, precise) +
        /// the fixture allowlist gate. Unmatched layers return null and the user maps them
        /// explicitly in the Map-DWG-Layers UI. Returns null for non-fixture layers.</summary>
        public static DwgSymbolMapping ResolveLayer(Autodesk.Revit.DB.Document doc, string layerName)
            => Resolve(doc, "", layerName, inferredCategory: null);   // null => no structural fallback

        /// <summary>The project-override file path (&lt;project&gt;/_BIM_COORD/dwg_symbol_map.json),
        /// or null when the document is unsaved.</summary>
        public static string ProjectOverridePath(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                if (string.IsNullOrEmpty(doc?.PathName)) return null;
                string dir = Path.GetDirectoryName(doc.PathName);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "_BIM_COORD", "dwg_symbol_map.json");
            }
            catch { return null; }
        }

        /// <summary>Merge user layer assignments into the EXISTING project override
        /// (&lt;project&gt;/_BIM_COORD/dwg_symbol_map.json) as ByLayer rules, preserving any
        /// block rules / fallbacks already there, then Reload so the next run honours them.
        /// Idempotent — re-saving a layer replaces its rule. Returns the count written.
        /// Never touches the corporate baseline.</summary>
        public static int SaveLayerRulesToProjectOverride(Autodesk.Revit.DB.Document doc, IEnumerable<LayerRuleInput> rules)
        {
            string path = ProjectOverridePath(doc);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Save the project to disk first — the override lives under _BIM_COORD.");
            var inputs = (rules ?? Enumerable.Empty<LayerRuleInput>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Layer)).ToList();

            JObject root = null;
            try { if (File.Exists(path)) root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"DwgSymbolMapRegistry.SaveLayer read: {ex.Message}"); }
            if (root == null)
                root = new JObject
                {
                    ["version"] = "v1",
                    ["description"] = "Project DWG layer->STING map override (Map DWG Layers UI). Layered over the corporate Data/Placement/DWG_SYMBOL_MAP.json by DwgSymbolMapRegistry."
                };

            var arr = root["rules"] as JArray ?? new JArray();
            // Drop any existing layer rules for the layers being (re)assigned — idempotent.
            var touched = new HashSet<string>(inputs.Select(i => i.Layer), StringComparer.OrdinalIgnoreCase);
            var kept = new JArray();
            foreach (var r in arr.OfType<JObject>())
            {
                bool isLayer = string.Equals((string)r["match"], "layer", StringComparison.OrdinalIgnoreCase);
                string pat = (string)r["pattern"] ?? "";
                if (isLayer && touched.Contains(pat)) continue; // replaced below
                kept.Add(r);
            }
            // Append the new assignments (blank category = leave unmapped, i.e. just removed above).
            int written = 0;
            foreach (var i in inputs)
            {
                if (string.IsNullOrWhiteSpace(i.Category)) continue;
                var rule = new JObject
                {
                    ["match"] = "layer",
                    ["pattern"] = i.Layer,
                    ["category"] = i.Category
                };
                if (!string.IsNullOrWhiteSpace(i.VariantHint)) rule["variantHint"] = i.VariantHint;
                rule["anchor"] = string.IsNullOrWhiteSpace(i.Anchor) ? "WALL_MIDPOINT" : i.Anchor;
                kept.Add(rule);
                written++;
            }
            root["rules"] = kept;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Error("DwgSymbolMapRegistry.SaveLayer write", ex);
                throw;
            }
            Reload(doc);
            return written;
        }

        /// <summary>The MEP/fixture categories this bridge is allowed to place (corporate
        /// allowlist unioned with the project override). A resolution to any category NOT in
        /// here is treated as "not a fixture" and returns null. Empty ⇒ no gate (legacy).</summary>
        public static HashSet<string> GetFixtureCategories(Autodesk.Revit.DB.Document doc)
            => new HashSet<string>(GetMap(doc).FixtureCategories, StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolve a captured block/layer to its STING FIXTURE placement mapping.
        /// Returns null when the block is a run/structure category (skip), nothing maps, or
        /// the resolved category is not in the fixture allowlist (D1 — a fixture bridge must
        /// not place doors/windows/furniture/structural/annotation).</summary>
        public static DwgSymbolMapping Resolve(Autodesk.Revit.DB.Document doc,
            string blockName, string layerName, string inferredCategory)
        {
            var data = GetMap(doc);

            // Skip runs/structure outright (handled by the duct/pipe/conduit modeller).
            if (!string.IsNullOrWhiteSpace(inferredCategory) && data.SkipCategories.Contains(inferredCategory))
                return null;

            string bn = blockName ?? "";
            string ln = layerName ?? "";

            // 1) explicit rule (first substring match on block or layer wins).
            foreach (var r in data.Rules)
            {
                if (string.IsNullOrEmpty(r.Pattern)) continue;
                string hay = r.ByLayer ? ln : bn;
                if (hay.IndexOf(r.Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return GateFixture(data, new DwgSymbolMapping
                    {
                        Category    = r.Category,
                        // variant: rule's hint, else the raw block name (STING seeds name
                        // each type after its variant, so a 'SOCKET_2G' block resolves the
                        // 'SOCKET_2G' type directly); the seed default is the final fallback.
                        VariantHint = string.IsNullOrWhiteSpace(r.VariantHint) ? bn : r.VariantHint,
                        Anchor      = string.IsNullOrWhiteSpace(r.Anchor) ? "WALL_MIDPOINT" : r.Anchor
                    });
            }

            // 2) coarse-category fallback (keyed by CADToModelEngine InferredCategory).
            if (!string.IsNullOrWhiteSpace(inferredCategory)
                && data.CategoryFallback.TryGetValue(inferredCategory, out var fb))
                return GateFixture(data, new DwgSymbolMapping
                {
                    Category    = fb.Category,
                    VariantHint = string.IsNullOrWhiteSpace(fb.VariantHint) ? bn : fb.VariantHint,
                    Anchor      = string.IsNullOrWhiteSpace(fb.Anchor) ? "WALL_MIDPOINT" : fb.Anchor
                });

            return null; // unmapped — skipped (and reported by the bridge)
        }

        /// <summary>D1 gate — only a resolved category in the fixture allowlist counts as a
        /// fixture mapping. Returns null otherwise (e.g. a Doors/Furniture resolution from the
        /// structural inferer). When the allowlist is empty (legacy/no data), don't gate.</summary>
        private static DwgSymbolMapping GateFixture(MapData data, DwgSymbolMapping m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Category)) return null;
            if (data.FixtureCategories.Count == 0) return m;
            return data.FixtureCategories.Contains(m.Category) ? m : null;
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
                string path = StingToolsApp.FindDataFile("DWG_SYMBOL_MAP.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    ParseInto(File.ReadAllText(path), data, prepend: false);
                else
                    StingLog.Warn("DwgSymbolMapRegistry: DWG_SYMBOL_MAP.json not found in data path.");
            }
            catch (Exception ex) { StingLog.Warn($"DwgSymbolMapRegistry.LoadCorporate: {ex.Message}"); }
        }

        private static void MergeProjectOverride(Autodesk.Revit.DB.Document doc, MapData data)
        {
            try
            {
                string baseDir = null;
                try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = Path.GetDirectoryName(doc.PathName); }
                catch { }
                if (string.IsNullOrEmpty(baseDir)) return;
                string ovr = Path.Combine(baseDir, "_BIM_COORD", "dwg_symbol_map.json");
                if (!File.Exists(ovr)) return;
                ParseInto(File.ReadAllText(ovr), data, prepend: true); // project rules win
            }
            catch (Exception ex) { StingLog.Warn($"DwgSymbolMapRegistry.MergeProjectOverride: {ex.Message}"); }
        }

        private static void ParseInto(string json, MapData data, bool prepend)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex) { StingLog.Warn($"DwgSymbolMapRegistry parse: {ex.Message}"); return; }

            var parsed = new List<Rule>();
            if (root["rules"] is JArray rules)
                foreach (var r in rules.OfType<JObject>())
                {
                    string cat = (string)r["category"];
                    if (string.IsNullOrWhiteSpace(cat)) continue;
                    parsed.Add(new Rule
                    {
                        ByLayer     = string.Equals((string)r["match"], "layer", StringComparison.OrdinalIgnoreCase),
                        Pattern     = (string)r["pattern"] ?? "",
                        Category    = cat,
                        VariantHint = (string)r["variantHint"] ?? "",
                        Anchor      = (string)r["anchor"] ?? "WALL_MIDPOINT"
                    });
                }
            if (prepend) data.Rules.InsertRange(0, parsed);
            else data.Rules.AddRange(parsed);

            if (root["categoryFallback"] is JObject fb)
                foreach (var p in fb.Properties())
                {
                    if (!(p.Value is JObject o)) continue;
                    data.CategoryFallback[p.Name] = new DwgSymbolMapping
                    {
                        Category    = (string)o["category"] ?? "",
                        VariantHint = (string)o["variantHint"] ?? "",
                        Anchor      = (string)o["anchor"] ?? "WALL_MIDPOINT"
                    };
                }

            if (root["skipCategories"] is JArray skip)
                foreach (var s in skip)
                {
                    string v = (string)s;
                    if (!string.IsNullOrWhiteSpace(v)) data.SkipCategories.Add(v);
                }

            // D1 — fixture allowlist (union: corporate + project override can extend it).
            if (root["fixtureCategories"] is JArray fix)
                foreach (var s in fix)
                {
                    string v = (string)s;
                    if (!string.IsNullOrWhiteSpace(v)) data.FixtureCategories.Add(v);
                }
        }
    }
}
