// NumericTextMirrorRule - which numeric parameter pairs with which TEXT twin.
//
// Split from NumericTextMirror so this half has no dependency on StingToolsApp
// or the Revit API and can be compiled into StingTools.Tags.Tests. The pairing
// is the part worth testing; reading a file and writing a parameter is not.
//
// WHY ANY OF IT EXISTS
//
// Revit has no number-to-string conversion in family formulas, so a label's
// Text calculated value cannot reference a NUMBER, LENGTH or YESNO parameter -
// `if(BOOL, <NUMBER>, "")` has two differently-typed branches and is rejected
// as "Inconsistent Units". Measured 2026-09-22 while hand-building the LPS
// master: twelve rows across both build sheets could not be entered at all.
//
// The library already answers this with an _NR / _TXT pairing. The twins were
// defined and nothing ever filled them, so a label pointed at one would have
// rendered blank on every element forever, with "the data is missing" as the
// obvious explanation.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    /// <summary>The numeric/TEXT pairing rule. Revit-free.</summary>
    public static class NumericTextMirrorRule
    {
        /// <summary>Datatypes that cannot appear in a Text label formula.</summary>
        public static readonly HashSet<string> NonText = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        { "NUMBER", "INTEGER", "LENGTH", "AREA", "VOLUME", "ANGLE", "YESNO", "CURRENCY" };

        // Unit suffixes a twin may drop. Both spellings are in use:
        // ELC_LPS_PROTECTION_ANGLE_DEG pairs with ..._ANGLE_TXT, while
        // ELC_LPS_INSPECTION_INTERVAL_MONTHS pairs with ..._MONTHS_TXT. Trying
        // only one reported the other as having no twin, which would have
        // silently dropped a real label row.
        private static readonly Regex UnitSuffix = new Regex(
            @"_(NR|BOOL|MM2|MM|DEG|OHM|MONTHS|YRS|KG|PCT|M)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Twin names to try, in order. Unit-kept before unit-dropped.</summary>
        public static IEnumerable<string> Candidates(string numericName)
        {
            if (string.IsNullOrEmpty(numericName)) yield break;
            yield return numericName + "_TXT";
            string stem = UnitSuffix.Replace(numericName, "");
            if (!string.Equals(stem, numericName, StringComparison.Ordinal))
                yield return stem + "_TXT";
        }

        /// <summary>
        /// numeric parameter name to its TEXT twin, for every pair present in
        /// the supplied definitions.
        /// </summary>
        public static Dictionary<string, string> Pairs(
            IEnumerable<KeyValuePair<string, string>> definitions)
        {
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in definitions ?? Enumerable.Empty<KeyValuePair<string, string>>())
                if (!string.IsNullOrEmpty(d.Key) && !types.ContainsKey(d.Key))
                    types[d.Key] = d.Value ?? "";

            // Claim strength, so two numerics cannot fight over one twin.
            //
            // ASS_WEIGHT_KG and ASS_WEIGHT_KG_NR both reach ASS_WEIGHT_KG_TXT:
            // the first directly (name + _TXT), the second only after stripping
            // _NR. Without a rule the winner depends on dictionary order, and
            // the tag shows a different quantity depending on which parameter
            // the loop happened to reach last.
            //
            // The DIRECT claim wins. A twin named for exactly this parameter
            // belongs to it; one reached by removing a suffix is a guess, and a
            // guess should not displace a certainty.
            var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
            var claimedBy = new Dictionary<string, (string Numeric, int Rank)>(StringComparer.Ordinal);

            foreach (var kv in types.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!NonText.Contains(kv.Value)) continue;

                int rank = 0;
                foreach (string twin in Candidates(kv.Key))
                {
                    string t;
                    if (types.TryGetValue(twin, out t) &&
                        string.Equals(t, "TEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        (string Numeric, int Rank) held;
                        if (claimedBy.TryGetValue(twin, out held))
                        {
                            // Already spoken for by an equal or stronger claim.
                            if (held.Rank <= rank) break;
                            pairs.Remove(held.Numeric);
                        }
                        claimedBy[twin] = (kv.Key, rank);
                        pairs[kv.Key] = twin;
                        break;
                    }
                    rank++;
                }
            }
            return pairs;
        }
    }
}
