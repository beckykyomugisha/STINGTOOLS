// StingTools — a sheet's current revision, decided once.
//
// WHY THIS FILE EXISTS
// --------------------
// Three places answered "what revision is this sheet at?" and all three took the
// NEWEST revision on the sheet, issued or not:
//
//   * TitleBlockRevisionSyncer wrote Revision.RevisionNumber of the highest
//     SequenceNumber onto the revision box;
//   * the Export Centre read SHEET_CURRENT_REVISION into the file name;
//   * Title Block Populate read the same parameter into the CDE REF cell.
//
// Issuing P03 opens a DRAFT P04 so the team can cloud the next round. The first P04
// cloud put P04 on the sheet, and from that moment the title block and every export
// file name said P04 — a revision nobody had issued. ISO 19650 is unambiguous: a
// drawing carries the revision it was last ISSUED at; work in progress on top of it
// does not change what it is.
//
// The second defect was the number itself. Revision.RevisionNumber is the PROJECT
// number. A sheet first issued in the fifth issue cycle printed P05 on its first issue;
// ISO 19650 (and every UK NA example) numbers per document — its first issue is P01.
// Revit answers that per sheet through ViewSheet.GetRevisionNumberOnSheet, once the
// project is set to per-sheet numbering (RevisionNumberingSetup).
//
// So the rule is: the current revision of a sheet is the latest ISSUED revision on
// that sheet, numbered as it appears ON THAT SHEET. This file is that rule, Revit-free
// and unit-tested; SheetRevisionReader is the thin Revit adapter that feeds it.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>One revision as it relates to one sheet.</summary>
    public sealed class SheetRevisionFact
    {
        /// <summary>The revision's element id (as a long, so this stays Revit-free).</summary>
        public long Id { get; set; }
        /// <summary>Revit's creation-order counter. Higher = later. Used only for ordering.</summary>
        public int Sequence { get; set; }
        /// <summary>True once the revision has been marked Issued (Revit then locks it).</summary>
        public bool Issued { get; set; }
        /// <summary>The number as it prints ON THIS SHEET (GetRevisionNumberOnSheet).</summary>
        public string NumberOnSheet { get; set; } = "";
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>Revision.IssuedTo — STING's repurposed SUIT column.</summary>
        public string IssuedTo { get; set; } = "";
    }

    /// <summary>What a sheet is at, and what is being worked on above it.</summary>
    public sealed class SheetRevisionState
    {
        /// <summary>The latest issued revision on the sheet, or null when none is issued.</summary>
        public SheetRevisionFact Issued { get; set; }
        /// <summary>The newest revision on the sheet, issued or not — what Revit's own
        /// "Current Revision" reports.</summary>
        public SheetRevisionFact Newest { get; set; }
        /// <summary>Un-issued revisions on the sheet, newest first.</summary>
        public IReadOnlyList<SheetRevisionFact> Drafts { get; set; } = Array.Empty<SheetRevisionFact>();

        /// <summary>The number the drawing must carry: the issued revision's number on this
        /// sheet, or "" when nothing has been issued.</summary>
        public string Number => Issued?.NumberOnSheet ?? "";

        /// <summary>True when a draft sits above the issued revision. Normal between
        /// issues; it only matters if something PRINTS the draft (see LeakCheck).</summary>
        public bool HasDraftAhead => Newest != null && !Newest.Issued;
    }

    /// <summary>One sheet whose printed revision disagrees with its issued revision.</summary>
    public sealed class RevisionLeakFinding
    {
        public string SheetNumber { get; set; } = "";
        public string Issued { get; set; } = "";
        public string Found { get; set; } = "";
        /// <summary>Where the disagreeing value lives (parameter name or "native").</summary>
        public string Source { get; set; } = "";
        /// <summary>True for a STING-written stamp (a real leak); false for Revit's own
        /// "Current Revision", which a title block label may print.</summary>
        public bool IsStamp { get; set; }
        public override string ToString() =>
            $"{SheetNumber}: {Source} = '{Found}', issued = '{(Issued.Length == 0 ? "(none)" : Issued)}'";
    }

    public static class SheetRevisionResolver
    {
        /// <summary>Decide the sheet's revision state from its revisions.</summary>
        public static SheetRevisionState Resolve(IEnumerable<SheetRevisionFact> facts)
        {
            var list = (facts ?? Enumerable.Empty<SheetRevisionFact>())
                .Where(f => f != null)
                .OrderByDescending(f => f.Sequence)
                .ToList();
            return new SheetRevisionState
            {
                Newest = list.FirstOrDefault(),
                Issued = list.FirstOrDefault(f => f.Issued),
                Drafts = list.Where(f => !f.Issued).ToList(),
            };
        }

        /// <summary>
        /// Compare what a sheet PRINTS against what it is issued at. <paramref name="stamps"/>
        /// maps a source name (e.g. "SHT_REV_TXT") to the value found there; a blank stamp is
        /// a leak only when an issued revision exists (the sync has not run), and a non-blank
        /// stamp on a never-issued sheet is a leak (a draft was written). The native current
        /// revision is reported when it is a draft — informational, IsStamp = false.
        /// </summary>
        public static List<RevisionLeakFinding> Check(string sheetNumber, SheetRevisionState state,
            IEnumerable<KeyValuePair<string, string>> stamps)
        {
            var found = new List<RevisionLeakFinding>();
            if (state == null) return found;
            string issued = state.Number ?? "";
            foreach (var kv in stamps ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (kv.Value == null) continue;   // parameter absent on this element — nothing printed
                string v = kv.Value.Trim();
                if (!string.Equals(v, issued, StringComparison.OrdinalIgnoreCase))
                    found.Add(new RevisionLeakFinding
                    {
                        SheetNumber = sheetNumber ?? "", Issued = issued, Found = v,
                        Source = kv.Key, IsStamp = true,
                    });
            }
            if (state.HasDraftAhead)
                found.Add(new RevisionLeakFinding
                {
                    SheetNumber = sheetNumber ?? "", Issued = issued,
                    Found = state.Newest.NumberOnSheet ?? "", Source = "native Current Revision (draft)",
                    IsStamp = false,
                });
            return found;
        }
    }

    /// <summary>One issued revision whose on-sheet number would change if the project's
    /// numbering mode changed. Issued numbers are contractual: they are reported and a
    /// change needs explicit consent.</summary>
    public sealed class RenumberChange
    {
        public string SheetNumber { get; set; } = "";
        public long RevisionId { get; set; }
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
        public override string ToString() => $"{SheetNumber}: {Before} → {After}";
    }

    public static class RevisionNumberingPlan
    {
        /// <summary>The issued revisions whose printed number differs between the two
        /// numbering modes. Drafts are ignored — nothing has been issued under them.</summary>
        public static List<RenumberChange> IssuedChanges(
            IEnumerable<(string sheet, long revId, bool issued, string before, string after)> rows)
        {
            return (rows ?? Enumerable.Empty<(string, long, bool, string, string)>())
                .Where(r => r.issued &&
                            !string.Equals((r.before ?? "").Trim(), (r.after ?? "").Trim(), StringComparison.Ordinal))
                .Select(r => new RenumberChange
                {
                    SheetNumber = r.sheet ?? "", RevisionId = r.revId,
                    Before = r.before ?? "", After = r.after ?? "",
                })
                .OrderBy(c => c.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
