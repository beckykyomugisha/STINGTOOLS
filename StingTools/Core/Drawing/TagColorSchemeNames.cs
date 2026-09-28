// StingTools — the names of the tag colour schemes, in one place.
//
// A scheme name travels a long way: a view style pack's tagColorScheme is written
// to the view's STING_VIEW_TAG_STYLE by TokenProfileApplier, "Set View Tag Style"
// writes it too, the Tag Studio buttons pass it to Apply Color Scheme, and
// TagStyleEngine finally looks it up. Nothing checked the name on the way, and the
// spellings had drifted: the engine's key is "Mono" while the Mono button, Set View
// Tag Style and the drawing types say "Monochrome"; six packs said "STING
// Discipline". An unknown name fell through silently — the view kept whatever
// scheme the caller passed, and the tags kept their style.
//
// This file owns the vocabulary and the aliases. TagStyleEngine's scheme tables
// must carry exactly these keys (TagColorSchemeNamesTests parses them).
//
// Revit-free; unit-tested.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class TagColorSchemeNames
    {
        /// <summary>Discipline colour schemes (TagStyleEngine.BuiltInSchemes).</summary>
        public static readonly string[] Discipline = { "Discipline", "Warm", "Cool", "Red", "Yellow", "Blue", "Mono", "Dark" };

        /// <summary>Colour-by-value schemes (TagStyleEngine.VariableSchemes).</summary>
        public static readonly string[] Variable = { "System", "Status", "Zone", "Level", "Location", "Function",
            "MedicalGas", "Pressure", "ElectricalSupply", "FireRating", "Radiation", "AntiLigature", "WaterSafety" };

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Monochrome", "Mono" },
            { "STING Discipline", "Discipline" },
            { "ByDiscipline", "Discipline" },
            { "By Discipline", "Discipline" },
            // The clarification pack's "RAG Status": lifecycle status is the red/amber/green
            // reading the model carries (NEW green, TEMPORARY amber, DEMOLISHED red).
            { "RAG Status", "Status" },
            { "RAG", "Status" },
        };

        /// <summary>The scheme's canonical name, or null when no scheme has that name.</summary>
        public static string Resolve(string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) return null;
            if (Aliases.TryGetValue(n, out var alias)) n = alias;
            return Discipline.Concat(Variable).FirstOrDefault(s => string.Equals(s, n, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A parameter value as the schemes compare it: trimmed, and a number
        /// written without trailing zeros or units ("60.0" and "60 min" read "60").</summary>
        public static string NormaliseValue(string raw)
        {
            var v = (raw ?? "").Trim();
            if (v.Length == 0) return v;
            int i = 0;
            while (i < v.Length && (char.IsDigit(v[i]) || v[i] == '.')) i++;
            if (i > 0 && (i == v.Length || v[i] == ' ')
                && double.TryParse(v.Substring(0, i), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return v;
        }

        public static bool IsVariable(string canonical)
            => Variable.Any(s => string.Equals(s, canonical, StringComparison.OrdinalIgnoreCase));

        /// <summary>What to tell a user whose scheme name did not resolve.</summary>
        public static string Unknown(string name)
            => $"No tag colour scheme is named '{name}'. Known: {string.Join(", ", Discipline.Concat(Variable))}.";
    }
}
