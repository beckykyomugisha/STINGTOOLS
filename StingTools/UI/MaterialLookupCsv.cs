using System;
using System.Collections.Generic;
using System.IO;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Lazy reader for the corporate material reference CSV
    /// (<c>MATERIAL_LOOKUP.csv</c>). Reads the file and delegates format
    /// handling to the pure (Revit-free) <see cref="MaterialLookupParser"/>.
    ///
    /// Z-24: the shipped file is LONG-format —
    /// <c>Category,TypeKey,Property,Value,Unit,Description</c> — keyed by
    /// (Category, TypeKey). The previous loader assumed a WIDE format with a
    /// "Material"/"Name" column; against the long file the name column resolved
    /// to −1, the cache loaded empty, and GetCarbon/GetCost/GetDensity always
    /// returned 0 (the "Phase 76+ canonical Tier-1 lookup" was dead at runtime).
    /// The parser now pivots the long format into one <see cref="MaterialLookupRow"/>
    /// per (Category, TypeKey), indexed under "Category TypeKey", "Category:TypeKey",
    /// the bare TypeKey when globally unique (so GetCarbon("C30") works), and the
    /// per-category DEFAULT row under the bare Category name.
    ///
    /// Data note: the file carries CARBON_KG_PER_M3 (concrete grades) + formula
    /// constants only — NO density / cost / thermal columns, so GetDensity /
    /// GetCost / GetThermalConductivity return 0 until such properties are added
    /// (data gap, not a loader bug). Use <see cref="GetProperty"/> for any
    /// long-format property.
    ///
    /// NOTE (resolver order): fixing this loader makes Tier-2 of the carbon/cost
    /// resolver capable of returning values again, but the resolver order is
    /// UNCHANGED — Tier-1 (the element's material parameter) still wins. Whether
    /// LOOKUP should supersede the per-row BLE/MEP columns is a separate,
    /// value-reconciled PR (see docs/UI_CLEANUP_CAMPAIGN.md Z-24 §3).
    /// </summary>
    public static class MaterialLookupCsv
    {
        private static Dictionary<string, MaterialLookupRow> _cache;
        private static readonly object _lock = new object();

        public static void Reload()
        {
            lock (_lock) { _cache = null; }
        }

        public static MaterialLookupRow Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var cache = EnsureLoaded();
            if (cache.TryGetValue(name, out var row)) return row;
            // Strip common prefixes for a second-chance lookup.
            foreach (var prefix in new[] { "BLE_", "MEP_", "STING_" })
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    cache.TryGetValue(name.Substring(prefix.Length), out var stripped))
                    return stripped;
            return null;
        }

        public static double GetCost(string name) => Get(name)?.Cost ?? 0;
        public static double GetCarbon(string name) => Get(name)?.CarbonKgCo2e ?? 0;            // net = fossil + biogenic
        // Z-25 — WLCA-separated A1-A3 carbon (RIBA 2030 / LETI / RICS). Net API above
        // is unchanged; these expose the split for whole-life reports.
        public static double GetCarbonFossil(string name) => Get(name)?.FossilCarbonKgCo2e ?? 0;
        public static double GetCarbonBiogenic(string name) => Get(name)?.BiogenicCarbonKgCo2e ?? 0;
        public static double GetDensity(string name) => Get(name)?.DensityKgM3 ?? 0;
        public static double GetThermalConductivity(string name) => Get(name)?.ThermalConductivityWmK ?? 0;

        /// <summary>
        /// Generic accessor for any long-format Property (e.g.
        /// "CEMENT_BAGS_PER_M3", "WASTE_PCT", "STEEL_KG_PER_M3"). Returns 0 when
        /// the material or property is absent. Case-insensitive on the property.
        /// </summary>
        public static double GetProperty(string name, string property)
        {
            var row = Get(name);
            if (row?.Properties == null || string.IsNullOrEmpty(property)) return 0;
            return row.Properties.TryGetValue(property, out var v) ? v : 0;
        }

        private static Dictionary<string, MaterialLookupRow> EnsureLoaded()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                var cache = new Dictionary<string, MaterialLookupRow>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = StingToolsApp.FindDataFile("MATERIAL_LOOKUP.csv");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        cache = MaterialLookupParser.Parse(File.ReadAllLines(path));
                        StingLog.Info($"MaterialLookupCsv: loaded {cache.Count} material keys from {path}");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"MaterialLookupCsv.EnsureLoaded: {ex.Message}"); }
                _cache = cache;
                return _cache;
            }
        }
    }
}
