// StingTools — Drawing Template Manager
//
// LegacyLevelShape — the level token as production wrote it BEFORE DTW-198, kept so
// sheets made then are still recognised. Revit-free (StingTools.Tags.Tests).
//
// Before DTW-198 {lvl} reached both the sheet number and the sheet name as the raw
// level name, which SheetNumberEngine.SafeShort cut to eight characters: "Ground
// Floor" → "GroundFl", "Basement 1" → "Basement". DTW-198 gave names the full level
// name and numbers SheetNumberEngine.ShortLevel ("Basemen1"), so:
//   * DTW-221: a reused sheet's pre-round-8 generated name never matched the new
//     rule, so DTW-209's rename never fired on it;
//   * DTW-222: Renumber read a pre-round-8 number on a long, digit-ending level as
//     a different shape and converted it — moving numbers the DTW-198 decision said
//     stay where they are.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    internal static class LegacyLevelShape
    {
        /// <summary>
        /// {lvl} for a NUMBER before DTW-198: the raw name (SafeShort cuts it
        /// downstream). Only meaningful for a non-ISO pattern — an ISO pattern took the
        /// ISO level code then as now.
        /// </summary>
        internal static string NumberLevel(string pattern, string levelName, string fallback)
            => levelName ?? fallback ?? "";

        /// <summary>
        /// True when a sheet on <paramref name="levelName"/> was numbered differently
        /// before DTW-198 — a non-ISO pattern and a long, digit-ending level name.
        /// </summary>
        internal static bool NumberShapeChanged(string pattern, string levelName)
        {
            if (string.IsNullOrEmpty(pattern) || levelName == null) return false;
            if (SheetNumberPolicy.IsAlreadyIso(pattern)) return false;
            if (pattern.IndexOf("{lvl}", StringComparison.OrdinalIgnoreCase) < 0) return false;
            return !string.Equals(SheetNumberEngine.SafeShort(levelName),
                                  SheetNumberEngine.SafeShort(SheetNumberEngine.ShortLevel(levelName)),
                                  StringComparison.Ordinal);
        }

        /// <summary>
        /// The NAME production gave a sheet before DTW-198: the name pattern through
        /// number shaping (level cut to eight characters; an ISO-shaped pattern took the
        /// ISO level code), plus — DTW-51 — the area when the pattern does not name it.
        /// </summary>
        internal static string SheetName(string pattern, string disc, string levelName, string isoCode,
            string fallbackLevel, string sys, string tag, string purpose, int seq,
            IDictionary<string, string> extras, string areaName)
        {
            string lvl = levelName == null ? (fallbackLevel ?? "")
                : (SheetNumberPolicy.IsAlreadyIso(pattern) && !string.IsNullOrEmpty(isoCode) ? isoCode : levelName);
            var name = SheetNumberEngine.ApplyTokenPattern(pattern ?? "", disc ?? "", lvl, sys ?? "",
                tag ?? "", tag ?? "", purpose ?? "", seq, extras);
            if (!string.IsNullOrWhiteSpace(areaName))
            {
                var p = pattern ?? "";
                bool namesArea = p.IndexOf("{mark}", StringComparison.OrdinalIgnoreCase) >= 0
                              || p.IndexOf("{spool}", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!namesArea && (name ?? "").IndexOf(areaName, StringComparison.OrdinalIgnoreCase) < 0)
                    name = $"{name} - {areaName}";
            }
            return name;
        }

        /// <summary>A number's sequence read against <paramref name="template"/>, with or
        /// without a trailing suitability/revision (DTW-44).</summary>
        internal static int? Sequence(string number, string template)
            => SheetNumberEngine.ExtractSequence(number, template)
            ?? SheetNumberEngine.ExtractSequence(SheetNumberPolicy.StripStatusSuffix(number), template);

        /// <summary>
        /// DTW-222: which shape <paramref name="number"/> is in — today's template first,
        /// then the pre-DTW-198 one. <c>Sequence</c> is null when neither matches (a real
        /// shape change); <c>Legacy</c> says the old shape matched, so the sheet keeps it.
        /// </summary>
        internal static (string Template, int? Sequence, bool Legacy) MatchTemplate(
            string number, string currentTemplate, string legacyTemplate)
        {
            var n = Sequence(number, currentTemplate);
            if (n.HasValue) return (currentTemplate, n, false);
            if (!string.IsNullOrEmpty(legacyTemplate)
                && !string.Equals(legacyTemplate, currentTemplate, StringComparison.Ordinal))
            {
                n = Sequence(number, legacyTemplate);
                if (n.HasValue) return (legacyTemplate, n, true);
            }
            return (currentTemplate, null, false);
        }
    }
}
