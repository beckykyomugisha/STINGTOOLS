using StingTools.Core;
// StingTools — Drawing Template Manager · Week 2
//
// ViewStylePackRegistry — mirrors DrawingTypeRegistry for style
// packs. Loads Data/STING_VIEW_STYLE_PACKS.json, layers the project
// override at <project>/_BIM_COORD/view_style_packs.json, and
// resolves the Extends chain at read-time so consumers never have
// to walk it themselves. Caches per-document like the Drawing Type
// registry does.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public static class ViewStylePackRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, ViewStylePackLibrary> _cache
            = new Dictionary<string, ViewStylePackLibrary>(StringComparer.OrdinalIgnoreCase);

        public static ViewStylePack Get(Document doc, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var lib = GetLibrary(doc);
            var raw = lib.Packs.FirstOrDefault(
                p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            return raw == null ? null : ResolveExtends(lib, raw);
        }

        public static IReadOnlyList<ViewStylePack> ListAll(Document doc)
            => GetLibrary(doc).Packs;

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_cache.ContainsKey(key)) _cache.Remove(key);
            }
        }

        public static ViewStylePackLibrary GetLibrary(Document doc)
        {
            var key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var corporate = LoadCorporate();
                var project   = LoadProjectOverride(doc);
                var merged    = Merge(corporate, project);
                _cache[key] = merged;
                return merged;
            }
        }

        private static ViewStylePackLibrary LoadCorporate()
        {
            try
            {
                var path = StingTools.Core.StingToolsApp.FindDataFile("STING_VIEW_STYLE_PACKS.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(path));
                    if (lib?.Packs != null && lib.Packs.Count > 0)
                    {
                        foreach (var p in lib.Packs)
                            if (string.IsNullOrEmpty(p.Origin)) p.Origin = "corporate";
                        return lib;
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ViewStylePackRegistry: corporate load failed — {ex.Message}");
            }
            return BuildDefaults();
        }

        private static ViewStylePackLibrary LoadProjectOverride(Document doc)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD");
                var path = Path.Combine(dir, "view_style_packs.json");
                if (!File.Exists(path)) return null;
                var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(path));
                if (lib?.Packs != null)
                    foreach (var p in lib.Packs)
                        if (string.IsNullOrEmpty(p.Origin)) p.Origin = "project";
                return lib;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ViewStylePackRegistry: project override load failed — {ex.Message}");
                return null;
            }
        }

        private static ViewStylePackLibrary Merge(ViewStylePackLibrary baseLib, ViewStylePackLibrary over)
        {
            if (over == null) return baseLib ?? new ViewStylePackLibrary();
            var merged = new ViewStylePackLibrary
            {
                Version = Math.Max(baseLib?.Version ?? 1, over.Version),
                Packs = new List<ViewStylePack>(baseLib?.Packs ?? new List<ViewStylePack>()),
            };
            var byId = merged.Packs.ToDictionary(p => p.Id ?? "", StringComparer.OrdinalIgnoreCase);
            foreach (var p in over.Packs ?? new List<ViewStylePack>())
            {
                if (string.IsNullOrWhiteSpace(p.Id)) continue;
                byId[p.Id] = p;
            }
            merged.Packs = byId.Values.ToList();
            return merged;
        }

        /// <summary>
        /// Walk the extends chain and return a fully-merged pack —
        /// child fields override parent where set. Loop-detection via
        /// a visited set so corrupt JSON with cycles fails safely
        /// rather than stack-overflowing.
        /// </summary>
        private static ViewStylePack ResolveExtends(ViewStylePackLibrary lib, ViewStylePack leaf)
        {
            var chain = new List<ViewStylePack>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = leaf;
            while (cur != null)
            {
                if (!visited.Add(cur.Id ?? Guid.NewGuid().ToString()))
                {
                    StingTools.Core.StingLog.Warn($"ViewStylePack extends cycle at '{cur.Id}'.");
                    break;
                }
                chain.Add(cur);
                if (string.IsNullOrWhiteSpace(cur.Extends)) break;
                cur = lib.Packs.FirstOrDefault(
                    p => string.Equals(p.Id, cur.Extends, StringComparison.OrdinalIgnoreCase));
            }

            // Fold parent → child. Later entries overwrite earlier.
            chain.Reverse();
            var merged = new ViewStylePack { Id = leaf.Id, Name = leaf.Name, Origin = leaf.Origin };
            foreach (var p in chain)
            {
                if (p.LineWeightScale != 0) merged.LineWeightScale = p.LineWeightScale;
                if (!string.IsNullOrEmpty(p.TextStyle))      merged.TextStyle      = p.TextStyle;
                if (!string.IsNullOrEmpty(p.DimensionStyle)) merged.DimensionStyle = p.DimensionStyle;
                if (!string.IsNullOrEmpty(p.HatchPalette))   merged.HatchPalette   = p.HatchPalette;
                if (p.Filters != null) foreach (var f in p.Filters) merged.Filters.Add(f);
                if (p.VgOverrides != null)
                    foreach (var kv in p.VgOverrides) merged.VgOverrides[kv.Key] = kv.Value;
                if (p.TagFamilies != null)
                    foreach (var kv in p.TagFamilies) merged.TagFamilies[kv.Key] = kv.Value;
            }
            return merged;
        }

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }

        // Built-in defaults ------------------------------------------------
        private static ViewStylePackLibrary BuildDefaults()
        {
            var lib = new ViewStylePackLibrary { Version = 1 };
            lib.Packs.Add(new ViewStylePack {
                Id = "corp-standard-plan", Name = "Corporate Standard Plan",
                Origin = "corporate", LineWeightScale = 1.0,
                TextStyle = "STING - 2.5mm", DimensionStyle = "STING - Linear",
            });
            lib.Packs.Add(new ViewStylePack {
                Id = "corp-presentation", Name = "Corporate Presentation",
                Origin = "corporate", LineWeightScale = 0.8,
                TextStyle = "STING - 3.0mm Presentation", HatchPalette = "Rich",
            });
            lib.Packs.Add(new ViewStylePack {
                Id = "corp-fabrication", Name = "Corporate Fabrication Shop",
                Origin = "corporate", LineWeightScale = 1.1,
                TextStyle = "STING - 2.0mm Shop", DimensionStyle = "STING - Ordinate",
            });
            return lib;
        }
    }
}
