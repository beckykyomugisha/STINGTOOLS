// StingTools — Drawing Template Manager
//
// FillPatternNames — the Revit-free half of resolving a fill / line pattern
// named in STING_AEC_FILTERS.json or a style pack.
//
// DTW-165: patterns were matched by exact element name. 45 filter defaults and
// every generated MEP system filter say "Solid fill", but Revit's solid pattern
// is named "<Solid fill>", so none of them resolved. The other names the data
// uses (Crosshatch, Concrete, Diagonal up, …) were created by nothing — the
// template manager's CreateFillPatterns makes "STING - Crosshatch",
// "STING - Concrete Model", … — and a miss was skipped without a word.
//
// Solid is now recognised by meaning (FillPattern.IsSolidFill), and every other
// name is tried as written, then as the Revit default template spells it, then
// as the STING pattern CreateFillPatterns creates.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    internal static class FillPatternNames
    {
        /// <summary>Does this name mean Revit's solid fill?</summary>
        internal static bool IsSolidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var n = name.Trim().Trim('<', '>').Trim();
            return string.Equals(n, "Solid fill", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Solid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Solidfill", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Data name → other names the same pattern goes by: the Revit default
        /// template's spelling first, then the pattern TemplateManager.FillPatternDefs
        /// creates. Keys are matched case-insensitively.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string[]> Aliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Crosshatch"]       = new[] { "Crosshatch", "STING - Crosshatch" },
            ["Crosshatch fine"]  = new[] { "Crosshatch-small", "Crosshatch small", "STING - Crosshatch" },
            ["Cross"]            = new[] { "Crosshatch", "STING - Crosshatch" },
            ["Diagonal up"]      = new[] { "Diagonal up", "STING - Diagonal Up" },
            ["Diagonal down"]    = new[] { "Diagonal down", "STING - Diagonal Down" },
            ["Diagonal cross"]   = new[] { "Diagonal crosshatch", "STING - Diagonal Cross" },
            ["Horizontal"]       = new[] { "Horizontal", "STING - Horizontal" },
            ["Vertical"]         = new[] { "Vertical", "STING - Vertical" },
            ["Concrete"]         = new[] { "Concrete", "STING - Concrete Model" },
            ["Brick"]            = new[] { "Brick 75x225", "Masonry - Brick", "STING - Brick Model" },
            ["Tile"]             = new[] { "Tile 300x300", "STING - Tile Model" },
            ["Insulation"]       = new[] { "Insulation", "STING - Insulation Model" },
            ["Sand"]             = new[] { "Sand", "STING - Sand Model" },
            ["Earth"]            = new[] { "Earth", "STING - Earth Model" },
            ["Wood - End"]       = new[] { "Wood 1", "Wood - End grain" },
        };

        /// <summary>Names to try, in order, for <paramref name="name"/> — the name itself first.</summary>
        internal static List<string> Candidates(string name)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(name)) return list;
            var n = name.Trim();
            list.Add(n);
            if (Aliases.TryGetValue(n, out var more))
                foreach (var m in more)
                    if (!list.Contains(m, StringComparer.OrdinalIgnoreCase)) list.Add(m);
            return list;
        }

        /// <summary>
        /// The pattern to use for a colour. A colour with no pattern draws nothing in
        /// Revit, so a stated colour with no stated pattern means solid fill.
        /// Returns null when neither is stated.
        /// </summary>
        internal static string EffectivePattern(string pattern, string color)
        {
            if (!string.IsNullOrWhiteSpace(pattern)) return pattern;
            return string.IsNullOrWhiteSpace(color) ? null : "<Solid fill>";
        }
    }
}
