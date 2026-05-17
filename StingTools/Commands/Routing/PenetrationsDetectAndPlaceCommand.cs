// StingTools — PenetrationsDetectAndPlaceCommand.
//
// Standalone command to run the full penetration sweep against the
// active selection (or, when nothing is selected, every MEPCurve in
// the active view). Replaces the previous behaviour where the only
// way to fire SlabPenetrationDetector + FrpPenetrationPlacer was via
// ConduitAutoRouteCommand — a brand-new project with already-routed
// MEP had no way to retro-fit the FRP register.
//
// Pipeline per run:
//   1. Slab penetrations  (SlabPenetrationDetector)
//   2. Wall penetrations  (WallPenetrationDetector — fire-rated walls)
//   3. Beam penetrations  (BeamPenetrationDetector — structural review)
//   4. Place / update FRP family instances (FrpPenetrationPlacer)
//   5. Report into a StingResultPanel with per-host counts +
//      structural-review breakdown.

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
using StingTools.Core.Routing;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PenetrationsDetectAndPlaceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var memberIds = CollectMemberIds(doc, uidoc);
            if (memberIds.Count == 0)
            {
                TaskDialog.Show("STING — Penetrations",
                    "No MEP runs in scope.\n\nSelect pipes / ducts / conduits / cable trays " +
                    "before running, or open a view containing them.");
                return Result.Cancelled;
            }

            var slab = new List<PenetrationRecord>();
            var wall = new List<PenetrationRecord>();
            var beam = new List<PenetrationRecord>();
            FrpPlacementResult placeResult = null;

            try
            {
                using (var tx = new Transaction(doc, "STING Penetration Sweep + Place"))
                {
                    tx.Start();

                    slab = SlabPenetrationDetector.Detect(doc, memberIds);
                    wall = WallPenetrationDetector.Detect(doc, memberIds);
                    beam = BeamPenetrationDetector.Detect(doc, memberIds);

                    var all = new List<PenetrationRecord>(slab.Count + wall.Count + beam.Count);
                    all.AddRange(slab); all.AddRange(wall); all.AddRange(beam);

                    placeResult = FrpPenetrationPlacer.Place(doc, all);

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("PenetrationsDetectAndPlaceCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(slab, wall, beam, placeResult);
            return Result.Succeeded;
        }

        private static List<ElementId> CollectMemberIds(Document doc, UIDocument uidoc)
        {
            var ids = new List<ElementId>();
            var sel = uidoc.Selection.GetElementIds();
            if (sel != null && sel.Count > 0)
            {
                foreach (var id in sel)
                {
                    var el = doc.GetElement(id);
                    if (el is Pipe || el is Duct || el is Conduit || el is CableTray ||
                        el is FlexPipe || el is FlexDuct) ids.Add(id);
                }
                return ids;
            }
            var view = doc.ActiveView;
            if (view == null) return ids;
            var cats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_FlexDuctCurves,
            };
            var filter = new ElementMulticategoryFilter(cats);
            var col = new FilteredElementCollector(doc, view.Id)
                .WherePasses(filter)
                .WhereElementIsNotElementType();
            foreach (var el in col) ids.Add(el.Id);
            return ids;
        }

        private static void ShowResult(List<PenetrationRecord> slab,
            List<PenetrationRecord> wall, List<PenetrationRecord> beam, FrpPlacementResult place)
        {
            var panel = StingResultPanel.Create("STING Penetration Sweep");
            panel.SetSubtitle($"Slab + wall + beam crossings detected and stamped");

            panel.AddSection("DETECTED")
                 .Metric("Slab penetrations",  slab.Count.ToString())
                 .Metric("Wall penetrations",  wall.Count.ToString())
                 .Metric("Beam penetrations",  beam.Count.ToString())
                 .Metric("Total",              (slab.Count + wall.Count + beam.Count).ToString());

            if (place != null)
            {
                panel.AddSection("PLACED")
                     .Metric("FRP instances placed", place.Placed.ToString())
                     .Metric("FRP instances updated (idempotent)", place.Stamped.ToString())
                     .Metric("Skipped",              place.Skipped.ToString())
                     .Metric("Errors",               place.Errors.ToString());
            }

            // Beam structural review — surface critical findings.
            int structFail = beam.Count(b => b.StructuralFlag == "STRUCT_FAIL");
            int structReview = beam.Count(b => b.StructuralFlag == "STRUCT_REVIEW");
            int structOk = beam.Count(b => b.StructuralFlag == "STRUCT_OK");
            if (beam.Count > 0)
            {
                panel.AddSection("STRUCTURAL REVIEW (beam hosts)")
                     .Metric("STRUCT_OK (no review needed)", structOk.ToString())
                     .Metric("STRUCT_REVIEW (engineer sign-off)", structReview.ToString())
                     .Metric("STRUCT_FAIL (must reroute)", structFail.ToString());
                if (structFail > 0)
                    panel.Text($"⚠ {structFail} beam penetration(s) violate AISC DG2 / BS EN 1992 location or size limits — reroute before fabrication.");
                if (structReview > 0)
                    panel.Text($"ℹ {structReview} beam penetration(s) need structural-engineer sign-off (close to support or > 0.4 d ratio).");
            }

            if (place?.Warnings != null && place.Warnings.Count > 0)
            {
                foreach (var w in place.Warnings.Take(20)) panel.Text(w);
                if (place.Warnings.Count > 20) panel.Text($"(+{place.Warnings.Count - 20} more — see StingLog)");
            }

            panel.Show();
        }
    }
}
