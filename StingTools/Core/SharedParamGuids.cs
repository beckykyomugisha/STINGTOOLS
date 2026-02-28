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
                    _allCategoryEnums = ParamRegistry.ResolveUniversalCategoryEnums();
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
                    _disciplineBindings = ParamRegistry.BuildDisciplineBindings();
                return _disciplineBindings;
            }
        }
        private static Dictionary<string, BuiltInCategory[]> _disciplineBindings;

        /// <summary>
        /// Build a CategorySet from BuiltInCategory enum values (type-safe).
        /// </summary>
        public static CategorySet BuildCategorySet(Document doc, BuiltInCategory[] categories)
        {
            CategorySet catSet = new CategorySet();
            Categories cats = doc.Settings.Categories;
            foreach (BuiltInCategory bic in categories)
            {
                try
                {
                    Category cat = cats.get_Item(bic);
                    if (cat != null && cat.AllowsBoundParameters)
                        catSet.Insert(cat);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Category {bic} not available: {ex.Message}");
                }
            }
            return catSet;
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

            return discrepancies;
        }
    }
}
