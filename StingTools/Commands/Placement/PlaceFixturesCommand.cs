// StingTools v4 MVP — Place Fixtures command.
//
// Reads the current selection (if empty, treats scope = all rooms),
// runs FixturePlacementEngine, and shows the result in StingResultPanel.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceFixturesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Scope: selected rooms → those; else all rooms in project.
            var selectedRoomIds = new List<ElementId>();
            foreach (var id in uidoc.Selection.GetElementIds())
            {
                if (doc.GetElement(id) is Room) selectedRoomIds.Add(id);
            }

            bool dryRun = PromptDryRunChoice(selectedRoomIds.Count);
            if (dryRun == false && !ConfirmPlacement(selectedRoomIds.Count)) return Result.Cancelled;

            PlacementResult res;
            try
            {
                res = FixturePlacementEngine.PlaceFixturesInScope(
                    doc, selectedRoomIds.Count > 0 ? selectedRoomIds : null, null, dryRun);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlaceFixturesCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(res);

            // Select placed elements so user sees immediate feedback
            if (!dryRun && res.PlacedIds.Count > 0)
            {
                try { uidoc.Selection.SetElementIds(res.PlacedIds); }
                catch (Exception ex) { StingLog.Warn($"PlaceFixturesCommand select failed: {ex.Message}"); }
            }

            return Result.Succeeded;
        }

        private bool PromptDryRunChoice(int selectedRoomCount)
        {
            string scope = selectedRoomCount > 0
                ? $"{selectedRoomCount} selected room(s)"
                : "ALL rooms in project";

            var td = new TaskDialog("STING v4 — Place Fixtures")
            {
                MainInstruction = "Run preview first?",
                MainContent =
                    $"Scope: {scope}\n\n" +
                    "PREVIEW: score candidates and show the result without placing anything.\n" +
                    "PLACE: execute placement in a single transaction.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preview (dry run)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Place now");
            var r = td.Show();
            return r != TaskDialogResult.CommandLink2;
        }

        private bool ConfirmPlacement(int selectedRoomCount)
        {
            string scope = selectedRoomCount > 0
                ? $"{selectedRoomCount} room(s)"
                : "the entire project";
            var r = TaskDialog.Show(
                "STING v4 — Confirm placement",
                $"About to place fixtures across {scope}. Continue?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                TaskDialogResult.No);
            return r == TaskDialogResult.Yes;
        }

        private void ShowResult(PlacementResult res)
        {
            var panel = StingResultPanel.Create("v4 Fixture Placement");

            panel.SetSubtitle(res.DryRun ? "PREVIEW (dry run, nothing placed)" : "Live placement");

            panel.AddSection("SUMMARY")
                .Metric("Rooms visited",       res.RoomsVisited.ToString())
                .Metric("Candidates evaluated", res.CandidatesEvaluated.ToString())
                .Metric("Placed",               res.PlacedIds.Count.ToString())
                .Metric("Skipped",              res.SkippedCount.ToString());

            if (res.CountsByRule != null && res.CountsByRule.Count > 0)
            {
                panel.AddSection("PER-RULE COUNTS");
                foreach (var kv in res.CountsByRule.OrderByDescending(k => k.Value).Take(20))
                    panel.Metric(kv.Key, kv.Value.ToString());
            }

            if (res.Warnings != null && res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(30)) panel.Text(w);
                if (res.Warnings.Count > 30)
                    panel.Text($"(+{res.Warnings.Count - 30} more — see StingLog)");
            }

            panel.Show();
        }
    }
}
