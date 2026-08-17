// ══════════════════════════════════════════════════════════════════════════
//  ManualRowPlacer.cs — MAT-SCHED placement of the rows no model can produce.
//
//  Extracted from MaterialScheduleBuilder (which needs a Revit Document and so
//  cannot be tested) because both behaviours here shipped as defects:
//
//    • FindSectionForCategory — the original used
//        section.Title.IndexOf(category) >= 0
//      and "ANYTHING".IndexOf("") returns 0, so a provisional-sum row with an
//      empty Category matched the FIRST section. A 30m electrical PS could file
//      itself under Tools and Equipment, silently.
//
//    • BuildLabourLine — the original summed EVERY model row in the document,
//      once per section, so all eight stages advertised the whole project's
//      labour as their own. AmountUGX stayed 0 so no total was corrupted, but a
//      QS reading "Labour (suggested 45,000,000)" on every section would
//      reasonably read it as per-stage. In a document whose purpose is
//      arithmetic honesty, an advisory number that is wrong is worse than none.
//
//  Both are pure functions over plain data, so they live here and are pinned by
//  ManualRowPlacerTests.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>
    /// One model row's labour contribution, flattened out of the BOQ so this
    /// layer never sees a Revit type. <see cref="HasSplit"/> is false when the
    /// BOQ's L/P/M split was absent or was nulled by a manual override or by
    /// modal-rate aggregation.
    /// </summary>
    public sealed class LabourContribution
    {
        public string TraceRef = "";
        public double LabourTotalUGX;
        public bool HasSplit;
    }

    public static class ManualRowPlacer
    {
        /// <summary>
        /// The stage a provisional-sum category belongs to, resolved through the
        /// SAME Categories table the model rows use, or "" when it routes nowhere.
        ///
        /// Matching by section TITLE does not work: the shipped library routes
        /// category "Electrical Equipment" to the stage titled "ELEMENT 06:
        /// ELECTRICAL INSTALLATION", which does not contain that string. A title
        /// match minted a duplicate section and left the real routing unused.
        /// </summary>
        public static string ResolveStageIdForCategory(IReadOnlyList<StageDefinition> stageDefs, string category)
        {
            if (stageDefs == null || string.IsNullOrWhiteSpace(category)) return "";
            string needle = category.Trim();

            var hit = stageDefs
                .OrderBy(d => d.Order)
                .FirstOrDefault(d => d.Categories != null
                    && d.Categories.Any(c => string.Equals(c, needle, StringComparison.OrdinalIgnoreCase)));
            return hit?.StageId ?? "";
        }

        /// <summary>
        /// The existing section a provisional-sum category belongs to, or null
        /// when the caller should mint one. Resolves by stage id first, then falls
        /// back to a section-title match for categories the library does not list.
        ///
        /// A blank category matches NOTHING. Never let an empty needle match the
        /// first haystack — "ANYTHING".IndexOf("") returns 0, which is how an
        /// uncategorised provisional sum used to file itself under whatever
        /// section happened to come first.
        /// </summary>
        public static StageSection ResolveSection(IEnumerable<StageSection> stages,
                                                  IReadOnlyList<StageDefinition> stageDefs,
                                                  string category)
        {
            if (stages == null || string.IsNullOrWhiteSpace(category)) return null;

            string stageId = ResolveStageIdForCategory(stageDefs, category);
            if (!string.IsNullOrEmpty(stageId))
            {
                var byId = stages.FirstOrDefault(s =>
                    string.Equals(s?.StageId, stageId, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
                return null;   // known stage, not yet materialised — caller mints + inserts
            }

            return FindSectionByTitle(stages, category);
        }

        /// <summary>Title substring match, for categories the stage library does not list.</summary>
        public static StageSection FindSectionByTitle(IEnumerable<StageSection> stages, string category)
        {
            if (stages == null || string.IsNullOrWhiteSpace(category)) return null;
            string needle = category.Trim();
            return stages.FirstOrDefault(s =>
                !string.IsNullOrEmpty(s?.Title)
                && s.Title.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Insert a section at the position its stage definition implies, so a
        /// stage that carried no modelled commodities but does carry a provisional
        /// sum still reads in library order. A section with no definition is
        /// appended — unknown work belongs at the end, not spliced into the middle.
        /// </summary>
        public static void InsertByDefinitionOrder(IList<StageSection> stages,
                                                   IReadOnlyList<StageDefinition> stageDefs,
                                                   StageSection section)
        {
            if (stages == null || section == null) return;

            int OrderOf(string stageId)
            {
                var d = stageDefs?.FirstOrDefault(x =>
                    string.Equals(x.StageId, stageId, StringComparison.OrdinalIgnoreCase));
                return d?.Order ?? int.MaxValue;
            }

            int mine = OrderOf(section.StageId);
            if (mine == int.MaxValue) { stages.Add(section); return; }

            for (int i = 0; i < stages.Count; i++)
            {
                if (OrderOf(stages[i].StageId) > mine) { stages.Insert(i, section); return; }
            }
            stages.Add(section);
        }

        /// <summary>
        /// The labour line for one section. The suggestion counts ONLY the rows
        /// that fed this section's commodities, matched through their trace refs,
        /// and is offered only when every one of those rows carries an L/P/M
        /// split. AmountUGX stays 0 — the figure a QS types is the only one that
        /// reaches a total.
        /// </summary>
        public static LabourLine BuildLabourLine(StageSection section,
                                                 IEnumerable<LabourContribution> allContributions)
        {
            var line = new LabourLine { Description = "Labour", AmountUGX = 0 };

            var refs = new HashSet<string>(
                (section?.Commodities ?? new List<MaterialCommodity>())
                    .SelectMany(c => c?.TraceRefs ?? new List<string>())
                    .Where(r => !string.IsNullOrWhiteSpace(r)),
                StringComparer.OrdinalIgnoreCase);

            if (refs.Count == 0)
            {
                line.SuggestionBasis = "no suggestion — this section traces to no priced model row";
                return line;
            }

            var mine = (allContributions ?? Enumerable.Empty<LabourContribution>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.TraceRef) && refs.Contains(c.TraceRef))
                .ToList();

            int withSplit = mine.Count(c => c.HasSplit);
            if (mine.Count > 0 && withSplit == mine.Count)
            {
                line.SuggestedUGX = mine.Sum(c => c.LabourTotalUGX);
                line.SuggestionBasis = $"{withSplit} of {mine.Count} contributing rows carry an L/P/M split";
            }
            else
            {
                line.SuggestionBasis = mine.Count == 0
                    ? "no suggestion — this section traces to no priced model row"
                    : $"no suggestion — only {withSplit} of {mine.Count} contributing rows carry an L/P/M split";
            }
            return line;
        }
    }
}
