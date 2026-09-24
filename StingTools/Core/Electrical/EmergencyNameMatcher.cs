// EmergencyNameMatcher — Revit-free test of whether a luminaire family name
// marks it as emergency lighting.
//
// The audit matched raw substrings, including "e-" and "em-". Those hit
// ordinary names — "Surface-Mounted" contains "e-", "System-Panel" contains
// "em-" — so normal fittings were counted as emergency and rooms with no
// emergency cover could pass.
//
// Two rules now:
//  - the long keywords (emergency, emerg, maintained) are specific enough to
//    match anywhere, so CamelCase names like "EmergencyLight_LED" still count;
//    "exit" must at least START a word ("ExitSign", not "Deexited").
//  - the short "EM" abbreviation must stand alone: not preceded by a letter
//    ("System", "Item"), and not followed by a lower-case letter ("Emerald").
//    It is matched on the ORIGINAL casing so "EMBulkhead" still counts.

using System.Text.RegularExpressions;

namespace StingTools.Core.Electrical
{
    internal static class EmergencyNameMatcher
    {
        private static readonly Regex LongKeyword = new Regex(
            @"emergency|emerg|maintained|(?<![a-z])exit",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Case-sensitive on purpose: "EM"/"Em"/"em" as its own token.
        private static readonly Regex EmToken = new Regex(
            @"(?<![A-Za-z])(EM|Em|em)(?![a-z])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Pass the family name in its original casing.</summary>
        public static bool IsEmergencyName(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return false;
            return LongKeyword.IsMatch(familyName) || EmToken.IsMatch(familyName);
        }
    }
}
