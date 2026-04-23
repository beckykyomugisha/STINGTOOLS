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
    /// <summary>
    /// Static option surface for Place Fixtures, populated by the
    /// TAGS → Fixtures sub-tab before Execute runs. Each property
    /// maps 1:1 to a CheckBox in StingDockPanel.xaml. Defaults match
    /// the XAML IsChecked="True" initial state.
    /// </summary>
    public static class PlaceFixturesOptions
    {
        public static bool DryRunPreference { get; set; } = true;
        public static bool SnapTo300mmGrid  { get; set; } = true;

        // Category filters (from the Fixtures panel).
        public static bool IncludeElectricalFixtures  { get; set; } = true;
        public static bool IncludeLightingDevices     { get; set; } = true;
        public static bool IncludeLightingFixtures    { get; set; } = true;
        public static bool IncludeCommunicationDevices{ get; set; } = true;
        public static bool IncludeDataDevices         { get; set; } = true;
        public static bool IncludeSecurityDevices     { get; set; } = true;
        public static bool IncludeFireAlarmDevices    { get; set; } = true;
        public static bool IncludePlumbingFixtures    { get; set; } = true;
        public static bool IncludeAirTerminals        { get; set; } = true;
        public static bool IncludeSprinklers          { get; set; } = true;

        // Standards toggles (advisory; rules carry the standard in their ids).
        public static bool EnforceDocM    { get; set; } = true;
        public static bool EnforceBS7671  { get; set; } = true;
        public static bool EnforceBS5266  { get; set; } = true;
        public static bool EnforceBS5839  { get; set; } = true;
        public static bool EnforceBS6465  { get; set; } = true;
        public static bool EnforceEN12464 { get; set; } = true;

        // Collision constraints.
        public static bool RejectInsideWall       { get; set; } = true;
        public static bool RejectOutsideRoom      { get; set; } = true;
        public static bool MinDoorClearance300    { get; set; } = true;
        public static bool MinWindowClearance100  { get; set; } = true;

        /// <summary>
        /// Return the set of Revit category names this command should
        /// consider, based on the discipline checkboxes. Used by
        /// FixturePlacementEngine to filter PlacementRule.CategoryFilter.
        /// </summary>
        public static HashSet<string> AllowedCategoryNames()
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (IncludeElectricalFixtures)   s.Add("Electrical Fixtures");
            if (IncludeLightingDevices)      s.Add("Lighting Devices");
            if (IncludeLightingFixtures)     s.Add("Lighting Fixtures");
            if (IncludeCommunicationDevices) s.Add("Communication Devices");
            if (IncludeDataDevices)          s.Add("Data Devices");
            if (IncludeSecurityDevices)      s.Add("Security Devices");
            if (IncludeFireAlarmDevices)     s.Add("Fire Alarm Devices");
            if (IncludePlumbingFixtures)     s.Add("Plumbing Fixtures");
            if (IncludeAirTerminals)         s.Add("Air Terminals");
            if (IncludeSprinklers)           s.Add("Sprinklers");
            return s;
        }
    }

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

            // Fixtures sub-tab: "Dry-run preview first" checkbox decides
            // whether we prompt. When unchecked, go straight to a
            // confirm-only dialog.
            bool dryRun;
            if (PlaceFixturesOptions.DryRunPreference)
            {
                dryRun = PromptDryRunChoice(selectedRoomIds.Count);
            }
            else
            {
                if (!ConfirmPlacement(selectedRoomIds.Count)) return Result.Cancelled;
                dryRun = false;
            }
            if (dryRun == false
                && PlaceFixturesOptions.DryRunPreference == false
                && !ConfirmPlacement(selectedRoomIds.Count)) return Result.Cancelled;

            // Category filter: discipline checkboxes from the Fixtures
            // panel restrict which PlacementRule.CategoryFilter values
            // the engine evaluates. Null/empty set means "all".
            var allowedCats = PlaceFixturesOptions.AllowedCategoryNames();
            if (allowedCats.Count == 0)
            {
                TaskDialog.Show("STING v4 — Place Fixtures",
                    "All category checkboxes are off — nothing to place. " +
                    "Enable at least one category in the Fixtures tab.");
                return Result.Cancelled;
            }

            // Load rules and filter down to the categories whose
            // checkboxes are on. The engine itself takes IList<PlacementRule>,
            // so filtering here keeps the engine signature unchanged.
            List<PlacementRule> rules = null;
            try
            {
                rules = PlacementRuleLoader.Load(doc.PathName);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlaceFixturesCommand: rule load failed: {ex.Message}");
            }

            List<PlacementRule> filtered = null;
            if (rules != null && rules.Count > 0)
            {
                filtered = rules
                    .Where(r => allowedCats.Contains(r.CategoryFilter ?? ""))
                    .ToList();
                if (filtered.Count == 0)
                {
                    TaskDialog.Show("STING v4 — Place Fixtures",
                        "No placement rules match the selected categories. " +
                        "Either enable more categories in the Fixtures tab, " +
                        "or add a rule for the target category to " +
                        "STING_PLACEMENT_RULES.json.");
                    return Result.Cancelled;
                }
            }

            PlacementResult res;
            try
            {
                res = FixturePlacementEngine.PlaceFixturesInScope(
                    doc,
                    selectedRoomIds.Count > 0 ? selectedRoomIds : null,
                    filtered,
                    dryRun);
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

            // Revit's TaskDialog.DefaultButton must refer to a button in CommonButtons —
            // it cannot point at a CommandLink. Leave DefaultButton unset so Revit picks
            // the first-added CommandLink as the default.
            var td = new TaskDialog("STING v4 — Place Fixtures")
            {
                MainInstruction = "Run preview first?",
                MainContent =
                    $"Scope: {scope}\n\n" +
                    "PREVIEW: score candidates and show the result without placing anything.\n" +
                    "PLACE: execute placement in a single transaction.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
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
