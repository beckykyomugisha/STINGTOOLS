// ══════════════════════════════════════════════════════════════════════════
//  PatternMatch.cs — a pattern matches a WORD, not a run of letters.
//
//  Found by running the schedule over a second project. An oil painting
//  arrived in ELEMENT 03: ROOF —
//
//      Art_Piece_-_Draw_Bridge_at_Arles_-_Van_Gogh_5866
//
//  — because the roof stage lists the type pattern "ridge", and "bRIDGEe"
//  contains it. Every bridge, cartridge, porridge and Aldridge in every
//  future project routes to the roof.
//
//  The same class of bug sat in the wall take-off: `material.Contains("rc")`
//  decides a wall is reinforced concrete, and "poRCelain" contains "rc". A
//  porcelain-tiled surface would have decomposed into concrete, rebar and
//  formwork.
//
//  Neither produces an error. Both produce a confident wrong row in a
//  section a reader has no reason to question — which is why this is a
//  matcher rather than a longer list of exceptions.
//
//  A boundary is anything that is not a letter or a digit, so "roof-cap",
//  "roof cap" and "roof_cap" all match the pattern "roof cap" once their
//  separators are normalised by the caller, while "bridge" never matches
//  "ridge". Patterns that are themselves multi-word are matched as a run
//  with the same boundary rule at each end.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core.MaterialSchedule
{
    public static class PatternMatch
    {
        /// <summary>
        /// True when <paramref name="pattern"/> appears in <paramref name="text"/>
        /// as a whole word — bounded at both ends by a non-alphanumeric
        /// character or the end of the string.
        ///
        /// Case-insensitive, like every other match in this file.
        /// </summary>
        public static bool Contains(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(pattern)) return false;
            string pat = pattern.Trim();

            int i = 0;
            while (true)
            {
                i = text.IndexOf(pat, i, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                if (IsBoundedAt(text, i, pat.Length)) return true;
                i++;   // an inner hit; keep looking for a bounded one
            }
        }

        /// <summary>
        /// A digit counts as part of a word, so "g28" does not match inside
        /// "g285" — a gauge is not a prefix of another gauge.
        /// </summary>
        private static bool IsBoundedAt(string text, int start, int length)
        {
            bool leftOk = start == 0 || !IsWordChar(text[start - 1]);
            int end = start + length;
            bool rightOk = end >= text.Length || !IsWordChar(text[end]);
            return leftOk && rightOk;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
    }
}
