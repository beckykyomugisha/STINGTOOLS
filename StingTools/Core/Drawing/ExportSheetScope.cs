// StingTools — Produce & Export: which stamped sheets an export covers
//
// "Finalize + Export" exported every STING-stamped sheet, so issuing revision C
// re-published the sheets revision C never touched, under their old revision. When
// the project has a current revision (the newest in the revision sequence), the
// export is limited to the sheets carrying it; "all" keeps the old behaviour.
//
//   params.sheets   "current-revision" | "all"
//                   default: current-revision for mode=finalize, all for mode=produce
//                   (sheets a produce run has just made carry no revision yet)
//
// Revit-free: StingTools.Tags.Tests compiles this file. Sheets and revisions are
// passed as ids so the rule can be tested without the Revit API.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class ExportSheetScope
    {
        public const string CurrentRevision = "current-revision";
        public const string All = "all";

        /// <summary>
        /// The scope a step param names; <paramref name="defaultScope"/> when blank; null
        /// with <paramref name="error"/> for anything else.
        /// </summary>
        public static string Parse(string raw, string defaultScope, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) return defaultScope;
            var k = new string(raw.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            switch (k)
            {
                case "currentrevision": case "current": case "revision": return CurrentRevision;
                case "all": case "allsheets": case "allstamped": return All;
                default:
                    error = $"params.sheets '{raw.Trim()}' is not 'current-revision' or 'all'.";
                    return null;
            }
        }

        /// <summary>
        /// The sheets to export. <see cref="All"/>, or no current revision
        /// (<paramref name="currentRevisionId"/> null): every sheet. Otherwise only those
        /// whose revisions (<paramref name="revisionIdsOf"/>) include the current one, in
        /// the input order. <paramref name="excluded"/> counts the sheets left out.
        /// </summary>
        public static List<T> Select<T>(IList<T> sheets, Func<T, IEnumerable<long>> revisionIdsOf,
            long? currentRevisionId, string scope, out int excluded)
        {
            var all = (sheets ?? new List<T>()).ToList();
            excluded = 0;
            if (scope != CurrentRevision || currentRevisionId == null || revisionIdsOf == null) return all;
            var picked = all.Where(s => (revisionIdsOf(s) ?? Enumerable.Empty<long>()).Contains(currentRevisionId.Value)).ToList();
            excluded = all.Count - picked.Count;
            return picked;
        }
    }
}
