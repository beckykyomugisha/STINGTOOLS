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
using Autodesk.Revit.DB;

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
        // ── Rate / factor caches ─────────────────────────────────────────
        private static Dictionary<string, double> _costRates;
        private static Dictionary<string, double> _carbonFactors;

        /// <summary>Load cost_rates_5d.csv (Category column → Rate column).
        /// Cached after first call.</summary>
        public static Dictionary<string, double> LoadCostRates()
        {
            if (_costRates != null) return _costRates;
            _costRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = StingTools.Core.StingToolsApp.FindDataFile("cost_rates_5d.csv");
                if (path == null || !File.Exists(path)) return _costRates;
                bool first = true;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (first) { first = false; continue; }
                    var cells = line.Split(',');
                    if (cells.Length < 5) continue;
                    string cat = cells[0]?.Trim();
                    if (!double.TryParse(cells[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)) continue;
                    if (!string.IsNullOrEmpty(cat)) _costRates[cat] = rate;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadCostRates: {ex.Message}"); }
            return _costRates;
        }

        /// <summary>Default embodied-carbon factors by Revit category.
        /// Sourced from the ICE Database v3.0 cradle-to-gate values used
        /// elsewhere in SustainabilityEngine. Values in kgCO₂e per kg of
        /// element mass — but here keyed by category so we can apply a
        /// flat per-element factor without traversing the material take-
        /// off (which the production SustainabilityEngine handles in a
        /// separate, slower path).</summary>
        public static Dictionary<string, double> LoadCarbonFactors()
        {
            if (_carbonFactors != null) return _carbonFactors;
            _carbonFactors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Walls"]                = 250,   // concrete-block average
                ["Floors"]               = 280,
                ["Roofs"]                = 220,
                ["Structural Columns"]   = 700,   // steel/RC weighted
                ["Structural Framing"]   = 700,
                ["Structural Foundation"] = 350,
                ["Doors"]                = 80,
                ["Windows"]              = 90,
                ["Curtain Wall Panels"]  = 110,
                ["Mechanical Equipment"] = 350,
                ["Electrical Equipment"] = 300,
                ["Plumbing Fixtures"]    = 150,
                ["Lighting Fixtures"]    = 60,
                ["Ducts"]                = 180,
                ["Pipes"]                = 140,
                ["Conduits"]             = 110,
                ["Cable Trays"]          = 130,
                ["Furniture"]            = 70,
                ["Generic Models"]       = 150,
            };
            return _carbonFactors;
        }

        // ── Public roll-up ───────────────────────────────────────────────
        public static List<OptionRollupRow> Build(Document doc)
        {
            var rows = new List<OptionRollupRow>();
            if (doc == null) return rows;

            var costs = LoadCostRates();
            var carb = LoadCarbonFactors();
            var sets = DesignOptionRegistry.Snapshot(doc);

            // Main-model baseline first
            rows.Add(BuildRow(doc, null, DesignOptionParams.MAIN_MODEL_LABEL,
                              "Main Model", isPrimary: true, costs, carb));

            foreach (var s in sets)
            foreach (var o in s.Options)
            {
                rows.Add(BuildRow(doc, o.OptionId, s.Name, o.Name, o.IsPrimary, costs, carb));
            }
            return rows;
        }

        private static OptionRollupRow BuildRow(
            Document doc, ElementId optionId, string setName, string optionName,
            bool isPrimary,
            Dictionary<string, double> costs, Dictionary<string, double> carb)
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
                    if (area > 0) row.TotalAreaM2 += area * 0.092903; // ft² → m²
                    if (vol  > 0) row.TotalVolumeM3 += vol * 0.0283168; // ft³ → m³

                    double rate = costs != null && costs.TryGetValue(cat, out double r) ? r : 0.0;
                    double cost = rate * Math.Max(area * 0.092903, 0);
                    if (cost <= 0 && rate > 0) cost = rate;     // per-unit fallback
                    row.TotalCost += cost;
                    if (cost > 0)
                    {
                        row.CostByCategory.TryGetValue(cat, out double accC);
                        row.CostByCategory[cat] = accC + cost;
                    }

                    double cf = carb != null && carb.TryGetValue(cat, out double f) ? f : 0.0;
                    double kg = cf * Math.Max(vol * 0.0283168, 0) * 2300; // density approx kg/m³
                    if (kg <= 0 && cf > 0) kg = cf;
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
