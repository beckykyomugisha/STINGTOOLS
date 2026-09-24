// EmergencyNameMatcher — Revit-free test of whether a luminaire family name
// marks it as emergency lighting.
//
// The audit matched raw substrings, including "e-" and "em-". Those hit
// ordinary names — "Surface-Mounted" contains "e-", "System-Panel" contains
// "em-" — so normal fittings were counted as emergency and rooms with no
// emergency cover could pass. Keywords now match whole tokens only.

using System.Text.RegularExpressions;

namespace StingTools.Core.Electrical
{
    internal static class EmergencyNameMatcher
    {
        // A keyword counts only when it is a whole token: bounded by the start
        // or end of the name or by a non-letter (space, dash, underscore, dot,
        // digit, bracket). "maintained" also covers "non-maintained".
        private static readonly Regex Token = new Regex(
            @"(?<![a-z])(emergency|emerg|exit|maintained|em)(?![a-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsEmergencyName(string familyName)
            => !string.IsNullOrWhiteSpace(familyName) && Token.IsMatch(familyName);
    }
}
