using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Cluster detection across project materials.
    ///
    /// Modes mirror the user-facing combo in the Duplicates sub-tab:
    ///   • SameName       — case-insensitive exact match on Material.Name
    ///   • FuzzyName      — Levenshtein ≤ 2 (and length ≥ 4 to suppress
    ///                      false positives between short tokens)
    ///   • SameRgb        — same colour within ±5 per channel
    ///   • SameAppearance — same AppearanceAssetId — the most reliable
    ///                      signal that two materials would render
    ///                      identically.
    /// </summary>
    public enum DuplicateMode { SameName, FuzzyName, SameRgb, SameAppearance }

    /// <summary>One row in the Duplicates DataGrid.</summary>
    public class DuplicateRow
    {
        public string ClusterKey { get; set; }  // displayed identifier of the cluster
        public bool IsKeeper { get; set; }      // checkbox column; one true per cluster
        public string Name { get; set; }
        public int UsageCount { get; set; }
        public ElementId Id { get; set; }
    }

    public static class MaterialDuplicateFinder
    {
        public static List<DuplicateRow> Find(Document doc, DuplicateMode mode)
        {
            var rows = new List<DuplicateRow>();
            if (doc == null) return rows;
            try
            {
                var materials = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>().ToList();
                var usage = MaterialRowBuilder.ComputeUsageCounts(doc);

                var clusters = BuildClusters(materials, mode);
                int clusterIdx = 0;
                foreach (var cluster in clusters.Where(c => c.Count > 1).OrderByDescending(c => c.Count))
                {
                    clusterIdx++;
                    string clusterKey = $"#{clusterIdx}";
                    // The most-used material is the default keeper.
                    var byUse = cluster.OrderByDescending(m =>
                        usage.TryGetValue(m.Id.Value, out int u) ? u : 0).ToList();
                    for (int i = 0; i < byUse.Count; i++)
                    {
                        var m = byUse[i];
                        usage.TryGetValue(m.Id.Value, out int use);
                        rows.Add(new DuplicateRow
                        {
                            ClusterKey = clusterKey,
                            IsKeeper = (i == 0),
                            Name = m.Name ?? "",
                            UsageCount = use,
                            Id = m.Id,
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Error("MaterialDuplicateFinder.Find", ex); }
            return rows;
        }

        private static List<List<Material>> BuildClusters(List<Material> materials, DuplicateMode mode)
        {
            var clusters = new List<List<Material>>();
            switch (mode)
            {
                case DuplicateMode.SameName:
                {
                    clusters.AddRange(materials
                        .GroupBy(m => m.Name ?? "", StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.ToList()));
                    break;
                }
                case DuplicateMode.SameRgb:
                {
                    var byColor = new Dictionary<string, List<Material>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in materials)
                    {
                        try
                        {
                            var c = m.Color; if (c == null || !c.IsValid) continue;
                            // Bucket each channel to nearest 5 → equivalence class.
                            int r = (c.Red   / 5) * 5;
                            int g = (c.Green / 5) * 5;
                            int b = (c.Blue  / 5) * 5;
                            string key = $"{r}-{g}-{b}";
                            if (!byColor.TryGetValue(key, out var list))
                                byColor[key] = list = new List<Material>();
                            list.Add(m);
                        }
                        catch (Exception ex) { StingLog.Warn($"DupRgb '{m?.Name}': {ex.Message}"); }
                    }
                    clusters.AddRange(byColor.Values.Where(l => l.Count > 1));
                    break;
                }
                case DuplicateMode.SameAppearance:
                {
                    var byApp = new Dictionary<long, List<Material>>();
                    foreach (var m in materials)
                    {
                        try
                        {
                            long aid = m.AppearanceAssetId?.Value ?? 0;
                            if (aid <= 0) continue;
                            if (!byApp.TryGetValue(aid, out var list))
                                byApp[aid] = list = new List<Material>();
                            list.Add(m);
                        }
                        catch (Exception ex) { StingLog.Warn($"DupApp '{m?.Name}': {ex.Message}"); }
                    }
                    clusters.AddRange(byApp.Values.Where(l => l.Count > 1));
                    break;
                }
                case DuplicateMode.FuzzyName:
                {
                    var visited = new HashSet<long>();
                    foreach (var seed in materials)
                    {
                        if (visited.Contains(seed.Id.Value)) continue;
                        var cluster = new List<Material> { seed };
                        visited.Add(seed.Id.Value);
                        foreach (var other in materials)
                        {
                            if (visited.Contains(other.Id.Value)) continue;
                            if ((seed.Name?.Length ?? 0) < 4 || (other.Name?.Length ?? 0) < 4) continue;
                            if (Levenshtein(seed.Name, other.Name) <= 2)
                            {
                                cluster.Add(other);
                                visited.Add(other.Id.Value);
                            }
                        }
                        if (cluster.Count > 1) clusters.Add(cluster);
                    }
                    break;
                }
            }
            return clusters;
        }

        /// <summary>
        /// Merge a chosen set of duplicate rows. Per cluster the row with
        /// IsKeeper=true wins; every other material is repointed to the
        /// keeper everywhere it's used, then deleted.
        /// </summary>
        public static int Merge(Document doc, IReadOnlyList<DuplicateRow> rows)
        {
            if (doc == null || rows == null || rows.Count == 0) return 0;
            int merged = 0;
            var clusters = rows.GroupBy(r => r.ClusterKey).ToList();
            using (var t = new Transaction(doc, "STING Material Merge"))
            {
                t.Start();
                foreach (var cluster in clusters)
                {
                    var keeper = cluster.FirstOrDefault(r => r.IsKeeper);
                    if (keeper == null) keeper = cluster.OrderByDescending(r => r.UsageCount).First();
                    var keeperId = keeper.Id;
                    if (keeperId == null || keeperId.Value <= 0) continue;

                    foreach (var loser in cluster.Where(r => r.Id != null && r.Id.Value != keeperId.Value))
                    {
                        try
                        {
                            RepointUsages(doc, loser.Id, keeperId);
                            doc.Delete(loser.Id);
                            MaterialAuditLogger.Log(doc, "MAT_Merge", loser.Name,
                                new Dictionary<string, object>
                                {
                                    ["mergedInto"] = keeper.Name,
                                    ["cluster"] = cluster.Key,
                                });
                            merged++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Merge '{loser.Name}' → '{keeper.Name}': {ex.Message}"); }
                    }
                }
                t.Commit();
            }
            return merged;
        }

        private static void RepointUsages(Document doc, ElementId fromId, ElementId toId)
        {
            if (fromId == null || toId == null) return;
            try
            {
                var els = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
                foreach (var el in els)
                {
                    try
                    {
                        var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId &&
                            p.AsElementId() == fromId)
                            p.Set(toId);
                    }
                    catch (Exception ex) { StingLog.Warn($"Repoint {el?.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"RepointUsages: {ex.Message}"); }
        }

        private static int Levenshtein(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;
            var m = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) m[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) m[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                m[i, j] = Math.Min(Math.Min(m[i - 1, j] + 1, m[i, j - 1] + 1), m[i - 1, j - 1] + cost);
            }
            return m[a.Length, b.Length];
        }
    }
}
