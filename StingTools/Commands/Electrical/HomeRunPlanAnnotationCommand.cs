// StingTools — Home-run plan annotation (I3).
//
// Annotates conduit home-runs on the active floor-plan view.
//
// A home-run conduit is identified by either:
//   • ELC_CONDUIT_HOME_RUN_BOOL = 1  (shared parameter), or
//   • ELC_CIRCUIT_REF_TXT is non-empty (any conduit with a circuit reference
//     is treated as a potential home run).
//
// For each qualifying conduit, the command:
//   1. Determines the home-run endpoint (the conduit endpoint closest to the
//      panel, heuristically the end with the lowest X coordinate, or overridden
//      by ELC_CONDUIT_HOME_RUN_END_TXT = "Start"/"End").
//   2. Places a 45° detail-line arrow (two line segments, 5mm length) at that
//      endpoint pointing toward the lower-left (canonical panel direction).
//   3. Places a TextNote showing "{PanelName}-{CircuitNum}" derived from
//      ELC_CIRCUIT_REF_TXT, stamped with STING_HOME_RUN_ANNOT in the
//      ALL_MODEL_INSTANCE_COMMENTS parameter for later removal.
//   4. Conduits are grouped by circuit reference; only one annotation is placed
//      per unique circuit ref.
//
// ClearHomeRunAnnotationsCommand removes all TextNotes and DetailCurves
// previously stamped by this command in the active view.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Places home-run arrow annotations on conduit runs in the active
    /// floor-plan view.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HomeRunPlanAnnotationCommand : IExternalCommand
    {
        private const string HomeRunMarker = "STING_HOME_RUN_ANNOT";
        // Arrow size in Revit internal units (feet). 5mm ≈ 0.0164 ft.
        private const double ArrowLenFt = 5.0 / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var view = doc.ActiveView;

            // ── Require a floor plan view ─────────────────────────────────────
            if (view == null || view.ViewType != ViewType.FloorPlan)
            {
                TaskDialog.Show("STING Home-Run Annotation",
                    "The active view must be a Floor Plan.\n\n" +
                    "Open a floor plan view and run the command again.");
                return Result.Cancelled;
            }

            // ── Collect qualifying conduits in the active view ─────────────────
            var conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(el => IsHomeRunConduit(el))
                .ToList();

            if (conduits.Count == 0)
            {
                TaskDialog.Show("STING Home-Run Annotation",
                    "No home-run conduits found in the active view.\n\n" +
                    "Set ELC_CONDUIT_HOME_RUN_BOOL = 1 on conduits, or populate " +
                    "ELC_CIRCUIT_REF_TXT with the circuit reference.");
                return Result.Cancelled;
            }

            // ── Resolve default text type (first available TextNote type) ─────
            var textTypeId = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstElementId();
            if (textTypeId == null || textTypeId == ElementId.InvalidElementId)
            {
                message = "No TextNote types found in the document.";
                return Result.Failed;
            }

            // ── Group conduits by circuit reference ───────────────────────────
            var groups = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in conduits)
            {
                string circRef = GetCircuitRef(el);
                if (string.IsNullOrEmpty(circRef)) circRef = el.Id.ToString();
                if (!groups.ContainsKey(circRef)) groups[circRef] = new List<Element>();
                groups[circRef].Add(el);
            }

            int annotated = 0;

            using (var tx = new Transaction(doc, "STING Home-Run Annotation"))
            {
                tx.Start();

                foreach (var kvp in groups)
                {
                    string circRef   = kvp.Key;
                    // Take the first conduit from the group for annotation.
                    var    conduit   = kvp.Value[0];
                    XYZ    arrowPt   = GetHomeRunEndpoint(conduit);
                    if (arrowPt == null) continue;

                    // ── Place 45° arrow lines ─────────────────────────────────
                    // Two lines forming a ">" arrow pointing toward lower-left.
                    try
                    {
                        // Arrow shaft: horizontal line going left from arrowPt.
                        var shaftStart = arrowPt;
                        var shaftEnd   = new XYZ(arrowPt.X - ArrowLenFt * 1.5, arrowPt.Y, arrowPt.Z);
                        var shaft      = Line.CreateBound(shaftStart, shaftEnd);
                        var shaftCurve = doc.Create.NewDetailCurve(view, shaft);
                        StampMarker(shaftCurve, HomeRunMarker);

                        // Arrow upper barb: 45° up-left from arrowPt.
                        double bx = arrowPt.X - ArrowLenFt * Math.Cos(Math.PI / 4.0);
                        double by = arrowPt.Y + ArrowLenFt * Math.Sin(Math.PI / 4.0);
                        var upBarb   = Line.CreateBound(arrowPt, new XYZ(bx, by, arrowPt.Z));
                        var upCurve  = doc.Create.NewDetailCurve(view, upBarb);
                        StampMarker(upCurve, HomeRunMarker);

                        // Arrow lower barb: 45° down-left from arrowPt.
                        var dnBarb   = Line.CreateBound(arrowPt, new XYZ(bx, 2 * arrowPt.Y - by, arrowPt.Z));
                        var dnCurve  = doc.Create.NewDetailCurve(view, dnBarb);
                        StampMarker(dnCurve, HomeRunMarker);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"HomeRun arrow [{circRef}]: {ex.Message}");
                    }

                    // ── Place TextNote with circuit reference ─────────────────
                    try
                    {
                        // Position text slightly above the arrow point.
                        var textPt = new XYZ(
                            arrowPt.X - ArrowLenFt * 2.0,
                            arrowPt.Y + ArrowLenFt * 0.5,
                            arrowPt.Z);

                        var tnOpts = new TextNoteOptions(textTypeId)
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Left
                        };
                        var tn = TextNote.Create(doc, view.Id, textPt, circRef, tnOpts);
                        StampMarker(tn, HomeRunMarker);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"HomeRun text [{circRef}]: {ex.Message}");
                    }

                    annotated++;
                }

                tx.Commit();
            }

            TaskDialog.Show("STING Home-Run Annotation",
                $"Annotated {annotated} circuit(s) on '{view.Name}'.\n\n" +
                "Run 'Clear Home-Run Annotations' to remove all placed annotations.");
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool IsHomeRunConduit(Element el)
        {
            try
            {
                var boolParam = el.LookupParameter("ELC_CONDUIT_HOME_RUN_BOOL");
                if (boolParam != null && boolParam.AsInteger() == 1) return true;
            }
            catch { /* parameter absent */ }

            try
            {
                string circRef = el.LookupParameter("ELC_CIRCUIT_REF_TXT")?.AsString() ?? "";
                return !string.IsNullOrEmpty(circRef);
            }
            catch { return false; }
        }

        private static string GetCircuitRef(Element el)
        {
            try { return el.LookupParameter("ELC_CIRCUIT_REF_TXT")?.AsString()?.Trim() ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// Returns the home-run endpoint of a conduit element.
        /// Reads ELC_CONDUIT_HOME_RUN_END_TXT ("Start" or "End") first;
        /// falls back to the endpoint with the lower X coordinate (closest to
        /// left/panel side of plan).
        /// </summary>
        private static XYZ GetHomeRunEndpoint(Element el)
        {
            try
            {
                string endHint = el.LookupParameter("ELC_CONDUIT_HOME_RUN_END_TXT")
                                   ?.AsString()?.Trim()?.ToUpperInvariant() ?? "";

                if (el.Location is LocationCurve lc)
                {
                    var curve = lc.Curve;
                    var p0    = curve.GetEndPoint(0);
                    var p1    = curve.GetEndPoint(1);

                    if (endHint == "START") return p0;
                    if (endHint == "END")   return p1;

                    // Heuristic: return the endpoint with the lower X (panel side).
                    return p0.X <= p1.X ? p0 : p1;
                }
            }
            catch (Exception ex) { StingLog.Warn($"HomeRunEndpoint: {ex.Message}"); }
            return null;
        }

        private static void StampMarker(Element el, string marker)
        {
            try
            {
                var p = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                p?.Set(marker);
            }
            catch { /* not all element types support this parameter */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes all TextNotes and DetailCurves placed by
    /// <see cref="HomeRunPlanAnnotationCommand"/> in the active view.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearHomeRunAnnotationsCommand : IExternalCommand
    {
        private const string HomeRunMarker = "STING_HOME_RUN_ANNOT";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var view = doc.ActiveView;

            if (view == null)
            {
                TaskDialog.Show("STING Clear Home-Run", "No active view.");
                return Result.Cancelled;
            }

            // Collect stamped TextNotes.
            var toDelete = new List<ElementId>();

            var textNotes = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(TextNote))
                .Cast<Element>()
                .Where(n => GetComments(n) == HomeRunMarker)
                .Select(n => n.Id)
                .ToList();
            toDelete.AddRange(textNotes);

            // Collect stamped DetailCurves.
            var detailCurves = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(DetailCurve))
                .Cast<Element>()
                .Where(n => GetComments(n) == HomeRunMarker)
                .Select(n => n.Id)
                .ToList();
            toDelete.AddRange(detailCurves);

            if (toDelete.Count == 0)
            {
                TaskDialog.Show("STING Clear Home-Run",
                    "No home-run annotations found in the active view.");
                return Result.Cancelled;
            }

            int deleted = 0;

            using (var tx = new Transaction(doc, "STING Clear Home-Run Annotations"))
            {
                tx.Start();
                foreach (var id in toDelete)
                {
                    try { doc.Delete(id); deleted++; }
                    catch (Exception ex) { StingLog.Warn($"ClearHomeRun delete {id}: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Clear Home-Run",
                $"Removed {deleted} home-run annotation element(s) from '{view.Name}'.");
            return Result.Succeeded;
        }

        private static string GetComments(Element el)
        {
            try { return el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? ""; }
            catch { return ""; }
        }
    }
}
