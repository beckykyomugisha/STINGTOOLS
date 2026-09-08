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
    ///   • single-char wildcard       "DN2?"                    → glob (? = any one char)
    ///   • character class            "DN20-PN1[06]"            → glob ([…] as in regex)
    ///   • alternation                "*SPLIT*|*PACKAGED*"      → any branch wins
    /// </summary>
    public static class ProdPatternMatcher
    {
        // One parsed alternative: either a bare substring (Sub) or a compiled glob (Rx).
        // Weight is the alternative's LITERAL length — the pattern with the wildcards
        // stripped — and is what makes "most specific wins" computable. See Strength.
        private sealed class Alt { public string Sub; public Regex Rx; public int Weight; }

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

        /// <summary>
        /// How SPECIFICALLY a pattern matches: the literal length of the longest
        /// matching alternative, or -1 when nothing matches. Zero is a legal
        /// strength (a pattern that is nothing but wildcards).
        ///
        /// <para>This exists because STING_PROD_CODES.csv reads like a dictionary of
        /// independent rules and behaves like an ordered chain. Ten shipped rows were
        /// written by authors who expected the specific rule to win and it did not:
        /// <c>*Boiler Feed*</c> sat below <c>*Boiler*</c>, <c>*Fire Damper*</c> below
        /// <c>*Damper*</c>, <c>*Fume Hood*</c> below <c>*Hood*</c>, <c>*Mop Sink*</c>
        /// below <c>*Sink*</c>. Three rows could never fire at all. Nothing errored —
        /// a boiler feed pump simply tagged as a boiler.</para>
        ///
        /// <para>Reordering the file would have fixed those ten and left the eleventh
        /// author to make the same mistake, so the ranking moved here instead. Length
        /// of the LITERAL is the measure: <c>*Fire Damper*</c> (12) beats
        /// <c>*Damper*</c> (6) because it says more about the thing it matched.</para>
        /// </summary>
        public static int Strength(string nameUpper, string patternUpper)
        {
            if (string.IsNullOrEmpty(nameUpper) || string.IsNullOrEmpty(patternUpper))
                return -1;

            int best = -1;
            Alt[] alts = _cache.GetOrAdd(patternUpper, Parse);
            for (int i = 0; i < alts.Length; i++)
            {
                Alt a = alts[i];
                bool hit = a.Sub != null
                    ? nameUpper.Contains(a.Sub)
                    : (a.Rx != null && a.Rx.IsMatch(nameUpper));
                if (hit && a.Weight > best) best = a.Weight;
            }
            return best;
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

                int weight = LiteralLength(alt);

                if (alt.IndexOfAny(GlobChars) < 0)
                {
                    list.Add(new Alt { Sub = alt, Weight = weight }); // bare substring — fast path
                    continue;
                }

                try
                {
                    string rx = GlobToRegex(alt);
                    list.Add(new Alt
                    {
                        Rx = new Regex(rx, RegexOptions.Compiled | RegexOptions.CultureInvariant),
                        Weight = weight,
                    });
                }
                catch { /* malformed glob — skip this alternative */ }
            }
            return list.ToArray();
        }

        private static readonly char[] GlobChars = { '*', '?', '[' };

        /// <summary>
        /// The alternative's literal length: every character that is not a wildcard.
        /// A character class counts as ONE character, because it stands for one —
        /// <c>DN20-PN1[06]</c> says as much about a match as <c>DN20-PN106</c> does.
        /// </summary>
        private static int LiteralLength(string alt)
        {
            int n = 0;
            for (int i = 0; i < alt.Length; i++)
            {
                char c = alt[i];
                if (c == '*') continue;
                if (c == '[')
                {
                    int close = alt.IndexOf(']', i + 1);
                    if (close >= 0) { n++; i = close; continue; }
                }
                n++;   // '?' included: it still occupies a position
            }
            return n;
        }

        /// <summary>
        /// Translate a glob (<c>*</c> = any run, <c>?</c> = any one char,
        /// <c>[…]</c> = regex char class) into an anchored regex. Everything
        /// outside the wildcards is regex-escaped so literal punctuation
        /// (<c>- ( ) / .</c>) in family names matches verbatim.
        /// </summary>
        private static string GlobToRegex(string glob)
        {
            var sb = new System.Text.StringBuilder(glob.Length + 8);
            sb.Append('^');
            for (int i = 0; i < glob.Length; i++)
            {
                char c = glob[i];
                switch (c)
                {
                    case '*': sb.Append(".*"); break;
                    case '?': sb.Append('.'); break;
                    case '[':
                        int close = glob.IndexOf(']', i + 1);
                        if (close < 0) { sb.Append("\\["); break; } // unterminated → literal '['
                        sb.Append('[');
                        int j = i + 1;
                        if (j < close && (glob[j] == '!' || glob[j] == '^')) { sb.Append('^'); j++; }
                        for (; j < close; j++)
                        {
                            char cc = glob[j];
                            if (cc == '\\' || cc == ']' || cc == '^') sb.Append('\\');
                            sb.Append(cc);
                        }
                        sb.Append(']');
                        i = close; // resume after ']'
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }
            sb.Append('$');
            return sb.ToString();
        }
    }
}
