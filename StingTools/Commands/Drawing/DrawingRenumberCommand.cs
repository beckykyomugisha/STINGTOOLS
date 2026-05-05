// StingTools — Drawing Template Manager · Phase 168
//
// DrawingRenumberCommand compacts sheet-number gaps within a
// (DrawingTypeId, packageId) bucket. The producer's monotonic counter
// never reuses a deleted sheet's number — over time a project that
// deletes-and-recreates sheets accumulates gaps (e.g. 001, 002, 004,
// 007). This command walks every stamped sheet, groups by
// (DrawingTypeId, packageId), parses the trailing digit run as the
// sequence, and rewrites the SheetNumberPattern with a compacted
// 1..N seq. Locked sheets (STING_STYLE_LOCKED_BOOL = Yes) are skipped.
//
// Two-pass renaming avoids the Revit collision where a sheet can't
// briefly share a number with another sheet during the rewrite.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingRenumberCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var stamped = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .Select(s => new
                    {
                        Sheet = s,
                        DtId  = ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID) ?? "",
                        Pkg   = ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "",
                        Locked = DrawingTypeStamper.IsLocked(s),
                    })
                    .Where(x => !string.IsNullOrEmpty(x.DtId))
                    .ToList();

                if (stamped.Count == 0)
                {
                    TaskDialog.Show("STING — Renumber", "No stamped sheets found. Renumber operates on sheets carrying STING_DRAWING_TYPE_ID_TXT.");
                    return Result.Succeeded;
                }

                // Group by (DtId, Pkg). Within each group order by current seq
                // ascending; compact to 1..N.
                var groups = stamped
                    .GroupBy(x => (x.DtId, x.Pkg))
                    .OrderBy(g => g.Key.DtId).ThenBy(g => g.Key.Pkg)
                    .ToList();

                var planned = new List<(ViewSheet sheet, string oldNo, string newNo, bool locked)>();
                foreach (var g in groups)
                {
                    var dt = DrawingTypeRegistry.Get(doc, g.Key.DtId);
                    if (dt == null || string.IsNullOrEmpty(dt.SheetNumberPattern)) continue;

                    var ordered = g
                        .OrderBy(x => DrawingTokenContext.ExtractSeqFromSheetNumber(x.Sheet.SheetNumber) ?? int.MaxValue)
                        .ThenBy(x => x.Sheet.SheetNumber, StringComparer.Ordinal)
                        .ToList();
                    int seq = 1;
                    foreach (var x in ordered)
                    {
                        var tokens = DrawingTokenContext.Build(
                            doc:        doc,
                            dt:         dt,
                            discCode:   dt.Discipline,
                            discipline: dt.Discipline,
                            seq:        seq);
                        // Reuse the resolver via a peek-style call — the
                        // SheetNumberPattern is just another templated string.
                        string newNumber = ResolvePattern(doc, dt.SheetNumberPattern, tokens);
                        if (!string.IsNullOrEmpty(newNumber)
                            && !string.Equals(newNumber, x.Sheet.SheetNumber, StringComparison.Ordinal))
                            planned.Add((x.Sheet, x.Sheet.SheetNumber, newNumber, x.Locked));
                        seq++;
                    }
                }

                if (planned.Count == 0)
                {
                    TaskDialog.Show("STING — Renumber", "Sheet numbers are already gap-free.");
                    return Result.Succeeded;
                }

                var lockedCount = planned.Count(p => p.locked);
                var preview = new StringBuilder();
                preview.AppendLine($"{planned.Count - lockedCount} sheet(s) will be renumbered ({lockedCount} locked, will be skipped):");
                foreach (var p in planned.Take(15))
                    preview.AppendLine($"  {p.oldNo}  →  {p.newNo}{(p.locked ? "  (locked, skip)" : "")}");
                if (planned.Count > 15) preview.AppendLine($"  …({planned.Count - 15} more)");

                var confirm = new TaskDialog("STING — Renumber Sheets")
                {
                    MainInstruction = $"Compact gaps in {groups.Count} bucket(s)",
                    MainContent = preview.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok,
                };
                if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                int renumbered = 0;
                using (var tx = new Transaction(doc, "STING — Renumber Sheets"))
                {
                    tx.Start();
                    // Two-pass rename: first prefix every sheet with a unique
                    // sentinel, then rewrite to the planned final number.
                    // Avoids any "duplicate sheet number" collision mid-batch.
                    string sentinel = "ZZ_STING_RENUM_" + DateTime.UtcNow.Ticks + "_";
                    int idx = 0;
                    foreach (var p in planned)
                    {
                        if (p.locked) continue;
                        try { p.sheet.SheetNumber = sentinel + (idx++).ToString("D4"); }
                        catch (Exception ex) { StingLog.Warn($"Renumber pass1 {p.oldNo}: {ex.Message}"); }
                    }
                    foreach (var p in planned)
                    {
                        if (p.locked) continue;
                        try { p.sheet.SheetNumber = p.newNo; renumbered++; }
                        catch (Exception ex) { StingLog.Warn($"Renumber pass2 → {p.newNo}: {ex.Message}"); }
                    }

                    // Phase 169 — push the post-compaction high-water mark back
                    // into SheetSequenceStore so the producer's next "new sheet"
                    // call picks up from the renumbered total, not the original
                    // gap-laden max.
                    foreach (var g in groups)
                    {
                        var dt = DrawingTypeRegistry.Get(doc, g.Key.DtId);
                        if (dt == null) continue;
                        SheetSequenceStore.Set(doc, g.Key.DtId, g.Key.Pkg ?? "",
                            dt.Discipline ?? "", dt.IsoNaming?.Volume ?? "",
                            g.Count());
                    }
                    tx.Commit();
                }

                TaskDialog.Show("STING — Renumber Sheets",
                    $"Renumbered {renumbered} sheet(s). Skipped {lockedCount} locked sheet(s).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingRenumber", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        // Lightweight pattern resolver matching DrawingProducer.SubstituteTokens.
        // Re-implemented here to avoid making SubstituteTokens public; it does
        // exactly the same {token}/{token:Dn}/${X} substitution.
        private static string ResolvePattern(Document doc, string template, IDictionary<string, string> tokens)
        {
            // Round-trip through the applier's resolver via Peek-style call:
            // build a one-entry profile so Peek does the work, read result.
            var dt = new DrawingType { TitleBlockParams = new Dictionary<string, string> { { "_n", template } } };
            var peek = TitleBlockParamApplier.Peek(doc, dt, tokens);
            return peek.TryGetValue("_n", out var v) ? v : template;
        }
    }
}
