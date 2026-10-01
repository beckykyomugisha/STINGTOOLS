// StingTools — Drawing Template Manager · Week 4
//
// DrawingSyncStylesCommand re-applies every stamped view's profile:
// useful after editing a ViewStylePack or DrawingType and wanting
// to propagate changes to existing views. Drift detector finds the
// out-of-spec views; Apply re-runs scale / detail / template /
// pack / annotation for each via DrawingTypePresentation.
//
// Workflow: user edits corp-standard-plan via the editor → saves
// to project override → presses 'Sync Styles' → every stamped
// plan view that references that pack snaps back into line. Views
// flagged STING_STYLE_LOCKED_BOOL are skipped so hand-tuned views
// are safe from blanket resync.

using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingSyncStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                // DTW-125: material-class filters are rebuilt from the model's current
                // materials on this pass, not taken from a session-old cache.
                ViewStylePackApplier.InvalidateMaterialClassFilterCache();

                // Phase 183 — pick up views affected by on-disk profile /
                // pack edits even when no drift would have shown up in
                // the live VG state yet. LiveProfileSync stages the
                // changed-id set whenever the registries are reloaded.
                var liveAffected = LiveProfileSync.GetAffectedViewIds(doc);

                // DTW-14: Scan returns a report for a view whose ONLY items are
                // template-suppressed. Re-applying those is Force Resync's job —
                // Sync Styles re-applied them anyway, inflating the count and
                // behaving like a force. Keep the actionable reports; count the rest.
                var scanned = DrawingDriftDetector.Scan(doc);
                var reports = scanned.Where(r => r.AnyActionable).ToList();
                int suppressedOnly = scanned.Count(r => !r.AnyActionable && r.AnySuppressed);
                if (reports.Count == 0 && liveAffected.Count == 0)
                {
                    string msg2 = suppressedOnly > 0
                        ? $"Every actionable view is already in sync with its Drawing Type.\n{suppressedOnly} view(s) have fields controlled by a view template — those are informational only (Force Resync re-applies them)."
                        : "Every stamped view is already in sync with its Drawing Type.";
                    PresetDialog.Show("STING — Sync Styles", msg2, ref msg);
                    return Result.Succeeded;
                }

                // Merge LiveProfileSync-affected views into the report
                // set so the resync pass picks them up. Build synthetic
                // DriftReports for views that didn't appear in the live
                // scan but whose profile / pack source has changed.
                if (liveAffected.Count > 0)
                {
                    var existing = new HashSet<long>(reports.Select(r => r.ViewId?.Value ?? -1L));
                    foreach (var vid in liveAffected)
                    {
                        if (existing.Contains(vid.Value)) continue;
                        if (!(doc.GetElement(vid) is View vv) || vv.IsTemplate) continue;
                        var stampedId = DrawingTypeStamper.Read(vv);
                        if (string.IsNullOrEmpty(stampedId)) continue;
                        var synth = new DriftReport
                        {
                            ViewId = vid,
                            ViewName = vv.Name,
                            DrawingTypeId = stampedId,
                        };
                        synth.Drifts.Add("PROFILE_RELOADED: profile or pack edited since last load");
                        reports.Add(synth);
                    }
                }

                var confirm = new TaskDialog("STING — Sync Styles")
                {
                    MainInstruction = $"{reports.Count} view(s) have drifted",
                    MainContent = BuildPreview(reports),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok,
                };
                // DTW-14: a raw Show() inside a workflow preset waited for a click
                // nobody could give. In a preset, running the step is the consent.
                if (!BatchProduceCommons.Confirm(confirm)) return Result.Cancelled;

                var warnings = new System.Collections.Generic.List<string>();
                var run = ResyncEach(doc, reports, "STING — Sync Drawing Type Styles", warnings);
                int resynced = run.Changed;

                // Phase 183 — clear the staged diff once every affected view has been
                // re-applied. DTW-201: only when every one of them was kept; a view
                // skipped or rolled back keeps the diff, so the next run picks it up.
                var liveIds = new HashSet<long>(liveAffected.Select(i => i.Value));
                var liveMissed = run.NotKept.Concat(run.NotReached).Where(n => liveIds.Contains(n.Id)).ToList();
                if (liveMissed.Count == 0) LiveProfileSync.ConsumeStagedDiff(doc);
                else warnings.Add($"{liveMissed.Count} view(s) affected by the profile edit were not re-synced; "
                                  + "the edit stays staged so the next Sync Styles re-applies them.");

                var sb = new StringBuilder();
                if (run.Stopped) sb.AppendLine(warnings[0]);
                sb.AppendLine($"Re-synced {resynced} of {reports.Count} drifted view(s).");
                if (run.NotKept.Count > 0)
                {
                    sb.AppendLine($"{run.NotKept.Count} view(s) NOT re-synced (skipped or rolled back, nothing changed on them):");
                    foreach (var n in run.NotKept.Take(15)) sb.AppendLine("  " + n.Name);
                    if (run.NotKept.Count > 15) sb.AppendLine($"  …({run.NotKept.Count - 15} more)");
                }
                if (suppressedOnly > 0)
                    sb.AppendLine($"{suppressedOnly} view(s) differ only where their view template controls the field — not touched (use Force Resync).");
                if (warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var w in warnings.Take(15)) sb.AppendLine("  " + w);
                    if (warnings.Count > 15) sb.AppendLine($"  …({warnings.Count - 15} more)");
                    if (PresetDialog.Quiet)
                        foreach (var w in warnings) StingLog.Warn($"Sync Styles: {w}");
                }
                PresetDialog.Show("STING — Sync Styles", sb.ToString(), ref msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingSyncStyles", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        internal sealed class ResyncRun
        {
            public int Changed;
            /// <summary>Views skipped (not editable) or rolled back — nothing changed on them.</summary>
            public List<(long Id, string Name)> NotKept = new List<(long, string)>();
            /// <summary>DTW-204: the user stopped the run with Escape.</summary>
            public bool Stopped;
            /// <summary>Views not reached because the run was stopped.</summary>
            public List<(long Id, string Name)> NotReached = new List<(long, string)>();
        }

        /// <summary>
        /// DTW-201: one transaction per view, inside one group (one undo). A single view
        /// owned by someone else used to roll back every view, and the report still said
        /// "Re-synced N". In a workshared model each view (and a sheet's title blocks) is
        /// pre-checked and skipped by name when it cannot be edited; a refusal at commit
        /// rolls back that view only. <see cref="ResyncRun.Changed"/> counts committed
        /// changes only.
        /// </summary>
        internal static ResyncRun ResyncEach(Document doc, List<DriftReport> reports, string groupName, List<string> warnings)
        {
            var run = new ResyncRun();
            using (var runner = new ProductionItemRunner(doc, "Sync Styles", reports.Count))
            using (var tg = new TransactionGroup(doc, groupName))
            {
                tg.Start();
                foreach (var r in reports)
                {
                    if (runner.ShouldStop())   // DTW-204: between views, never inside one
                    {
                        run.NotReached.Add((r.ViewId?.Value ?? -1L, r.ViewName));
                        continue;
                    }
                    if (!(doc.GetElement(r.ViewId) is View v)) continue;
                    var dt = DrawingTypeRegistry.Get(doc, r.DrawingTypeId);
                    if (dt == null) continue;
                    string name = v is ViewSheet vs ? $"{vs.SheetNumber} - {vs.Name}" : v.Name;
                    bool changed = false;
                    var outcome = runner.Run($"{groupName} - {name}", $"[{name}]",
                        () => runner.Preflight.Active ? runner.Preflight.Check(EditedBy(doc, v)) : null,
                        () =>
                        {
                            var applied = Resync(doc, v, dt, out changed);
                            if (applied.Warnings.Count > 0)
                                warnings.AddRange(applied.Warnings.Select(w => $"[{name}] {w}"));
                        },
                        st => $"[{name}] the transaction did not commit ({st}); not re-synced.",
                        warnings);
                    if (outcome == ProductionItemRunner.ItemResult.Committed) { if (changed) run.Changed++; }
                    else run.NotKept.Add((v.Id.Value, name));
                }
                tg.Assimilate();   // a stopped run keeps what it committed
                run.Stopped = runner.Stopped;
                if (run.Stopped) warnings.Insert(0, runner.StoppedLine("view(s)"));
            }
            return run;
        }

        /// <summary>What re-syncing <paramref name="v"/> writes to: the view, and on a sheet its title blocks.</summary>
        private static ICollection<ElementId> EditedBy(Document doc, View v)
        {
            var ids = new List<ElementId> { v.Id };
            if (v is ViewSheet)
                ids.AddRange(new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType().ToElementIds());
            return ids;
        }

        /// <summary>
        /// Re-apply one stamped view's profile. DTW-4: a sheet has no scale,
        /// template or pack — its drift (TITLE_BLOCK_PARAM, title-block spec) is
        /// healed by <see cref="DrawingTypePresentation.ApplyToSheet"/>. Running the
        /// view pipeline on it no-oped, so sheet drift was reported and never
        /// healed. <paramref name="changed"/> is true when something was written.
        /// </summary>
        internal static DrawingTypePresentation.ApplyResult Resync(
            Document doc, View v, DrawingType dt, out bool changed)
        {
            if (v is ViewSheet sheet)
            {
                var sr = DrawingTypePresentation.ApplyToSheet(doc, sheet, dt);
                changed = sr.TitleBlockParamsWritten > 0;
                return sr;
            }
            // Phase 137 — explicit annotation skips so SyncStyles
            // re-applies VG/template/managed-template state without
            // running auto-tag / auto-dim / decorative / spot passes.
            var applied = DrawingTypePresentation.Apply(doc, v, dt, new DrawingTypePresentation.ApplyOptions
            {
                AnnotationOptions = new AnnotationRunOptions
                {
                    SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true
                },
                SkipSymbolDriftCheck = true // heal pass — drift is handled separately
            });
            changed = applied.ScaleApplied || applied.DetailLevelApplied
                      || applied.TemplateApplied || applied.PackApplied
                      || applied.TokenProfileApplied;
            return applied;
        }

        private static string BuildPreview(System.Collections.Generic.List<DriftReport> reports)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Sample of drifted views (first 10):");
            foreach (var r in reports.Take(10))
            {
                sb.AppendLine($"  {r.ViewName}  [{r.DrawingTypeId}]");
                foreach (var d in r.Drifts) sb.AppendLine($"     · {d}");
            }
            if (reports.Count > 10) sb.AppendLine($"  …({reports.Count - 10} more)");
            sb.AppendLine();
            sb.AppendLine("OK = re-apply profile to every drifted view (skips STYLE_LOCKED views).");
            return sb.ToString();
        }
    }

    /// <summary>
    /// FG-10 / INT-07: force-resync command. Re-applies every stamped
    /// view's profile, including the views whose drifts are suppressed
    /// because their currently-applied view template controls the
    /// parameter. The applier still respects STING_STYLE_LOCKED_BOOL —
    /// only the template-control suppression is overridden.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingForceResyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var reports = DrawingDriftDetector.Scan(doc)
                    .Where(r => r.Any || r.AnySuppressed).ToList();
                if (reports.Count == 0)
                {
                    PresetDialog.Show("STING — Force Resync",
                        "No stamped views need re-syncing — every profile-controlled value matches the live state.", ref msg);
                    return Result.Succeeded;
                }

                var confirm = new TaskDialog("STING — Force Resync (Suppressed)")
                {
                    MainInstruction = $"{reports.Count} view(s) will be re-applied",
                    MainContent =
                        "Force-resync re-runs every stamped view's profile, including the ones whose " +
                        "drift was previously suppressed because the view template controls the parameter. " +
                        "Use this after editing a view template that intentionally diverges from the profile " +
                        "but you want the profile back as the authority.\n\n" +
                        "STING_STYLE_LOCKED views are still skipped.",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                if (!BatchProduceCommons.Confirm(confirm)) return Result.Cancelled;

                // DTW-201: per view, as Sync Styles — one owned view no longer rolls back all.
                var warnings = new List<string>();
                var run = DrawingSyncStylesCommand.ResyncEach(doc, reports, "STING — Force Resync (Suppressed)", warnings);
                var sb = new StringBuilder();
                if (run.Stopped) sb.AppendLine(warnings[0]);
                sb.AppendLine($"Re-applied profile on {run.Changed} view(s).");
                if (run.NotKept.Count > 0)
                {
                    sb.AppendLine($"{run.NotKept.Count} view(s) NOT re-applied (skipped or rolled back):");
                    foreach (var n in run.NotKept.Take(15)) sb.AppendLine("  " + n.Name);
                }
                foreach (var w in warnings) StingLog.Warn($"Force Resync: {w}");
                foreach (var w in warnings.Take(10)) sb.AppendLine("  " + w);
                PresetDialog.Show("STING — Force Resync", sb.ToString(), ref msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingForceResync", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }
}
