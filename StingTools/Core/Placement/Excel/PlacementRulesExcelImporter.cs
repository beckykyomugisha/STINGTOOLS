// Phase 139 E2 — Placement rules Excel importer.
//
// Reads each non-SCHEMA sheet, maps columns to PlacementRule fields by
// name (case-insensitive), and returns (rules, errors).  Caller
// decides whether to abort on errors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;

namespace StingTools.Core.Placement.Excel
{
    public static class PlacementRulesExcelImporter
    {
        public static (List<PlacementRule> rules, List<string> errors) Import(string filePath)
        {
            var rules  = new List<PlacementRule>();
            var errors = new List<string>();
            if (!File.Exists(filePath))
            {
                errors.Add($"Excel file not found: {filePath}");
                return (rules, errors);
            }
            using (var wb = new XLWorkbook(filePath))
            {
                foreach (var ws in wb.Worksheets)
                {
                    if (string.Equals(ws.Name, "SCHEMA", StringComparison.OrdinalIgnoreCase)) continue;
                    ImportSheet(ws, rules, errors);
                }
            }
            return (rules, errors);
        }

        private static void ImportSheet(IXLWorksheet ws, List<PlacementRule> rules, List<string> errors)
        {
            int firstRow = 1;
            int lastCol  = 0;
            try { lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            if (lastCol == 0) return;

            // Read header row.
            var headers = new Dictionary<int, string>();
            for (int c = 1; c <= lastCol; c++)
            {
                string h = ws.Cell(firstRow, c).GetString().Trim();
                if (!string.IsNullOrEmpty(h)) headers[c] = h;
            }
            if (headers.Count == 0) return;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var t = typeof(PlacementRule);
            for (int r = firstRow + 1; r <= lastRow; r++)
            {
                var rule = new PlacementRule();
                bool anyValue = false;
                foreach (var (col, header) in headers)
                {
                    var prop = t.GetProperty(header, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null) continue;
                    string raw = ws.Cell(r, col).GetString().Trim();
                    if (string.IsNullOrEmpty(raw)) continue;
                    anyValue = true;
                    try { SetProperty(rule, prop, raw); }
                    catch (Exception ex)
                    {
                        errors.Add($"Sheet '{ws.Name}' row {r} column '{header}': {ex.Message}");
                    }
                }
                if (!anyValue) continue;
                if (string.IsNullOrEmpty(rule.CategoryFilter))
                {
                    errors.Add($"Sheet '{ws.Name}' row {r}: missing required CategoryFilter");
                    continue;
                }
                if (string.IsNullOrEmpty(rule.SourcePack))
                    rule.SourcePack = ws.Name;
                rules.Add(rule);
            }
        }

        private static void SetProperty(PlacementRule rule, PropertyInfo prop, string raw)
        {
            var pt = prop.PropertyType;
            if (pt == typeof(string))
            {
                prop.SetValue(rule, raw);
            }
            else if (pt == typeof(bool))
            {
                bool b = raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                      || raw == "1"
                      || raw.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                prop.SetValue(rule, b);
            }
            else if (pt == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    prop.SetValue(rule, n);
            }
            else if (pt == typeof(double))
            {
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    prop.SetValue(rule, d);
            }
            else if (pt == typeof(string[]))
            {
                prop.SetValue(rule, raw.Split('|').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray());
            }
            else if (pt == typeof(List<string>))
            {
                prop.SetValue(rule, raw.Split('|').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList());
            }
            else if (pt.IsEnum)
            {
                if (Enum.TryParse(pt, raw, true, out object enVal))
                    prop.SetValue(rule, enVal);
                else
                    throw new ArgumentException($"value '{raw}' is not a valid {pt.Name}");
            }
        }
    }
}
