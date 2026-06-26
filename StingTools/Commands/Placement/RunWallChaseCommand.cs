// Phase 139.3 — Run In-Wall Chase command.
//
// Pick a host wall + two endpoints, look up the matching WALL_CHASE
// rule (or default), then call InWallChaseRouter.Route inside a single
// transaction. Reports availability vs requirement on a depth check
// failure so designers know whether to thicken the wall, swap the pipe
// size, or reject the run.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunWallChaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData?.Application;
            if (uiapp?.ActiveUIDocument?.Document == null) { message = "No active document."; return Result.Failed; }
            try { RunInteractive(uiapp)?.Show(); return Result.Succeeded; }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error($"RunWallChaseCommand: {ex.Message}", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Interactive chase route — pick a wall + two points, preview the
        /// depth-check verdict in a rolled-back transaction, confirm, then commit.
        /// Returns a result Builder so the Placement Centre can render it inline
        /// (run via its action ExternalEvent so picking + transactions are on the
        /// API thread). Throws OperationCanceledException if the user escapes a pick.
        /// </summary>
        public static StingResultPanel.Builder RunInteractive(UIApplication uiapp)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            Reference wallRef = uidoc.Selection.PickObject(ObjectType.Element,
                new WallSelectionFilter(), "Pick the host wall for the chase route");
            Wall wall = doc.GetElement(wallRef) as Wall;
            if (wall == null)
                return StingResultPanel.Create("STING — Wall Chase").AddSection("RESULT").Text("Selected element is not a wall.");

            XYZ p1 = uidoc.Selection.PickPoint("Pick the chase start point on the wall");
            XYZ p2 = uidoc.Selection.PickPoint("Pick the chase end point on the wall");

            var rules = PlacementRuleLoader.Load(doc.PathName);
            var rule = rules.FirstOrDefault(r =>
                string.Equals(r.RoutingMode, "WALL_CHASE", StringComparison.OrdinalIgnoreCase));
            if (rule == null)
                return StingResultPanel.Create("STING — Wall Chase").AddSection("RESULT")
                    .Text("No WALL_CHASE rule loaded. Ship STING_PLACEMENT_RULES.in-wall-chase.json or define one.");

            var structural = new StructuralAwareness(doc);
            var router = new InWallChaseRouter(doc, structural);
            InWallChaseRouter.ChaseRouteResult outcome;

            // Preview pass: route inside a TransactionGroup that we always roll
            // back, so the user sees the depth-check verdict before committing.
            using (var tg = new TransactionGroup(doc, "STING Chase Preview"))
            {
                tg.Start();
                using (var ptx = new Transaction(doc, "STING Chase Preview Inner"))
                {
                    ptx.Start();
                    outcome = router.Route(wall, p1, p2, rule,
                        ElementId.InvalidElementId, ElementId.InvalidElementId);
                    ptx.RollBack();
                }
                tg.RollBack();
            }

            var confirmDialog = new TaskDialog("STING - Wall Chase Preview")
            {
                MainInstruction = $"Preview: {outcome.CreatedSegments.Count} pipe(s), {outcome.RejectedSegments} rejected, {outcome.SleevesPlaced} sleeve(s).",
                MainContent = $"Available chase depth: {outcome.AvailableChaseDepthMm:F0} mm\nRequired: {outcome.RequiredChaseDepthMm:F0} mm\n\nCommit?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirmDialog.Show() != TaskDialogResult.Yes)
                return StingResultPanel.Create("STING — Wall Chase").AddSection("RESULT")
                    .Text("Preview only — not committed.");

            using (var tx = new Transaction(doc, "STING In-Wall Chase Route"))
            {
                tx.Start();
                outcome = router.Route(wall, p1, p2, rule,
                    ElementId.InvalidElementId, ElementId.InvalidElementId);
                if (outcome.CreatedSegments.Count == 0 && outcome.RejectedSegments > 0)
                    tx.RollBack();
                else
                    tx.Commit();
            }

            var panel = StingResultPanel.Create("STING — Wall Chase")
                .AddSection("SUMMARY")
                .Metric("Pipes", outcome.CreatedSegments.Count.ToString())
                .Metric("Rejected", outcome.RejectedSegments.ToString())
                .Metric("Sleeves", outcome.SleevesPlaced.ToString())
                .Metric("Available chase depth (mm)", outcome.AvailableChaseDepthMm.ToString("F0"))
                .Metric("Required chase depth (mm)", outcome.RequiredChaseDepthMm.ToString("F0"));
            if (outcome.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in outcome.Warnings.Take(20)) panel.Text(w);
            }
            return panel;
        }

        private sealed class WallSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Wall;
            public bool AllowReference(Reference r, XYZ p) => true;
        }
    }
}
