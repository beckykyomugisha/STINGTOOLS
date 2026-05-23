using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// P-3 — Reverse index: Material ElementId → list of elements using it.
    /// Avoids the full-project walk every time an inline cost edit runs
    /// BumpRateConfidenceForMaterialUsers.
    ///
    /// Built lazily per-document; invalidated on Refresh + on material
    /// IUpdater events. Cheap to rebuild (one collector pass).
    /// </summary>
    public static class MaterialUsageIndex
    {
        private static readonly ConcurrentDictionary<string, Dictionary<long, List<ElementId>>> _byDoc
            = new ConcurrentDictionary<string, Dictionary<long, List<ElementId>>>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<ElementId> ElementsUsing(Document doc, ElementId materialId)
        {
            if (doc == null || materialId == null || materialId.Value <= 0)
                return Array.Empty<ElementId>();
            var map = _byDoc.GetOrAdd(DocKey(doc), _ => BuildMap(doc));
            return map.TryGetValue(materialId.Value, out var list) ? list : (IReadOnlyList<ElementId>)Array.Empty<ElementId>();
        }

        public static void Invalidate(Document doc)
        {
            if (doc == null) return;
            _byDoc.TryRemove(DocKey(doc), out _);
        }

        public static void InvalidateAll() => _byDoc.Clear();

        private static Dictionary<long, List<ElementId>> BuildMap(Document doc)
        {
            var map = new Dictionary<long, List<ElementId>>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    try
                    {
                        var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (p != null && p.StorageType == StorageType.ElementId)
                        {
                            var mid = p.AsElementId();
                            if (mid != null && mid.Value > 0) Add(map, mid.Value, el.Id);
                        }
                        var mats = el.GetMaterialIds(false);
                        if (mats != null)
                            foreach (var mid in mats)
                                if (mid != null && mid.Value > 0) Add(map, mid.Value, el.Id);
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("MatUsage.Walk", $"BuildMap walk: {ex.Message}"); }
                }
                StingLog.Info($"MaterialUsageIndex: indexed {map.Count} material(s) → {map.Sum(kv => kv.Value.Count)} element refs.");
            }
            catch (Exception ex) { StingLog.Warn($"MaterialUsageIndex.BuildMap: {ex.Message}"); }
            return map;
        }

        private static void Add(Dictionary<long, List<ElementId>> map, long matId, ElementId elId)
        {
            if (!map.TryGetValue(matId, out var list))
            {
                list = new List<ElementId>();
                map[matId] = list;
            }
            list.Add(elId);
        }

        private static string DocKey(Document doc)
            => string.IsNullOrEmpty(doc.PathName) ? (doc.Title ?? "(untitled)") : doc.PathName;
    }
}
