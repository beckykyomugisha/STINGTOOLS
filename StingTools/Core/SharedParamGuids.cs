using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Parameter GUID map and category binding definitions for shared parameter loading.
    /// Two binding passes:
    ///   Pass 1 (UniversalParams) — 17 ASS_MNG parameters → all 53 categories.
    ///   Pass 2 (DisciplineParams) — discipline-specific tag containers → correct category subsets.
    ///
    /// ALL DATA NOW LOADED FROM ParamRegistry (PARAMETER_REGISTRY.json).
    /// This class provides backwards-compatible accessors. To add/rename parameters,
    /// edit PARAMETER_REGISTRY.json and run "Sync Parameter Schema".
    /// </summary>
    public static class SharedParamGuids
    {
        /// <summary>Sequence number zero-padding width. Delegated to ParamRegistry.</summary>
        public static int NumPad => ParamRegistry.NumPad;

        /// <summary>Tag segment separator. Delegated to ParamRegistry.</summary>
        public static string Separator => ParamRegistry.Separator;

        /// <summary>Parameter name → GUID map. Delegated to ParamRegistry.</summary>
        public static Dictionary<string, Guid> ParamGuids => ParamRegistry.AllParamGuids;

        /// <summary>The 17 universal parameters bound to all 53 categories (Pass 1).</summary>
        public static string[] UniversalParams => ParamRegistry.UniversalParams;

        /// <summary>
        /// All 53 built-in categories targeted by Pass 1. Resolved from
        /// ParamRegistry.UniversalCategories via the category_enum_map.
        /// Cached after first access.
        /// </summary>
        public static BuiltInCategory[] AllCategoryEnums
        {
            get
            {
                if (_allCategoryEnums == null)
                {
                    try
                    {
                        StingLog.Info("SharedParamGuids.AllCategoryEnums: resolving (first access)");
                        _allCategoryEnums = ParamRegistry.ResolveUniversalCategoryEnums();
                        if (_allCategoryEnums == null)
                        {
                            StingLog.Warn("SharedParamGuids.AllCategoryEnums: resolved to null, using empty array");
                            _allCategoryEnums = Array.Empty<BuiltInCategory>();
                        }
                        StingLog.Info($"SharedParamGuids.AllCategoryEnums: {_allCategoryEnums.Length} categories resolved");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("SharedParamGuids.AllCategoryEnums: resolution failed, using empty array", ex);
                        _allCategoryEnums = Array.Empty<BuiltInCategory>();
                    }
                }
                return _allCategoryEnums;
            }
        }
        private static BuiltInCategory[] _allCategoryEnums;

        /// <summary>
        /// Discipline-specific parameter → category mappings for Pass 2.
        /// Built from ParamRegistry container group definitions.
        /// Cached after first access.
        /// </summary>
        public static Dictionary<string, BuiltInCategory[]> DisciplineBindings
        {
            get
            {
                if (_disciplineBindings == null)
                {
                    try
                    {
                        StingLog.Info("SharedParamGuids.DisciplineBindings: building (first access)");
                        _disciplineBindings = ParamRegistry.BuildDisciplineBindings();
                        if (_disciplineBindings == null)
                        {
                            StingLog.Warn("SharedParamGuids.DisciplineBindings: resolved to null, using empty dict");
                            _disciplineBindings = new Dictionary<string, BuiltInCategory[]>();
                        }
                        StingLog.Info($"SharedParamGuids.DisciplineBindings: {_disciplineBindings.Count} discipline params resolved");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("SharedParamGuids.DisciplineBindings: build failed, using empty dict", ex);
                        _disciplineBindings = new Dictionary<string, BuiltInCategory[]>();
                    }
                }
                return _disciplineBindings;
            }
        }
        private static Dictionary<string, BuiltInCategory[]> _disciplineBindings;

        /// <summary>
        /// Invalidate cached data so next access re-derives from ParamRegistry.
        /// Called by ParamRegistry.Reload() to prevent stale caches.
        /// </summary>
        internal static void InvalidateCache()
        {
            _allCategoryEnums = null;
            _disciplineBindings = null;
            _perParamCats = null;
        }

        /// <summary>
        /// Build a CategorySet from BuiltInCategory enum values (type-safe).
        /// </summary>
        public static CategorySet BuildCategorySet(Document doc, BuiltInCategory[] categories)
        {
            CategorySet catSet = new CategorySet();
            if (categories == null || categories.Length == 0)
            {
                StingLog.Warn("BuildCategorySet: categories array is null or empty");
                return catSet;
            }

            Categories cats = doc.Settings.Categories;
            int added = 0;
            foreach (BuiltInCategory bic in categories)
            {
                try
                {
                    Category cat = cats.get_Item(bic);
                    if (cat != null && cat.AllowsBoundParameters)
                    {
                        catSet.Insert(cat);
                        added++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Category {bic} not available: {ex.Message}");
                }
            }
            StingLog.Info($"BuildCategorySet: {added}/{categories.Length} categories resolved");
            return catSet;
        }

        /// <summary>
        /// Per-parameter category bindings loaded directly from CATEGORY_BINDINGS.csv.
        /// This is the authoritative, per-param×category source of truth used to drive
        /// binding (each parameter gets ITS OWN category set, not its container group's
        /// whole set). Cached after first access; call InvalidateCache() to refresh.
        ///
        /// Returns paramName → BuiltInCategory[] for every parameter that has one or more
        /// rows in CATEGORY_BINDINGS.csv, resolved through ParamRegistry.CategoryEnumMap.
        /// The pseudo-category "Materials" (OST_Materials does not accept bound parameters)
        /// is skipped here; material binding is handled separately by CleanMaterialBindings.
        /// </summary>
        public static Dictionary<string, BuiltInCategory[]> PerParamCategoryBindings
        {
            get
            {
                if (_perParamCats == null)
                {
                    try
                    {
                        _perParamCats = LoadPerParamCategoryBindings();
                        StingLog.Info($"SharedParamGuids.PerParamCategoryBindings: {_perParamCats.Count} params loaded from CATEGORY_BINDINGS.csv");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("SharedParamGuids.PerParamCategoryBindings: load failed, using empty dict", ex);
                        _perParamCats = new Dictionary<string, BuiltInCategory[]>(StringComparer.Ordinal);
                    }
                }
                return _perParamCats;
            }
        }
        private static Dictionary<string, BuiltInCategory[]> _perParamCats;

        /// <summary>
        /// Parse CATEGORY_BINDINGS.csv → paramName → BuiltInCategory[]. Comment / header
        /// lines are skipped. Categories that do not resolve through CategoryEnumMap
        /// (including "Materials") are dropped. Order is preserved and duplicates removed.
        /// </summary>
        private static Dictionary<string, BuiltInCategory[]> LoadPerParamCategoryBindings()
        {
            var result = new Dictionary<string, BuiltInCategory[]>(StringComparer.Ordinal);
            string path = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
            if (path == null)
            {
                StingLog.Warn("CATEGORY_BINDINGS.csv not found — per-param bindings empty");
                return result;
            }

            // paramName → ordered distinct BuiltInCategory list
            var acc = new Dictionary<string, List<BuiltInCategory>>(StringComparer.Ordinal);
            var seen = new Dictionary<string, HashSet<BuiltInCategory>>(StringComparer.Ordinal);

            foreach (string raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                string[] cols = StingToolsApp.ParseCsvLine(raw);
                if (cols.Length < 2) continue;
                string param = cols[0].Trim();
                string catName = cols[1].Trim();
                if (param.Length == 0 || param.Equals("Parameter_Name", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (catName.Length == 0 || catName.Equals("Materials", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!ParamRegistry.CategoryEnumMap.TryGetValue(catName, out string enumStr)) continue;
                if (!Enum.TryParse(enumStr, out BuiltInCategory bic)) continue;

                if (!acc.TryGetValue(param, out var list))
                {
                    list = new List<BuiltInCategory>();
                    acc[param] = list;
                    seen[param] = new HashSet<BuiltInCategory>();
                }
                if (seen[param].Add(bic)) list.Add(bic);
            }

            foreach (var kvp in acc)
                if (kvp.Value.Count > 0)
                    result[kvp.Key] = kvp.Value.ToArray();

            return result;
        }

        /// <summary>
        /// Validate DisciplineBindings against CATEGORY_BINDINGS.csv.
        /// Now delegates to ParamRegistry for the binding data.
        /// </summary>
        public static int ValidateBindingsFromCsv()
        {
            string path = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
            if (path == null)
            {
                StingLog.Info("CATEGORY_BINDINGS.csv not found — skipping binding validation");
                return -1;
            }

            int discrepancies = 0;

            try
            {
                // Build reverse lookup from ParamRegistry category_enum_map
                var catNameToEnum = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in ParamRegistry.CategoryEnumMap)
                {
                    if (Enum.TryParse(kvp.Value, out BuiltInCategory bic))
                        catNameToEnum[kvp.Key] = bic;
                }

                // Build set of registry bindings: "ParamName|CategoryEnum"
                var registryBindings = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kvp in DisciplineBindings)
                {
                    foreach (var bic in kvp.Value)
                        registryBindings.Add($"{kvp.Key}|{bic}");
                }

                // Parse CSV bindings
                var csvBindings = new HashSet<string>(StringComparer.Ordinal);
                var lines = File.ReadAllLines(path)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Skip(1);

                foreach (string line in lines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 2) continue;

                    string paramName = cols[0].Trim();
                    string catName = cols[1].Trim();

                    if (!DisciplineBindings.ContainsKey(paramName)) continue;
                    if (!catNameToEnum.TryGetValue(catName, out BuiltInCategory bic)) continue;

                    csvBindings.Add($"{paramName}|{bic}");
                }

                // Find discrepancies both directions
                foreach (string binding in csvBindings)
                {
                    if (!registryBindings.Contains(binding))
                    {
                        discrepancies++;
                        StingLog.Warn($"Binding in CSV but not in registry: {binding}");
                    }
                }
                foreach (string binding in registryBindings)
                {
                    if (!csvBindings.Contains(binding))
                    {
                        discrepancies++;
                        StingLog.Warn($"Binding in registry but not in CSV: {binding}");
                    }
                }

                if (discrepancies == 0)
                    StingLog.Info($"Binding validation passed: {registryBindings.Count} registry bindings match CSV");
                else
                    StingLog.Warn($"Binding validation: {discrepancies} discrepancies between registry and CSV");
            }
            catch (Exception ex)
            {
                StingLog.Error("Binding validation failed", ex);
                return -1;
            }

            // Cross-CSV consistency: CATEGORY_BINDINGS.csv (per-param) must agree with
            // PARAMETER_CATEGORIES.csv (the Categories column). A param whose two files
            // disagree is a data-integrity error that would silently mis-bind.
            try
            {
                int mismatches = AuditCrossCsvConsistency(out int checkedParams);
                if (mismatches == 0)
                    StingLog.Info($"Cross-CSV consistency passed: {checkedParams} params agree between CATEGORY_BINDINGS.csv and PARAMETER_CATEGORIES.csv");
                else
                    StingLog.Warn($"Cross-CSV consistency: {mismatches} param(s) disagree between CATEGORY_BINDINGS.csv and PARAMETER_CATEGORIES.csv");
                discrepancies += mismatches;
            }
            catch (Exception ex) { StingLog.Warn($"Cross-CSV consistency check failed: {ex.Message}"); }

            return discrepancies;
        }

        /// <summary>
        /// Assert that CATEGORY_BINDINGS.csv (per-param×category rows) and
        /// PARAMETER_CATEGORIES.csv (per-param Categories column) describe the same
        /// category set for every parameter present in both. Returns the number of
        /// mismatched parameters and logs each one. Comparison is by resolvable
        /// BuiltInCategory so unresolved names ("Materials", loads, analytical) don't
        /// produce false positives.
        /// </summary>
        public static int AuditCrossCsvConsistency(out int checkedParams)
        {
            checkedParams = 0;
            var perParam = PerParamCategoryBindings; // CATEGORY_BINDINGS.csv, resolved
            string pcPath = StingToolsApp.FindDataFile("PARAMETER_CATEGORIES.csv");
            if (pcPath == null) return 0;

            int mismatches = 0;
            foreach (string raw in File.ReadAllLines(pcPath))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                string[] cols = StingToolsApp.ParseCsvLine(raw);
                if (cols.Length < 5) continue;
                string param = cols[0].Trim();
                if (param.Length == 0 || param.Equals("Parameter Name", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!perParam.TryGetValue(param, out var cbCats)) continue; // only params in both

                // Resolve PARAMETER_CATEGORIES Categories column (comma-separated) to enums
                var pcNames = cols[4].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                var pcEnums = ParamRegistry.ResolveCategoryEnums(pcNames);

                var cbSet = new HashSet<BuiltInCategory>(cbCats);
                var pcSet = new HashSet<BuiltInCategory>(pcEnums);
                checkedParams++;
                if (!cbSet.SetEquals(pcSet))
                {
                    mismatches++;
                    var extra = cbSet.Except(pcSet).ToList();
                    var missing = pcSet.Except(cbSet).ToList();
                    StingLog.Warn($"Cross-CSV mismatch '{param}': CATEGORY_BINDINGS-only={string.Join("/", extra)} PARAMETER_CATEGORIES-only={string.Join("/", missing)}");
                }
            }
            return mismatches;
        }
    }
}
