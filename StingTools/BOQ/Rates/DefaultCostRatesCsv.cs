// ══════════════════════════════════════════════════════════════════════════
//  DefaultCostRatesCsv.cs — the parser for STING_DEFAULT_COST_RATES.csv (DSCH-34).
//
//  The file is the USD benchmark baseline (DefaultRateProvider, priority 60)
//  AND the one place that says which Revit categories are never bill items.
//  A rate cell holds one of:
//
//    a number > 0     a benchmark rate in USD per Unit.
//    NOT MEASURED     the category is never measured: rooms, areas, analytical
//                     elements, loads, annotation, links, containers, massing.
//                     The takeoff drops its elements before they reach the
//                     bill, and the health score does not count them as
//                     unpriced. It is a measurement decision, not a rate —
//                     NRM2 measures the work, and these objects are not work
//                     (or, for Parts / Assemblies / Model Groups, are already
//                     measured through their members or host).
//
//  A measurable category with no benchmark has NO ROW: it stays unpriced and
//  is flagged at risk in the bill, which is the tender query it should be.
//  A bare 0 is refused (reported, row skipped), as in cost_rates_5d.csv: it
//  used to stand for all three meanings at once.
//
//  Revit-free; tested in StingTools.Boq.Tests (DefaultCostRatesCsvTests).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using StingTools.Core;

namespace StingTools.BOQ.Rates
{
    public sealed class DefaultCostRatesTable
    {
        /// <summary>Category → (USD rate per unit, unit, description). Positive rates only.</summary>
        public Dictionary<string, (double ratePerUnit, string unit, string description)> Rates { get; }
            = new Dictionary<string, (double, string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Categories declared NOT MEASURED — excluded from takeoff.</summary>
        public HashSet<string> NotMeasured { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Header columns the reader needs and the file lacks. Non-empty = nothing was read.</summary>
        public List<string> MissingColumns { get; } = new List<string>();

        /// <summary>Row-level problems (bare 0, unreadable rate, duplicate category).</summary>
        public List<string> Problems { get; } = new List<string>();
    }

    public static class DefaultCostRatesCsv
    {
        /// <summary>The rate-cell declaration for a category that is never a bill item.</summary>
        public const string NotMeasuredToken = "NOT MEASURED";

        public static bool IsNotMeasured(string rateCell)
            => string.Equals((rateCell ?? "").Trim(), NotMeasuredToken, StringComparison.OrdinalIgnoreCase);

        public static DefaultCostRatesTable Parse(IEnumerable<string> lines, Func<string, string[]> splitLine)
        {
            var result = new DefaultCostRatesTable();
            var t = CsvTable.Parse(lines, splitLine);
            result.MissingColumns.AddRange(t.Missing("Category", "RatePerUnit_USD", "Unit"));
            if (result.MissingColumns.Count > 0) return result;

            int descCol = t.Col("Description");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in t.Rows)
            {
                string cat = (row["Category"] ?? "").Trim();
                if (cat.Length == 0) continue;
                if (!seen.Add(cat))
                {
                    result.Problems.Add($"line {row.Line}: duplicate category '{cat}' — first row kept");
                    continue;
                }
                string cell = (row["RatePerUnit_USD"] ?? "").Trim();
                if (IsNotMeasured(cell)) { result.NotMeasured.Add(cat); continue; }

                if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
                {
                    result.Problems.Add($"line {row.Line}: '{cat}' rate '{cell}' is not a number or {NotMeasuredToken} — row skipped");
                    continue;
                }
                if (rate <= 0)
                {
                    result.Problems.Add($"line {row.Line}: '{cat}' rate {cell} is not a rate — write {NotMeasuredToken} "
                        + "for a category that is never billed, or delete the row to leave it unpriced");
                    continue;
                }
                string unit = (row["Unit"] ?? "").Trim();
                string desc = descCol >= 0 ? (row["Description"] ?? "") : "";
                result.Rates[cat] = (rate, unit, desc.Length > 0 ? desc : cat);
            }
            return result;
        }
    }
}
