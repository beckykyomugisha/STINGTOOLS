// StingTools — Drawing Template Manager · familyMatch on annotation rules
//
// A rule's category is often too coarse. A roof plan wants its rainwater
// outlets and roof lights tagged, but a rainwater outlet is a Plumbing
// Fixture and a roof light is a Window — tagging the whole category would
// tag every basin and every wall window that shows through the view range.
// DRAW-8 dropped the RainwaterOutlets / RoofLights keys for exactly that
// reason: no category names them, and a guessed mapping tags the wrong things.
//
// familyMatch narrows a rule to elements whose "Family : Type" name matches a
// case-insensitive regular expression. The concept stays data: a project whose
// outlets are called "Gully - Roof" edits a pattern, not code.
//
// A pattern that does not compile disables the rule and says so. Falling back
// to "match everything" would silently tag the whole category — the very
// outcome the field exists to prevent.

using System;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class RuleFamilyFilter
    {
        /// <summary>
        /// Compile <paramref name="pattern"/>. Null pattern → null regex and no
        /// error (no filter). Invalid pattern → null regex and a non-null
        /// <paramref name="error"/>; the caller must then skip the rule.
        /// </summary>
        public static Regex Compile(string pattern, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException ex)
            {
                error = $"familyMatch '{pattern}' is not a valid regular expression ({ex.Message}); rule skipped.";
                return null;
            }
        }

        /// <summary>The text a pattern is tested against: "Family : Type".</summary>
        public static string Subject(string familyName, string typeName)
            => (familyName ?? "") + " : " + (typeName ?? "");

        public static bool Matches(Regex rx, string familyName, string typeName)
        {
            if (rx == null) return true;
            try { return rx.IsMatch(Subject(familyName, typeName)); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }
}
