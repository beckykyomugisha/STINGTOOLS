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

        /// <summary>An existing sheet as the DTW-225 decision sees it.</summary>
        internal readonly struct ExistingSheet
        {
            public ExistingSheet(string drawingTypeId, string context, string number)
            { DrawingTypeId = drawingTypeId; Context = context; Number = number; }
            public string DrawingTypeId { get; }
            /// <summary>The sheet's production context stamp (STING_SHEET_CONTEXT_TXT).</summary>
            public string Context { get; }
            public string Number { get; }
        }

        /// <summary>
        /// DTW-225: the {lvl} a NEW sheet's number takes when its drawing type already has
        /// sheets on this level in the pre-DTW-198 shape. Null means "today's shape".
        /// Without it a fourth sheet beside "E-Basement-001..003" came out "E-Basemen1-004"
        /// and one level's numbers ran in two shapes. The caller uses the returned level for
        /// the number AND its counter template, so the seed reads the old numbers too.
        /// </summary>
        internal static string NewSheetNumberLevel(string pattern, string drawingTypeId,
            string levelName, long? levelId, string fallback,
            string currentTemplate, string legacyTemplate, IEnumerable<ExistingSheet> existing)
        {
            // Only a non-ISO pattern on a long, digit-ending level ever had two shapes.
            if (!NumberShapeChanged(pattern, levelName)) return null;
            if (string.IsNullOrEmpty(legacyTemplate) || existing == null) return null;
            foreach (var s in existing)
            {
                if (!string.Equals(s.DrawingTypeId, drawingTypeId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SameLevel(s.Context, levelName, levelId)) continue;
                // Today's template is tried first, so a sheet already in the new shape
                // never counts as legacy.
                if (MatchTemplate(s.Number, currentTemplate, legacyTemplate).Legacy)
                    return NumberLevel(pattern, levelName, fallback);
            }
            return null;
        }

        /// <summary>The stamp's level is this level: by id when both have one (a rename
        /// keeps the id), else by the stamp's name part.</summary>
        private static bool SameLevel(string context, string levelName, long? levelId)
        {
            if (string.IsNullOrEmpty(context)) return false;
            var ids = ProductionContextIds.Parse(context);
            if (ids.LevelId.HasValue && levelId.HasValue) return ids.LevelId.Value == levelId.Value;
            return string.Equals(ProductionContextIds.LevelName(context), levelName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
