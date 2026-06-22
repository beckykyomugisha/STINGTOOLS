using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    /// <summary>
    /// Glob/alternation-aware matcher for PROD <c>FAMILY_PATTERN</c> rules
    /// (STING_PROD_CODES.csv / project prod_codes.csv).
    ///
    /// Pure, Revit-free logic so it can be unit-tested in StingTools.Tags.Tests.
    /// The historic matcher used a plain <c>String.Contains</c>, which silently
    /// never matched the shipped glob rows (e.g. <c>*Air Handling*</c>) because
    /// the literal '*' is not present in a family name. This restores the
    /// intended behaviour while staying backward-compatible with bare substrings.
    ///
    /// Each pattern is parsed ONCE and cached: bare alternatives become a fast
    /// substring check; wildcard alternatives become a single compiled, anchored
    /// Regex. A tagging batch resolves the same handful of patterns against
    /// thousands of elements, so caching avoids recompiling a Regex per element.
    ///
    /// Supported pattern forms (all matched against an already-upper-cased
    /// family+type name):
    ///   • plain substring            "AIR HANDLING"            → Contains
    ///   • leading/trailing wildcard  "*FCU*" / "FCU*" / "*FCU" → glob
    ///   • embedded wildcard          "VRV*UNIT"                → glob
    ///   • alternation                "*SPLIT*|*PACKAGED*"      → any branch wins
    /// </summary>
    public static class ProdPatternMatcher
    {
        // One parsed alternative: either a bare substring (Sub) or a compiled glob (Rx).
        private sealed class Alt { public string Sub; public Regex Rx; }

        // Keyed by the raw (upper-cased) pattern string; thread-safe + persists for the
        // session. Cleared via Reset() when the rule sets are reloaded.
        private static readonly ConcurrentDictionary<string, Alt[]> _cache =
            new ConcurrentDictionary<string, Alt[]>();

        /// <param name="nameUpper">Upper-cased family + type name of the element.</param>
        /// <param name="patternUpper">Upper-cased FAMILY_PATTERN cell from the CSV.</param>
        public static bool Matches(string nameUpper, string patternUpper)
        {
            if (string.IsNullOrEmpty(nameUpper) || string.IsNullOrEmpty(patternUpper))
                return false;

            Alt[] alts = _cache.GetOrAdd(patternUpper, Parse);
            for (int i = 0; i < alts.Length; i++)
            {
                Alt a = alts[i];
                if (a.Sub != null) { if (nameUpper.Contains(a.Sub)) return true; }
                else if (a.Rx != null && a.Rx.IsMatch(nameUpper)) return true;
            }
            return false;
        }

        /// <summary>Drop the compiled-pattern cache (call on rule-set reload).</summary>
        public static void Reset() => _cache.Clear();

        private static Alt[] Parse(string patternUpper)
        {
            var list = new List<Alt>();
            foreach (string altRaw in patternUpper.Split('|'))
            {
                string alt = altRaw.Trim();
                if (alt.Length == 0) continue;

                if (alt.IndexOf('*') < 0)
                {
                    list.Add(new Alt { Sub = alt }); // bare substring — fast path
                    continue;
                }

                string rx = "^" + Regex.Escape(alt).Replace("\\*", ".*") + "$";
                try
                {
                    list.Add(new Alt { Rx = new Regex(rx, RegexOptions.Compiled | RegexOptions.CultureInvariant) });
                }
                catch { /* malformed glob — skip this alternative */ }
            }
            return list.ToArray();
        }
    }
}
