// TmvTestImport — reads TMV commissioning / in-service test results (Revit-free).
//
// DSCH-46. TMV readings used to reach the model only by typing them into each element's
// properties: TMVEngine.WriteTMVData existed but nothing called it. The register CSV that
// Plumb_TMVEngine writes (TMV_Register.csv) is the input form: the tester fills
// Outlet_C (mixed water at the outlet, HTM 04-01 Supplement D 08 §11.1.2.2 / §11.2.2.1),
// TestDate and, if measured, InletHot_C / InletCold_C, and Plumb_TMVImportTests writes
// them back through WriteTMVData.
//
// Nothing is guessed. A row with no outlet reading is skipped (not measured, never 0);
// an unreadable number, an implausible temperature, a missing or non-ISO date, a date in
// the future, or an element id that appears twice is refused with the line number, and
// the rest of the file still imports.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StingTools.Core.Plumbing
{
    public sealed class TmvTestRow
    {
        public int     Line       { get; set; }
        public long    ElementId  { get; set; }
        /// <summary>Mixed water temperature measured at the outlet, °C.</summary>
        public double  OutletC    { get; set; }
        /// <summary>Hot supply at the valve inlet, °C; null = not recorded (not written).</summary>
        public double? InletHotC  { get; set; }
        /// <summary>Cold supply at the valve inlet, °C; null = not recorded (not written).</summary>
        public double? InletColdC { get; set; }
        /// <summary>Test date, yyyy-MM-dd.</summary>
        public string  TestDate   { get; set; } = "";
    }

    public sealed class TmvTestImportResult
    {
        public List<TmvTestRow> Rows { get; } = new List<TmvTestRow>();
        public List<string> Errors { get; } = new List<string>();
        /// <summary>Rows with no outlet reading (left untouched in the model).</summary>
        public int NotMeasured { get; set; }
    }

    public static class TmvTestImport
    {
        public const string ColElementId = "ElementId";
        public const string ColOutlet    = "Outlet_C";
        public const string ColTestDate  = "TestDate";
        public const string ColInletHot  = "InletHot_C";
        public const string ColInletCold = "InletCold_C";
        public const string DateFormat   = "yyyy-MM-dd";

        // Plausibility bounds for a water temperature reading, °C. A value outside them is a
        // typing or unit error (Fahrenheit, a missing decimal point), not a measurement.
        public const double MinPlausibleC = 1;
        public const double MaxPlausibleC = 99;

        /// <summary>Parses register CSV text. <paramref name="today"/> rejects future dates.</summary>
        public static TmvTestImportResult Parse(string csvText, DateTime today)
        {
            var res = new TmvTestImportResult();
            if (string.IsNullOrWhiteSpace(csvText)) { res.Errors.Add("the file is empty"); return res; }

            var lines = csvText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int headerIdx = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
            var header = SplitCsvLine(lines[headerIdx]).Select(h => h.Trim().TrimStart('﻿')).ToList();
            int Col(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            int cId = Col(ColElementId), cOut = Col(ColOutlet), cDate = Col(ColTestDate), cHot = Col(ColInletHot), cCold = Col(ColInletCold);
            var missing = new[] { (ColElementId, cId), (ColOutlet, cOut), (ColTestDate, cDate) }
                .Where(x => x.Item2 < 0).Select(x => x.Item1).ToList();
            if (missing.Count > 0)
            {
                res.Errors.Add($"missing column(s) {string.Join(", ", missing)} — use the TMV_Register.csv written by Plumb_TMVEngine");
                return res;
            }

            var seen = new HashSet<long>();
            var duplicates = new HashSet<long>();
            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                int lineNo = i + 1;
                var f = SplitCsvLine(lines[i]);
                string Cell(int c) => c >= 0 && c < f.Count ? f[c].Trim() : "";

                if (!long.TryParse(Cell(cId), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) || id <= 0)
                { res.Errors.Add($"line {lineNo}: ElementId '{Cell(cId)}' is not an element id"); continue; }

                string outTxt = Cell(cOut);
                if (outTxt == "") { res.NotMeasured++; continue; }
                if (!TryTemp(outTxt, out double outC, out string why))
                { res.Errors.Add($"line {lineNo} (element {id}): Outlet_C {why}"); continue; }
                if (outC <= 0) { res.NotMeasured++; continue; }   // older registers wrote 0.0 for "not measured"

                string dateTxt = Cell(cDate);
                if (!DateTime.TryParseExact(dateTxt, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                { res.Errors.Add($"line {lineNo} (element {id}): TestDate '{dateTxt}' is not a {DateFormat} date — a reading needs the date it was taken"); continue; }
                if (date.Date > today.Date)
                { res.Errors.Add($"line {lineNo} (element {id}): TestDate {dateTxt} is in the future"); continue; }

                if (!TryOptionalTemp(Cell(cHot), out double? hot, out string whyHot))
                { res.Errors.Add($"line {lineNo} (element {id}): InletHot_C {whyHot}"); continue; }
                if (!TryOptionalTemp(Cell(cCold), out double? cold, out string whyCold))
                { res.Errors.Add($"line {lineNo} (element {id}): InletCold_C {whyCold}"); continue; }

                if (!seen.Add(id)) { duplicates.Add(id); continue; }
                res.Rows.Add(new TmvTestRow
                {
                    Line = lineNo, ElementId = id, OutletC = outC, InletHotC = hot, InletColdC = cold,
                    TestDate = date.ToString(DateFormat, CultureInfo.InvariantCulture)
                });
            }

            // An element listed twice has two readings: neither is taken as the result.
            foreach (var id in duplicates)
            {
                res.Rows.RemoveAll(r => r.ElementId == id);
                res.Errors.Add($"element {id} appears more than once — none of its rows imported");
            }
            return res;
        }

        private static bool TryTemp(string s, out double v, out string why)
        {
            why = "";
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            { why = $"'{s}' is not a number"; return false; }
            if (v <= 0) return true;   // "not measured"; the caller decides
            if (v < MinPlausibleC || v > MaxPlausibleC)
            { why = $"{v:0.#} °C is not a plausible water temperature"; return false; }
            return true;
        }

        private static bool TryOptionalTemp(string s, out double? v, out string why)
        {
            v = null; why = "";
            if (s == "") return true;
            if (!TryTemp(s, out double d, out why)) return false;
            if (d > 0) v = d;
            return true;
        }

        /// <summary>RFC 4180 split of one line (quoted fields, doubled quotes).</summary>
        public static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (ch == '"') q = false;
                    else sb.Append(ch);
                }
                else if (ch == '"') q = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
