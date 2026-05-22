using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// P-1 / P-2 — Per-document material-name index. Replaces the
    /// per-call FilteredElementCollector inside the rate provider /
    /// carbon resolver — those used to walk the entire material list
    /// for every element being costed (O(N×M) on big projects).
    ///
    /// Cache is invalidated when StingMaterialUpdater detects a new
    /// material; the cache is doc-scoped via PathName/Title key so
    /// multiple open documents don't cross-pollute.
    /// </summary>
    public static class MaterialNameCache
    {
        private static readonly ConcurrentDictionary<string, Dictionary<string, ElementId>> _byName
            = new ConcurrentDictionary<string, Dictionary<string, ElementId>>(StringComparer.OrdinalIgnoreCase);

        public static ElementId Resolve(Document doc, string materialName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(materialName)) return ElementId.InvalidElementId;
            var map = GetMap(doc);
            return map.TryGetValue(materialName, out var id) ? id : ElementId.InvalidElementId;
        }

        public static Material ResolveMaterial(Document doc, string materialName)
        {
            var id = Resolve(doc, materialName);
            if (id == null || id == ElementId.InvalidElementId) return null;
            try { return doc.GetElement(id) as Material; }
            catch (Exception ex) { StingLog.WarnRateLimited("MatCache.Get", $"ResolveMaterial: {ex.Message}"); return null; }
        }

        public static void Invalidate(Document doc)
        {
            if (doc == null) return;
            string key = DocKey(doc);
            _byName.TryRemove(key, out _);
        }

        public static void InvalidateAll() => _byName.Clear();

        private static Dictionary<string, ElementId> GetMap(Document doc)
        {
            string key = DocKey(doc);
            return _byName.GetOrAdd(key, _ => BuildMap(doc));
        }

        private static Dictionary<string, ElementId> BuildMap(Document doc)
        {
            var map = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                {
                    string n = m.Name ?? "";
                    if (string.IsNullOrEmpty(n) || map.ContainsKey(n)) continue;
                    map[n] = m.Id;
                }
                StingLog.Info($"MaterialNameCache: indexed {map.Count} materials for '{DocKey(doc)}'.");
            }
            catch (Exception ex) { StingLog.Warn($"MaterialNameCache.BuildMap: {ex.Message}"); }
            return map;
        }

        private static string DocKey(Document doc)
            => string.IsNullOrEmpty(doc.PathName) ? (doc.Title ?? "(untitled)") : doc.PathName;
    }
}
