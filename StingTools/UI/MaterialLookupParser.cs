using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.UI
{
    /// <summary>
    /// Z-24 — pure (Revit-free) parser/pivot for MATERIAL_LOOKUP.csv. Split out
    /// of <see cref="MaterialLookupCsv"/> so the format handling is unit-tested
    /// without a Revit host (linked into StingTools.Boq.Tests the same way
    /// SeqAssigner / WasteFactor are). MaterialLookupCsv reads the file and
    /// delegates the parsing here.
    ///
    /// The shipped file is LONG-format: Category,TypeKey,Property,Value,Unit,
    /// Description — keyed by (Category, TypeKey) with one row per Property.
    /// <see cref="Parse"/> pivots it into one <see cref="MaterialLookupRow"/>
    /// per (Category, TypeKey). A legacy WIDE-format parser is retained.
    /// </summary>
    public static class MaterialLookupParser
    {
        /// <summary>
        /// Returns a key→row cache built from the CSV lines. Auto-detects long
        /// vs wide format from the header (Category+TypeKey+Property+Value =
        /// long; a Material/Name column = wide). Returns an empty cache for an
        /// unrecognised header. Comparer is case-insensitive.
        /// </summary>
        public static Dictionary<string, MaterialLookupRow> Parse(IEnumerable<string> rawLines)
        {
            var cache = new Dictionary<string, MaterialLookupRow>(StringComparer.OrdinalIgnoreCase);
            if (rawLines == null) return cache;
            var lines = rawLines.ToList();

            int headerIdx = 0;
            while (headerIdx < lines.Count)
            {
                string t = (lines[headerIdx] ?? "").Trim();
                if (t.Length == 0 || t.StartsWith("#")) { headerIdx++; continue; }
                break;
            }
            if (headerIdx >= lines.Count) return cache;

            var header = SplitCsv(lines[headerIdx]);
            int Idx(params string[] cands)
            {
                foreach (var c in cands)
                {
                    int i = Array.FindIndex(header, h => string.Equals(h, c, StringComparison.OrdinalIgnoreCase));
                    if (i >= 0) return i;
                }
                return -1;
            }

            int iCat = Idx("Category"), iType = Idx("TypeKey", "Type", "Key"),
                iProp = Idx("Property", "Prop"), iVal = Idx("Value", "Val");

            if (iCat >= 0 && iType >= 0 && iProp >= 0 && iVal >= 0)
                LoadLong(lines, headerIdx, iCat, iType, iProp, iVal, cache);
            else
                LoadWide(lines, headerIdx, Idx, cache);

            return cache;
        }

        private static void LoadLong(List<string> lines, int headerIdx,
            int iCat, int iType, int iProp, int iVal, Dictionary<string, MaterialLookupRow> cache)
        {
            var pivot = new Dictionary<(string, string), Dictionary<string, double>>();
            var order = new List<(string cat, string type)>();
            int max = Math.Max(Math.Max(iCat, iType), Math.Max(iProp, iVal));

            for (int li = headerIdx + 1; li < lines.Count; li++)
            {
                string raw = (lines[li] ?? "").Trim();
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                var f = SplitCsv(lines[li]);
                if (f.Length <= max) continue;
                string cat = (f[iCat] ?? "").Trim(), type = (f[iType] ?? "").Trim();
                string prop = (f[iProp] ?? "").Trim().ToUpperInvariant();
                if (cat.Length == 0 || type.Length == 0 || prop.Length == 0) continue;

                var key = (cat, type);
                if (!pivot.TryGetValue(key, out var props))
                {
                    props = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    pivot[key] = props;
                    order.Add(key);
                }
                props[prop] = ParseDouble(f[iVal]);
            }

            var typeCounts = order.GroupBy(k => k.type, StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var key in order)
            {
                var props = pivot[(key.cat, key.type)];
                var row = new MaterialLookupRow
                {
                    Name                   = $"{key.cat} {key.type}",
                    Category               = key.cat,
                    TypeKey                = key.type,
                    Properties             = props,
                    CarbonKgCo2e           = Prop(props, "CARBON_KG_PER_M3", "EMBODIED_CARBON", "CARBON"),
                    DensityKgM3            = Prop(props, "DENSITY_KG_M3", "DENSITY"),
                    Cost                   = Prop(props, "COST", "COST_USD", "RATE"),
                    ThermalConductivityWmK = Prop(props, "THERMAL_CONDUCTIVITY", "LAMBDA", "THERMAL_COND_W_MK"),
                };

                Register(cache, $"{key.cat} {key.type}", row);
                Register(cache, $"{key.cat}:{key.type}", row);
                if (typeCounts.TryGetValue(key.type, out int n) && n == 1 &&
                    !string.Equals(key.type, "DEFAULT", StringComparison.OrdinalIgnoreCase))
                    Register(cache, key.type, row);
                if (string.Equals(key.type, "DEFAULT", StringComparison.OrdinalIgnoreCase))
                    Register(cache, key.cat, row);
            }
        }

        private static void LoadWide(List<string> lines, int headerIdx,
            Func<string[], int> idx, Dictionary<string, MaterialLookupRow> cache)
        {
            int iName    = idx(new[] { "Material", "Name", "MaterialName" });
            int iCost    = idx(new[] { "Cost", "Cost_USD", "Cost_per_unit" });
            int iCarbon  = idx(new[] { "EmbodiedCarbon", "Carbon_kgCO2e", "kgCO2e", "EmbodiedCarbon_kgCO2eperkg" });
            int iDensity = idx(new[] { "Density", "Density_kg_m3", "DensityKgM3" });
            int iLambda  = idx(new[] { "ThermalConductivity", "Lambda", "ThermalCond_W_mK" });
            if (iName < 0) return;

            for (int li = headerIdx + 1; li < lines.Count; li++)
            {
                string raw = (lines[li] ?? "").Trim();
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                var f = SplitCsv(lines[li]);
                if (f.Length <= iName) continue;
                string key = (f[iName] ?? "").Trim();
                if (key.Length == 0) continue;
                Register(cache, key, new MaterialLookupRow
                {
                    Name                   = key,
                    Cost                   = ParseDouble(At(f, iCost)),
                    CarbonKgCo2e           = ParseDouble(At(f, iCarbon)),
                    DensityKgM3            = ParseDouble(At(f, iDensity)),
                    ThermalConductivityWmK = ParseDouble(At(f, iLambda)),
                });
            }
        }

        // First registration wins so a unique bare TypeKey is not clobbered by
        // a later composite that happens to be the same string.
        private static void Register(Dictionary<string, MaterialLookupRow> cache, string key, MaterialLookupRow row)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!cache.ContainsKey(key)) cache[key] = row;
        }

        private static double Prop(Dictionary<string, double> props, params string[] names)
        {
            foreach (var n in names) if (props.TryGetValue(n, out var v)) return v;
            return 0;
        }

        private static string At(string[] f, int i) => i >= 0 && i < f.Length ? f[i] : "";

        private static double ParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        /// <summary>Minimal quote-aware CSV splitter (handles "" escaping).</summary>
        private static string[] SplitCsv(string line)
        {
            if (line == null) return Array.Empty<string>();
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }

    public class MaterialLookupRow
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string TypeKey { get; set; }
        public double Cost { get; set; }
        public double CarbonKgCo2e { get; set; }
        public double DensityKgM3 { get; set; }
        public double ThermalConductivityWmK { get; set; }
        /// <summary>All long-format Property→Value pairs (upper-cased keys);
        /// null for legacy wide-format rows.</summary>
        public Dictionary<string, double> Properties { get; set; }
    }
}
