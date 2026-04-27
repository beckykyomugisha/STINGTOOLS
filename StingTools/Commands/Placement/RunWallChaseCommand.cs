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

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunWallChaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData?.Application?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            try
            {
                Reference wallRef = uidoc.Selection.PickObject(ObjectType.Element,
                    new WallSelectionFilter(), "Pick the host wall for the chase route");
                Wall wall = doc.GetElement(wallRef) as Wall;
                if (wall == null) { message = "Selected element is not a wall."; return Result.Failed; }

                XYZ p1 = uidoc.Selection.PickPoint("Pick the chase start point on the wall");
                XYZ p2 = uidoc.Selection.PickPoint("Pick the chase end point on the wall");

                var rules = PlacementRuleLoader.Load(doc.PathName);
                var rule = rules.FirstOrDefault(r =>
                    string.Equals(r.RoutingMode, "WALL_CHASE", StringComparison.OrdinalIgnoreCase));
                if (rule == null)
                {
                    TaskDialog.Show("STING - Wall Chase",
                        "No WALL_CHASE rule loaded. Ship STING_PLACEMENT_RULES.in-wall-chase.json or define one.");
                    return Result.Cancelled;
                }

                var structural = new StructuralAwareness(doc);
                var router = new InWallChaseRouter(doc, structural);
                InWallChaseRouter.ChaseRouteResult outcome;

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

                string warnText = outcome.Warnings.Count == 0
                    ? "(no warnings)"
                    : string.Join("\n  ", outcome.Warnings.Take(20));
                TaskDialog.Show("STING - Wall Chase",
                    $"Created: {outcome.CreatedSegments.Count}  Rejected: {outcome.RejectedSegments}\n\n" +
                    $"Available chase depth: {outcome.AvailableChaseDepthMm:F0} mm\n" +
                    $"Required chase depth:  {outcome.RequiredChaseDepthMm:F0} mm\n\n" +
                    $"Warnings:\n  {warnText}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error($"RunWallChaseCommand: {ex.Message}", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private sealed class WallSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Wall;
            public bool AllowReference(Reference r, XYZ p) => true;
        }
    }
}
