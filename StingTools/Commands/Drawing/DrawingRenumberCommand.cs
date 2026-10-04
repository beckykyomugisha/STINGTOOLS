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

                    ReadProductionContext(doc, s, out var levelName, out var tag);
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
                    // DTW-222: DTW-198 changed {lvl} in non-ISO numbers to ShortLevel, so a
                    // sheet numbered before it on a long, digit-ending level ("E-Basement-004"
                    // where today gives "E-Basemen1-…") is not in today's template. It is
                    // still the same numbering, not a shape change: it is read, compacted and
                    // renumbered in its own shape, so it does not move to the new one.
                    string legacyTemplate = null, legacyLevel = null;
                    if (LegacyLevelShape.NumberShapeChanged(pattern, levelName))
                    {
                        legacyLevel = LegacyLevelShape.NumberLevel(pattern, levelName, dt.IsoNaming?.Level);
                        legacyTemplate = SheetNumberEngine.Template(pattern, dt.Discipline ?? "", legacyLevel,
                            dt.System ?? "", tag ?? "", tag ?? "", dt.Purpose ?? "", probeTokens);
                    }
                    var match = LegacyLevelShape.MatchTemplate(s.SheetNumber, template, legacyTemplate);
                    int? current = match.Sequence;
                    bool keepsLegacyShape = match.Legacy;
                    if (keepsLegacyShape) template = match.Template;
                    // DTW-211: a number not in its pattern's shape is converted, not compacted.
                    bool shapeChange = !current.HasValue;
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
                        ShapeChange = shapeChange,
                        Issued = shapeChange && HasIssuedRevision(doc, s),
                        NumberFor = keepsLegacyShape
                            ? (Func<int, string>)(seq => SheetNumberTidy.Collapse(DrawingProducer.ApplyTokenPattern(
                                pattern, dt.Discipline ?? "", legacyLevel, dt.System ?? "", tag ?? "", tag ?? "",
                                dt.Purpose ?? "", seq, DrawingProducer.BuildTokenDict(doc, dt, levelName, tag, pkg, seq))))
                            : seq => SheetNumberTidy.Collapse(DrawingProducer.SubstituteTokens(
                                pattern, dt, levelName, tag, seq,
                                DrawingProducer.BuildTokenDict(doc, dt, levelName, tag, pkg, seq))),
                    });
                }

                if (items.Count == 0)
                {
                    BatchProduceCommons.Show("STING — Renumber",
                        "No renumberable sheets found. Renumber operates on sheets carrying " +
                        "STING_DRAWING_TYPE_ID_TXT whose pattern has one {seq} token." +
                        (notes.Count > 0 ? "\n\n" + string.Join("\n", notes.Take(10)) : ""));
                    return Result.Succeeded;
                }

                // DTW-7: the policy decides whether a sheet already carrying a full
                // ISO identifier may move — under the profile policy it is pinned.
                // DTW-211: issued sheets are not converted to another numbering shape unless
                // asked — in a preset by params.convertIssued="true", interactively below.
                bool convertIssued = BatchProduceCommons.Headless
                    && string.Equals((WorkflowEngine.StepParam("convertIssued") ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase);
                var allNumbers = allSheets.Select(s => s.SheetNumber).ToList();
                var plan = SheetNumberEngine.PlanRenumber(items, allNumbers, policy, convertIssued);
                int lockedCount = items.Count(i => i.Locked);
                int buckets = items.Select(i => i.Bucket).Distinct().Count();

                if (plan.Moves.Count == 0 && plan.Conflicts.Count == 0)
                {
                    BatchProduceCommons.Show("STING — Renumber", "Sheet numbers are already gap-free." +
                        (plan.IsoPreserved.Count > 0
                            ? $"\n{plan.IsoPreserved.Count} sheet(s) carrying a full ISO 19650 identifier were kept as they are."
                            : "") +
                        (plan.IssuedKept.Count > 0
                            ? $"\n{plan.IssuedKept.Count} issued sheet(s) are not in the {policy} numbering and were kept "
                              + "(convert them deliberately: params.convertIssued=\"true\" in a preset, or the option in this dialog)."
                            : ""));
                    return Result.Succeeded;
                }

                if (!BatchProduceCommons.Headless)
                {
                    var choice = ConfirmPlan(plan, policy, buckets, lockedCount, unplannable);
                    if (choice == ConfirmChoice.Cancel) return Result.Cancelled;
                    if (choice == ConfirmChoice.ConvertIssuedToo)
                        plan = SheetNumberEngine.PlanRenumber(items, allNumbers, policy, convertIssued: true);
                }
                return ApplyPlan(doc, plan, items, sheetById, policy, lockedCount, notes);
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingRenumber", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private enum ConfirmChoice { Cancel, Renumber, ConvertIssuedToo }

        /// <summary>
        /// DTW-211: the preview. Conversions (a number changing shape — under the ISO
        /// policy, a profile-era sheet becoming an ISO identifier) are listed apart from
        /// gap-closing moves and named in the title; issued sheets a conversion would
        /// touch are kept unless the user picks the second option.
        /// </summary>
        private static ConfirmChoice ConfirmPlan(SheetNumberEngine.RenumberPlan plan, SheetNumberPolicyKind policy,
            int buckets, int lockedCount, int unplannable)
        {
            var conversions = new HashSet<string>(plan.Conversions.Select(m => m.Id), StringComparer.Ordinal);
            var gaps = plan.Moves.Where(m => !conversions.Contains(m.Id)).ToList();
            var preview = new StringBuilder();
            if (plan.Conversions.Count > 0)
            {
                preview.AppendLine($"{plan.Conversions.Count} sheet(s) are not numbered in the {policy} pattern and will be CONVERTED to it:");
                foreach (var m in plan.Conversions.Take(15)) preview.AppendLine($"  {m.From}  →  {m.To}");
                if (plan.Conversions.Count > 15) preview.AppendLine($"  …({plan.Conversions.Count - 15} more)");
                preview.AppendLine();
            }
            if (plan.IssuedKept.Count > 0)
            {
                preview.AppendLine($"{plan.IssuedKept.Count} ISSUED sheet(s) would also be converted; they keep their numbers unless you choose to convert them:");
                foreach (var k in plan.IssuedKept.Take(10)) preview.AppendLine("  " + k);
                if (plan.IssuedKept.Count > 10) preview.AppendLine($"  …({plan.IssuedKept.Count - 10} more)");
                preview.AppendLine();
            }
            preview.AppendLine($"{gaps.Count} sheet(s) will be renumbered to close gaps across {buckets} counter bucket(s); " +
                               $"{lockedCount} locked sheet(s) keep their numbers.");
            if (plan.IsoPreserved.Count > 0)
            {
                preview.AppendLine($"{plan.IsoPreserved.Count} sheet(s) already carry a full ISO 19650 identifier and keep it:");
                foreach (var p in plan.IsoPreserved.Take(5)) preview.AppendLine("  " + p);
                if (plan.IsoPreserved.Count > 5) preview.AppendLine($"  …({plan.IsoPreserved.Count - 5} more)");
            }
            foreach (var m in gaps.Take(15)) preview.AppendLine($"  {m.From}  →  {m.To}");
            if (gaps.Count > 15) preview.AppendLine($"  …({gaps.Count - 15} more)");
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
                MainInstruction = plan.Conversions.Count > 0
                    ? $"Convert {plan.Conversions.Count} sheet(s) to {policy} numbering" + (gaps.Count > 0 ? $" and compact {gaps.Count}" : "")
                    : "Compact sheet-number gaps",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                plan.IssuedKept.Count > 0 ? $"Renumber, keeping the {plan.IssuedKept.Count} issued sheet(s) on their numbers" : "Renumber");
            if (plan.IssuedKept.Count > 0)
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    $"Renumber and convert the {plan.IssuedKept.Count} issued sheet(s) too",
                    "Their numbers change on drawings already sent out — reissue them.");
            var r = confirm.Show();
            if (r == TaskDialogResult.CommandLink1) return ConfirmChoice.Renumber;
            if (r == TaskDialogResult.CommandLink2) return ConfirmChoice.ConvertIssuedToo;
            return ConfirmChoice.Cancel;
        }

        private static Result ApplyPlan(Document doc, SheetNumberEngine.RenumberPlan plan,
            List<SheetNumberEngine.RenumberItem> items, Dictionary<string, ViewSheet> sheetById,
            SheetNumberPolicyKind policy, int lockedCount, List<string> notes)
        {
            {
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
                    StingTx.Commit(tx);
                }

                var report = new StringBuilder();
                report.AppendLine($"Renumbered {outcome.Done} sheet(s). {lockedCount} locked sheet(s) kept their numbers.");
                if (plan.IsoPreserved.Count > 0)
                {
                    report.AppendLine($"{plan.IsoPreserved.Count} sheet(s) kept their ISO 19650 identifier (policy: {policy}).");
                    foreach (var p in plan.IsoPreserved) StingLog.Info($"Renumber: {p}");
                }
                if (outcome.Failed > 0)
                {
                    report.AppendLine($"{outcome.Failed} rename(s) refused by Revit and restored:");
                    foreach (var f in outcome.Failures.Take(8)) report.AppendLine(f);
                }
                if (plan.Conflicts.Count > 0) report.AppendLine($"{plan.Conflicts.Count} sheet(s) left unchanged to avoid a clash.");
                if (plan.Conversions.Count > 0)
                {
                    report.AppendLine($"{plan.Conversions.Count} of the renumbered sheet(s) were converted to the {policy} numbering:");
                    foreach (var m in plan.Conversions) StingLog.Info($"Renumber conversion: {m.From} -> {m.To}");
                    foreach (var m in plan.Conversions.Take(8)) report.AppendLine($"  {m.From}  →  {m.To}");
                }
                if (plan.IssuedKept.Count > 0)
                {
                    report.AppendLine($"{plan.IssuedKept.Count} issued sheet(s) kept their numbers (not converted).");
                    foreach (var k in plan.IssuedKept) StingLog.Info($"Renumber: {k}");
                }
                if (stampFailures > 0)
                    report.AppendLine($"{stampFailures} renumbered sheet(s) could not have {DrawingTypeStamper.PARAM_SHEET_SEQUENCE} stamped.");
                foreach (var n in notes.Take(8)) report.AppendLine(n);
                if (outcome.HistoryPath == null) report.AppendLine("Rename history could NOT be recorded.");
                BatchProduceCommons.Show("STING — Renumber Sheets", report.ToString());
                return Result.Succeeded;
            }
        }

        /// <summary>DTW-211: whether any revision on <paramref name="s"/> is issued.</summary>
        private static bool HasIssuedRevision(Document doc, ViewSheet s)
        {
            try
            {
                foreach (var id in s.GetAllRevisionIds())
                    if (doc.GetElement(id) is Revision rev && rev.Issued) return true;
            }
            catch (Exception ex)
            {
                // Cannot tell: treat it as issued, so it is kept rather than converted unasked.
                StingLog.Warn($"DrawingRenumber revisions of {s?.SheetNumber}: {ex.Message} — treated as issued.");
                return true;
            }
            return false;
        }

        /// <summary>
        /// The level name and tag (mark / spool) a sheet was produced for, as the
        /// producer stamped them in "level::room::tag[::scopeBox]". Falls back to
        /// PRJ_SHEET_LEVEL_TXT for sheets produced before the context stamp; an
        /// unresolved "{lvl}" the producer left there is treated as absent.
        /// </summary>
        private static void ReadProductionContext(Document doc, ViewSheet s, out string levelName, out string tag)
        {
            levelName = null; tag = null;
            var ctx = DrawingTypeStamper.ReadSheetContext(s);
            if (!string.IsNullOrEmpty(ctx))
            {
                var parts = ctx.Split(new[] { "::" }, StringSplitOptions.None);
                if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0])) levelName = parts[0];
                if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2])) tag = parts[2];

                // DTW-118: the stamp's name part is the level's name WHEN PRODUCED. After a
                // rename the ISO level map (keyed by current names) has no code for it, and
                // the plan came out "...-Level1-DR-...". The id (DTW-42) names the level;
                // its current name is what the producer would use today.
                var ids = ProductionContextIds.Parse(ctx);
                if (ids.LevelId.HasValue && doc != null)
                {
                    try
                    {
                        if (doc.GetElement(new ElementId(ids.LevelId.Value)) is Level lvl && !string.IsNullOrEmpty(lvl.Name))
                            levelName = lvl.Name;
                    }
                    catch (Exception ex) { StingLog.Warn($"DrawingRenumber level #{ids.LevelId} for {s.SheetNumber}: {ex.Message}"); }
                }
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
