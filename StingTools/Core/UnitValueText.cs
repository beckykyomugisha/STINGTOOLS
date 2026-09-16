// StingTools — numeric text extracted from a Revit value string.
//
// Revit-free by design so it can be unit-tested: this is string handling, and the
// culture-and-separator cases are exactly the kind that look obvious and are not.

using System;

namespace StingTools.Core
{
    public static class UnitValueText
    {
        /// <summary>
        /// Drop the unit that <c>Parameter.AsValueString()</c> appends, keeping the number
        /// and its separators.
        ///
        /// WHY. A _TXT display mirror carries a PLAIN NUMBER by convention — MapDimension
        /// has always written "900", never "900 mm" — and the tag row supplies the unit
        /// through its own prefix/suffix. `AsValueString()` does not follow that
        /// convention: it returns "100 A", "18 W", "1200 lm". Feed that to a mirror whose
        /// tag row already declares <c>suffix_override: " A"</c> and the drawing reads
        /// <c>MCB: 100 A A</c>.
        ///
        /// The unit CONVERSION in AsValueString is worth keeping — it respects the
        /// project's display units, which the raw internal value does not — so the string
        /// is trimmed rather than replaced by a re-formatted raw value.
        ///
        /// A separator is kept only when a digit follows it, so "1 200 W" (space as a
        /// thousands separator) keeps "1 200" while "100 A" stops at "100". Input with no
        /// leading number is returned unchanged: a value string like "By Category" is not
        /// a quantity and must not be silently truncated to nothing.
        /// </summary>
        public static string StripUnitSuffix(string valueString)
        {
            if (string.IsNullOrEmpty(valueString)) return valueString;

            string s = valueString.Trim();
            int end = 0;
            bool seenDigit = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (char.IsDigit(c))
                {
                    seenDigit = true;
                    end = i + 1;
                    continue;
                }

                // A leading sign, before any digit.
                if (!seenDigit && (c == '-' || c == '+' || c == '−'))
                {
                    end = i + 1;
                    continue;
                }

                // A separator counts only when a digit follows it — that is what
                // distinguishes "1 200" (thousands) from "100 A" (a unit).
                if (seenDigit && (c == '.' || c == ',' || c == ' ' ||
                                  c == ' ' || c == '\'' || c == ' '))
                {
                    if (i + 1 < s.Length && char.IsDigit(s[i + 1]))
                    {
                        end = i + 1;
                        continue;
                    }
                }

                break;
            }

            if (!seenDigit) return valueString;   // not a quantity — leave it alone

            string head = s.Substring(0, end).Trim();
            return head.Length > 0 ? head : valueString;
        }
    }
}
