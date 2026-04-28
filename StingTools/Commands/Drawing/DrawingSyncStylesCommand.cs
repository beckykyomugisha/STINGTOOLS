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
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var allReports = DrawingDriftDetector.Scan(doc);
                // C-8 / E-2: skip reports whose only entries are suppressed by
                // a controlling view template — re-applying does nothing for
                // those, and they would otherwise resurface every Sync run.
                var reports = allReports.Where(r => r.AnyActionable).ToList();
                int suppressedOnly = allReports.Count - reports.Count;
                if (reports.Count == 0)
                {
                    string msg2 = suppressedOnly > 0
                        ? $"Every actionable view is already in sync with its Drawing Type.\n{suppressedOnly} view(s) have fields controlled by a view template — those are informational only."
                        : "Every stamped view is already in sync with its Drawing Type.";
                    TaskDialog.Show("STING — Sync Styles", msg2);
                    return Result.Succeeded;
                }

                var confirm = new TaskDialog("STING — Sync Styles")
                {
                    MainInstruction = $"{reports.Count} view(s) have drifted",
                    MainContent = BuildPreview(reports),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok,
                };
                if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                int resynced = 0;
                var warnings = new System.Collections.Generic.List<string>();
                using (var tx = new Transaction(doc, "STING — Sync Drawing Type Styles"))
                {
                    tx.Start();
                    foreach (var r in reports)
                    {
                        if (!(doc.GetElement(r.ViewId) is View v)) continue;
                        var dt = DrawingTypeRegistry.Get(doc, r.DrawingTypeId);
                        if (dt == null) continue;
                        // Phase 137 — explicit annotation skips so SyncStyles
                        // re-applies VG/template/managed-template state without
                        // running auto-tag / auto-dim / decorative / spot passes.
                        var applied = DrawingTypePresentation.Apply(doc, v, dt, new DrawingTypePresentation.ApplyOptions
                        {
                            AnnotationOptions = new AnnotationRunOptions
                            {
                                SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true
                            }
                        });
                        if (applied.Warnings.Count > 0)
                            warnings.AddRange(applied.Warnings.Select(w => $"[{v.Name}] {w}"));
                        if (applied.ScaleApplied || applied.DetailLevelApplied || applied.TemplateApplied || applied.PackApplied)
                            resynced++;
                    }
                    tx.Commit();
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Re-synced {resynced} of {reports.Count} drifted view(s).");
                if (warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var w in warnings.Take(15)) sb.AppendLine("  " + w);
                    if (warnings.Count > 15) sb.AppendLine($"  …({warnings.Count - 15} more)");
                }
                TaskDialog.Show("STING — Sync Styles", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingSyncStyles", ex);
                msg = ex.Message;
                return Result.Failed;
            }
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
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var reports = DrawingDriftDetector.Scan(doc)
                    .Where(r => r.Any || r.AnySuppressed).ToList();
                if (reports.Count == 0)
                {
                    TaskDialog.Show("STING — Force Resync",
                        "No stamped views need re-syncing — every profile-controlled value matches the live state.");
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
                if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                int resynced = 0;
                using (var tx = new Transaction(doc, "STING — Force Resync (Suppressed)"))
                {
                    tx.Start();
                    foreach (var r in reports)
                    {
                        if (!(doc.GetElement(r.ViewId) is View v)) continue;
                        var dt = DrawingTypeRegistry.Get(doc, r.DrawingTypeId);
                        if (dt == null) continue;
                        var applied = DrawingTypePresentation.Apply(doc, v, dt, new DrawingTypePresentation.ApplyOptions
                        {
                            AnnotationOptions = new AnnotationRunOptions
                            {
                                SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true
                            }
                        });
                        if (applied.ScaleApplied || applied.DetailLevelApplied
                            || applied.TemplateApplied || applied.PackApplied
                            || applied.TokenProfileApplied)
                            resynced++;
                    }
                    tx.Commit();
                }
                TaskDialog.Show("STING — Force Resync", $"Re-applied profile on {resynced} view(s).");
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
