// StingTools v4 MVP — PlaceHangersCommand.
//
// Walks the current selection (or all MEP curves in the active view
// when the selection is empty), invokes HangerPlacementEngine.Plan,
// and either:
//   • dry-runs: emits a DetailCurve crosshair at every candidate plus
//     a tabular result panel
//   • applies: places a hanger family at each candidate
//
// Phase D ships DRY-RUN only — the engine produces every bit of data
// a family placement would need (host run, XYZ, anchor type, strut
// rod length, trapeze flag) but we don't ship a hanger family with
// the plugin, so writing FamilyInstance.Create would fail. The
// engine + command are the wiring; the family binding is a Phase D.2
// deliverable once the family library lands.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceHangersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var runs = CollectRuns(doc, uidoc);
            if (runs.Count == 0)
            {
                TaskDialog.Show("STING v4 — Place Hangers",
                    "No pipes / ducts / conduits / cable trays found in scope.\n\n" +
                    "Select runs in the view, or switch to a view showing the " +
                    "MEP network and re-run.");
                return Result.Cancelled;
            }

            HangerPlacementResult res;
            try
            {
                res = HangerPlacementEngine.Plan(doc, runs);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlaceHangersCommand: plan failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // Draw a cross at every candidate so the user can preview.
            var view = doc.ActiveView;
            if (view is ViewPlan || view is ViewSection)
            {
                using (var tx = new Transaction(doc, "STING v4 Hanger preview"))
                {
                    try
                    {
                        tx.Start();
                        const double armFt = 0.5;
                        foreach (var c in res.Candidates)
                        {
                            try
                            {
                                var p = c.Point;
                                var h = Line.CreateBound(
                                    new XYZ(p.X - armFt, p.Y, p.Z),
                                    new XYZ(p.X + armFt, p.Y, p.Z));
                                var v = Line.CreateBound(
                                    new XYZ(p.X, p.Y - armFt, p.Z),
                                    new XYZ(p.X, p.Y + armFt, p.Z));
                                doc.Create.NewDetailCurve(view, h);
                                doc.Create.NewDetailCurve(view, v);
                            }
                            catch (Exception ex)
                            { StingLog.Warn($"PlaceHangers: DetailCurve at {c.Point}: {ex.Message}"); }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                        StingLog.Error("PlaceHangers preview transaction", ex);
                    }
                }
            }

            ShowResult(res);
            return Result.Succeeded;
        }

        private static List<Element> CollectRuns(Document doc, Autodesk.Revit.UI.UIDocument uidoc)
        {
            var scope = new List<Element>();
            var sel = uidoc.Selection.GetElementIds();
            if (sel != null && sel.Count > 0)
            {
                foreach (var id in sel)
                {
                    var el = doc.GetElement(id);
                    if (el is Pipe || el is Duct || el is Conduit || el is CableTray)
                        scope.Add(el);
                }
            }
            else
            {
                var view = doc.ActiveView;
                if (view == null) return scope;
                foreach (Type t in new[] { typeof(Pipe), typeof(Duct), typeof(Conduit), typeof(CableTray) })
                {
                    var col = new FilteredElementCollector(doc, view.Id).OfClass(t);
                    foreach (var el in col) scope.Add(el);
                }
            }
            return scope;
        }

        private void ShowResult(HangerPlacementResult res)
        {
            var panel = StingResultPanel.Create("v4 Hanger Placement (Phase D)");
            panel.SetSubtitle("Preview only — families not installed");

            panel.AddSection("SUMMARY")
                 .Metric("Runs scanned",      res.RunsScanned.ToString())
                 .Metric("Candidates",        res.CandidatesGenerated.ToString())
                 .Metric("Concrete anchors",  res.ConcreteAnchorCount.ToString())
                 .Metric("Beam clamps",       res.BeamClampCount.ToString())
                 .Metric("Generic",           res.GenericCount.ToString())
                 .Metric("Trapeze groups",    res.TrapezeGroups.ToString());

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                if (res.Warnings.Count > 40) panel.Text($"(+{res.Warnings.Count - 40} more — see StingLog)");
            }

            if (res.Candidates.Count > 0)
            {
                panel.AddSection("PER-CANDIDATE (first 40)");
                foreach (var c in res.Candidates.Take(40))
                {
                    panel.Text($"#{c.HostRun.Value} {c.AnchorType} span={c.MaxSpanMm:F0}mm rod={c.StrutRodMm:F0}mm" +
                               (c.OnTrapeze ? " [trapeze]" : "") +
                               $"  — {c.SpacingBasis}");
                }
                if (res.Candidates.Count > 40)
                    panel.Text($"(+{res.Candidates.Count - 40} more)");
            }
            panel.Show();
        }
    }
}
