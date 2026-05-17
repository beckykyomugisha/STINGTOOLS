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

                var reports = DrawingDriftDetector.Scan(doc);
                if (reports.Count == 0)
                {
                    TaskDialog.Show("STING — Sync Styles",
                        "Every stamped view is already in sync with its Drawing Type.");
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
                        var applied = DrawingTypePresentation.Apply(doc, v, dt, runAnnotation: false);
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
}
