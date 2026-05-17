using StingTools.Core;
// StingTools — Symbol Concept registry (Phase 175)
//
// Maps a stable conceptId (e.g. "MEP_FCU") to per-standard family names.
// Resolution order: viewContextOverrides → scaleVariants → standard default
// → fallback standard → IEC → null (warn).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Symbols
{
    public static class SymbolConceptRegistry
    {
        private static readonly object _lock = new object();
        private static ConceptsFile _data;
        private static bool _loaded;
        private static Dictionary<string, string> _familyToConcept;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                _data = Load();
                _familyToConcept = BuildReverseIndex(_data);
                _loaded = true;
            }
        }

        public static void Reload()
        {
            lock (_lock) { _loaded = false; _data = null; _familyToConcept = null; }
        }

        public static SymbolConcept GetConcept(string conceptId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(conceptId)) return null;
            return _data.Concepts.TryGetValue(conceptId, out var c) ? c : null;
        }

        public static IReadOnlyList<SymbolConcept> ListConcepts()
        {
            EnsureLoaded();
            return _data?.Concepts?.Values?.ToList() ?? new List<SymbolConcept>();
        }

        public static IReadOnlyList<SymbolConcept> GetConceptsForCategory(string revitCategory)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(revitCategory))
                return new List<SymbolConcept>();
            return _data.Concepts.Values
                .Where(c => string.Equals(c.RevitCategory, revitCategory, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static string GetFamilyName(
            string conceptId, string standardId,
            string viewContext = null, string scaleTier = null,
            string orientationState = null)
        {
            var concept = GetConcept(conceptId);
            if (concept == null) return null;

            // Walk fallback chain on standardId until a mapping exists.
            string std = standardId;
            for (int hop = 0; hop < 6 && !string.IsNullOrEmpty(std); hop++)
            {
                if (concept.StandardMappings.TryGetValue(std, out var map))
                {
                    string fam = ResolveFromMapping(map, viewContext, scaleTier);
                    if (!string.IsNullOrWhiteSpace(fam)) return fam;
                }
                var fb = SymbolStandardRegistry.GetFallback(std);
                if (string.Equals(fb, std, StringComparison.OrdinalIgnoreCase)) break;
                std = fb;
            }

            // Last resort: IEC default mapping.
            if (concept.StandardMappings.TryGetValue("IEC", out var iec))
            {
                string fam = ResolveFromMapping(iec, viewContext, scaleTier);
                if (!string.IsNullOrWhiteSpace(fam)) return fam;
            }

            StingTools.Core.StingLog.Warn(
                $"SymbolConceptRegistry: no family resolved for {conceptId}/{standardId}.");
            return null;
        }

        private static string ResolveFromMapping(ConceptStandardMapping map,
            string viewContext, string scaleTier)
        {
            if (map == null) return null;
            if (!string.IsNullOrEmpty(viewContext)
                && map.ViewContextOverrides != null
                && map.ViewContextOverrides.TryGetValue(viewContext, out var vc)
                && !string.IsNullOrWhiteSpace(vc))
                return vc;
            if (!string.IsNullOrEmpty(scaleTier)
                && map.ScaleVariants != null
                && map.ScaleVariants.TryGetValue(scaleTier, out var sv)
                && !string.IsNullOrWhiteSpace(sv))
                return sv;
            return map.TagFamily ?? map.GenericAnnotation;
        }

        public static string GetTagFamilyName(string conceptId, string standardId)
        {
            var concept = GetConcept(conceptId);
            if (concept == null) return null;
            if (concept.StandardMappings.TryGetValue(standardId, out var map))
                return map.TagFamily;
            return null;
        }

        public static string GetAnnotationFamilyName(string conceptId, string standardId)
        {
            var concept = GetConcept(conceptId);
            if (concept == null) return null;
            if (concept.StandardMappings.TryGetValue(standardId, out var map))
                return map.GenericAnnotation;
            return null;
        }

        public static string GetConceptForFamily(string familyName)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(familyName)) return null;
            return _familyToConcept.TryGetValue(familyName, out var cid) ? cid : null;
        }

        // ── Loader ──────────────────────────────────────────────────────

        private static ConceptsFile Load()
        {
            try
            {
                var path = StingTools.Core.StingToolsApp.FindDataFile("STING_SYMBOL_CONCEPTS.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return JsonConvert.DeserializeObject<ConceptsFile>(File.ReadAllText(path))
                        ?? new ConceptsFile();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SymbolConceptRegistry: load failed — {ex.Message}");
            }
            return new ConceptsFile();
        }

        private static Dictionary<string, string> BuildReverseIndex(ConceptsFile data)
        {
            var idx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (data?.Concepts == null) return idx;
            foreach (var kv in data.Concepts)
            {
                var concept = kv.Value;
                if (concept?.StandardMappings == null) continue;
                foreach (var map in concept.StandardMappings.Values)
                {
                    if (!string.IsNullOrEmpty(map.GenericAnnotation))
                        idx[map.GenericAnnotation] = kv.Key;
                    if (!string.IsNullOrEmpty(map.TagFamily))
                        idx[map.TagFamily] = kv.Key;
                    if (map.ViewContextOverrides != null)
                        foreach (var vc in map.ViewContextOverrides.Values)
                            if (!string.IsNullOrEmpty(vc) && !idx.ContainsKey(vc))
                                idx[vc] = kv.Key;
                    if (map.ScaleVariants != null)
                        foreach (var sv in map.ScaleVariants.Values)
                            if (!string.IsNullOrEmpty(sv) && !idx.ContainsKey(sv))
                                idx[sv] = kv.Key;
                }
            }
            return idx;
        }
    }
}
