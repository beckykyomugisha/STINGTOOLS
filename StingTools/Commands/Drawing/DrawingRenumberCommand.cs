// StingTools — Drawing Template Manager · Phase 168
//
// DrawingRenumberCommand compacts sheet-number gaps. The producer's monotonic
// counter never reuses a deleted sheet's number, so a project that deletes and
// recreates sheets accumulates gaps (001, 002, 004, 007). This command closes
// them, bucket by bucket, and resets each counter so production resumes after
// the compacted run.
//
// It plans through SheetNumberEngine and numbers through
// DrawingProducer.SubstituteTokens — the same two pieces production uses — so
// a renumbered sheet gets exactly the number production would have given it:
//
//   * the BUCKET is the producer's counter bucket (SheetNumberEngine.
//     CounterBucket), so the run that is compacted is the run the counter
//     hands out. Under the ISO policy that is the number's template, not the
//     drawing type.
//   * each sheet keeps its OWN level and mark, recovered from the production
//     context stamped on it (STING_SHEET_CONTEXT_TXT). P-3: the old code
//     passed no level, so "A-RCP-L02-001" renumbered to "A-RCP-001".
//   * locked sheets keep their number AND their sequence; the compacted run
//     steps round them, and the counter resumes above the highest of either.
//     P-4: the counter used to be set to the group COUNT, below a locked
//     sheet's number, so the next production collided with it.
//   * a target already held by a sheet outside the plan pins the mover where
//     it is and is reported. P-4: the old two-pass rename left such a sheet on
//     "ZZ_STING_RENUM_<ticks>_NNNN", committed.
//   * the rename goes through SheetNumbering.Apply, which parks, renames,
//     restores on refusal, re-tags the ISO identifier and records history.

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
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var allSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder).ToList();

                var policy = SheetNumberPolicy.Parse(DrawingProducer.ReadSheetNumberPolicy(doc));
                var notes = new List<string>();
                var items = new List<SheetNumberEngine.RenumberItem>();
                var sheetById = new Dictionary<string, ViewSheet>(StringComparer.Ordinal);
                int unplannable = 0;

                foreach (var s in allSheets)
                {
                    var dtId = ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID) ?? "";
                    if (string.IsNullOrEmpty(dtId)) continue;
                    var dt = DrawingTypeRegistry.Get(doc, dtId);
                    if (dt == null)
                    {
                        notes.Add($"{s.SheetNumber}: drawing type '{dtId}' is not in the catalogue; left alone.");
                        continue;
                    }
                    var pkg = ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "";
                    var pattern = SheetNumberPolicy.ResolvePattern(dt, policy);
                    if (string.IsNullOrEmpty(pattern)) continue;

                    ReadProductionContext(s, out var levelName, out var tag);
                    var probeTokens = DrawingProducer.BuildTokenDict(doc, dt, levelName, tag, pkg, 0);
                    var template = DrawingProducer.NumberTemplate(pattern, dt, levelName, tag, probeTokens);
                    if (template == null)
                    {
                        // No single {seq} token: nothing to compact, and guessing a
                        // sequence out of a number that has none is how a "gap" gets
                        // closed by rewriting the wrong segment.
                        unplannable++;
                        continue;
                    }

                    // The sequence is read against the number's own shape. An ISO
                    // number ends in a revision, so the last digit run is not it.
                    int? current = SheetNumberEngine.ExtractSequence(s.SheetNumber, template);
                    if (!current.HasValue)
                    {
                        var stamped = ParameterHelpers.GetInt(s, DrawingTypeStamper.PARAM_SHEET_SEQUENCE, 0);
                        current = stamped > 0 ? stamped : SheetNumberEngine.ExtractTrailingSequence(s.SheetNumber);
                    }

                    var id = s.Id.Value.ToString();
                    sheetById[id] = s;
                    items.Add(new SheetNumberEngine.RenumberItem
                    {
                        Id = id,
                        CurrentNumber = s.SheetNumber,
                        Bucket = SheetNumberEngine.CounterBucket(policy, template, dt.Id, pkg,
                            dt.Discipline ?? "", dt.IsoNaming?.Volume ?? ""),
                        CurrentSeq = current,
                        Locked = DrawingTypeStamper.IsLocked(s),
                        NumberFor = seq => SheetNumberTidy.Collapse(DrawingProducer.SubstituteTokens(
                            pattern, dt, levelName, tag, seq,
                            DrawingProducer.BuildTokenDict(doc, dt, levelName, tag, pkg, seq))),
                    });
                }

                if (items.Count == 0)
                {
                    TaskDialog.Show("STING — Renumber",
                        "No renumberable sheets found. Renumber operates on sheets carrying " +
                        "STING_DRAWING_TYPE_ID_TXT whose pattern has one {seq} token." +
                        (notes.Count > 0 ? "\n\n" + string.Join("\n", notes.Take(10)) : ""));
                    return Result.Succeeded;
                }

                var plan = SheetNumberEngine.PlanRenumber(items, allSheets.Select(s => s.SheetNumber));
                int lockedCount = items.Count(i => i.Locked);
                int buckets = items.Select(i => i.Bucket).Distinct().Count();

                if (plan.Moves.Count == 0 && plan.Conflicts.Count == 0)
                {
                    TaskDialog.Show("STING — Renumber", "Sheet numbers are already gap-free.");
                    return Result.Succeeded;
                }

                var preview = new StringBuilder();
                preview.AppendLine($"{plan.Moves.Count} sheet(s) will be renumbered across {buckets} counter bucket(s); " +
                                   $"{lockedCount} locked sheet(s) keep their numbers.");
                foreach (var m in plan.Moves.Take(15)) preview.AppendLine($"  {m.From}  →  {m.To}");
                if (plan.Moves.Count > 15) preview.AppendLine($"  …({plan.Moves.Count - 15} more)");
                if (plan.Conflicts.Count > 0)
                {
                    preview.AppendLine();
                    preview.AppendLine($"{plan.Conflicts.Count} sheet(s) left unchanged to avoid a clash:");
                    foreach (var c in plan.Conflicts.Take(8)) preview.AppendLine("  " + c);
                }
                if (unplannable > 0)
                    preview.AppendLine($"\n{unplannable} stamped sheet(s) skipped: their pattern has no single {{seq}} token.");
                preview.AppendLine("\nSheet-number policy: " + policy);

                var confirm = new TaskDialog("STING — Renumber Sheets")
                {
                    MainInstruction = "Compact sheet-number gaps",
                    MainContent = preview.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok,
                };
                if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                var changes = plan.Moves
                    .Where(m => sheetById.ContainsKey(m.Id))
                    .Select(m => new SheetNumbering.Change { Sheet = sheetById[m.Id], Old = m.From, New = m.To })
                    .ToList();
                var outcome = SheetNumbering.Apply(doc, changes, "STING — Renumber Sheets");

                // Sequence stamps for what actually moved. A sheet Revit refused was
                // put back on its old number by Apply, so its old sequence stands.
                // Counters take the plan's high-water mark, which already includes
                // locked and pinned sheets and never falls below a refused sheet's
                // sequence (refused sheets keep numbers at or below it).
                int stampFailures = 0;
                using (var tx = new Transaction(doc, "STING — Renumber: counters"))
                {
                    tx.Start();
                    foreach (var m in plan.Moves)
                    {
                        if (!sheetById.TryGetValue(m.Id, out var sh)) continue;
                        if (!string.Equals(sh.SheetNumber, m.To, StringComparison.Ordinal)) continue;
                        if (!DrawingTypeStamper.StampSheetSequence(sh, m.Seq)) stampFailures++;
                    }
                    foreach (var kv in plan.HighWater)
                    {
                        int keep = Math.Max(kv.Value, items
                            .Where(i => i.Bucket == kv.Key && i.CurrentSeq.HasValue
                                     && sheetById.TryGetValue(i.Id, out var sh)
                                     && string.Equals(sh.SheetNumber, i.CurrentNumber, StringComparison.Ordinal))
                            .Select(i => i.CurrentSeq.Value).DefaultIfEmpty(0).Max());
                        try { SheetSequenceStore.SetForBucket(doc, kv.Key, keep); }
                        catch (Exception ex) { notes.Add($"Counter '{kv.Key}' not reset: {ex.Message}"); }
                    }
                    tx.Commit();
                }

                var report = new StringBuilder();
                report.AppendLine($"Renumbered {outcome.Done} sheet(s). {lockedCount} locked sheet(s) kept their numbers.");
                if (outcome.Failed > 0)
                {
                    report.AppendLine($"{outcome.Failed} rename(s) refused by Revit and restored:");
                    foreach (var f in outcome.Failures.Take(8)) report.AppendLine(f);
                }
                if (plan.Conflicts.Count > 0) report.AppendLine($"{plan.Conflicts.Count} sheet(s) left unchanged to avoid a clash.");
                if (stampFailures > 0)
                    report.AppendLine($"{stampFailures} renumbered sheet(s) could not have {DrawingTypeStamper.PARAM_SHEET_SEQUENCE} stamped.");
                foreach (var n in notes.Take(8)) report.AppendLine(n);
                if (outcome.HistoryPath == null) report.AppendLine("Rename history could NOT be recorded.");
                TaskDialog.Show("STING — Renumber Sheets", report.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingRenumber", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// The level name and tag (mark / spool) a sheet was produced for, as the
        /// producer stamped them in "level::room::tag[::scopeBox]". Falls back to
        /// PRJ_SHEET_LEVEL_TXT for sheets produced before the context stamp; an
        /// unresolved "{lvl}" the producer left there is treated as absent.
        /// </summary>
        private static void ReadProductionContext(ViewSheet s, out string levelName, out string tag)
        {
            levelName = null; tag = null;
            var ctx = DrawingTypeStamper.ReadSheetContext(s);
            if (!string.IsNullOrEmpty(ctx))
            {
                var parts = ctx.Split(new[] { "::" }, StringSplitOptions.None);
                if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0])) levelName = parts[0];
                if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2])) tag = parts[2];
            }
            if (levelName == null)
            {
                var stamped = ParameterHelpers.GetString(s, "PRJ_SHEET_LEVEL_TXT");
                if (!string.IsNullOrWhiteSpace(stamped) && !stamped.StartsWith("{", StringComparison.Ordinal))
                    levelName = stamped;
            }
        }
    }
}
