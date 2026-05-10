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

            var inst = StingPlumbingPanel.Instance;
            var opts = inst?.ReadRouteAutoOptions();
            string scope = opts?.Scope ?? "Selected";
            double radius = opts != null && opts.MaxRadiusMm > 0 ? opts.MaxRadiusMm : AutoDropOptions.MaxSearchRadiusMm;

            var fixtures = CollectByScope(ctx, scope, BuiltInCategory.OST_PlumbingFixtures);
            if (fixtures.Count == 0)
            {
                string msg = scope == "Selected"
                    ? "Select one or more plumbing fixtures, then re-run Auto-Route."
                    : $"No plumbing fixtures in scope '{scope}'.";
                if (inst != null) { inst.SetStatus("Auto-Route · " + msg); return Result.Cancelled; }
                TaskDialog.Show("STING Plumbing — Auto-Route", msg);
                return Result.Cancelled;
            }
            var engine = new AutoPipeDrop(ctx.Doc) { SearchRadiusMm = radius };
            var result = engine.Execute(fixtures);

            string status = $"Auto-Route · {scope} · {fixtures.Count} fix · "
                          + $"created {result.CreatedIds.Count} · connected {result.ConnectedCount} · "
                          + $"skipped {result.SkippedCount} · failed {result.FailedCount}";
            if (inst != null) { inst.SetStatus(status); return Result.Succeeded; }

            var panel = StingResultPanel.Create("Auto-Route Plumbing");
            panel.SetSubtitle($"{fixtures.Count} fixture(s) · radius {radius:F0} mm · scope {scope}");
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

        internal static List<Element> CollectByScope(StingCommandContext ctx, string scope, BuiltInCategory bic)
        {
            switch ((scope ?? "").ToUpperInvariant())
            {
                case "SELECTED":
                    return ctx.UIDoc.Selection.GetElementIds()
                        .Select(id => ctx.Doc.GetElement(id))
                        .Where(e => e?.Category != null && (BuiltInCategory)e.Category.Id.Value == bic)
                        .ToList();
                case "VIEW":
                    var v = ctx.Doc.ActiveView;
                    if (v == null) return new List<Element>();
                    return new FilteredElementCollector(ctx.Doc, v.Id)
                        .OfCategory(bic).WhereElementIsNotElementType()
                        .ToElements().ToList();
                default:
                    return new FilteredElementCollector(ctx.Doc)
                        .OfCategory(bic).WhereElementIsNotElementType()
                        .ToElements().ToList();
            }
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

            // Resolve scope from the DRAINAGE-tab radio group ("Selected" /
            // "View" / "Project"). Null scope => engine collects every drainage
            // pipe in the document (legacy behaviour).
            var inst  = StingPlumbingPanel.Instance;
            string scopeName = inst?.ReadDrainageSlopeScope() ?? "Project";
            IEnumerable<Pipe> scope = ResolveSlopeScope(ctx, scopeName);

            var preview = SlopeAutoCorrector.Preview(ctx.Doc, scope);
            int flagged = preview.Fixes.Count(f => f.Action == "FLIP" || f.Action == "DEPRESS");
            if (flagged == 0)
            {
                string none = $"Slope · {scopeName} · {preview.PipesScanned} pipes · 0 to fix";
                if (inst != null)
                {
                    inst.SetDrainageSlopeResult(new List<DrainageSlopeRow>(), none);
                    return Result.Succeeded;
                }
                TaskDialog.Show("STING Plumbing — Slope Fix",
                    "No drainage pipes need slope correction.\n\n" +
                    $"Pipes scanned: {preview.PipesScanned}\n" +
                    $"Pipes already OK: {preview.PipesUnchanged}");
                return Result.Succeeded;
            }

            // Inline panel path: populate the compact grid with the preview
            // and let the user opt-in to the wide dialog only when they want
            // connector-impact detail.
            if (inst != null)
            {
                var previewRows = preview.Fixes
                    .Where(f => f.Action == "FLIP" || f.Action == "DEPRESS")
                    .Select(f => new DrainageSlopeRow
                    {
                        Apply   = f.Success && f.ConnectorImpact != ConnectorImpact.SkippedConnected,
                        Pipe    = f.PipeId?.Value.ToString() ?? "",
                        DElevMm = f.DeltaZFt * 304.8
                    }).ToList();
                inst.SetDrainageSlopeResult(previewRows,
                    $"Slope · {scopeName} · {preview.PipesScanned} scanned · {flagged} to fix (preview)");

                var dec = SlopeFixPreviewDialog.Show(preview);
                if (dec.Decision != SlopeFixDecision.ApplyAll) return Result.Cancelled;
                var fixed1 = SlopeAutoCorrector.RunFix(ctx.Doc, scope, dryRun: false);
                inst.SetDrainageSlopeResult(previewRows,
                    $"Slope · {scopeName} · flipped {fixed1.PipesFlipped} · depressed {fixed1.PipesDepressed} · "
                    + $"unchanged {fixed1.PipesUnchanged} · skipped {fixed1.PipesSkippedConnectedBothEnds} · failed {fixed1.PipesFailed}");
                return Result.Succeeded;
            }

            // Legacy popup path
            var dec2 = SlopeFixPreviewDialog.Show(preview);
            if (dec2.Decision != SlopeFixDecision.ApplyAll) return Result.Cancelled;
            var fixResult = SlopeAutoCorrector.RunFix(ctx.Doc, scope, dryRun: false);
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

        private static IEnumerable<Pipe> ResolveSlopeScope(StingCommandContext ctx, string scope)
        {
            switch ((scope ?? "").ToUpperInvariant())
            {
                case "SELECTED":
                    return ctx.UIDoc.Selection.GetElementIds()
                        .Select(id => ctx.Doc.GetElement(id))
                        .OfType<Pipe>()
                        .ToList();
                case "VIEW":
                    var view = ctx.Doc.ActiveView;
                    if (view == null) return null;
                    return new FilteredElementCollector(ctx.Doc, view.Id)
                        .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
                default:
                    return null; // Project — engine collects every drainage pipe
            }
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

            var inst = StingPlumbingPanel.Instance;
            var opts = inst?.ReadRoutePTrapOptions();
            string scope = opts?.Scope ?? "View";

            var td = new TaskDialog("Plumb_InsertPTraps")
            {
                MainInstruction = "Insert P-traps on fixtures missing one?",
                MainContent = $"Scope: {scope}. Walks plumbing fixtures and adds a P-trap family on the drainage connector when no trap is detected.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Scan only (report missing)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Insert traps where missing");
            var pick = td.Show();
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2) return Result.Cancelled;
            bool place = pick == TaskDialogResult.CommandLink2;

            // Filter by scope (optionally further filtered by fixture-type checkboxes).
            IEnumerable<Element> scoped = PlumbAutoRouteCommand.CollectByScope(ctx, scope, BuiltInCategory.OST_PlumbingFixtures);
            if (opts != null) scoped = FilterByPTrapFlags(scoped, opts);

            PTrapResult r;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing P-trap insert"))
            {
                tx.Start();
                r = PTrapInserter.Scan(ctx.Doc, scoped, placeFamily: place);
                if (place) tx.Commit(); else tx.RollBack();
            }

            string status = $"P-Trap · {scope} · {r.FixturesScanned} fix · "
                          + $"{r.TrapsPlaced} placed · {r.FixturesAlreadyTrapped} already · {r.FixturesSkipped} skipped"
                          + (place ? "" : " (preview)");
            if (inst != null) { inst.SetStatus(status); return Result.Succeeded; }

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

        private static IEnumerable<Element> FilterByPTrapFlags(IEnumerable<Element> fixtures, RoutePTrapOptions opts)
        {
            // Drop fixtures the user unchecked. Match on family/type name keywords;
            // if every flag is unticked we treat the filter as "all on" (sane default).
            bool any = opts.IncludeWc || opts.IncludeBasin || opts.IncludeShower
                    || opts.IncludeBath || opts.IncludeSink || opts.IncludeGully || opts.IncludeFloor;
            if (!any) return fixtures;
            return fixtures.Where(el =>
            {
                var n = ((el.Name ?? "") + " " + (el.Category?.Name ?? "")).ToUpperInvariant();
                if (opts.IncludeWc     && (n.Contains("WC")     || n.Contains("TOILET"))) return true;
                if (opts.IncludeBasin  &&  n.Contains("BASIN"))                            return true;
                if (opts.IncludeShower &&  n.Contains("SHOWER"))                           return true;
                if (opts.IncludeBath   &&  n.Contains("BATH"))                             return true;
                if (opts.IncludeSink   &&  n.Contains("SINK"))                             return true;
                if (opts.IncludeGully  &&  n.Contains("GULLY"))                            return true;
                if (opts.IncludeFloor  && (n.Contains("FLOOR")  && n.Contains("DRAIN")))   return true;
                return false;
            });
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

            var inst = StingPlumbingPanel.Instance;
            var opts = inst?.ReadRouteSleeveOptions();
            string scope    = opts?.Scope ?? "View";
            double minOdMm  = opts != null && opts.MinOdMm > 0 ? opts.MinOdMm : 0.0;

            // Filter pipes by scope (Selected / View / Project) and minimum OD.
            var allInScope = CollectPipesByScope(ctx, scope);
            var pipes = minOdMm > 0
                ? allInScope.Where(p => DiameterMm(p) >= minOdMm).ToList()
                : allInScope;

            if (pipes.Count == 0)
            {
                string msg = $"No plumbing pipes in scope '{scope}'"
                           + (minOdMm > 0 ? $" above {minOdMm:F0} mm OD." : ".");
                if (inst != null) { inst.SetStatus("Sleeves · " + msg); return Result.Cancelled; }
                TaskDialog.Show("STING Plumbing — Sleeves", msg);
                return Result.Cancelled;
            }
            SleeveResult r;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Place Sleeves"))
            {
                tx.Start();
                r = SleeveEngine.PlaceSleeves(ctx.Doc, pipes, dryRun: false);
                tx.Commit();
            }

            string status = $"Sleeves · {scope} · {r.MepCurvesScanned} pipes · "
                          + $"{r.PenetrationsFound} penetrations · {r.Placed} placed · {r.Failed} failed";
            if (inst != null) { inst.SetStatus(status); return Result.Succeeded; }

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

        internal static List<Pipe> CollectPipesByScope(StingCommandContext ctx, string scope)
        {
            switch ((scope ?? "").ToUpperInvariant())
            {
                case "SELECTED":
                    return ctx.UIDoc.Selection.GetElementIds()
                        .Select(id => ctx.Doc.GetElement(id)).OfType<Pipe>().ToList();
                case "VIEW":
                    var v = ctx.Doc.ActiveView;
                    if (v == null) return new List<Pipe>();
                    return new FilteredElementCollector(ctx.Doc, v.Id)
                        .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
                default:
                    return new FilteredElementCollector(ctx.Doc)
                        .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
            }
        }

        private static double DiameterMm(Pipe p)
        {
            try
            {
                var prm = p.LookupParameter("Outside Diameter") ?? p.LookupParameter("Diameter");
                return prm != null ? prm.AsDouble() * 304.8 : 0;
            }
            catch { return 0; }
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

            var inst = StingPlumbingPanel.Instance;
            var opts = inst?.ReadRouteHangerOptions();
            string scope = opts?.Scope ?? "View";

            var pipes = PlumbPlaceSleevesCommand.CollectPipesByScope(ctx, scope).Cast<Element>().ToList();
            if (pipes.Count == 0)
            {
                string msg = $"No pipes to support in scope '{scope}'.";
                if (inst != null) { inst.SetStatus("Hangers · " + msg); return Result.Cancelled; }
                TaskDialog.Show("STING Plumbing — Hangers", msg);
                return Result.Cancelled;
            }
            // Phase 179c ships the planning pass — actual family placement
            // is delegated to the existing hanger emit pipeline (see
            // AutoPipeDrop.EmitSupports). Reports candidate count so the
            // user can decide whether to invoke the routing-side emit.
            var plan = HangerPlacementEngine.Plan(ctx.Doc, pipes);

            string status = $"Hangers · {scope} · {plan.RunsScanned} runs · "
                          + $"{plan.CandidatesGenerated} candidates · {plan.TrapezeGroups} trapezes "
                          + $"(rod {opts?.RodSize ?? "Auto"}, planning only — apply via Auto-Drop)";
            if (inst != null) { inst.SetStatus(status); return Result.Succeeded; }

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
