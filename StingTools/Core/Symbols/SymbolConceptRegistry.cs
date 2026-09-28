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
using Autodesk.Revit.DB;
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
            string orientationState = null, Document doc = null)
        {
            // Orientation-aware: the per-orientation variant comes first, then the base
            // family. A null orientationState gives the base family exactly as before.
            var candidates = GetFamilyNameCandidates(
                conceptId, standardId, viewContext, scaleTier, orientationState, doc);
            return candidates.Count > 0 ? candidates[0] : null;
        }

        /// <summary>
        /// Ordered candidate family names for a concept, most specific first. When
        /// <paramref name="orientationStateKey"/> is a non-plan orientation (for example
        /// <c>PIPE_VERTICAL_VIEW_PLAN</c>) and the concept declares that state in its
        /// <c>orientationStates</c> map, the per-orientation variant is offered ahead of
        /// the base family: first an explicit family in viewContextOverrides /
        /// scaleVariants keyed by the orientation token, then the naming convention
        /// <c>&lt;base&gt;_&lt;key without discipline prefix&gt;</c>. Callers try each in
        /// turn; the base family always ends the list. Variant names are not filtered by
        /// <paramref name="doc"/> (the caller checks what is loaded); the base is resolved
        /// with <paramref name="doc"/> exactly as <see cref="GetFamilyName"/> did.
        /// </summary>
        public static IReadOnlyList<string> GetFamilyNameCandidates(
            string conceptId, string standardId,
            string viewContext = null, string scaleTier = null,
            string orientationStateKey = null, Document doc = null)
        {
            var list = new List<string>();
            var concept = GetConcept(conceptId);
            if (concept == null) return list;

            string baseFam = ResolveBaseFamily(concept, standardId, viewContext, scaleTier, doc);

            if (!string.IsNullOrWhiteSpace(orientationStateKey)
                && !string.IsNullOrWhiteSpace(baseFam)
                && concept.OrientationStates != null
                && concept.OrientationStates.TryGetValue(orientationStateKey, out var token)
                && !string.IsNullOrWhiteSpace(token))
            {
                string explicitVar = ResolveOrientationOverride(concept, standardId, token);
                if (!string.IsNullOrWhiteSpace(explicitVar)) list.Add(explicitVar);

                string suffix = OrientationSuffix(orientationStateKey);
                if (!string.IsNullOrEmpty(suffix))
                {
                    string conv = baseFam + "_" + suffix;
                    if (!list.Contains(conv, StringComparer.OrdinalIgnoreCase)) list.Add(conv);
                }
            }

            if (!string.IsNullOrWhiteSpace(baseFam)
                && !list.Contains(baseFam, StringComparer.OrdinalIgnoreCase))
                list.Add(baseFam);

            if (list.Count == 0)
                StingTools.Core.StingLog.Warn(
                    $"SymbolConceptRegistry: no family resolved for {conceptId}/{standardId}.");
            return list;
        }

        private static string ResolveBaseFamily(SymbolConcept concept, string standardId,
            string viewContext, string scaleTier, Document doc)
        {
            // Walk fallback chain on standardId until a mapping exists.
            string std = standardId;
            for (int hop = 0; hop < 6 && !string.IsNullOrEmpty(std); hop++)
            {
                if (concept.StandardMappings.TryGetValue(std, out var map))
                {
                    string fam = ResolveFromMapping(map, viewContext, scaleTier, doc);
                    if (!string.IsNullOrWhiteSpace(fam)) return fam;
                }
                var fb = SymbolStandardRegistry.GetFallback(std);
                if (string.Equals(fb, std, StringComparison.OrdinalIgnoreCase)) break;
                std = fb;
            }

            // Last resort: IEC default mapping.
            if (concept.StandardMappings.TryGetValue("IEC", out var iec))
            {
                string fam = ResolveFromMapping(iec, viewContext, scaleTier, doc);
                if (!string.IsNullOrWhiteSpace(fam)) return fam;
            }
            return null;
        }

        /// <summary>An explicit per-orientation family declared in the standard mapping
        /// (viewContextOverrides / scaleVariants keyed by the orientation token, e.g.
        /// "vertical_plan"). Null when none is declared.</summary>
        private static string ResolveOrientationOverride(SymbolConcept concept,
            string standardId, string token)
        {
            if (concept?.StandardMappings == null || string.IsNullOrWhiteSpace(token)) return null;
            foreach (var key in new[] { standardId, "IEC" })
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (!concept.StandardMappings.TryGetValue(key, out var map) || map == null) continue;
                if (map.ViewContextOverrides != null
                    && map.ViewContextOverrides.TryGetValue(token, out var vc)
                    && !string.IsNullOrWhiteSpace(vc)) return vc;
                if (map.ScaleVariants != null
                    && map.ScaleVariants.TryGetValue(token, out var sv)
                    && !string.IsNullOrWhiteSpace(sv)) return sv;
            }
            return null;
        }

        /// <summary>Family-name suffix for an orientation-state key, dropping the leading
        /// discipline token: "PIPE_VERTICAL_VIEW_PLAN" gives "VERTICAL_VIEW_PLAN".</summary>
        private static string OrientationSuffix(string orientationStateKey)
        {
            if (string.IsNullOrWhiteSpace(orientationStateKey)) return null;
            string k = orientationStateKey.Trim();
            int us = k.IndexOf('_');
            string suffix = (us > 0 && us < k.Length - 1) ? k.Substring(us + 1) : k;
            return string.IsNullOrWhiteSpace(suffix) ? null : suffix;
        }

        /// <summary>
        /// Resolves the family name for a mapping. Precedence is viewContextOverrides →
        /// scaleVariants → base (TagFamily ?? GenericAnnotation). A missing override
        /// family MUST degrade to the base: when a <paramref name="doc"/> is supplied and
        /// the override/scale-variant family is not loaded in that document, the override
        /// is skipped so the caller receives a base family that actually exists instead
        /// of a dangling override name that silently fails to place. With no doc the
        /// legacy behaviour (return the override when non-empty) is preserved.
        /// </summary>
        private static string ResolveFromMapping(ConceptStandardMapping map,
            string viewContext, string scaleTier, Document doc = null)
        {
            if (map == null) return null;
            string baseName = map.TagFamily ?? map.GenericAnnotation;

            if (!string.IsNullOrEmpty(viewContext)
                && map.ViewContextOverrides != null
                && map.ViewContextOverrides.TryGetValue(viewContext, out var vc)
                && !string.IsNullOrWhiteSpace(vc)
                && (doc == null || IsFamilyLoaded(doc, vc)))
                return vc;
            if (!string.IsNullOrEmpty(scaleTier)
                && map.ScaleVariants != null
                && map.ScaleVariants.TryGetValue(scaleTier, out var sv)
                && !string.IsNullOrWhiteSpace(sv)
                && (doc == null || IsFamilyLoaded(doc, sv)))
                return sv;
            return baseName;
        }

        /// <summary>True when a family with the given name is loaded in the document.</summary>
        private static bool IsFamilyLoaded(Document doc, string familyName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(familyName)) return false;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Any(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"IsFamilyLoaded('{familyName}'): {ex.Message}");
                return false;
            }
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
