// StingTools — Drawing Template Manager · sheet-sequence seeding (Revit-free)
//
// SheetSequenceStore seeds a counter the first time it sees a bucket, from the
// highest sequence already issued in that bucket. This decides whether an
// existing sheet belongs to the bucket. Revit-free so it can be unit-tested.

using System;
using System.Linq;

namespace StingTools.Core.Drawing
{
    internal static class SheetSequenceSeed
    {
        private static readonly char[] Separators = { '-', '_', '.', ' ', '/' };

        /// <summary>
        /// DTW-162 — does an existing sheet (already matched on drawing type and
        /// package) belong to the bucket's discipline and vol?
        ///
        /// The seed used to ignore both, so the first sheet of a new level was
        /// numbered after the highest sequence on EVERY level of the type: a gap
        /// on first use. A blank bucket token matches anything. Otherwise the
        /// token must be one of the sheet number's segments, or (discipline
        /// only) equal the sheet's stamped SHT_DISC_TXT. A sheet whose number
        /// shows neither is left out: the composer's unique-number backstop
        /// handles the rare case where that under-seeds.
        /// </summary>
        internal static bool MatchesBucket(string sheetNumber, string stampedDiscipline, string discipline, string vol)
        {
            var segs = (sheetNumber ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            bool Has(string token) => segs.Any(s => string.Equals(s, token, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(discipline)
                && !Has(discipline)
                && !string.Equals((stampedDiscipline ?? "").Trim(), discipline, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrWhiteSpace(vol) && !Has(vol))
                return false;
            return true;
        }
    }
}
