// StingTools — Drawing Template Manager
//
// DrawingRouteResolver — the Revit half of DrawingRouteRequests: ask the routing
// table (DrawingDispatcher, project rules first) for a request's key, and fall
// back to the shipped id only when the table routes the key nowhere. So a project
// override that re-points E / FIRE_ALARM_SCHEMATIC, say, is what the command uses.

using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    internal static class DrawingRouteResolver
    {
        /// <summary>The drawing type for <paramref name="req"/>; null only when neither the route nor the fall-back exists.</summary>
        internal static DrawingType Resolve(Document doc, DrawingRouteRequest req)
        {
            if (doc == null || req == null) return null;
            DrawingType dt = null;
            try { dt = DrawingDispatcher.Resolve(doc, req.Discipline, "*", req.DocType); }
            catch (System.Exception ex) { StingLog.Warn($"DrawingRouteResolver {req.Discipline}/{req.DocType}: {ex.Message}"); }
            if (dt != null) return dt;

            var fb = DrawingTypeRegistry.Get(doc, req.FallbackDrawingTypeId);
            StingLog.Warn($"DrawingRouteResolver: routing sends {req.Discipline} / {req.DocType} nowhere — "
                + (fb != null ? $"using '{req.FallbackDrawingTypeId}'." : $"and '{req.FallbackDrawingTypeId}' is not in the catalogue."));
            return fb;
        }

        /// <summary>The id to stamp for <paramref name="req"/> (the fall-back id when nothing resolves).</summary>
        internal static string IdFor(Document doc, DrawingRouteRequest req)
            => Resolve(doc, req)?.Id ?? req?.FallbackDrawingTypeId;
    }
}
