// StingTools — Matrix variant source (F2): the valid type-variant names for a category.
//
// A Matrix Place column's Variant is a dropdown, not free text. This resolves a STING
// fixture category to its seed's declared type variants: category -> seedId
// (CategoryToSeedRegistry) -> Data/Seeds/<seedId>.json -> SymbolLibrary.Symbols[].TypeVariants[].Name.
// Falls back to the loaded seed family's type names, then to an empty list (the dialog then
// allows free text). Cached per (document, category).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core.Symbols;

namespace StingTools.Core.Placement.Matrix
{
    public static class MatrixVariants
    {
        private static readonly Dictionary<string, List<string>> _cache
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        private static string Key(Document doc, string category)
        { try { return (doc?.PathName ?? "") + "" + (category ?? ""); } catch { return category ?? ""; } }

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                string prefix = (SafePath(doc)) + "";
                foreach (var k in _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                    _cache.Remove(k);
            }
        }

        private static string SafePath(Document doc) { try { return doc?.PathName ?? ""; } catch { return ""; } }

        /// <summary>Variant names for a category (seed spec variants ∪ loaded seed family type names).
        /// Empty when the category has no seed / no variants — the caller then allows free text.</summary>
        public static List<string> ForCategory(Document doc, string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return new List<string>();
            string k = Key(doc, category);
            lock (_lock) { if (_cache.TryGetValue(k, out var cached)) return cached; }

            var names = new List<string>();
            try
            {
                string seedId = CategoryToSeedRegistry.Resolve(doc, category);
                if (!string.IsNullOrWhiteSpace(seedId))
                {
                    // 1) Seed spec (authoritative, always on disk).
                    names.AddRange(FromSpec(seedId));
                    // 2) Union with any loaded seed family's type names (covers project-added types).
                    foreach (var t in FromLoadedFamily(doc, category, seedId))
                        if (!names.Contains(t, StringComparer.OrdinalIgnoreCase)) names.Add(t);
                }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixVariants.ForCategory '{category}': {ex.Message}"); }

            lock (_lock) { _cache[k] = names; }
            return names;
        }

        private static IEnumerable<string> FromSpec(string seedId)
        {
            try
            {
                string spec = StingToolsApp.FindDataFile(seedId + ".json");
                if (string.IsNullOrEmpty(spec) || !File.Exists(spec)) return Enumerable.Empty<string>();
                var lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(spec));
                var result = new List<string>();
                foreach (var sym in lib?.Symbols ?? new List<SymbolDefinition>())
                    foreach (var v in sym?.TypeVariants ?? new List<TypeVariantDefinition>())
                        if (!string.IsNullOrWhiteSpace(v?.Name) && !result.Contains(v.Name, StringComparer.OrdinalIgnoreCase))
                            result.Add(v.Name);
                return result;
            }
            catch (Exception ex) { StingLog.Warn($"MatrixVariants.FromSpec '{seedId}': {ex.Message}"); return Enumerable.Empty<string>(); }
        }

        private static IEnumerable<string> FromLoadedFamily(Document doc, string category, string seedId)
        {
            var result = new List<string>();
            try
            {
                BuiltInCategory bic = BuiltInCategory.INVALID;
                try { bic = FixturePlacementEngine.ResolveBuiltInCategoryByName(doc, category); } catch { }
                var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                if (bic != BuiltInCategory.INVALID) collector = collector.OfCategory(bic);
                foreach (FamilySymbol fs in collector.Cast<FamilySymbol>())
                {
                    bool isSeed = false;
                    try { isSeed = string.Equals(fs.LookupParameter("STING_SEED_FAMILY_TXT")?.AsString(), seedId, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(fs.Family?.Name, seedId, StringComparison.OrdinalIgnoreCase); }
                    catch { }
                    if (isSeed && !string.IsNullOrWhiteSpace(fs.Name)) result.Add(fs.Name);
                }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixVariants.FromLoadedFamily '{category}': {ex.Message}"); }
            return result;
        }
    }
}
