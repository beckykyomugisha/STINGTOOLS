// StingTools — AEC/FM Filter Factory, Revit-free decisions
//
// The parts of AecFilterFactory that can be wrong without Revit, kept here so
// they are unit-testable (AecFilterRuleLogicTests):
//
//   * DTW-171 — a compound AND that loses a child is a BROADER filter (it
//     matches more than the definition says). The factory used to drop the
//     failed child and build the rest, so a filter meant to highlight
//     "interior AND non-bearing" walls highlighted every interior wall. An AND
//     with a failed child now fails the whole filter; an OR with a failed child
//     is narrower, so the child is dropped with a warning. And a value that
//     does not parse as the rule's number type is refused — it used to become 0.
//   * DTW-166 — "None" as a phase / level value means "no element" — Revit's
//     InvalidElementId — not a Phase named "None".
//   * DTW-167 — a definition hash, stamped on the filter, so a corrected
//     definition reaches projects whose filter was minted before the fix.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    internal static class AecFilterRuleLogic
    {
        /// <summary>A phase / level value that means "none" (InvalidElementId).</summary>
        internal static bool IsNoneValue(string value)
        {
            if (value == null) return true;
            var v = value.Trim();
            return v.Length == 0
                || string.Equals(v, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "<None>", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Strict invariant integer parse. A YesNo rule may also say true / false.</summary>
        internal static bool TryParseInt(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) { result = 1; return true; }
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) { result = 0; return true; }
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>Strict invariant number parse.</summary>
        internal static bool TryParseDouble(string value, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                   && !double.IsNaN(result) && !double.IsInfinity(result);
        }

        internal static bool IsOr(string logic) => string.Equals(logic, "or", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Combine built children of a compound rule. <paramref name="children"/>
        /// holds null for a child that could not be built. Returns null when the
        /// compound must fail (an AND lost a child, or nothing survived).
        /// </summary>
        internal static List<T> Combine<T>(string logic, IList<T> children, List<string> warnings) where T : class
        {
            if (children == null || children.Count == 0) return null;
            int failed = children.Count(c => c == null);
            if (failed == 0) return children.ToList();

            if (!IsOr(logic))
            {
                warnings?.Add($"{failed} of {children.Count} AND condition(s) could not be built — the filter is refused, "
                    + "since dropping a condition would match more than the definition says.");
                return null;
            }
            var kept = children.Where(c => c != null).ToList();
            if (kept.Count == 0)
            {
                warnings?.Add("No OR condition could be built — the filter is refused.");
                return null;
            }
            warnings?.Add($"{failed} of {children.Count} OR condition(s) could not be built and were left out — "
                + "the filter matches less than the definition says.");
            return kept;
        }

        /// <summary>
        /// Hash of what decides which elements a filter matches: its categories (in
        /// order-independent form) and its rule tree. Overrides, notes and tags are
        /// not part of it — they live on the view, not on the filter element.
        /// </summary>
        internal static string DefinitionHash(IEnumerable<string> categories, AecFilterRule rule)
        {
            var cats = (categories ?? Enumerable.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var json = JsonConvert.SerializeObject(new { c = cats, r = rule }, Formatting.None);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
