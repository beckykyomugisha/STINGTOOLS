// StingTools — view-context resolver (Phase 175)
//
// Maps a Revit View to one of the seven contexts that drive symbol
// family selection (Plan / CeilingPlan / Elevation / Section / Detail
// / Schematic / ThreeD).

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public enum SymbolViewContext { Plan, CeilingPlan, Elevation, Section, Detail, Schematic, ThreeD }

    public static class SymbolViewContextResolver
    {
        public static SymbolViewContext Resolve(View view)
        {
            if (view == null) return SymbolViewContext.Plan;
            if (IsSchematicView(view)) return SymbolViewContext.Schematic;

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:    return SymbolViewContext.Plan;
                case ViewType.CeilingPlan:  return SymbolViewContext.CeilingPlan;
                case ViewType.Elevation:    return SymbolViewContext.Elevation;
                case ViewType.Section:      return SymbolViewContext.Section;
                case ViewType.Detail:       return SymbolViewContext.Detail;
                case ViewType.DraftingView: return SymbolViewContext.Schematic;
                case ViewType.ThreeD:       return SymbolViewContext.ThreeD;
                default:                    return SymbolViewContext.Plan;
            }
        }

        public static bool IsSchematicView(View view)
        {
            if (view == null) return false;
            try
            {
                if (view.ViewType == ViewType.DraftingView) return true;

                string n = (view.Name ?? "").ToLowerInvariant();
                if (n.Contains("schematic") || n.Contains("sld") ||
                    n.Contains("riser")     || n.Contains("diagram"))
                    return true;

                // Try the DrawingType stamp left by DrawingTypePresentation.
                try
                {
                    var p = view.LookupParameter("STING_DRAWING_TYPE_ID_TXT");
                    string id = p?.AsString() ?? "";
                    string lid = id.ToLowerInvariant();
                    if (lid.Contains("riser") || lid.Contains("schematic")
                        || lid.Contains("legend") || lid.Contains("sld"))
                        return true;
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"IsSchematicView dt lookup: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"IsSchematicView: {ex.Message}");
            }
            return false;
        }

        public static bool IsPlanLikeView(View view)
            => view != null && (view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.CeilingPlan);

        public static string ToKey(SymbolViewContext ctx)
        {
            switch (ctx)
            {
                case SymbolViewContext.Plan:        return "Plan";
                case SymbolViewContext.CeilingPlan: return "CeilingPlan";
                case SymbolViewContext.Elevation:   return "Elevation";
                case SymbolViewContext.Section:     return "Section";
                case SymbolViewContext.Detail:      return "Detail";
                case SymbolViewContext.Schematic:   return "Schematic";
                case SymbolViewContext.ThreeD:      return "ThreeD";
                default: return "Plan";
            }
        }
    }
}
