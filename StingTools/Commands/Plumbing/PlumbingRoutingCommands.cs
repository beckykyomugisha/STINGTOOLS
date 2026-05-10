// PlumbingRoutingCommands — Phase 179c ROUTE tab.
//
// Plumb_AutoRoute      — wraps AutoPipeDrop, scoped to current selection.
// Plumb_FixSlopes      — wraps SlopeAutoCorrector with view scope choice.
// Plumb_InsertPTraps   — wraps PTrapInserter.
// Plumb_PlaceSleeves   — wraps existing PlaceSleevesCommand path on plumbing pipes.
// Plumb_PlaceHangers   — wraps HangerPlacementEngine with hanger emit.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.Core.Mep;
using StingTools.Core.Plumbing;
using StingTools.Core.Routing;
using StingTools.Commands.Routing;
using StingTools.UI;
using StingTools.UI.Plumbing;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbAutoRouteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var sel = ctx.UIDoc.Selection.GetElementIds();
            var fixtures = sel.Select(id => ctx.Doc.GetElement(id))
                .Where(el => el != null && el.Category != null
                          && (BuiltInCategory)el.Category.Id.Value == BuiltInCategory.OST_PlumbingFixtures)
                .ToList();
            if (fixtures.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Auto-Route",
                    "Select one or more plumbing fixtures, then re-run Auto-Route.");
                return Result.Cancelled;
            }
            var engine = new AutoPipeDrop(ctx.Doc) { SearchRadiusMm = AutoDropOptions.MaxSearchRadiusMm };
            var result = engine.Execute(fixtures);

            var panel = StingResultPanel.Create("Auto-Route Plumbing");
            panel.SetSubtitle($"{fixtures.Count} fixture(s) · radius {AutoDropOptions.MaxSearchRadiusMm:F0} mm");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes created",  result.CreatedIds.Count.ToString())
                 .Metric("Connected",      result.ConnectedCount.ToString())
                 .Metric("Take-offs",      result.TakeoffCount.ToString())
                 .Metric("Skipped",        result.SkippedCount.ToString())
                 .Metric("Failed",         result.FailedCount.ToString());
            if (result.Warnings.Any())
            {
                panel.AddSection("WARNINGS");
                foreach (var w in result.Warnings.Take(40)) panel.Text(w);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbFixSlopesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var preview = SlopeAutoCorrector.Preview(ctx.Doc);
            int flagged = preview.Fixes.Count(f => f.Action == "FLIP" || f.Action == "DEPRESS");
            if (flagged == 0)
            {
                TaskDialog.Show("STING Plumbing — Slope Fix",
                    "No drainage pipes need slope correction.\n\n" +
                    $"Pipes scanned: {preview.PipesScanned}\n" +
                    $"Pipes already OK: {preview.PipesUnchanged}");
                return Result.Succeeded;
            }
            var dec = SlopeFixPreviewDialog.Show(preview);
            if (dec.Decision != SlopeFixDecision.ApplyAll)
            {
                return Result.Cancelled;
            }
            var fixResult = SlopeAutoCorrector.RunFix(ctx.Doc, dryRun: false);

            var panel = StingResultPanel.Create("Slope Auto-Fix");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes scanned",   fixResult.PipesScanned.ToString())
                 .Metric("Pipes flipped",   fixResult.PipesFlipped.ToString())
                 .Metric("Pipes depressed", fixResult.PipesDepressed.ToString())
                 .Metric("Unchanged",       fixResult.PipesUnchanged.ToString())
                 .Metric("Skipped (locked)",fixResult.PipesSkippedConnectedBothEnds.ToString())
                 .Metric("Pipes failed",    fixResult.PipesFailed.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbInsertPTrapsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var td = new TaskDialog("Plumb_InsertPTraps")
            {
                MainInstruction = "Insert P-traps on fixtures missing one?",
                MainContent = "Walks all OST_PlumbingFixtures and adds a P-trap family on the drainage connector when no trap is detected.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Scan only (report missing)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Insert traps where missing");
            var pick = td.Show();
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2) return Result.Cancelled;
            bool place = pick == TaskDialogResult.CommandLink2;

            PTrapResult r;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing P-trap insert"))
            {
                tx.Start();
                r = PTrapInserter.Scan(ctx.Doc, scope: null, placeFamily: place);
                if (place) tx.Commit(); else tx.RollBack();
            }
            var panel = StingResultPanel.Create("P-Trap Inserter");
            panel.AddSection("SUMMARY")
                 .Metric("Fixtures scanned",       r.FixturesScanned.ToString())
                 .Metric("Already trapped",        r.FixturesAlreadyTrapped.ToString())
                 .Metric("Traps placed",           r.TrapsPlaced.ToString())
                 .Metric("Skipped",                r.FixturesSkipped.ToString());
            if (r.Warnings.Any())
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(40)) panel.Text(w);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbPlaceSleevesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Restrict to plumbing pipes — supply or drainage. The shared SleeveEngine
            // handles fire-rating inheritance + IFC PfV UUID; we just hand it the right scope.
            var pipes = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
            if (pipes.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Sleeves", "No pipes in active document.");
                return Result.Cancelled;
            }
            SleeveResult r;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Place Sleeves"))
            {
                tx.Start();
                r = SleeveEngine.PlaceSleeves(ctx.Doc, pipes, dryRun: false);
                tx.Commit();
            }
            var panel = StingResultPanel.Create("Plumbing Sleeve Placement");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes scanned",       r.MepCurvesScanned.ToString())
                 .Metric("Penetrations found",  r.PenetrationsFound.ToString())
                 .Metric("Sleeves placed",      r.Placed.ToString())
                 .Metric("Cut applied",         r.CutApplied.ToString())
                 .Metric("Fire rating written", r.FireRatingWritten.ToString())
                 .Metric("Skipped",             r.Skipped.ToString())
                 .Metric("Failed",              r.Failed.ToString());
            if (r.Warnings.Any())
            {
                panel.AddSection("WARNINGS (first 30)");
                foreach (var w in r.Warnings.Take(30)) panel.Text(w);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbPlaceHangersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var pipes = new FilteredElementCollector(ctx.Doc).OfClass(typeof(Pipe)).Cast<Element>().ToList();
            if (pipes.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Hangers", "No pipes to support.");
                return Result.Cancelled;
            }
            // Phase 179c ships the planning pass — actual family placement
            // is delegated to the existing hanger emit pipeline (see
            // AutoPipeDrop.EmitSupports). Reports candidate count so the
            // user can decide whether to invoke the routing-side emit.
            var plan = HangerPlacementEngine.Plan(ctx.Doc, pipes);

            var panel = StingResultPanel.Create("Hanger Placement Plan");
            panel.AddSection("SUMMARY")
                 .Metric("Runs scanned",         plan.RunsScanned.ToString())
                 .Metric("Candidates generated", plan.CandidatesGenerated.ToString())
                 .Metric("Concrete anchors",    plan.ConcreteAnchorCount.ToString())
                 .Metric("Beam clamps",         plan.BeamClampCount.ToString())
                 .Metric("Generic supports",    plan.GenericCount.ToString())
                 .Metric("Trapeze groups",      plan.TrapezeGroups.ToString());
            if (plan.Warnings.Any())
            {
                panel.AddSection("WARNINGS");
                foreach (var w in plan.Warnings.Take(40)) panel.Text(w);
            }
            panel.AddSection("NOTE")
                 .Text("Hanger families are emitted by the routing pipeline " +
                       "(AutoDropOptions.EmitSupports = true) when pipes are first " +
                       "routed via Plumb_AutoRoute. Use the ROUTING tab's Auto-Drop " +
                       "command to apply these candidates as physical hangers.");
            panel.Show();
            return Result.Succeeded;
        }
    }
}
