// StingTools — project-specific concept alias map (Phase 175)
//
// Augments the heuristic concept resolver in
// SymbolOverlayManager.ResolveConceptForElement with an explicit
// project-side override. When a project loads families with
// non-descriptive names ("Type 1", "Standard", "Default") the keyword
// scoring falls back to first-concept-wins; this map lets the user
// pin specific family/type names to specific concept ids.
//
// Two layers, mirroring AecFilterRegistry / DrawingTypeRegistry:
//   * Corporate baseline: data/Symbols/STING_SYMBOL_ALIASES.json
//                         (resolved via StingToolsApp.FindDataFile).
//                         Ships with the plugin; covers naming
//                         conventions shared across an organisation.
//   * Project override:   <project>/_BIM_COORD/symbol_aliases.json.
//                         Project entries win by key; project-only
//                         keys are appended.
//
// Schema (both files):
//   {
//     "version": "1.0",
//     "aliases": {
//       "<key>": "<conceptId>",
//       ...
//     }
//   }
//
// Match key forms (checked in this order, first hit wins):
//   1. Exact "<FamilyName>::<TypeName>"
//   2. Exact "<FamilyName>"
//   3. Exact "<TypeName>"
//   4. Glob "<FamilyName-prefix>*"  (case-insensitive)
//   5. Glob "*<substring>*"          (case-insensitive)
//
// Edits to either JSON require a Reload via SymbolAliasRegistry.Reload
// (or close/reopen the document — registry is per-document).

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Symbols
{
    public sealed class SymbolAliasFile
    {
        [JsonProperty("version")] public string Version { get; set; } = "1.0";
        [JsonProperty("aliases")] public Dictionary<string, string> Aliases { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class SymbolAliasRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, SymbolAliasFile> _cache
            = new Dictionary<string, SymbolAliasFile>(StringComparer.OrdinalIgnoreCase);

        public static void Reload(Autodesk.Revit.DB.Document doc)
        {
            string key = doc?.PathName ?? "";
            lock (_lock) { _cache.Remove(key); }
        }

        public static SymbolAliasFile GetForDocument(Autodesk.Revit.DB.Document doc)
        {
            string key = doc?.PathName ?? "";
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
            }
            var corporate = LoadCorporate();
            var project   = LoadProject(doc);
            var merged    = Merge(corporate, project);
            lock (_lock) { _cache[key] = merged; }
            return merged;
        }

        /// <summary>
        /// Project entries win by key; project-only keys are appended.
        /// Mirrors the AecFilterRegistry / DrawingTypeRegistry merge
        /// pattern.
        /// </summary>
        private static SymbolAliasFile Merge(SymbolAliasFile corporate, SymbolAliasFile project)
        {
            var merged = new SymbolAliasFile { Version = corporate?.Version ?? project?.Version ?? "1.0" };
            if (corporate?.Aliases != null)
                foreach (var kv in corporate.Aliases) merged.Aliases[kv.Key] = kv.Value;
            if (project?.Aliases != null)
                foreach (var kv in project.Aliases) merged.Aliases[kv.Key] = kv.Value;
            return merged;
        }

        public static string ResolveAlias(Autodesk.Revit.DB.Document doc,
            string familyName, string typeName, string instanceName)
        {
            var file = GetForDocument(doc);
            if (file?.Aliases == null || file.Aliases.Count == 0) return null;

            // 1. Exact "<family>::<type>".
            if (!string.IsNullOrEmpty(familyName) && !string.IsNullOrEmpty(typeName))
            {
                if (file.Aliases.TryGetValue($"{familyName}::{typeName}", out var v1))
                    return v1;
            }
            // 2. Exact family.
            if (!string.IsNullOrEmpty(familyName)
                && file.Aliases.TryGetValue(familyName, out var v2))
                return v2;
            // 3. Exact type.
            if (!string.IsNullOrEmpty(typeName)
                && file.Aliases.TryGetValue(typeName, out var v3))
                return v3;

            // 4 + 5. Glob matches. Multi-segment globs like
            // "*recessed*downlight*" match when each segment between the
            // asterisks appears in order somewhere in the haystack.
            string composite = $"{familyName} {typeName} {instanceName}".ToLowerInvariant();
            foreach (var kv in file.Aliases)
            {
                if (string.IsNullOrEmpty(kv.Key) || !kv.Key.Contains("*")) continue;
                if (MatchesGlob(kv.Key, composite, familyName))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// Match a glob pattern (containing one or more <c>*</c> wildcards)
        /// against the composite haystack.
        /// <list type="bullet">
        /// <item><c>prefix*</c> matches when the haystack (or family
        /// name) starts with the prefix.</item>
        /// <item><c>*suffix</c> matches when the haystack ends with the
        /// suffix.</item>
        /// <item><c>*foo*</c> matches when the haystack contains foo.</item>
        /// <item><c>*foo*bar*</c> and longer multi-segment forms match
        /// when every literal segment between asterisks appears in order
        /// in the haystack.</item>
        /// </list>
        /// All comparisons are case-insensitive.
        /// </summary>
        private static bool MatchesGlob(string pattern, string haystack, string familyName)
        {
            string p = pattern.ToLowerInvariant();
            // Whole-string anchored prefix glob: "AcmeCorp*"
            if (!p.StartsWith("*") && p.EndsWith("*"))
            {
                string prefix = p.Substring(0, p.Length - 1);
                if (haystack.StartsWith(prefix)) return true;
                if (!string.IsNullOrEmpty(familyName)
                    && familyName.ToLowerInvariant().StartsWith(prefix))
                    return true;
                return false;
            }
            // Suffix-anchored "*foo": tail-match.
            if (p.StartsWith("*") && !p.EndsWith("*"))
            {
                string suffix = p.Substring(1);
                return haystack.EndsWith(suffix);
            }
            // Multi-segment "*a*b*c*" — split on '*' and require each
            // non-empty segment to appear in order.
            var segments = p.Split('*');
            int cursor = 0;
            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg)) continue;
                int idx = haystack.IndexOf(seg, cursor, StringComparison.Ordinal);
                if (idx < 0) return false;
                cursor = idx + seg.Length;
            }
            return true;
        }

        private static SymbolAliasFile LoadCorporate()
        {
            try
            {
                string path = StingTools.Core.StingToolsApp.FindDataFile("STING_SYMBOL_ALIASES.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new SymbolAliasFile();
                return JsonConvert.DeserializeObject<SymbolAliasFile>(File.ReadAllText(path))
                    ?? new SymbolAliasFile();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"SymbolAliasRegistry: corporate load failed — {ex.Message}");
                return new SymbolAliasFile();
            }
        }

        private static SymbolAliasFile LoadProject(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                if (string.IsNullOrEmpty(doc?.PathName)) return new SymbolAliasFile();
                string path = Path.Combine(
                    Path.GetDirectoryName(doc.PathName),
                    "_BIM_COORD", "symbol_aliases.json");
                if (!File.Exists(path)) return new SymbolAliasFile();
                return JsonConvert.DeserializeObject<SymbolAliasFile>(File.ReadAllText(path))
                    ?? new SymbolAliasFile();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"SymbolAliasRegistry: project load failed — {ex.Message}");
                return new SymbolAliasFile();
            }
        }
    }
}
