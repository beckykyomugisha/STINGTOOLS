// StingTools v4 MVP — Routing tab supporting commands.
//
// GenerateLayoutCommand now drives the A* voxel pathfinder: the user
// picks two points in the active view, the command collects wall /
// floor / column obstacles inside a padded AABB, runs RoutingPathfinder
// (VoxelGrid + AStarSolver), and emits a preview polyline in the
// active view using DetailCurve elements. No MEPCurves are created in
// this Phase A preview — it's a dry-run that demonstrates the
// pathfinder is alive and returns what the full auto-router will use
// when the path→MEPCurve conversion lands.
//
// ValidateFillsCommand remains a thin pointer to the Run All
// Validators command surfaced in the Validation tab.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Core.Routing;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateLayoutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            XYZ start, goal;
            try
            {
                TaskDialog.Show("STING v4 — Generate Layout",
                    "Pick two points in the active view:\n\n" +
                    "  1. START point (source / riser head)\n" +
                    "  2. GOAL point (fixture / outlet)\n\n" +
                    "The command will run the A* voxel pathfinder against " +
                    "walls / floors / columns in a padded AABB containing " +
                    "both points and emit a preview polyline as DetailCurves.");
                start = uidoc.Selection.PickPoint("Pick START point");
                goal  = uidoc.Selection.PickPoint("Pick GOAL point");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"Point pick failed: {ex.Message}";
                return Result.Failed;
            }

            var obstacles = RoutingPathfinder.CollectObstaclesInAABB(doc, start, goal);
            RoutingPath path;
            try
            {
                path = RoutingPathfinder.FindPath(start, goal, obstacles);
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateLayoutCommand: pathfinder crash", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(path, obstacles.Count);

            if (!path.Success || path.Points.Count < 2) return Result.Succeeded;

            // Draw the polyline as DetailCurves for preview (no MEPCurves —
            // Phase A keeps the pathfinder consequence-free so the user
            // can verify the path before routing).
            using (var tx = new Transaction(doc, "STING v4 A* layout preview"))
            {
                try
                {
                    tx.Start();
                    var view = doc.ActiveView;
                    if (!(view is ViewPlan) && !(view is ViewSection) && !(view is View3D))
                    {
                        StingLog.Warn("GenerateLayoutCommand: ActiveView is not Plan/Section/3D; skipping preview draw");
                    }
                    else if (view is View3D)
                    {
                        // 3D views do not accept DetailCurves — log only.
                        StingLog.Info("GenerateLayoutCommand: 3D view, preview not drawn (path found OK).");
                    }
                    else
                    {
                        for (int i = 0; i + 1 < path.Points.Count; i++)
                        {
                            try
                            {
                                var line = Line.CreateBound(path.Points[i], path.Points[i + 1]);
                                doc.Create.NewDetailCurve(view, line);
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"GenerateLayoutCommand: DetailCurve draw failed at seg {i}: {ex.Message}");
                            }
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    StingLog.Error("GenerateLayoutCommand: transaction failed", ex);
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            return Result.Succeeded;
        }

        private void ShowResult(RoutingPath path, int obstacleCount)
        {
            var panel = StingResultPanel.Create("v4 A* Layout Preview");
            panel.SetSubtitle(path.Success
                ? $"Path found ({path.Points.Count} vertices)"
                : "Path not found");
            panel.AddSection("PATHFINDER")
                 .Metric("Cells built",    path.CellsBuilt.ToString())
                 .Metric("Obstacles",      obstacleCount.ToString())
                 .Metric("Nodes expanded", path.NodesExpanded.ToString())
                 .Metric("Total cost",     path.TotalCost.ToString("F2"))
                 .Metric("Polyline pts",   path.Points.Count.ToString());
            if (!path.Success && !string.IsNullOrEmpty(path.FailureReason))
                panel.AddSection("FAILURE").Text(path.FailureReason);
            panel.Show();
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateFillsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Thin wrapper around FillValidator — the engine still runs
            // via RunAllValidatorsCommand on the Validation tab. This
            // command exists so the Routing tab button dispatch resolves;
            // it surfaces a TaskDialog pointing at the unified entry.
            try
            {
                TaskDialog.Show("STING v4 — Validate Fills",
                    "Fill validation runs as part of RunAllValidatorsCommand.\n\n" +
                    "Open the Validation tab and click 'Run all validators' to\n" +
                    "execute ConnectivityValidator, FillValidator, SpecValidator,\n" +
                    "TerminationValidator, SlopeValidator in sequence and view\n" +
                    "the aggregated findings.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ValidateFillsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
