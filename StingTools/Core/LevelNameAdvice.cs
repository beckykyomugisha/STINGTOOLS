// LevelNameAdvice.cs — what the Project Setup wizard says about level names.
//
// Revit-free: StingTools.Tags.Tests <Compile Include>s this file.
//
// A level's NAME is used raw in two places that cannot take a space: the {lvl}
// token in sheet-number patterns, and the level segment of a scope-box name
// (STING::<type>::<level>, STING-AREA::<area>::<level>), whose grammar allows
// only A-Z 0-9 . _ -. So "L02 - Office Level" passes the ISO 19650 prefix check
// but produces sheet numbers with spaces in them and cannot be used in a box
// name. The wizard warns (non-blocking) and suggests the short code.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    public static class LevelNameAdvice
    {
        /// <summary>ISO 19650 level code, optionally followed by a descriptive suffix.</summary>
        private static readonly Regex IsoLevelRegex = new Regex(
            @"^(B\d{1,2}|GF|L\d{2,3}|MZ\d{1,2}|RF\d?|UR|XX)(\s*[-–_]\s*.+)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>True when the name starts with an ISO 19650 code (B01, GF, L02, MZ01, RF, …).</summary>
        public static bool IsIsoLevelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return IsoLevelRegex.IsMatch(name.Trim());
        }

        /// <summary>True when the name contains any whitespace, leading and trailing included.</summary>
        public static bool HasWhitespace(string name)
            => !string.IsNullOrEmpty(name) && name.Any(char.IsWhiteSpace);

        /// <summary>
        /// The short code to use instead of <paramref name="name"/>: its ISO prefix
        /// ("L02 - Office Level" → "L02"), else the name with its whitespace removed
        /// ("Level 2" → "Level2"). Null for a blank name.
        /// </summary>
        public static string SuggestShortCode(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var m = IsoLevelRegex.Match(name.Trim());
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            return Regex.Replace(name, @"\s+", "");
        }

        /// <summary>
        /// Every name that contains whitespace, with its suggested short code, in input
        /// order. Duplicates are reported once.
        /// </summary>
        public static List<(string Name, string Suggestion)> WhitespaceFindings(IEnumerable<string> names)
        {
            var result = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in names ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(n) || !HasWhitespace(n) || !seen.Add(n)) continue;
                result.Add((n, SuggestShortCode(n)));
            }
            return result;
        }
    }
}
