// StingTools — Revit adapter for SheetRevisionResolver.
//
// Reads a sheet's revisions into SheetRevisionFact rows. The number is always taken
// from ViewSheet.GetRevisionNumberOnSheet: under per-project numbering it equals
// Revision.RevisionNumber, under per-sheet numbering it is the number the sheet prints
// — so one call is right in both modes. The DECISION (latest issued) is made by the
// Revit-free resolver.
//
// Also owns the per-sheet numbering switch (RevisionNumberingSetup) because it needs
// the same per-sheet read to show what an issued revision would renumber to.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class SheetRevisionReader
    {
        /// <summary>The sheet's revision state. Never null; empty when unreadable.</summary>
        public static SheetRevisionState Read(Document doc, ViewSheet sheet)
            => SheetRevisionResolver.Resolve(Facts(doc, sheet));

        /// <summary>The issued revision's number on this sheet, or "" when none is issued.</summary>
        public static string IssuedNumber(Document doc, ViewSheet sheet) => Read(doc, sheet).Number;

        /// <summary>The issued revision element itself, or null.</summary>
        public static Revision IssuedRevision(Document doc, ViewSheet sheet)
        {
            var s = Read(doc, sheet);
            return s.Issued == null ? null : doc.GetElement(new ElementId(s.Issued.Id)) as Revision;
        }

        public static List<SheetRevisionFact> Facts(Document doc, ViewSheet sheet)
        {
            var facts = new List<SheetRevisionFact>();
            if (doc == null || sheet == null) return facts;
            IList<ElementId> ids;
            try { ids = sheet.GetAllRevisionIds(); }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetRevisionReader: revisions of '{sheet.SheetNumber}' unreadable — {ex.Message}");
                return facts;
            }
            foreach (var id in ids ?? new List<ElementId>())
            {
                if (!(doc.GetElement(id) is Revision r)) continue;
                var f = new SheetRevisionFact
                {
                    Id = id.Value,
                    Sequence = r.SequenceNumber,
                    Issued = r.Issued,
                    Date = r.RevisionDate ?? "",
                    Description = r.Description ?? "",
                };
                try { f.IssuedTo = r.IssuedTo ?? ""; } catch { f.IssuedTo = ""; }
                f.NumberOnSheet = NumberOnSheet(sheet, r);
                facts.Add(f);
            }
            return facts;
        }

        /// <summary>The number <paramref name="r"/> prints on <paramref name="sheet"/>, else
        /// the project number. Empty when the revision has no number at all (numbering "None"):
        /// that is the truth, and an empty revision is reported downstream as not set (NOREV in
        /// the file name). It used to invent "R{sequence}", which then reached the title block,
        /// the export file name and the ACC metadata as if it were a real revision (R13).</summary>
        public static string NumberOnSheet(ViewSheet sheet, Revision r)
        {
            try
            {
                string n = sheet.GetRevisionNumberOnSheet(r.Id);
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetRevisionReader: number of revision {r.Id} on '{sheet.SheetNumber}' — {ex.Message}");
            }
            try
            {
                string n = r.RevisionNumber;
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetRevisionReader: project number of revision {r.Id} — {ex.Message}");
            }
            StingLog.Warn($"SheetRevisionReader: revision seq {r.SequenceNumber} has no number on '{sheet?.SheetNumber}' " +
                          "(its numbering is 'None') — reported as no revision, not given an invented one.");
            return "";
        }
    }

    /// <summary>
    /// ISO 19650 numbers revisions per document, so a sheet's first issue is P01 whatever
    /// cycle it joins in. Revit does that only in per-sheet numbering mode, which nothing
    /// in STING set. This switches it — safely:
    ///   * already per-sheet → nothing to do;
    ///   * no issued revision anywhere → switching renumbers nothing issued, so it is done;
    ///   * issued revisions exist → the switch is TRIED in a transaction, every issued
    ///     revision whose printed number would change is listed, and the transaction is
    ///     committed only with explicit consent. Issued drawings are never silently renumbered.
    /// </summary>
    public static class RevisionNumberingSetup
    {
        public sealed class Outcome
        {
            public bool AlreadyPerSheet;
            public bool Switched;
            public bool NeedsConsent;
            public List<RenumberChange> IssuedChanges = new List<RenumberChange>();
            public string Message = "";
        }

        public static bool IsPerSheet(Document doc)
        {
            try { return RevisionSettings.GetRevisionSettings(doc).RevisionNumbering == RevisionNumbering.PerSheet; }
            catch (Exception ex) { StingLog.Warn("RevisionNumberingSetup: settings unreadable — " + ex.Message); return false; }
        }

        /// <summary>
        /// Switch to per-sheet numbering. <paramref name="consent"/> is asked ONLY when issued
        /// revisions would renumber; it receives the list and returns true to proceed. Null
        /// consent = never renumber issued drawings (safe for startup / unattended callers).
        /// Must be called with no open transaction.
        /// </summary>
        public static Outcome EnsurePerSheet(Document doc, Func<IReadOnlyList<RenumberChange>, bool> consent)
        {
            var o = new Outcome();
            if (doc == null) { o.Message = "No document."; return o; }
            if (IsPerSheet(doc)) { o.AlreadyPerSheet = true; o.Message = "Revision numbering is already per sheet."; return o; }

            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder).ToList();
            var before = Snapshot(doc, sheets);

            using (var tx = new Transaction(doc, "STING Revision Numbering Per Sheet"))
            {
                tx.Start();
                try
                {
                    RevisionSettings.GetRevisionSettings(doc).RevisionNumbering = RevisionNumbering.PerSheet;
                    doc.Regenerate();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    o.Message = "Could not switch revision numbering to per sheet: " + ex.Message;
                    StingLog.Warn("RevisionNumberingSetup: " + o.Message);
                    return o;
                }
                var after = Snapshot(doc, sheets);
                o.IssuedChanges = RevisionNumberingPlan.IssuedChanges(before.Select(b =>
                {
                    after.TryGetValue((b.Key.sheet, b.Key.rev), out var a);
                    return (b.Key.sheet, b.Key.rev, b.Value.issued, b.Value.number, a.number ?? "");
                }));

                if (o.IssuedChanges.Count > 0 && (consent == null || !consent(o.IssuedChanges)))
                {
                    tx.RollBack();
                    o.NeedsConsent = true;
                    o.Message = $"Per-sheet numbering NOT applied: {o.IssuedChanges.Count} issued revision(s) " +
                                "would print a different number. Run 'Per-Sheet #' (BIM tab > Revision Management) to review and confirm.";
                    StingLog.Info("RevisionNumberingSetup: " + o.Message);
                    return o;
                }
                tx.Commit();
            }
            o.Switched = true;
            o.Message = o.IssuedChanges.Count == 0
                ? "Revision numbering set to per sheet (no issued revision changed number)."
                : $"Revision numbering set to per sheet; {o.IssuedChanges.Count} issued revision number(s) changed with consent.";
            StingLog.Info("RevisionNumberingSetup: " + o.Message);
            return o;
        }

        private static Dictionary<(string sheet, long rev), (bool issued, string number)> Snapshot(
            Document doc, IEnumerable<ViewSheet> sheets)
        {
            var map = new Dictionary<(string, long), (bool, string)>();
            foreach (var s in sheets)
                foreach (var f in SheetRevisionReader.Facts(doc, s))
                    map[(s.SheetNumber ?? "", f.Id)] = (f.Issued, f.NumberOnSheet);
            return map;
        }
    }
}
