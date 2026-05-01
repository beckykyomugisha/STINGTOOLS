// StingTools — AEC/FM Filter Registry
//
// Loads STING_AEC_FILTERS.json once per document, layers an optional
// project override at <project>/_BIM_COORD/aec_filters.json, and
// caches the merged result. Mirrors ViewStylePackRegistry / DrawingTypeRegistry
// in shape so consumers learn one pattern.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public static class AecFilterRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, AecFilterLibrary> _cache
            = new Dictionary<string, AecFilterLibrary>(StringComparer.OrdinalIgnoreCase);

        public static AecFilterDefinition Get(Document doc, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return GetLibrary(doc).Filters
                .FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Find a filter by Revit filter name. Used by ViewStylePackApplier
        /// when a pack references a filter that doesn't exist in the doc yet
        /// — the registry can match it back to a definition by name and
        /// create it lazily.
        /// </summary>
        public static AecFilterDefinition GetByName(Document doc, string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName)) return null;
            return GetLibrary(doc).Filters
                .FirstOrDefault(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<AecFilterDefinition> ListAll(Document doc)
            => GetLibrary(doc).Filters;

        public static IReadOnlyList<AecFilterDefinition> ListByTag(Document doc, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Array.Empty<AecFilterDefinition>();
            return GetLibrary(doc).Filters
                .Where(f => f.Tags != null && f.Tags.Any(t =>
                    string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_cache.ContainsKey(key)) _cache.Remove(key);
            }
        }

        public static AecFilterLibrary GetLibrary(Document doc)
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

        private static AecFilterLibrary LoadCorporate()
        {
            try
            {
                var path = StingTools.Core.StingToolsApp.FindDataFile("STING_AEC_FILTERS.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    // S3.6.2 — version gate before deserialise.
                    StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                        path, "planscape.aec-filters",
                        StingTools.Core.PluginSchemaVersion.CurrentAecFilters);
                    var lib = JsonConvert.DeserializeObject<AecFilterLibrary>(File.ReadAllText(path));
                    if (lib?.Filters != null && lib.Filters.Count > 0)
                    {
                        foreach (var f in lib.Filters)
                            if (string.IsNullOrEmpty(f.Origin)) f.Origin = "corporate";
                        return lib;
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"AecFilterRegistry: corporate load failed — {ex.Message}");
            }
            return new AecFilterLibrary();
        }

        private static AecFilterLibrary LoadProjectOverride(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                var dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) return null;
                var path = Path.Combine(dir, "_BIM_COORD", "aec_filters.json");
                if (!File.Exists(path)) return null;

                var lib = JsonConvert.DeserializeObject<AecFilterLibrary>(File.ReadAllText(path));
                if (lib?.Filters != null)
                    foreach (var f in lib.Filters)
                        if (string.IsNullOrEmpty(f.Origin)) f.Origin = "project";
                return lib;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"AecFilterRegistry: project override load failed — {ex.Message}");
                return null;
            }
        }

        private static AecFilterLibrary Merge(AecFilterLibrary corporate, AecFilterLibrary project)
        {
            var merged = corporate ?? new AecFilterLibrary();
            if (project?.Filters == null) return merged;

            // Project entries win by Id (replace) and any new ids append.
            var byId = merged.Filters.ToDictionary(
                f => f.Id ?? Guid.NewGuid().ToString(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var pf in project.Filters)
            {
                if (string.IsNullOrEmpty(pf.Id)) continue;
                byId[pf.Id] = pf;
            }
            merged.Filters = byId.Values.ToList();
            return merged;
        }

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }
    }
}
