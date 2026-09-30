// StingTools — SLD / riser diagram: onto a drawing-type sheet
//
// SLD_Generate and SLD_RiserDiagram draw drafting views that nothing put on a sheet:
// the person had to make a sheet, pick a title block, number it and drag the view on,
// and the result carried no drawing-type stamp, so Doctor, Renumber, Heal TBs and
// Produce & Export never saw it. They now go through DrawingProducer.PlaceExistingView
// — the sheet path production uses — so the sheet is found by stamp on a re-run and
// the previous run's diagram is taken off it (see ExistingViewPlacement).

using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;

namespace StingTools.Core.SLD
{
    internal static class SldSheetPlacement
    {
        /// <summary>
        /// Place <paramref name="view"/> on the sheet of the drawing type the routing
        /// table gives <paramref name="req"/>'s (discipline, docType) — a project override
        /// can re-point it — falling back to the request's shipped id.
        /// <paramref name="contextTag"/> (else the request's own) keeps the sheet apart.
        /// </summary>
        internal static string Place(Document doc, DrawingRouteRequest req, View view, string contextTag = null)
        {
            if (req == null) return "No drawing type requested — the view is not on a sheet.";
            string id = DrawingRouteResolver.IdFor(doc, req);
            return Place(doc, id, view, contextTag ?? req.ContextTag, DrawingRouteRequests.StampIds(id, req));
        }

        /// <summary>
        /// Place <paramref name="view"/> on the sheet of drawing type
        /// <paramref name="drawingTypeId"/>, in its own transaction. Returns one line for
        /// the command's report — the sheet, or why the view is not on one. Never throws.
        /// <paramref name="contextTag"/> keeps this sheet apart from sheets the same
        /// drawing type gets from other production paths.
        /// </summary>
        internal static string Place(Document doc, string drawingTypeId, View view, string contextTag = null,
            System.Collections.Generic.IReadOnlyCollection<string> formerDrawingTypeIds = null)
        {
            if (doc == null || view == null) return "No view to put on a sheet.";
            try
            {
                var dt = DrawingTypeRegistry.Get(doc, drawingTypeId);
                if (dt == null)
                    return $"Drawing type '{drawingTypeId}' is not in the catalogue — '{view.Name}' is not on a sheet.";

                ProduceResult pr;
                using (var tx = new Transaction(doc, "STING Place Diagram on Sheet"))
                {
                    tx.Start();
                    pr = DrawingProducer.PlaceExistingView(doc, dt, new DrawingContext { Tag = contextTag, FormerDrawingTypeIds = formerDrawingTypeIds }, view);
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        foreach (var w in pr.Warnings.Distinct()) StingLog.Warn($"SldSheetPlacement {drawingTypeId}: {w}");
                        return $"'{view.Name}' is not on a sheet: the placement did not commit ({status}).";
                    }
                }
                foreach (var w in pr.Warnings.Distinct()) StingLog.Warn($"SldSheetPlacement {drawingTypeId}: {w}");
                if (!(doc.GetElement(pr.SheetId) is ViewSheet sheet))
                    return $"'{view.Name}' is not on a sheet: " + (pr.Warnings.FirstOrDefault() ?? "no sheet was made") + ".";
                string where = $"sheet {sheet.SheetNumber} - {sheet.Name}";
                if (pr.ViewportIds.Count == 0)
                    return $"'{view.Name}' could not be placed on {where}: " + (pr.Warnings.LastOrDefault() ?? "see the STING log") + ".";
                return (pr.SheetReused ? "On existing " : "On new ") + where
                     + (pr.Warnings.Count > 0 ? $" ({pr.Warnings.Distinct().Count()} note(s) in the STING log)." : ".");
            }
            catch (Exception ex)
            {
                StingLog.Error("SldSheetPlacement.Place", ex);
                return $"'{view.Name}' is not on a sheet: {ex.Message}";
            }
        }
    }
}
