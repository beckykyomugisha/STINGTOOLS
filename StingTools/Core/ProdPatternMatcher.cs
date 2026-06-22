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
    /// Supported pattern forms (all matched against an already-upper-cased
    /// family+type name):
    ///   • plain substring            "AIR HANDLING"            → Contains
    ///   • leading/trailing wildcard  "*FCU*" / "FCU*" / "*FCU" → glob
    ///   • embedded wildcard          "VRV*UNIT"                → glob
    ///   • alternation                "*SPLIT*|*PACKAGED*"      → any branch wins
    /// </summary>
    public static class ProdPatternMatcher
    {
        /// <param name="nameUpper">Upper-cased family + type name of the element.</param>
        /// <param name="patternUpper">Upper-cased FAMILY_PATTERN cell from the CSV.</param>
        public static bool Matches(string nameUpper, string patternUpper)
        {
            if (string.IsNullOrEmpty(nameUpper) || string.IsNullOrEmpty(patternUpper))
                return false;

            foreach (string altRaw in patternUpper.Split('|'))
            {
                string alt = altRaw.Trim();
                if (alt.Length == 0) continue;

                // No wildcard → plain substring (back-compatible with bare patterns).
                if (alt.IndexOf('*') < 0)
                {
                    if (nameUpper.Contains(alt)) return true;
                    continue;
                }

                // Glob → anchored regex with '*' as the only wildcard.
                string rx = "^" + Regex.Escape(alt).Replace("\\*", ".*") + "$";
                try { if (Regex.IsMatch(nameUpper, rx)) return true; }
                catch { /* malformed branch — ignore, try the next alternative */ }
            }
            return false;
        }
    }
}
