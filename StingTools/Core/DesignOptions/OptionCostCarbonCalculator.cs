// StingTools — per-option 5D cost + embodied carbon roll-up.
//
// Phase 175 — feeds cost_rates_5d.csv (project currency / unit) and the
// SustainabilityEngine's BS EN 15978 lifecycle carbon factors against
// ElementDesignOptionFilter-segmented collectors so every option gets a
// directly comparable capex + embodied-carbon total. Output is a list
// of OptionRollupRow rows ready for CSV export, the dashboard, the
// option_value_engineering deliverable template, and the workflow
// preset's comparison step.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.DesignOptions
{
    public class OptionRollupRow
    {
        public string SetName;
        public string OptionName;
        public bool   IsPrimary;
        public int    ElementCount;
        public double TotalAreaM2;
        public double TotalVolumeM3;
        public double TotalCost;
        public double TotalCarbonKg;
        public Dictionary<string, double> CostByCategory  = new Dictionary<string, double>();
        public Dictionary<string, double> CarbonByCategory = new Dictionary<string, double>();
    }

    public static class OptionCostCarbonCalculator
    {
        // ── Rate cache ────────────────────────────────────────────────────
        // WP0 — single source of truth. Cost rates come from the SAME loader the
        // BOQ engine uses (UGX column 4 of cost_rates_5d.csv) — the previous fork
        // read the USD column (3), diverging ~3700×. Embodied carbon is no longer
        // a parallel category dictionary here: every element routes through the
        // ONE canonical BOQCostManager.ComputeElementCarbonKg → CarbonFactorResolver.
        private static Dictionary<string, (double rate, string unit)> _costRates;

        /// <summary>WP0 — delegate to the canonical BOQ rate loader (UGX).</summary>
        public static Dictionary<string, (double rate, string unit)> LoadCostRates()
            => _costRates ??= StingTools.BOQ.BOQCostManager.LoadCsvRates();

        // ── Public roll-up ───────────────────────────────────────────────
        public static List<OptionRollupRow> Build(Document doc)
        {
            var rows = new List<OptionRollupRow>();
            if (doc == null) return rows;

            var costs = LoadCostRates();
            var sets = DesignOptionRegistry.Snapshot(doc);

            // Main-model baseline first
            rows.Add(BuildRow(doc, null, DesignOptionParams.MAIN_MODEL_LABEL,
                              "Main Model", isPrimary: true, costs));

            foreach (var s in sets)
            foreach (var o in s.Options)
            {
                rows.Add(BuildRow(doc, o.OptionId, s.Name, o.Name, o.IsPrimary, costs));
            }
            return rows;
        }

        private static OptionRollupRow BuildRow(
            Document doc, ElementId optionId, string setName, string optionName,
            bool isPrimary,
            Dictionary<string, (double rate, string unit)> costs)
        {
            var row = new OptionRollupRow
            {
                SetName = setName,
                OptionName = optionName,
                IsPrimary = isPrimary
            };

            try
            {
                IList<Element> elems;
                if (optionId == null || optionId == ElementId.InvalidElementId)
                {
                    // Main model = elements with DesignOption == null.
                    elems = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.DesignOption == null)
                        .ToList();
                }
                else
                {
                    var f = new ElementDesignOptionFilter(optionId);
                    elems = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .WherePasses(f)
                        .ToElements();
                }

                row.ElementCount = elems.Count;

                foreach (var el in elems)
                {
                    string cat = el.Category?.Name ?? "(uncategorised)";
                    double area = ReadDouble(el, BuiltInParameter.HOST_AREA_COMPUTED);
                    double vol  = ReadDouble(el, BuiltInParameter.HOST_VOLUME_COMPUTED);
                    double lenFt = ReadDouble(el, BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (lenFt <= 0) lenFt = ReadDouble(el, BuiltInParameter.INSTANCE_LENGTH_PARAM);
                    double areaM2 = area > 0 ? area * 0.092903 : 0;  // ft² → m²
                    double volM3  = vol  > 0 ? vol * 0.0283168 : 0;  // ft³ → m³
                    double lenM   = lenFt > 0 ? lenFt * 0.3048 : 0;  // ft → m
                    row.TotalAreaM2 += areaM2;
                    row.TotalVolumeM3 += volM3;

                    // WP-FIX — cost on the measure the rate's UNIT calls for, so the
                    // option comparison matches the bill's basis (each → ×count,
                    // m³ → ×volume, m → ×length, m²/default → ×area).
                    double rate = 0.0; string unit = "m2";
                    if (costs != null && costs.TryGetValue(cat, out var rc)) { rate = rc.rate; unit = (rc.unit ?? "m2").Trim().ToLowerInvariant(); }
                    double measure;
                    switch (unit)
                    {
                        case "each": case "item": case "nr": case "no": case "":  measure = 1; break;
                        case "m3": case "m³":                                       measure = volM3; break;
                        case "m": case "lm": case "rm":                            measure = lenM; break;
                        default:                                                    measure = areaM2; break; // m2/m²
                    }
                    double cost = rate * Math.Max(measure, 0);
                    if (cost <= 0 && rate > 0) cost = rate;     // per-unit fallback when the measure is unavailable
                    row.TotalCost += cost;
                    if (cost > 0)
                    {
                        row.CostByCategory.TryGetValue(cat, out double accC);
                        row.CostByCategory[cat] = accC + cost;
                    }

                    // WP0 — embodied carbon via the ONE canonical resolver
                    // (BOQ engine), keyed on the element's primary material with
                    // EPD/library/legacy fallback + correct per-m³/per-kg units —
                    // not a parallel per-category dictionary.
                    double kg = StingTools.BOQ.BOQCostManager.ComputeElementCarbonKg(el, volM3);
                    row.TotalCarbonKg += kg;
                    if (kg > 0)
                    {
                        row.CarbonByCategory.TryGetValue(cat, out double accK);
                        row.CarbonByCategory[cat] = accK + kg;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildRow {setName}/{optionName}: {ex.Message}"); }
            return row;
        }

        private static double ReadDouble(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                return 0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        // ── CSV export ───────────────────────────────────────────────────
        public static string ExportCsv(Document doc, List<OptionRollupRow> rows = null)
        {
            rows ??= Build(doc);
            string folder = OptionFolderManager.GetOptionsRoot(doc);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = Path.Combine(folder,
                $"option_comparison_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("set,option,is_primary,element_count,area_m2,volume_m3,total_cost,total_carbon_kgCO2e");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(r.SetName), Csv(r.OptionName), r.IsPrimary ? "1" : "0",
                    r.ElementCount.ToString(CultureInfo.InvariantCulture),
                    r.TotalAreaM2.ToString("F2", CultureInfo.InvariantCulture),
                    r.TotalVolumeM3.ToString("F3", CultureInfo.InvariantCulture),
                    r.TotalCost.ToString("F2", CultureInfo.InvariantCulture),
                    r.TotalCarbonKg.ToString("F1", CultureInfo.InvariantCulture)
                }));
            }
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Contains(',') || s.Contains('"')
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}
