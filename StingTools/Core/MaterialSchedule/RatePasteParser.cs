// ══════════════════════════════════════════════════════════════════════════
//  RatePasteParser.cs — turn a column copied out of Excel into rates.
//
//  This is the reason the editor is worth having rather than a form. A
//  supplier quote arrives as a spreadsheet; pricing 58 commodities by typing
//  58 numbers is the work the grid was supposed to remove, not preserve.
//
//  Three decisions here are load-bearing, and each is a way a paste could go
//  wrong in silence:
//
//  * A BLANK line consumes a row without changing it. Copy a column with gaps
//    and the gaps must stay aligned with the rows they came from — skipping
//    them instead would shift every rate below the gap onto the wrong
//    commodity, and every one of those would look like a deliberate figure.
//
//  * A line with NO number is reported, never treated as zero. A pasted
//    header row ("Rate") or a "TBC" is a thing the user must see, not a free
//    commodity.
//
//  * Multi-column paste takes the LAST numeric field, because Excel columns
//    are copied left-to-right and the rate is conventionally rightmost — and
//    when that is wrong the user sees a wrong number in a cell they can fix,
//    rather than a silently skipped row they cannot notice.
//
//  Revit-free, so all of that is testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One pasted cell: a value, or a reason there isn't one.</summary>
    public sealed class PastedRate
    {
        /// <summary>The parsed rate, or NULL for a blank line (leave the row alone).</summary>
        public double? Value;

        /// <summary>The original text, for the report when it did not parse.</summary>
        public string RawText = "";

        /// <summary>True when the line held text but no number.</summary>
        public bool Unparseable;
    }

    public sealed class RatePasteResult
    {
        public readonly List<PastedRate> Cells = new List<PastedRate>();

        /// <summary>Lines that held text but no number, verbatim, for the report.</summary>
        public readonly List<string> Rejected = new List<string>();

        public int ValueCount => Cells.Count(c => c.Value.HasValue);
        public int BlankCount => Cells.Count(c => !c.Value.HasValue && !c.Unparseable);

        /// <summary>
        /// What the paste did, or NULL when there was nothing on the clipboard
        /// worth reporting.
        /// </summary>
        public string Summary(int applied, int ranOutOfRows)
        {
            if (Cells.Count == 0) return null;

            string s = $"Pasted {applied} rate(s).";
            if (BlankCount > 0)
                s += $" {BlankCount} blank line(s) left their row unchanged — a gap in a copied "
                   + "column keeps its place, so the rates below it stay on the right commodities.";
            if (Rejected.Count > 0)
                s += $" {Rejected.Count} line(s) held no number and were NOT applied: "
                   + string.Join(", ", Rejected.Take(5).Select(r => "'" + r + "'"))
                   + (Rejected.Count > 5 ? ", …" : "")
                   + ". A line with no number is not a rate of zero.";
            if (ranOutOfRows > 0)
                s += $" {ranOutOfRows} value(s) had no row left to land on and were discarded — "
                   + "the paste started too far down the grid.";
            return s;
        }
    }

    public static class RatePasteParser
    {
        /// <summary>
        /// Parse clipboard text into one cell per line.
        ///
        /// Handles what Excel actually puts on the clipboard: CRLF line ends, a
        /// trailing newline, tab-separated columns, thousands separators, a
        /// currency prefix, and parentheses for negatives (which are rejected
        /// downstream as negative, not silently made positive).
        /// </summary>
        public static RatePasteResult Parse(string clipboardText)
        {
            var result = new RatePasteResult();
            if (string.IsNullOrEmpty(clipboardText)) return result;

            string[] lines = clipboardText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // Excel appends a trailing newline to a copied range. Dropping only
            // the LAST empty line keeps interior blanks, which carry meaning.
            int end = lines.Length;
            if (end > 0 && lines[end - 1].Trim().Length == 0) end--;

            for (int i = 0; i < end; i++)
            {
                string line = lines[i];
                var cell = new PastedRate { RawText = line.Trim() };

                if (cell.RawText.Length == 0) { result.Cells.Add(cell); continue; }

                double? v = LastNumberIn(line);
                if (v.HasValue) cell.Value = v;
                else
                {
                    cell.Unparseable = true;
                    result.Rejected.Add(cell.RawText.Length > 40
                        ? cell.RawText.Substring(0, 40) + "…" : cell.RawText);
                }
                result.Cells.Add(cell);
            }
            return result;
        }

        /// <summary>
        /// The last tab-separated field that parses as a number.
        ///
        /// Last rather than first: a two-column copy is conventionally
        /// description-then-rate, and a wrong number in a visible cell is a
        /// better failure than a skipped row nobody notices.
        /// </summary>
        private static double? LastNumberIn(string line)
        {
            var fields = (line ?? "").Split('\t');
            for (int i = fields.Length - 1; i >= 0; i--)
            {
                double? v = ParseNumber(fields[i]);
                if (v.HasValue) return v;
            }
            return null;
        }

        /// <summary>Parse one field, or null. Never throws, never guesses zero.</summary>
        public static double? ParseNumber(string field)
        {
            string t = (field ?? "").Trim();
            if (t.Length == 0) return null;

            bool parenNegative = t.StartsWith("(") && t.EndsWith(")");
            if (parenNegative) t = t.Substring(1, t.Length - 2).Trim();

            // Strip currency and separators. Everything removed here is
            // decoration; anything left that is not a number stays, so a value
            // like "28k" is REJECTED rather than read as 28.
            t = t.Replace("UGX", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("USh", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("$", "").Replace("£", "").Replace("€", "")
                 .Replace(",", "").Replace(" ", "").Replace(" ", "");
            if (t.Length == 0) return null;

            if (!double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)
             && !double.TryParse(t, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
                return null;

            return parenNegative ? -d : d;
        }
    }
}
