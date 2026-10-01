// ============================================================================
// TagStyleFamilyParams.cs — which style parameters a NEW tag family carries.
//
// TAGFAM-9 (2026-10-01). A tag family used to receive all 128
// TAG_{size}{style}_{colour}_BOOL style switches. Measured in Revit 2025:
// FamilyManager.AddParameter costs ~0.78 s a parameter, so the switches alone
// were ~100 s of a ~124 s door-tag build. A headless audit of all 211 shipped
// tag families found every one carrying the switches and NONE with a switch
// associated to any family element — they changed nothing a tag shows.
//
// A family's style therefore lives in its TYPE (TagStyleCatalogue type
// variants) plus the single TAG_STYLE_CODE_TXT parameter. The switch matrix is
// opt-in: tag_style_catalogue.json "family_style_switches" lists the style
// codes whose switches are still added to new families (empty by default;
// "*" means all of them). Existing families keep whatever they carry —
// nothing here removes a switch.
//
// Revit-free on purpose: StingTools.Tags.Tests compiles this file and parses
// the SHIPPED catalogue through it, so the field cannot be silently unbound.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    public static class TagStyleFamilyParams
    {
        /// <summary>The single style parameter (same string as ParamRegistry.TAG_STYLE_CODE).</summary>
        public const string StyleCodeParam = "TAG_STYLE_CODE_TXT";

        /// <summary>The catalogue key that opts style switches back in.</summary>
        public const string CatalogueKey = "family_style_switches";

        /// <summary>Catalogue entry meaning "every size × style × colour switch".</summary>
        public const string AllSwitches = "*";

        /// <summary>Style code, e.g. ("2.5","BOLD","BLUE") → "2.5BOLD_BLUE" — the
        /// TAG-01 format ParamRegistry and TagStyleEngine already use.</summary>
        public static string StyleCode(string size, string style, string colour)
            => $"{size}{style}_{colour}";

        /// <summary>Style code → switch parameter, "2.5BOLD_BLUE" → "TAG_2.5BOLD_BLUE_BOOL".</summary>
        public static string SwitchParamName(string code) => $"TAG_{code}_BOOL";

        /// <summary>True for a name in the TAG_{size}{style}_{colour}_BOOL switch matrix shape.</summary>
        public static bool LooksLikeSwitch(string paramName)
            => !string.IsNullOrEmpty(paramName)
               && paramName.StartsWith("TAG_", StringComparison.Ordinal)
               && paramName.EndsWith("_BOOL", StringComparison.Ordinal)
               && paramName.Length > 9
               && char.IsDigit(paramName[4]);

        /// <summary>
        /// Parse a style code against the catalogue's dimensions. Accepts the code
        /// ("2.5BOLD_BLUE") or the switch name ("TAG_2.5BOLD_BLUE_BOOL"); returns
        /// the canonical code, or null when it is not a size × style × colour combination.
        /// </summary>
        public static string ParseCode(string entry, IEnumerable<string> sizes,
            IEnumerable<string> styles, IEnumerable<string> colours)
        {
            if (string.IsNullOrWhiteSpace(entry)) return null;
            string s = entry.Trim();
            if (s.StartsWith("TAG_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            if (s.EndsWith("_BOOL", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 5);

            int us = s.LastIndexOf('_');
            if (us <= 0 || us == s.Length - 1) return null;
            string head = s.Substring(0, us), colourIn = s.Substring(us + 1);

            string colour = (colours ?? Enumerable.Empty<string>())
                .FirstOrDefault(c => string.Equals(c, colourIn, StringComparison.OrdinalIgnoreCase));
            if (colour == null) return null;

            foreach (string size in sizes ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(size) || !head.StartsWith(size, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = head.Substring(size.Length);
                string style = (styles ?? Enumerable.Empty<string>())
                    .FirstOrDefault(st => string.Equals(st, rest, StringComparison.OrdinalIgnoreCase));
                if (style != null) return StyleCode(size, style, colour);
            }
            return null;
        }

        /// <summary>Every switch parameter for the given dimensions (sizes × styles × colours).</summary>
        public static List<string> AllSwitchParams(IEnumerable<string> sizes,
            IEnumerable<string> styles, IEnumerable<string> colours)
        {
            var list = new List<string>();
            foreach (var sz in sizes ?? Enumerable.Empty<string>())
                foreach (var st in styles ?? Enumerable.Empty<string>())
                    foreach (var co in colours ?? Enumerable.Empty<string>())
                        list.Add(SwitchParamName(StyleCode(sz, st, co)));
            return list;
        }

        /// <summary>
        /// Read <see cref="CatalogueKey"/> from the catalogue root and return the switch
        /// PARAMETER names to add to new families. An absent key and an empty list both
        /// mean none. Entries that are not a valid combination go to
        /// <paramref name="rejected"/> — the caller must report them, never drop them.
        /// A key that is present but not a JSON array is itself a rejection.
        /// </summary>
        public static List<string> ParseSwitches(JObject root, IEnumerable<string> sizes,
            IEnumerable<string> styles, IEnumerable<string> colours, List<string> rejected)
        {
            var result = new List<string>();
            var token = root?[CatalogueKey];
            if (token == null || token.Type == JTokenType.Null) return result;
            if (!(token is JArray arr))
            {
                rejected?.Add($"{CatalogueKey} is {token.Type}, not a list");
                return result;
            }

            var sz = (sizes ?? Enumerable.Empty<string>()).ToList();
            var st = (styles ?? Enumerable.Empty<string>()).ToList();
            var co = (colours ?? Enumerable.Empty<string>()).ToList();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in arr)
            {
                string raw = item.Type == JTokenType.String ? (string)item : item.ToString();
                if (string.Equals(raw?.Trim(), AllSwitches, StringComparison.Ordinal))
                {
                    foreach (var p in AllSwitchParams(sz, st, co))
                        if (seen.Add(p)) result.Add(p);
                    continue;
                }
                string code = item.Type == JTokenType.String ? ParseCode(raw, sz, st, co) : null;
                if (code == null) { rejected?.Add(raw ?? "(null)"); continue; }
                string param = SwitchParamName(code);
                if (seen.Add(param)) result.Add(param);
            }
            return result;
        }

        /// <summary>
        /// The style parameters a new tag family carries: <see cref="StyleCodeParam"/>,
        /// then the opted-in switches, then the appearance parameters (box, leader,
        /// scale/depth caches). Order is stable and duplicates are dropped.
        /// </summary>
        public static List<string> Compose(IEnumerable<string> optedInSwitches,
            IEnumerable<string> appearanceParams)
        {
            var list = new List<string> { StyleCodeParam };
            foreach (var p in (optedInSwitches ?? Enumerable.Empty<string>())
                         .Concat(appearanceParams ?? Enumerable.Empty<string>()))
                if (!string.IsNullOrEmpty(p) && !list.Contains(p)) list.Add(p);
            return list;
        }

        /// <summary>
        /// Family Conformance check (4), out of 10. A family carrying
        /// <see cref="StyleCodeParam"/> passes in full — that is the style contract now.
        /// Otherwise the sampled switches score 5 each (the pre-TAGFAM-9 contract,
        /// still honoured for families built with the matrix). <paramref name="missing"/>
        /// names what is absent only when the family satisfies neither.
        /// </summary>
        public static int ConformancePoints(bool hasStyleCode, IEnumerable<string> sampledSwitches,
            Func<string, bool> has, List<string> missing)
        {
            if (hasStyleCode) return 10;
            int pts = 0;
            var absent = new List<string>();
            foreach (var name in sampledSwitches ?? Enumerable.Empty<string>())
            {
                if (has != null && has(name)) pts += 5;
                else absent.Add(name);
            }
            pts = Math.Min(pts, 10);
            if (pts < 10 && missing != null)
            {
                missing.Add($"Tag style: no {StyleCodeParam} and the switch sample is incomplete " +
                            "(rebuild with Create Tag Families / Family Parameter Creator to add the code parameter)");
                foreach (var a in absent) missing.Add($"Tag style param missing: {a}");
            }
            return pts;
        }
    }
}
