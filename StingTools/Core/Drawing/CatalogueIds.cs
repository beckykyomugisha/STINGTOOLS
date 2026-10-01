// StingTools — Drawing Template Manager
//
// CatalogueIds — id hygiene for the drawing-type / style-pack editors.
// Revit-free so it is unit-tested (DTW-186).

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class CatalogueIds
    {
        /// <summary>"&lt;id&gt;-copy", then "-copy-2", "-copy-3"… — the first
        /// one no existing entry uses. A second Clone used to mint a duplicate
        /// id, and Merge keeps only one entry per id (DTW-186).</summary>
        public static string UniqueCopyId(string sourceId, IEnumerable<string> existing)
        {
            var taken = new HashSet<string>(existing.Where(s => !string.IsNullOrEmpty(s)), StringComparer.OrdinalIgnoreCase);
            var stem = (string.IsNullOrWhiteSpace(sourceId) ? "drawing-type" : sourceId.Trim()) + "-copy";
            if (!taken.Contains(stem)) return stem;
            for (int n = 2; ; n++)
                if (!taken.Contains(stem + "-" + n)) return stem + "-" + n;
        }

        /// <summary>Empty or duplicate ids in a catalogue, as user-facing lines.
        /// Merge skips a blank id and keeps one entry per id, so saving either
        /// would silently drop entries (DTW-186).</summary>
        public static List<string> IdProblems(string what, IEnumerable<string> ids)
        {
            var problems = new List<string>();
            var list = ids.ToList();
            int blank = list.Count(string.IsNullOrWhiteSpace);
            if (blank > 0) problems.Add($"{blank} {what}(s) have no id.");
            foreach (var g in list.Where(s => !string.IsNullOrWhiteSpace(s))
                                  .GroupBy(s => s.Trim(), StringComparer.OrdinalIgnoreCase)
                                  .Where(g => g.Count() > 1))
                problems.Add($"{what} id '{g.Key}' is used {g.Count()} times.");
            return problems;
        }
    }
}
