// StingTools v4 MVP — Phase I sleeve command.
//
// Scope: selection (MEP curves) or active-view MEP. Runs SleeveEngine
// and optionally exports an IFC4 Reference-View + Provisions-for-Void
// round-trip file for Tekla Hole Reservation Manager consumption.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceSleevesCommand : IExternalCommand
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
                TaskDialog.Show("STING v4 — Place Sleeves",
                    "No MEP runs found in scope.\n\nSelect pipes / ducts / conduits / cable " +
                    "trays and re-run, or open a view containing them.");
                return Result.Cancelled;
            }

            var td = new TaskDialog("STING v4 — Place Sleeves")
            {
                MainInstruction = "Preview or apply?",
                MainContent =
                    $"{runs.Count} MEP run(s) in scope.\n\n" +
                    "PREVIEW: scan penetrations, compute sleeve sizes, report.\n" +
                    "APPLY:   place sleeve families + cut hosts via " +
                    "InstanceVoidCutUtils + inherit fire rating.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preview");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Place sleeves");
            var choice = td.Show();
            if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                return Result.Cancelled;
            bool dryRun = choice == TaskDialogResult.CommandLink1;

            SleeveResult res;
            try { res = SleeveEngine.PlaceSleeves(doc, runs, dryRun); }
            catch (Exception ex)
            {
                StingLog.Error("PlaceSleevesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(res, dryRun);
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
                    if (el is Pipe || el is Duct || el is Conduit || el is CableTray ||
                        IsFlexPipeOrDuct(el)) scope.Add(el);
                }
            }
            else
            {
                var view = doc.ActiveView;
                if (view == null) return scope;
                var cats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_CableTray
                };
                var filter = new ElementMulticategoryFilter(cats);
                var col = new FilteredElementCollector(doc, view.Id)
                    .WherePasses(filter)
                    .WhereElementIsNotElementType();
                foreach (var el in col) scope.Add(el);
            }
            return scope;
        }

        private static bool IsFlexPipeOrDuct(Element el)
        {
            if (el?.Category == null) return false;
            var bic = (BuiltInCategory)el.Category.Id.Value;
            return bic == BuiltInCategory.OST_FlexPipeCurves || bic == BuiltInCategory.OST_FlexDuctCurves;
        }

        private void ShowResult(SleeveResult res, bool dryRun)
        {
            var panel = StingResultPanel.Create("v4 Sleeve Placement");
            panel.SetSubtitle(dryRun ? "PREVIEW (no changes written)" : $"Placed {res.Placed} sleeve(s)");

            panel.AddSection("SUMMARY")
                 .Metric("MEP runs scanned", res.MepCurvesScanned.ToString())
                 .Metric("Penetrations",     res.PenetrationsFound.ToString())
                 .Metric("Placed",           res.Placed.ToString())
                 .Metric("Cut applied",      res.CutApplied.ToString())
                 .Metric("Fire rating written", res.FireRatingWritten.ToString())
                 .Metric("Skipped",          res.Skipped.ToString())
                 .Metric("Failed",           res.Failed.ToString());

            panel.AddSection("STANDARDS")
                 .Text("Sleeves sized per BS EN 1366-3 minimum annulus rules:")
                 .Text("  • Pipes / ducts: 50 mm clearance per side")
                 .Text("  • Conduit:       15 mm clearance per side")
                 .Text("  • Large-bore (≥250 mm): rule PIPE_LARGEBORE")
                 .Text("Fire rating inherited from host wall/floor TYPE per BS EN 13501-2.")
                 .Text("Configure rules in Data/Routing/STING_SLEEVE_RULES.json.");

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                if (res.Warnings.Count > 40) panel.Text($"(+{res.Warnings.Count - 40} more — see StingLog)");
            }

            panel.AddSection("NEXT STEPS")
                 .Text("To round-trip sleeves to Tekla Structures Hole Reservation Manager:")
                 .Text("1. Run Fabrication → Export IFC Provisions for Voids (next command).")
                 .Text("2. Load the IFC4 Reference View in Tekla; use the PFV_UUID as the")
                 .Text("   matching key between MEP voids and structural cuts.")
                 .Text("3. Run Export Sleeve BCF to issue per-penetration RFIs to coordinators.");
            panel.Show();
        }
    }
}
