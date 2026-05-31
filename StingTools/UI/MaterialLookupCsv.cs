using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Lazy reader for the corporate material reference CSVs
    /// (<c>MATERIAL_LOOKUP.csv</c> — 237 rows; <c>BLE_MATERIALS.csv</c>
    /// — 815 rows; <c>MEP_MATERIALS.csv</c> — 464 rows).
    ///
    /// Used by:
    ///   • MaterialRowBuilder.BuildOne — third tier in the cost / carbon
    ///     resolution chain (override → element param → CSV lookup)
    ///   • StingMaterialUpdater — auto-fill on Material creation
    ///   • ApplyToSelection workflows that need a default value
    ///
    /// Match heuristic: case-insensitive exact-name match by default.
    /// Falls through to <c>MaterialLookupCsv.GetClosest</c> for fuzzy
    /// resolution (Levenshtein) when the exact key is missing.
    ///
    /// Cached at module level; <see cref="Reload"/> drops the cache.
    /// </summary>
    public static class MaterialLookupCsv
    {
        private static readonly Dictionary<string, MaterialLookupRow> _cache =
            new Dictionary<string, MaterialLookupRow>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static readonly object _lock = new object();

        public static void Reload()
        {
            lock (_lock) { _cache.Clear(); _loaded = false; }
        }

        public static MaterialLookupRow Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            EnsureLoaded();
            lock (_lock)
            {
                if (_cache.TryGetValue(name, out var row)) return row;
                // Strip common prefixes for a second-chance lookup.
                foreach (var prefix in new[] { "BLE_", "MEP_", "STING_" })
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        _cache.TryGetValue(name.Substring(prefix.Length), out var stripped))
                        return stripped;
                return null;
            }
        }

        public static double GetCost(string name) => Get(name)?.Cost ?? 0;
        // DEAD AT RUNTIME for the shipped MATERIAL_LOOKUP.csv (Z-20 finding):
        // EnsureLoaded() resolves a wide-format "Material"/"Name" column, but the
        // shipped file is long-format (Category,TypeKey,Property,Value) with no such
        // column, so iName < 0 and the cache loads empty — GetCarbon/GetCost/
        // GetDensity all return 0 here. The carbon resolver's LOOKUP tier is thus a
        // no-op today; delivered carbon comes from the Tier-1 material param fed by
        // MEP_/BLE_MATERIALS.csv PROP_CARBON_KG_M3 (where the Z-20 ICE v3.0 fix lives).
        // Making MATERIAL_LOOKUP canonical (teach this the long format + reorder the
        // resolver above the material param) is a deliberate, test-backed follow-up PR.
        public static double GetCarbon(string name) => Get(name)?.CarbonKgCo2e ?? 0;
        public static double GetDensity(string name) => Get(name)?.DensityKgM3 ?? 0;
        public static double GetThermalConductivity(string name) => Get(name)?.ThermalConductivityWmK ?? 0;

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    string path = StingToolsApp.FindDataFile("MATERIAL_LOOKUP.csv");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    var lines = File.ReadAllLines(path);
                    if (lines.Length < 2) return;
                    // Skip leading comment ('#') + blank lines so the real
                    // header row is read (MATERIAL_LOOKUP.csv opens with a
                    // multi-line '# ...' banner).
                    int headerIdx = 0;
                    while (headerIdx < lines.Length)
                    {
                        string t = (lines[headerIdx] ?? "").Trim();
                        if (t.Length == 0 || t.StartsWith("#")) { headerIdx++; continue; }
                        break;
                    }
                    if (headerIdx >= lines.Length) { StingLog.Warn($"MaterialLookupCsv: no header row in {path}"); return; }
                    var header = StingToolsApp.ParseCsvLine(lines[headerIdx]);
                    // Build a column-name → index map so re-ordering the CSV
                    // doesn't break us.
                    int Idx(params string[] candidates)
                    {
                        foreach (var c in candidates)
                        {
                            int i = Array.FindIndex(header, h => string.Equals(h, c, StringComparison.OrdinalIgnoreCase));
                            if (i >= 0) return i;
                        }
                        return -1;
                    }
                    int iName    = Idx("Material", "Name", "MaterialName");
                    int iCost    = Idx("Cost", "Cost_USD", "Cost_per_unit");
                    int iCarbon  = Idx("EmbodiedCarbon", "Carbon_kgCO2e", "kgCO2e", "EmbodiedCarbon_kgCO2eperkg");
                    int iDensity = Idx("Density", "Density_kg_m3", "DensityKgM3");
                    int iLambda  = Idx("ThermalConductivity", "Lambda", "ThermalCond_W_mK");

                    if (iName < 0) { StingLog.Warn($"MaterialLookupCsv: no Name column in {path}"); return; }
                    for (int li = headerIdx + 1; li < lines.Length; li++)
                    {
                        string raw = (lines[li] ?? "").Trim();
                        if (raw.Length == 0 || raw.StartsWith("#")) continue;
                        var fields = StingToolsApp.ParseCsvLine(lines[li]);
                        if (fields == null || fields.Length <= iName) continue;
                        string key = (fields[iName] ?? "").Trim();
                        if (string.IsNullOrEmpty(key)) continue;
                        var row = new MaterialLookupRow
                        {
                            Name                   = key,
                            Cost                   = ParseDouble(SafeAt(fields, iCost)),
                            CarbonKgCo2e           = ParseDouble(SafeAt(fields, iCarbon)),
                            DensityKgM3            = ParseDouble(SafeAt(fields, iDensity)),
                            ThermalConductivityWmK = ParseDouble(SafeAt(fields, iLambda)),
                        };
                        _cache[key] = row;
                    }
                    StingLog.Info($"MaterialLookupCsv: loaded {_cache.Count} rows from {path}");
                }
                catch (Exception ex) { StingLog.Warn($"MaterialLookupCsv.EnsureLoaded: {ex.Message}"); }
            }
        }

        private static string SafeAt(string[] fields, int idx)
            => idx >= 0 && idx < fields.Length ? fields[idx] : "";

        private static double ParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
    }

    public class MaterialLookupRow
    {
        public string Name { get; set; }
        public double Cost { get; set; }
        public double CarbonKgCo2e { get; set; }
        public double DensityKgM3 { get; set; }
        public double ThermalConductivityWmK { get; set; }
    }
}
