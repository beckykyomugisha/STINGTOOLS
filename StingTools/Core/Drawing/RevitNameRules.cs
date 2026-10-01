// StingTools — Drawing Template Manager · Revit element-name rules (Revit-free)
//
// Revit refuses an element name containing any of  \ : { } [ ] | ; < > ? ` ~
// ("name cannot include prohibited characters"). The corporate data names things
// for people — "STING - Struct: Concrete", "STING - Coord: LOD < 300" — and 263 of
// the 287 shipped AEC filters carried a colon, so ParameterFilterElement.Create
// refused essentially the whole library in a real model (Drawing Self-Test,
// 2026-10-01). The JSON keeps its human names; the Revit boundary goes through
// here, for creation AND for every lookup by name, so the two cannot disagree.
//
// Sanitize is the identity on a legal name, so a filter a model already holds
// under a legal name is found under that same name. Candidates() still yields the
// raw name as a second try, for a model whose filter was named by some route that
// did not enforce the rule.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class RevitNameRules
    {
        /// <summary>Characters Revit refuses in an element name.</summary>
        public static readonly char[] Prohibited = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        /// <summary>True when <paramref name="name"/> holds none of <see cref="Prohibited"/> (and no control character).</summary>
        public static bool IsLegal(string name)
        {
            if (name == null) return true;
            foreach (var c in name)
                if (Array.IndexOf(Prohibited, c) >= 0 || char.IsControl(c)) return false;
            return true;
        }

        /// <summary>
        /// The name Revit will accept for <paramref name="name"/>. Deterministic:
        /// ":" becomes " - " ("STING - Struct: Concrete" → "STING - Struct - Concrete"),
        /// "&lt;" / "&gt;" become "lt" / "gt", brackets and braces become parentheses,
        /// "|" ";" "\" become " - ", and "?" "`" "~" and control characters are dropped;
        /// then runs of spaces collapse. A legal name is returned unchanged.
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name) || IsLegal(name)) return name;
            var sb = new StringBuilder(name.Length + 8);
            foreach (var c in name)
            {
                switch (c)
                {
                    case ':': case '|': case ';': case '\\': sb.Append(" - "); break;
                    case '<': sb.Append(" lt "); break;
                    case '>': sb.Append(" gt "); break;
                    case '[': case '{': sb.Append('('); break;
                    case ']': case '}': sb.Append(')'); break;
                    case '?': case '`': case '~': break;
                    default:
                        if (char.IsControl(c)) break;
                        sb.Append(c);
                        break;
                }
            }
            var s = Regex.Replace(sb.ToString(), @"\s+", " ");
            s = Regex.Replace(s, @"\(\s+", "(");
            s = Regex.Replace(s, @"\s+\)", ")");
            // "Struct:  - x" style doubles collapse to one separator.
            s = Regex.Replace(s, @"-(\s*-)+", "-");
            return s.Trim();
        }

        /// <summary>
        /// The names to look for in a model, most likely first: the sanitised name,
        /// then the raw one when it differs. Never empty for a non-blank name.
        /// </summary>
        public static IReadOnlyList<string> Candidates(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();
            var clean = Sanitize(name.Trim());
            var raw = name.Trim();
            return string.Equals(clean, raw, StringComparison.Ordinal)
                ? new[] { raw }
                : new[] { clean, raw };
        }

        /// <summary>
        /// True when the Revit element name <paramref name="revitName"/> is the element
        /// that <paramref name="dataName"/> (a name as written in the data) creates —
        /// its sanitised form or, for a legacy element, the raw form. Case-insensitive,
        /// as Revit compares element names.
        /// </summary>
        public static bool Matches(string revitName, string dataName)
        {
            if (string.IsNullOrWhiteSpace(revitName) || string.IsNullOrWhiteSpace(dataName)) return false;
            var r = revitName.Trim();
            foreach (var c in Candidates(dataName))
                if (string.Equals(r, c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
