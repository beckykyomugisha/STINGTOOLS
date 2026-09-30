// DeliverableRevisionRule.cs — where a deliverable's revision comes from.
//
// A deliverable row (deliverables.json) kept its own revision counter, bumped by the
// lifecycle, while the drawings it stands for were revised and issued in Revit. The two
// counters drifted as soon as anyone issued a sheet outside the deliverable workflow,
// and promotion made it worse (P03 → C03 in the row, C01 on the drawing).
//
// The rule: a deliverable LINKED to sheets (SheetNumbers) takes its revision from the
// Revit issue — the sheets' issued revision, as they print it. A deliverable with no
// linked sheet (a report, a schedule, a model) keeps its own counter, and says so via
// RevisionSource so nobody mistakes it for a drawing-derived value.
//
// Revit-free, unit-tested (StingTools.Tags.Tests).

using System;
using System.Collections.Generic;
using System.Linq;

namespace Planscape.Docs.Templates
{
    public sealed class DeliverableRevisionDecision
    {
        /// <summary>The revision the deliverable should carry.</summary>
        public string Revision { get; set; } = "";
        /// <summary>"SHEETS" when derived from the Revit issue, "OWN" when the deliverable's
        /// own counter was kept.</summary>
        public string Source { get; set; } = RevisionSources.Own;
        /// <summary>True when the linked sheets are at DIFFERENT issued revisions.</summary>
        public bool SheetsDisagree { get; set; }
        /// <summary>Why — shown in the log and written to the row's note.</summary>
        public string Note { get; set; } = "";
        public bool FromSheets => Source == RevisionSources.Sheets;
    }

    public static class RevisionSources
    {
        public const string Sheets = "SHEETS";
        public const string Own = "OWN";
    }

    public static class DeliverableRevisionRule
    {
        /// <param name="ownRevision">The deliverable's current revision (its own counter).</param>
        /// <param name="linkedSheets">Sheet numbers the deliverable is linked to; empty = unlinked.</param>
        /// <param name="issuedBySheet">Sheet number → issued revision as that sheet prints it
        /// ("" or absent = never issued). Sheets not in the model are simply absent.</param>
        public static DeliverableRevisionDecision Derive(string ownRevision,
            IEnumerable<string> linkedSheets, IReadOnlyDictionary<string, string> issuedBySheet)
        {
            var sheets = (linkedSheets ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string own = (ownRevision ?? "").Trim();

            if (sheets.Count == 0)
                return new DeliverableRevisionDecision
                {
                    Revision = own, Source = RevisionSources.Own,
                    Note = "not linked to any sheet — the deliverable keeps its own revision counter",
                };

            var issued = new List<(string sheet, string rev)>();
            foreach (var s in sheets)
                if (issuedBySheet != null && issuedBySheet.TryGetValue(s, out string r) && !string.IsNullOrWhiteSpace(r))
                    issued.Add((s, r.Trim()));

            if (issued.Count == 0)
                return new DeliverableRevisionDecision
                {
                    Revision = own, Source = RevisionSources.Own,
                    Note = $"linked sheet(s) {string.Join(", ", sheets)} have no issued revision yet — own counter kept",
                };

            var distinct = issued.Select(i => i.rev).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string best = distinct.OrderByDescending(Rank).ThenByDescending(Number).First();
            var d = new DeliverableRevisionDecision { Revision = best, Source = RevisionSources.Sheets };
            if (distinct.Count > 1)
            {
                d.SheetsDisagree = true;
                d.Note = "linked sheets are at different issued revisions (" +
                         string.Join(", ", issued.Select(i => $"{i.sheet}={i.rev}")) + $"); the most advanced, {best}, is used";
            }
            else d.Note = $"from the Revit issue of {string.Join(", ", issued.Select(i => i.sheet))}";
            int missing = sheets.Count - issued.Count;
            if (missing > 0)
                d.Note += $"; {missing} linked sheet(s) not issued or not in the model";
            return d;
        }

        /// <summary>Contractual beats preliminary; anything else ranks lowest.</summary>
        internal static int Rank(string rev)
        {
            string p = new string((rev ?? "").TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
            return p == "C" ? 2 : p == "P" ? 1 : 0;
        }

        internal static int Number(string rev)
        {
            string n = new string((rev ?? "").SkipWhile(char.IsLetter).ToArray());
            return int.TryParse(n, out int v) ? v : -1;
        }
    }
}
