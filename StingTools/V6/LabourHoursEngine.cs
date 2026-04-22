using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>N-G12: labour takeoff. Populates CST_INSTALL_HRS / CST_LABOUR_CREW_TXT / CST_LABOUR_RATE_GBP.</summary>
    public static class LabourHoursEngine
    {
        public class Rate
        {
            public string Category { get; set; }
            public string FamilyFilter { get; set; }
            public string Unit { get; set; }          // EA / LF / SF / CF
            public double HoursPerUnit { get; set; }
            public string Crew { get; set; }
            public double GbpPerHour { get; set; }
        }

        static List<Rate> _cached;

        public static List<Rate> LoadRates()
        {
            if (_cached != null) return _cached;
            var list = new List<Rate>();
            string path = Path.Combine(StingToolsApp.DataPath ?? "data", "Labour", "STING_LABOUR_RATES.csv");
            if (!File.Exists(path))
            {
                StingLog.Warn($"LabourHoursEngine: rates CSV not found at {path}");
                return _cached = list;
            }
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                var cols = StingToolsApp.ParseCsvLine(raw);
                if (cols == null || cols.Length < 6) continue;
                if (!double.TryParse(cols[3], out var hpu)) continue;
                double gph = 0; double.TryParse(cols[5], out gph);
                list.Add(new Rate
                {
                    Category = cols[0].Trim(),
                    FamilyFilter = cols[1].Trim(),
                    Unit = cols[2].Trim().ToUpperInvariant(),
                    HoursPerUnit = hpu, Crew = cols[4].Trim(), GbpPerHour = gph,
                });
            }
            return _cached = list;
        }

        public static Rate Resolve(List<Rate> rates, Element el)
        {
            if (el?.Category == null) return null;
            string catName = el.Category.Name;
            string familyName = "";
            if (el is FamilyInstance fi) familyName = fi.Symbol?.FamilyName ?? "";
            Rate best = null;
            foreach (var r in rates)
            {
                if (!string.Equals(r.Category, catName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(r.FamilyFilter))
                {
                    if (!string.IsNullOrEmpty(familyName) &&
                        familyName.IndexOf(r.FamilyFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        return r;
                    continue;
                }
                if (best == null) best = r;
            }
            return best;
        }

        /// <summary>Quantity in the rate's unit. LF/SF use Revit internal feet.</summary>
        public static double Quantity(Element el, string unit)
        {
            unit = (unit ?? "EA").ToUpperInvariant();
            if (unit == "EA") return 1.0;
            if (unit == "LF")
                return (el.Location is LocationCurve lc && lc.Curve != null) ? lc.Curve.Length : 1.0;
            if (unit == "SF")
            {
                var p = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                return (p != null && p.HasValue) ? p.AsDouble() : 1.0;
            }
            if (unit == "CF")
            {
                var p = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                return (p != null && p.HasValue) ? p.AsDouble() : 1.0;
            }
            return 1.0;
        }

        public class ApplyResult
        {
            public int ElementsTouched { get; set; }
            public double TotalHours { get; set; }
            public double TotalCostGbp { get; set; }
            public Dictionary<string, (int count, double hours)> ByCrew { get; } = new();
        }

        public static ApplyResult Apply(Document doc, IList<Element> elements)
        {
            var rates = LoadRates();
            var res = new ApplyResult();
            foreach (var el in elements)
            {
                var rate = Resolve(rates, el);
                if (rate == null) continue;
                double qty = Quantity(el, rate.Unit);
                double hrs = Math.Round(qty * rate.HoursPerUnit, 2);
                double cost = Math.Round(hrs * rate.GbpPerHour, 2);
                ParameterHelpers.SetString(el, ParamRegistry.CST_INSTALL_HRS, hrs.ToString("0.00"), overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.CST_LABOUR_CREW_TXT, rate.Crew, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.CST_LABOUR_RATE_GBP, rate.GbpPerHour.ToString("0.00"), overwrite: true);
                res.ElementsTouched++;
                res.TotalHours += hrs;
                res.TotalCostGbp += cost;
                if (!res.ByCrew.TryGetValue(rate.Crew, out var v)) v = (0, 0);
                res.ByCrew[rate.Crew] = (v.count + 1, v.hours + hrs);
            }
            return res;
        }
    }
}
