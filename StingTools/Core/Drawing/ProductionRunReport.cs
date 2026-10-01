// StingTools — Drawing Template Manager · what a production run tells the user
//
// DTW-195 / DTW-204 / DTW-205. The words a batch run reports when an item is skipped
// because someone else owns it, when the user stops the run, and the full warning list
// a dialog cannot show. Revit-free (StingTools.Tags.Tests compiles this file), so the
// wording a user acts on is tested rather than discovered on a stalled preset.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core.Drawing
{
    public static class ProductionRunReport
    {
        /// <summary>How many warnings a result dialog lists before pointing at the CSV.</summary>
        public const int DialogWarningLimit = 20;

        /// <summary>
        /// Why an item was not produced in a workshared model, or null when nothing blocks
        /// it. <paramref name="ownedByOthers"/> is element label → owner;
        /// <paramref name="outOfDate"/> are elements changed in central since the last
        /// reload; <paramref name="notObtained"/> are elements whose editing permission
        /// could not be borrowed. Owners are grouped so ten views held by one person read
        /// as one line.
        /// </summary>
        public static string BlockReason(IEnumerable<KeyValuePair<string, string>> ownedByOthers,
            IEnumerable<string> outOfDate, IEnumerable<string> notObtained)
        {
            var parts = new List<string>();
            foreach (var g in (ownedByOthers ?? Enumerable.Empty<KeyValuePair<string, string>>())
                         .GroupBy(kv => string.IsNullOrWhiteSpace(kv.Value) ? "another user" : kv.Value.Trim(),
                                  StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                parts.Add($"owned by {g.Key} ({Names(g.Select(kv => kv.Key))})");
            var stale = (outOfDate ?? Enumerable.Empty<string>()).ToList();
            if (stale.Count > 0) parts.Add($"not up to date ({Names(stale)}) — reload latest");
            var missing = (notObtained ?? Enumerable.Empty<string>()).ToList();
            if (missing.Count > 0) parts.Add($"editing permission not granted ({Names(missing)})");
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        private static string Names(IEnumerable<string> names)
        {
            var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return "an element";
            var shown = string.Join(", ", list.Take(4).Select(n => "'" + n + "'"));
            return list.Count > 4 ? shown + $" and {list.Count - 4} more" : shown;
        }

        /// <summary>The skip line for one item: "Level 2: skipped — owned by …".</summary>
        public static string Skipped(string item, string reason)
            => $"{(string.IsNullOrWhiteSpace(item) ? "item" : item)}: skipped — {reason}. Nothing of it was changed.";

        /// <summary>DTW-204: what a run stopped with Escape says.</summary>
        public static string Stopped(int done, int total, string unit)
            => $"Stopped (Escape) after {done} of {total} {(string.IsNullOrWhiteSpace(unit) ? "item(s)" : unit)}; "
             + "what was done before the stop is kept.";

        /// <summary>
        /// DTW-205: every warning, one row each, as CSV (header "No,Warning"). The dialog
        /// shows <see cref="DialogWarningLimit"/>; nothing past them may be lost.
        /// </summary>
        public static string Csv(IEnumerable<string> warnings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("No,Warning");
            int i = 0;
            foreach (var w in warnings ?? Enumerable.Empty<string>())
                sb.AppendLine($"{++i},{Quote(w)}");
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// DTW-205: the warning block of a result dialog — the first
        /// <see cref="DialogWarningLimit"/>, then where the rest are. <paramref name="csvPath"/>
        /// null means the file could not be written; the log still holds every line.
        /// </summary>
        public static string WarningBlock(IList<string> warnings, string csvPath)
        {
            if (warnings == null || warnings.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"Warnings ({warnings.Count}):");
            foreach (var w in warnings.Take(DialogWarningLimit)) sb.AppendLine("  • " + w);
            if (warnings.Count > DialogWarningLimit)
                sb.AppendLine($"  …and {warnings.Count - DialogWarningLimit} more — every warning is in the STING log"
                              + (csvPath != null ? " and in:\n  " + csvPath : " (the CSV could not be written)."));
            else if (csvPath != null)
                sb.AppendLine("  (also written to " + csvPath + ")");
            return sb.ToString();
        }
    }
}
