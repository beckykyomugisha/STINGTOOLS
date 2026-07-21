// StingTools — StandardsTokenMatcher.
//
// Normalised matching between a building profile's ActiveStandards and a
// placement rule's ApplicableStandards / StandardRef.
//
// The gate used to compare raw strings with a case-insensitive
// contains-either-direction test. That works for the easy cases
// ("BS 6465" vs "BS 6465-1:2006") but silently misses the ones that actually
// occur in the rule packs:
//
//   profile "BS 7671"        vs rule "BS7671"              — spacing
//   profile "BS EN 12464"    vs rule "BS EN 12464-1:2011"  — year suffix
//   profile "Approved Doc M" vs rule "Approved Doc M / BS 8300-2"
//                                                          — '/' not a separator
//
// A missed match silently drops a rule from the run, which looks like "my
// rule never fires" with no explanation. This normalises both sides before
// comparing:
//
//   1. split free text on , ; | and / into individual citations;
//   2. strip a trailing edition/year (":2011", " 2019", "-2:2006" keeps "-2");
//   3. reduce to letters+digits, uppercased, so spacing and punctuation
//      differences stop mattering;
//   4. compare contains-either-direction, so a broad profile token matches a
//      specific rule citation and vice-versa.
//
// Deliberately lenient: over-matching keeps a rule in the run (the previous
// default for untagged rules), whereas under-matching drops it.

using System;
using System.Collections.Generic;
using System.Text;

namespace StingTools.Core.Placement
{
    public static class StandardsTokenMatcher
    {
        private static readonly char[] Separators = { ',', ';', '|', '/' };

        /// <summary>
        /// Split a free-text or CSV standards string into individual citations.
        /// "Approved Doc M / BS 8300-2" -> ["Approved Doc M", "BS 8300-2"].
        /// </summary>
        public static List<string> Split(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// Reduce a citation to a comparison key: trailing edition/year removed,
        /// then letters and digits only, uppercased.
        /// "BS EN 12464-1:2011" -> "BSEN124641";  "BS7671" -> "BS7671".
        /// </summary>
        public static string Normalise(string citation)
        {
            if (string.IsNullOrWhiteSpace(citation)) return "";

            var s = citation.Trim();

            // Drop a trailing edition/year: ":2011" or a final free-standing
            // 4-digit year. "-1:2006" keeps the part number, drops the year.
            int colon = s.LastIndexOf(':');
            if (colon > 0 && IsYear(s.Substring(colon + 1))) s = s.Substring(0, colon);
            else
            {
                // A SPACE-separated trailing 4-digit group is only an edition year
                // when the citation still carries a standard number without it.
                //
                // Without that guard "BS EN 1838" (emergency lighting) collapses to
                // "BSEN", which then substring-matches EVERY other BS EN standard —
                // a false positive that also hits BS EN 1869 and the whole Eurocode
                // family (EN 1990-1999), all of whose numbers fall inside the
                // 1800-2199 year window. "BS 8233 2014" still reduces to "BS 8233"
                // because "BS 8233" retains a digit; "Equality Act 2010" keeps its
                // year because "Equality Act" has none.
                int sp = s.LastIndexOf(' ');
                if (sp > 0 && IsYear(s.Substring(sp + 1)) && HasDigit(s.Substring(0, sp)))
                    s = s.Substring(0, sp);
            }

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            return sb.ToString();
        }

        private static bool HasDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var ch in s) if (char.IsDigit(ch)) return true;
            return false;
        }

        private static bool IsYear(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length != 4) return false;
            foreach (var ch in s) if (!char.IsDigit(ch)) return false;
            // 1800-2199 keeps a part number like "2006" distinguishable from
            // an ordinary 4-digit standard number such as "7671".
            int y = int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            return y >= 1800 && y <= 2199;
        }

        /// <summary>
        /// True when any citation in <paramref name="ruleStandards"/> matches any
        /// entry in <paramref name="activeStandards"/>. An empty rule side means
        /// "untagged" and is the caller's decision — this returns false so the
        /// caller can apply its own default.
        /// </summary>
        public static bool Matches(string ruleStandards, IEnumerable<string> activeStandards)
        {
            if (string.IsNullOrWhiteSpace(ruleStandards) || activeStandards == null) return false;

            var activeKeys = new List<string>();
            foreach (var a in activeStandards)
                foreach (var citation in Split(a))
                {
                    var k = Normalise(citation);
                    if (k.Length > 0) activeKeys.Add(k);
                }
            if (activeKeys.Count == 0) return false;

            foreach (var citation in Split(ruleStandards))
            {
                var rk = Normalise(citation);
                if (rk.Length == 0) continue;
                foreach (var ak in activeKeys)
                {
                    if (rk.IndexOf(ak, StringComparison.Ordinal) >= 0 ||
                        ak.IndexOf(rk, StringComparison.Ordinal) >= 0)
                        return true;
                }
            }
            return false;
        }
    }
}
