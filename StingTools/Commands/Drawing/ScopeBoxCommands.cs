// StingTools — Scope-box planner commands
//
//   ScopeBox_Planner        open the planner (modeless): tick drawing types → plan → create → colour → produce
//   ScopeBox_RegisterSeeds  rename the selected boxes to STING-SEED::<w>x<d> from their measured size
//   ScopeBox_ImportSeeds    copy STING-SEED boxes from another project or template
//   ScopeBox_Colour         colour every STING box in all plan views by a chosen mode
//   ScopeBox_ClearColour    remove those colours
//   ScopeBox_ProduceAreas   produce views (and sheets) for every area box from the saved plan
//
// Each is a thin shell over ScopeBoxPlannerService, so a button, a workflow step and
// the dialog all run the same code.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    internal static class ScopeBoxCommandBase
    {
        public static UIApplication App(ExternalCommandData data)
            => data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp;

        public static Result Run(ExternalCommandData data, ref string message, string title, Func<UIApplication, Document, string> work)
        {
            try
            {
                var app = App(data);
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) { TaskDialog.Show(title, "No document open."); return Result.Failed; }
                var report = work(app, doc);
                if (!string.IsNullOrEmpty(report)) TaskDialog.Show(title, report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error(title, ex);
                message = ex.Message;
                TaskDialog.Show(title, "Failed: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxPlannerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var app = ScopeBoxCommandBase.App(data);
                if (app?.ActiveUIDocument?.Document == null) { TaskDialog.Show("STING Scope Box Planner", "No document open."); return Result.Failed; }
                // Modeless, like the Scope Box Manager: a modal window blocks the
                // ExternalEvent queue, so the dialog's writes would never run.
                var dlg = new StingTools.UI.ScopeBoxPlannerDialog(app);
                OwnByRevit(dlg);
                dlg.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ScopeBoxPlanner", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>Keep the window above Revit. Cosmetic: if it fails the planner still opens and works.</summary>
        private static void OwnByRevit(System.Windows.Window w)
        {
            try { new System.Windows.Interop.WindowInteropHelper(w).Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
            catch (Exception ex) { StingLog.Warn($"ScopeBoxPlanner owner: {ex.Message}"); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxRegisterSeedsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ScopeBoxCommandBase.Run(data, ref message, "STING Register Scope Box Seeds",
                (app, doc) => ScopeBoxPlannerService.RegisterSeeds(doc, app.ActiveUIDocument.Selection.GetElementIds()));
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxImportSeedsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ScopeBoxCommandBase.Run(data, ref message, "STING Import Scope Box Seeds", (app, doc) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose a project or template that holds STING-SEED:: scope boxes",
                    Filter = "Revit projects and templates (*.rvt;*.rte)|*.rvt;*.rte",
                };
                if (dlg.ShowDialog() != true) return null;
                return ScopeBoxPlannerService.ImportSeeds(app, doc, dlg.FileName);
            });
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxColourCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ScopeBoxCommandBase.Run(data, ref message, "STING Colour Scope Boxes", (app, doc) =>
            {
                var modes = Enum.GetNames(typeof(ScopeBoxColourMode)).Where(n => n != nameof(ScopeBoxColourMode.Off)).ToList();
                var pick = StingTools.Select.StingListPicker.Show("Colour scope boxes", "Colour every STING scope box in all plan views by:", modes);
                if (string.IsNullOrEmpty(pick) || !Enum.TryParse<ScopeBoxColourMode>(pick, out var mode)) return null;
                return ScopeBoxPlannerService.Colour(doc, mode, allPlanViews: true, active: doc.ActiveView);
            });
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxClearColourCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ScopeBoxCommandBase.Run(data, ref message, "STING Clear Scope Box Colours",
                (app, doc) => ScopeBoxPlannerService.Colour(doc, ScopeBoxColourMode.Off, allPlanViews: true, active: doc.ActiveView));
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxProduceAreasCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ScopeBoxCommandBase.Run(data, ref message, "STING Produce From Area Boxes", (app, doc) => RunInteractive(doc));

        /// <summary>
        /// Count, confirm, produce. Shared with the planner dialog, which calls it from its
        /// own ExternalEvent. Returns the report, or null when the person cancels.
        /// </summary>
        public static string RunInteractive(Document doc)
        {
            var notes = new List<string>();
            var plan = ScopeBoxPlannerService.LoadPlan(doc, out var err);
            if (err != null) return err;
            var items = ScopeBoxPlannerService.PlanProduction(doc, plan, notes);
            if (items.Count == 0)
                return "Nothing to produce." + (notes.Count > 0 ? "\n• " + string.Join("\n• ", notes) : "");

            // Say how much before doing it: area boxes × levels × types multiplies fast.
            var td = new TaskDialog("STING Produce From Area Boxes")
            {
                MainInstruction = $"{items.Count} view(s) to produce from {items.Select(i => i.Box.Id).Distinct().Count()} area box(es).",
                MainContent = notes.Count > 0 ? "Notes:\n• " + string.Join("\n• ", notes.Take(12)) : "Existing views are updated, not duplicated.",
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Views and sheets");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Views only");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            var r = td.Show();
            if (r != TaskDialogResult.CommandLink1 && r != TaskDialogResult.CommandLink2) return null;
            return ScopeBoxPlannerService.Produce(doc, items, sheets: r == TaskDialogResult.CommandLink1);
        }
    }
}
