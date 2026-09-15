// StingTools — Drawing Template Manager · what FORM of document a sheet is
//
// WHY THIS FILE EXISTS
// --------------------
// A site plan with three detail views on it was issued as
// SAH-PLNS-ZZ-01-LG-A-0001. "LG" is the ISO 19650 form code for a LEGEND, and
// the sheet is a drawing.
//
// Two separate faults produced that, and both are the same shape as the
// discipline census this replaced:
//
//   1. ViewType.DraftingView counted as a legend. A drafting view is a DRAWING —
//      it is where details, sections through details and standard construction
//      notes are drafted. Filing one as a legend is simply the wrong category,
//      and it is the most common non-model view on an architectural sheet, so
//      the error fires constantly.
//
//   2. ANY match won. One legend on a sheet of eight plans made the whole sheet
//      a legend, because the rule asked "is there a legend anywhere" rather than
//      "what is this sheet". A general arrangement carries other view types on
//      purpose; that is what a sheet IS.
//
// So: the title decides when it says something ("DOOR SCHEDULE", "LEGEND"), and
// otherwise the DOMINANT view type decides, with DR as the tie-break — a mixed
// sheet is a drawing, which is what it looks like to anyone holding it.
//
// Revit-free so StingTools.Tags.Tests can exercise it; the caller maps ViewType
// to these codes and passes a tally.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>ISO 19650 form codes for a sheet: DR drawing, SH schedule,
    /// M3 3D model, LG legend.</summary>
    public static class SheetFormResolver
    {
        public const string Drawing = "DR";
        public const string Schedule = "SH";
        public const string Model3D = "M3";
        public const string Legend = "LG";

        /// <summary>The form a sheet TITLE declares, or null. Whole words, for the
        /// same reason the discipline keywords are: "LEGEND" must not match
        /// "LEGENDARY", and a substring rule is how "DATA" came to match "DATA
        /// SHEET".</summary>
        public static string FromTitle(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName)) return null;

            var words = new HashSet<string>(
                sheetName.ToUpperInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

            foreach (var rule in TitleRules)
                if (rule.Value.Any(words.Contains))
                    return rule.Key;

            return null;
        }

        /// <summary>The form a tally of view types points to.
        ///
        /// Dominance, not presence. The leader must hold MORE THAN HALF the views
        /// to override DR, because a sheet is a drawing unless it is mostly
        /// something else — one schedule beside five plans does not make a schedule
        /// sheet, and the rule that said it did put "LG" on an issued drawing.</summary>
        public static string FromCensus(IDictionary<string, int> census)
        {
            if (census == null || census.Count == 0) return null;

            var counted = census.Where(kv => kv.Value > 0).ToList();
            if (counted.Count == 0) return null;

            double total = counted.Sum(kv => kv.Value);
            var top = counted.OrderByDescending(kv => kv.Value)
                             .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                             .First();

            if (top.Key == Drawing) return Drawing;
            return top.Value / total > 0.5 ? top.Key : Drawing;
        }

        /// <summary>Title first, then the tally, then DR.</summary>
        public static string Resolve(string sheetName, IDictionary<string, int> census)
            => FromTitle(sheetName) ?? FromCensus(census) ?? Drawing;

        private static readonly char[] Separators =
            { ' ', '-', '_', ',', '.', '/', '(', ')', '&', ':', ';', '\t' };

        /// <summary>In decision order. A sheet titled "SCHEDULES AND LEGENDS" is a
        /// schedule sheet; which of the two wins has to be readable here rather than
        /// decided by dictionary order.</summary>
        private static readonly List<KeyValuePair<string, string[]>> TitleRules =
            new List<KeyValuePair<string, string[]>>
            {
                new KeyValuePair<string, string[]>(Schedule,
                    new[] { "SCHEDULE", "SCHEDULES" }),
                new KeyValuePair<string, string[]>(Legend,
                    new[] { "LEGEND", "LEGENDS", "KEYNOTES", "SYMBOLS", "ABBREVIATIONS" }),
                new KeyValuePair<string, string[]>(Model3D,
                    new[] { "AXONOMETRIC", "ISOMETRIC", "PERSPECTIVE", "3D" }),
            };
    }
}
