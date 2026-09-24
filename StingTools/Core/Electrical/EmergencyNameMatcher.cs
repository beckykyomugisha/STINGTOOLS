// EmergencyNameMatcher — Revit-free test of whether a luminaire family name
// (or a circuit / system name) marks it as emergency lighting.
//
// The audit matched raw substrings, including "e-" and "em-". Those hit
// ordinary names — "Surface-Mounted" contains "e-", "System-Panel" contains
// "em-" — so normal fittings were counted as emergency and rooms with no
// emergency cover could pass.
//
// Two rules now:
//  - the long keywords (emergency, emerg, maintained, secours, …) are specific
//    enough to match anywhere, so CamelCase names like "EmergencyLight_LED"
//    still count; "exit" must at least START a word ("ExitSign", not "Deexited").
//  - the short "EM" abbreviation must stand alone: not preceded by a letter
//    ("System", "Item"), and not followed by a lower-case letter ("Emerald").
//    It is matched on the ORIGINAL casing so "EMBulkhead" still counts.
//
// The WORDS live in one place — EmergencyKeywords (Data/STING_EMERGENCY_KEYWORDS.json
// + project override); this class only applies the token rules to them.

namespace StingTools.Core.Electrical
{
    internal static class EmergencyNameMatcher
    {
        private static readonly EmergencyKeywords Default = EmergencyKeywords.BuiltIn();

        /// <summary>
        /// Pass the name in its original casing. <paramref name="keywords"/> is the
        /// document's list (EmergencyKeywordRegistry); null = the built-in list.
        /// </summary>
        public static bool IsEmergencyName(string name, EmergencyKeywords keywords = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var k = keywords ?? Default;
            return (k.LongRegex?.IsMatch(name) ?? false) || (k.AbbrRegex?.IsMatch(name) ?? false);
        }
    }
}
