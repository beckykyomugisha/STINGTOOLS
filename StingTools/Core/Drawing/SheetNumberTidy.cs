// StingTools — Drawing Template Manager · sheet-number tidying
//
// WHY THIS FILE EXISTS
// --------------------
// Sheet numbers come from a pattern: "A-RCP-{lvl}-{seq:D3}", "M-CO-{lvl}-{seq:D3}",
// and seven shipped drawing types use a nine-token ISO pattern. When a token is
// KNOWN but EMPTY — a sheet with no level, a drawing with no volume — the
// substitution yields "" and both separators stay:
//
//     A-{lvl}-{seq:D3}   with lvl = ""   ->   A--001
//
// That is what "A--001" on a live project is. It then flowed onward: the empty
// segment reached an export filename as "SAH--ZZ", and the sheet number is the
// Number field of the ISO identifier, so it was about to reach an issued
// drawing's document code too.
//
// COLLAPSE, NOT SUBSTITUTE. A missing ISO *field* should read ZZ, because the
// field positions carry meaning and a reader counts them. A Revit SHEET NUMBER
// is not a field-structured identifier — it is a short human key, and "A-001"
// is what someone would have typed. Padding it to "A-ZZ-001" would make the key
// longer for no reader's benefit, and the key is the thing that has to stay
// short (section and callout heads display it and can display nothing else).
//
// Revit-free, so it is unit-tested. The old behaviour was a silent byproduct of
// string substitution that nothing tested and nothing could see until it reached
// paper.

using System;
using System.Text;

namespace StingTools.Core.Drawing
{
    public static class SheetNumberTidy
    {
        /// <summary>Collapse runs of the separator left by empty tokens, and trim it
        /// from both ends.
        ///
        ///     "A--001"      -> "A-001"
        ///     "A-RCP--001"  -> "A-RCP-001"
        ///     "-A-001-"     -> "A-001"
        ///     "PRJ---0001"  -> "PRJ-0001"
        ///
        /// Returns the input unchanged when there is nothing to tidy, so a caller can
        /// compare and report only what it actually altered.</summary>
        public static string Collapse(string raw, char separator = '-')
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            var sb = new StringBuilder(raw.Length);
            bool lastWasSep = false;
            foreach (char c in raw)
            {
                if (c == separator)
                {
                    // Leading separators are dropped by the same rule: nothing has
                    // been written yet, so there is nothing for this one to separate.
                    if (lastWasSep || sb.Length == 0) continue;
                    lastWasSep = true;
                    continue;
                }
                if (lastWasSep) sb.Append(separator);
                lastWasSep = false;
                sb.Append(c);
            }
            // A trailing separator is never appended, because it is only written when
            // the NEXT real character arrives.
            return sb.ToString();
        }

        /// <summary>Does this string carry the damage Collapse repairs? Used to report
        /// honestly rather than renaming everything and claiming a count.</summary>
        public static bool NeedsTidying(string raw, char separator = '-')
            => !string.IsNullOrEmpty(raw) && !string.Equals(raw, Collapse(raw, separator), StringComparison.Ordinal);
    }
}
