// StingTools — project-specific concept alias map (Phase 175)
//
// Augments the heuristic concept resolver in
// SymbolOverlayManager.ResolveConceptForElement with an explicit
// project-side override. When a project loads families with
// non-descriptive names ("Type 1", "Standard", "Default") the keyword
// scoring falls back to first-concept-wins; this map lets the user
// pin specific family/type names to specific concept ids.
//
// File location: <project>/_BIM_COORD/symbol_aliases.json
// Schema:
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
// Edits to the JSON require a Reload via SymbolAliasRegistry.Reload
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
            var loaded = LoadFromDisk(doc);
            lock (_lock) { _cache[key] = loaded; }
            return loaded;
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

            // 4 + 5. Glob matches.
            string composite = $"{familyName} {typeName} {instanceName}".ToLowerInvariant();
            foreach (var kv in file.Aliases)
            {
                string pattern = kv.Key;
                if (pattern.StartsWith("*") && pattern.EndsWith("*") && pattern.Length > 2)
                {
                    string needle = pattern.Substring(1, pattern.Length - 2).ToLowerInvariant();
                    if (composite.Contains(needle)) return kv.Value;
                }
                else if (pattern.EndsWith("*") && pattern.Length > 1)
                {
                    string prefix = pattern.Substring(0, pattern.Length - 1).ToLowerInvariant();
                    if (composite.StartsWith(prefix)) return kv.Value;
                    if (!string.IsNullOrEmpty(familyName)
                        && familyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }
            }
            return null;
        }

        private static SymbolAliasFile LoadFromDisk(Autodesk.Revit.DB.Document doc)
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
                    $"SymbolAliasRegistry: load failed — {ex.Message}");
                return new SymbolAliasFile();
            }
        }
    }
}
