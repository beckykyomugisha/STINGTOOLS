// ══════════════════════════════════════════════════════════════════════════
//  IntermediateMeasures.cs — MAT-SCHED memorandum-row marking.
//
//  A compound take-off emits both the intermediate measure and the things you
//  actually buy from it: 390 m2 of brickwork AND the 23,377 bricks derived
//  from it, 11.66 m3 of mortar AND its cement and sand. Both belong in the
//  document — the area is how a QS checks the count — but only one of them is
//  purchasable, and the first real export offered rate cells on all of them.
//
//  Marking is CONDITIONAL on the children actually being present. A project
//  whose data produces no block count must keep its blockwork area priceable,
//  or the fix to a double-count becomes an omission — which is worse, because
//  a missing line is invisible and a duplicated one is at least arguable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One intermediate kind and the kinds bought in its place.</summary>
    public sealed class IntermediateMeasureRule
    {
        public string Kind = "";
        public List<string> Children = new List<string>();
        /// <summary>Shown against the row so the reader knows why it has no rate.</summary>
        public string Note = "";
    }

    public static class IntermediateMeasureMarker
    {
        /// <summary>
        /// Mark every commodity whose SourceKind names an intermediate AND at
        /// least one of whose children is present ANYWHERE in the document.
        ///
        /// Document-wide, not stage-wide, on purpose: a wall's blockwork routes
        /// to a storey stage while its plaster cement routes to Finishes, so a
        /// per-section check would leave half the intermediates priceable.
        /// </summary>
        public static int Apply(MaterialScheduleDocument doc,
                                IReadOnlyList<IntermediateMeasureRule> rules)
        {
            if (doc == null || rules == null || rules.Count == 0) return 0;

            var present = new HashSet<string>(
                doc.Stages.SelectMany(s => s.Commodities)
                          .Select(c => c?.SourceKind ?? "")
                          .Where(k => !string.IsNullOrWhiteSpace(k)),
                StringComparer.OrdinalIgnoreCase);

            var byKind = new Dictionary<string, IntermediateMeasureRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rules)
                if (r != null && !string.IsNullOrWhiteSpace(r.Kind) && !byKind.ContainsKey(r.Kind.Trim()))
                    byKind[r.Kind.Trim()] = r;

            int marked = 0;
            foreach (var c in doc.Stages.SelectMany(s => s.Commodities))
            {
                if (c == null || string.IsNullOrWhiteSpace(c.SourceKind)) continue;
                if (!byKind.TryGetValue(c.SourceKind.Trim(), out var rule)) continue;

                bool anyChild = (rule.Children ?? new List<string>())
                    .Any(k => !string.IsNullOrWhiteSpace(k) && present.Contains(k.Trim()));
                if (!anyChild) continue;   // nothing replaces it — leave it priceable

                c.IsMemorandum = true;
                c.MemorandumNote = string.IsNullOrWhiteSpace(rule.Note)
                    ? "measured quantity shown for checking; bought as its constituents"
                    : rule.Note;
                marked++;
            }
            return marked;
        }
    }
}
