// StingTools — Drawing Template Manager · the ISO level a sheet's stamp carries (DTW-129)
//
// Two things name a sheet's level: the ISO sheet NUMBER production gives it
// (DrawingProducer, through SheetNumberPolicy.LevelToken) and the sheet level STAMP
// Tag Sheets writes into SHT_TAG_1 (ParameterHelpers.DeriveSheetLevel). After DTW-105
// the number took a project-declared level code (spatial_codes.json) while the stamp
// still took the elevation-derived one, so on a level with a declared code the number
// and the stamp disagreed.
//
// Both now read ONE map (DrawingProducer.BuildIsoLevelMap → IsoLevelCode.BuildMap with
// the declared codes) and ONE per-level lookup (SheetNumberPolicy.LevelToken). This
// file is the stamp's half: the per-level lookup plus the sheet rule (one level → its
// code, several → ZZ, none → XX).
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class SheetLevelCode
    {
        /// <summary>
        /// The ISO code of one level: the code the sheet number takes for it — the map's
        /// (declared, else elevation-derived), else what the name states.
        /// </summary>
        public static string ForLevel(string levelName, IDictionary<string, string> isoCodesByName)
            => SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, levelName, isoCodesByName);

        /// <summary>
        /// The sheet's level from the levels its viewports draw (null entries = views with
        /// no level): one code → that code; several → <see cref="IsoLevelCode.Multiple"/>
        /// ("applies to more than one level", a real ISO code); none →
        /// <see cref="IsoLevelCode.NotApplicable"/>.
        /// </summary>
        public static string ForSheet(IEnumerable<string> viewLevelNames, IDictionary<string, string> isoCodesByName)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in viewLevelNames ?? Enumerable.Empty<string>())
            {
                if (name == null) continue;
                var code = ForLevel(name, isoCodesByName);
                if (!string.IsNullOrEmpty(code)) codes.Add(code);
            }
            if (codes.Count == 0) return IsoLevelCode.NotApplicable;
            return codes.Count == 1 ? codes.First() : IsoLevelCode.Multiple;
        }
    }
}
