// StingTools — Drawing Template Manager · production inputs for a workflow step
//
// Produce Per Level, Produce From Scope Boxes, Produce From Areas and Produce &
// Export ask their inputs in dialogs, so until now they could only be clicked.
// Inside a workflow preset they read the step's "params" instead:
//
//   drawingTypes     comma/semicolon list of drawing-type ids   default (per level): the
//                                                                plan type each modelled M, E,
//                                                                P, FP, MG discipline routes to
//                                                                (BatchProduceCommons.RoutedMepPerLevel);
//                                                                STING:: boxes: the MEP-bound ones
//   levels           comma/semicolon list of level names        default: every level
//   output           "Views and sheets" | "Views only"          default: views and sheets
//   duplicateOption  "Duplicate" | "DuplicateAsDependent"
//                    | "DuplicateWithDetailing"                 default: Duplicate
//   packageId        drawing package id                         default: none
//   mode             Produce & Export only: "produce" | "finalize"   default: produce
//   sheets           Produce & Export only: "current-revision" | "all" (ExportSheetScope)
//
// This file turns those strings into decisions and says what is wrong with them.
// An unknown drawing-type id or level, or an output value it cannot read, is an
// error: the step fails and names it. Guessing would produce a set nobody asked for.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class HeadlessProductionInputs
    {
        /// <summary>The disciplines an MEP drawing set covers when no types are named.</summary>
        public static readonly string[] MepDisciplines = { "M", "E", "P", "FP", "MG" };

        /// <summary>
        /// True for a drawing type discipline an MEP set covers. One rule for "which STING::
        /// boxes does an MEP preset produce" (ProduceFromScopeBoxes with no types named) and
        /// "which boxes stand in for a per-level plan" (DTW-99), so the two cannot disagree.
        /// </summary>
        public static bool IsMepDiscipline(string discipline)
            => MepDisciplines.Contains((discipline ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

        public const string DuplicateDefault      = "Duplicate";
        public const string DuplicateAsDependent  = "DuplicateAsDependent";
        public const string DuplicateWithDetailing = "DuplicateWithDetailing";

        /// <summary>Split a list param on commas, semicolons or pipes; trimmed, blanks and repeats dropped.</summary>
        public static List<string> ParseList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }

        /// <summary>
        /// "Views and sheets" (the default) → true; "Views only" → false. The two
        /// dialog labels and a few plain spellings are accepted. Anything else → null,
        /// which the caller reports: a typo must not quietly decide whether sheets exist.
        /// </summary>
        public static bool? ParseSheets(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            var k = Normalise(raw);
            switch (k)
            {
                case "viewsandsheets": case "sheets": case "true": case "yes": return true;
                case "viewsonly": case "views": case "false": case "no": return false;
                default: return null;
            }
        }

        /// <summary>The preset's duplicate-option word for a param value; null when unreadable.</summary>
        public static string ParseDuplicateOption(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DuplicateDefault;
            switch (Normalise(raw))
            {
                case "duplicate": return DuplicateDefault;
                case "duplicateasdependent": case "asdependent": case "dependent": return DuplicateAsDependent;
                case "duplicatewithdetailing": case "withdetailing": return DuplicateWithDetailing;
                default: return null;
            }
        }

        /// <summary>
        /// The drawing types to produce. Named ids win, in the order given, and any id
        /// not in the catalogue is returned in <paramref name="unknownIds"/>. With none
        /// named: every type of an MEP discipline whose purpose is one of
        /// <paramref name="purposes"/>.
        /// </summary>
        public static List<DrawingType> SelectTypes(IEnumerable<DrawingType> catalogue,
            IList<string> requestedIds, IList<string> purposes, out List<string> unknownIds)
        {
            unknownIds = new List<string>();
            var all = (catalogue ?? Enumerable.Empty<DrawingType>()).Where(t => t != null && !string.IsNullOrEmpty(t.Id)).ToList();
            if (requestedIds != null && requestedIds.Count > 0)
            {
                var picked = new List<DrawingType>();
                foreach (var id in requestedIds)
                {
                    var hit = all.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (hit == null) unknownIds.Add(id);
                    else if (!picked.Contains(hit)) picked.Add(hit);
                }
                return picked;
            }
            var purposeSet = new HashSet<string>(purposes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var discSet = new HashSet<string>(MepDisciplines, StringComparer.OrdinalIgnoreCase);
            return all.Where(t => discSet.Contains((t.Discipline ?? "").Trim())
                               && purposeSet.Contains((t.Purpose ?? "").Trim()))
                      .ToList();
        }

        /// <summary>
        /// The names to produce for, from <paramref name="available"/>: the named ones
        /// (unknown ones returned in <paramref name="unknown"/>), or all of them.
        /// </summary>
        public static List<string> SelectNames(IList<string> available, IList<string> requested, out List<string> unknown)
        {
            unknown = new List<string>();
            var avail = available ?? new List<string>();
            if (requested == null || requested.Count == 0) return avail.ToList();
            var picked = new List<string>();
            foreach (var r in requested)
            {
                var hit = avail.FirstOrDefault(a => string.Equals(a, r, StringComparison.OrdinalIgnoreCase));
                if (hit == null) unknown.Add(r);
                else if (!picked.Contains(hit)) picked.Add(hit);
            }
            return picked;
        }

        private static string Normalise(string s)
            => new string((s ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
    }
}
