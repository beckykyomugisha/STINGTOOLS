// XlsxTemplateRenderer.cs — template engine v1.1 (S07).
//
// ClosedXML-based workbook renderer. Token substitution + row-loop expansion.
// Loop mechanics:
//   - a cell containing {{#loop_name}} marks a loop's first row
//   - a cell containing {{/loop_name}} marks its closing row
//   - intermediate rows are cloned per loop item; tokens within each cloned
//     row are substituted against that item's dictionary
//   - ClosedXML's InsertRowsBelow shifts formulas automatically.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    public static class XlsxTemplateRenderer
    {
        private static readonly Regex TokenRx = new Regex(@"\{\{([^}#/][^}]*)\}\}", RegexOptions.Compiled);
        private static readonly Regex LoopStartRx = new Regex(@"\{\{#(\w+)\}\}",  RegexOptions.Compiled);
        private static readonly Regex LoopEndRx   = new Regex(@"\{\{\/(\w+)\}\}", RegexOptions.Compiled);

        public static void Render(string templatePath, TokenContext ctx, string outputPath)
        {
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException($"Template not found: {templatePath}", templatePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            using var workbook = new XLWorkbook(templatePath);
            var dict = ctx?.AsDictionary() ?? new Dictionary<string, object>();

            foreach (var sheet in workbook.Worksheets)
            {
                ExpandRowLoops(sheet, ctx);
                ReplaceTokensInSheet(sheet, dict);
            }

            workbook.SaveAs(outputPath);
        }

        // ── Loops: expand every {{#loop_name}}…{{/loop_name}} row pair ──
        private static void ExpandRowLoops(IXLWorksheet sheet, TokenContext ctx)
        {
            if (ctx == null) return;
            var loops = ctx.Loops;

            // Scan bottom-up so inserted rows do not invalidate higher indexes.
            var pairs = FindLoopRowPairs(sheet);
            pairs.Reverse();
            foreach (var (start, end, name) in pairs)
            {
                if (!loops.TryGetValue(name, out var items) || items == null || items.Count == 0)
                {
                    // Drop the marker rows entirely.
                    DeleteRows(sheet, start, end);
                    continue;
                }

                int templateRowCount = end - start + 1 - 2; // exclude marker rows
                if (templateRowCount < 0) templateRowCount = 0;

                // Grab the body template rows once (between start+1 and end-1).
                var bodyRows = new List<IXLRangeRow>();
                for (int r = start + 1; r <= end - 1; r++)
                    bodyRows.Add(sheet.Row(r).AsRange().RangeUsed() ?? sheet.Row(r).AsRange());

                // For each item, insert copies of the body rows below the end marker,
                // substituting tokens against the item dictionary.
                int insertAt = end; // insert before the end marker
                foreach (var item in items)
                {
                    sheet.Row(insertAt).InsertRowsAbove(templateRowCount);
                    for (int i = 0; i < templateRowCount; i++)
                    {
                        int srcRow = start + 1 + i + (templateRowCount * items.IndexOf(item));
                        // because we're using a relative template approach, copy from the original template rows
                    }
                }

                // Simpler alternative: rebuild by copying original cell values.
                // Delete the freshly inserted blanks above and re-render cleanly.
                DeleteRows(sheet, end + 1, end + (templateRowCount * items.Count));
                // Capture template cells before deletion so we can re-emit them.
                var capturedRows = new List<List<CellSnapshot>>();
                for (int r = start + 1; r <= end - 1; r++)
                    capturedRows.Add(CaptureRow(sheet, r));

                // Delete original marker rows + body rows.
                DeleteRows(sheet, start, end);

                // Emit expanded rows in place.
                int writeRow = start;
                foreach (var item in items)
                {
                    foreach (var template in capturedRows)
                    {
                        foreach (var cell in template)
                        {
                            string substituted = ApplyItemTokens(cell.ValueText, item, ctx.AsDictionary());
                            var target = sheet.Cell(writeRow, cell.Column);
                            target.Value = substituted;
                            if (cell.HasStyle) target.Style = cell.Style;
                        }
                        writeRow++;
                    }
                }
            }
        }

        private static List<(int start, int end, string name)> FindLoopRowPairs(IXLWorksheet sheet)
        {
            var result = new List<(int start, int end, string name)>();
            var used = sheet.RangeUsed();
            if (used == null) return result;

            int firstRow = used.FirstRow().RowNumber();
            int lastRow  = used.LastRow().RowNumber();

            var openStack = new Stack<(int row, string name)>();
            for (int r = firstRow; r <= lastRow; r++)
            {
                string rowText = string.Join("\n", sheet.Row(r).CellsUsed().Select(c => c.GetString()));
                var startMatch = LoopStartRx.Match(rowText);
                var endMatch   = LoopEndRx.Match(rowText);

                if (startMatch.Success) openStack.Push((r, startMatch.Groups[1].Value));
                if (endMatch.Success && openStack.Count > 0)
                {
                    var opened = openStack.Pop();
                    if (string.Equals(opened.name, endMatch.Groups[1].Value, StringComparison.Ordinal))
                        result.Add((opened.row, r, opened.name));
                }
            }
            return result;
        }

        private static void DeleteRows(IXLWorksheet sheet, int start, int end)
        {
            if (end < start) return;
            for (int r = end; r >= start; r--)
            {
                try { sheet.Row(r).Delete(); } catch { /* already gone */ }
            }
        }

        private readonly struct CellSnapshot
        {
            public CellSnapshot(int col, string text, IXLStyle style, bool hasStyle)
            { Column = col; ValueText = text; Style = style; HasStyle = hasStyle; }
            public int Column { get; }
            public string ValueText { get; }
            public IXLStyle Style { get; }
            public bool HasStyle { get; }
        }

        private static List<CellSnapshot> CaptureRow(IXLWorksheet sheet, int row)
        {
            var list = new List<CellSnapshot>();
            foreach (var c in sheet.Row(row).CellsUsed())
                list.Add(new CellSnapshot(c.Address.ColumnNumber, c.GetString(), c.Style, true));
            return list;
        }

        private static string ApplyItemTokens(string text, IDictionary<string, object> item, Dictionary<string, object> global)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("{{")) return text;
            string result = TokenRx.Replace(text, match =>
            {
                string key = match.Groups[1].Value.Trim();
                if (item != null && item.TryGetValue(key, out var v)) return v?.ToString() ?? "";
                if (global != null && global.TryGetValue(key, out v)) return v?.ToString() ?? "";
                return $"<TOKEN_NOT_FOUND:{key}>";
            });
            return result;
        }

        // ── Plain {{token}} substitution across the sheet ──
        private static void ReplaceTokensInSheet(IXLWorksheet sheet, Dictionary<string, object> dict)
        {
            var used = sheet.RangeUsed();
            if (used == null) return;
            foreach (var cell in used.CellsUsed())
            {
                string value = cell.GetString();
                if (string.IsNullOrEmpty(value) || !value.Contains("{{")) continue;
                string replaced = TokenRx.Replace(value, match =>
                {
                    string key = match.Groups[1].Value.Trim();
                    if (dict.TryGetValue(key, out var v)) return v?.ToString() ?? "";
                    return $"<TOKEN_NOT_FOUND:{key}>";
                });
                if (replaced != value) cell.Value = replaced;
            }
        }
    }
}
