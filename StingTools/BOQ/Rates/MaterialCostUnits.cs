// ══════════════════════════════════════════════════════════════════════════
//  MaterialCostUnits.cs — the unit a material library rate is quoted in.
//  Revit-free (compiled into StingTools.Boq.Tests).
//
//  DSCH-16. MATERIAL_SCHEMA.json requires MAT_COST_UNIT_OF_MEASURE and
//  MaterialLibraryRateProvider's D9 guard says "a rate with no unit is not a
//  rate" - but the libraries had no such column, so the guard checked the
//  CATEGORY's unit (from cost_rates_5d) instead of the material's. A per-litre
//  paint rate and a per-m2 wall then priced each other's quantities.
//
//  The column is now carried by BLE_MATERIALS.csv / MEP_MATERIALS.csv (filled
//  by tools/fix_material_data.py --only uom, conservative rules, blank where no
//  rule fires). This reads it BY HEADER NAME. A blank cell means "not declared".
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.BOQ.Rates
{
    public static class MaterialCostUnits
    {
        public const string Column = "MAT_COST_UNIT_OF_MEASURE";

        /// <summary>MAT_NAME -> declared cost unit, from one library file's text.
        /// The first row for a name wins (as MaterialRegistry does).</summary>
        public static Dictionary<string, string> Parse(string csvText, Func<string, string[]> splitLine)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(csvText) || splitLine == null) return map;
            int nameCol = -1, unitCol = -1;
            bool header = false;
            foreach (string raw in csvText.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimStart('﻿');
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                string[] f = splitLine(line);
                if (!header)
                {
                    header = true;
                    for (int i = 0; i < f.Length; i++)
                    {
                        string h = (f[i] ?? "").Trim();
                        if (h.Equals("MAT_NAME", StringComparison.OrdinalIgnoreCase)) nameCol = i;
                        else if (h.Equals(Column, StringComparison.OrdinalIgnoreCase)) unitCol = i;
                    }
                    if (nameCol < 0 || unitCol < 0) return map;   // older library: no units declared
                    continue;
                }
                if (f.Length <= Math.Max(nameCol, unitCol)) continue;
                string name = (f[nameCol] ?? "").Trim(), unit = (f[unitCol] ?? "").Trim();
                if (name.Length > 0 && unit.Length > 0 && !map.ContainsKey(name)) map[name] = unit;
            }
            return map;
        }

        /// <summary>
        /// The unit a material rate may be applied in, given the unit the bill
        /// measures (<paramref name="requestUnit"/>) and the material's declared unit.
        /// Returns false (refuse) when both are known and denote different
        /// quantities - a per-litre rate cannot price square metres.
        /// </summary>
        public static bool TryAgree(string requestUnit, string materialUnit, out string unit, out string reason)
        {
            reason = "";
            bool hasReq = !string.IsNullOrWhiteSpace(requestUnit);
            bool hasMat = !string.IsNullOrWhiteSpace(materialUnit);
            if (hasReq && hasMat && !BoqUnits.Align(requestUnit, materialUnit))
            {
                unit = null;
                reason = $"the bill measures in '{requestUnit}' but the material's rate is per '{materialUnit}'";
                return false;
            }
            unit = hasReq ? requestUnit : (hasMat ? materialUnit : null);
            if (unit == null) reason = "no unit of measure on the bill line or the material";
            return unit != null;
        }
    }
}
