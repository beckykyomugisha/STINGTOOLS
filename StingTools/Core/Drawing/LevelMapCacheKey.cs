// StingTools — the cache key for a session-cached ISO level map (DTW-138).
//
// SheetTagger caches the level map for a tagging run, because TagSheet runs per sheet.
// Keyed on the document alone, the cache outlived what it was built from: a level
// renamed or inserted, or spatial_codes.json edited, and the retag inside
// SheetNumbering.Apply stamped codes from the old map. The key is now everything the
// map is built from — each level's id, name, elevation and storey flag, and the
// project level-code file's timestamp — so a change to any of them misses the cache.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StingTools.Core.Drawing
{
    public static class LevelMapCacheKey
    {
        /// <summary>The key: document, every level (sorted by id), and the level-code file's
        /// last write time (null when the file is absent).</summary>
        public static string Compose(string docKey,
            IEnumerable<(long Id, string Name, double Elevation, bool? IsStorey)> levels,
            DateTime? levelCodeFileUtc)
        {
            var sb = new StringBuilder(docKey ?? "");
            foreach (var l in (levels ?? Enumerable.Empty<(long, string, double, bool?)>()).OrderBy(l => l.Item1))
            {
                sb.Append('|').Append(l.Item1.ToString(CultureInfo.InvariantCulture))
                  .Append(':').Append(l.Item2 ?? "")
                  .Append('@').Append(l.Item3.ToString("R", CultureInfo.InvariantCulture))
                  .Append(l.Item4 == null ? "" : l.Item4.Value ? "S" : "D");
            }
            sb.Append("|codes:").Append(levelCodeFileUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-");
            return sb.ToString();
        }
    }
}
