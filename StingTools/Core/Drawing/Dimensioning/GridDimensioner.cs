// StingTools — Drawing Template Manager · Phase 175
//
// GridDimensioner replaces the hardcoded grid-dim block previously in
// AnnotationRunner.RunDimRules with a strategy-aware implementation:
//   * Linear / Chain → one Linear DimensionType chain per axis with
//     every grid as a segment reference. Revit auto-segments.
//   * Ordinate       → one Ordinate DimensionType chain per axis whose
//     witness lines all originate from the first grid (the datum).
//
// 3D / non-2D views are skipped — Revit's Dimension API rejects them.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;

namespace StingTools.Core.Drawing.Dimensioning
{
    internal static class GridDimensioner
    {
        public const double GridDimOffsetMm = 1500;

        public static void Run(Document doc, View view, AnnotationRulePack pack, AnnotationResult result)
        {
            if (!IsDimensionable(view))
            {
                result.Warnings.Add(
                    $"GridDim: view '{view?.Name}' is not 2D — skipped. " +
                    "Revit's Dimension API rejects 3D views; place this rule on a plan / section / elevation.");
                return;
            }

            var grids = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Grid)).Cast<Grid>()
                .ToList();
            if (grids.Count < 2) return;

            var horiz = grids.Where(IsHorizontal).ToList();
            var vert  = grids.Where(IsVertical).ToList();

            var strategy = DimensionStrategy.Parse(pack.DimensionStrategy);
            var dimType  = DimensionStrategy.ResolveType(doc, strategy, pack.DimensionStyle);

            try { CreateChain(doc, view, vert,  XYZ.BasisX, strategy, dimType, result); }
            catch (Exception ex) { result.Warnings.Add($"GridDim X-axis: {ex.Message}"); }
            try { CreateChain(doc, view, horiz, XYZ.BasisY, strategy, dimType, result); }
            catch (Exception ex) { result.Warnings.Add($"GridDim Y-axis: {ex.Message}"); }
        }

        private static void CreateChain(Document doc, View view, List<Grid> grids,
            XYZ axis, DimStrategyKind strategy, DimensionType dimType, AnnotationResult result)
        {
            if (grids == null || grids.Count < 2) return;

            // Project each grid's intersection with the view plane onto the
            // axis so the chain reads in geographic order.
            var pts = new List<(Reference R, XYZ P)>();
            foreach (var g in grids)
            {
                if (g.Curve == null) continue;
                var origin = g.Curve.GetEndPoint(0);
                pts.Add((new Reference(g), origin));
            }
            if (pts.Count < 2) return;
            pts = DimensionStrategy.SortAlongAxis(pts, axis);

            var refArr = new ReferenceArray();
            foreach (var p in pts) refArr.Append(p.R);

            // Witness line — perpendicular to axis, dropped 1500mm clear of
            // the chain's bounding extent so it doesn't overlay the grids.
            var first = pts.First().P;
            var last  = pts.Last().P;
            var span  = (last - first).GetLength();
            var line  = DimensionStrategy.BuildWitnessLine(first, axis, GridDimOffsetMm, span + 1.0);

            try
            {
                Dimension dim = dimType != null
                    ? doc.Create.NewDimension(view, line, refArr, dimType)
                    : doc.Create.NewDimension(view, line, refArr);
                if (dim != null)
                {
                    result.DimsPlaced++;
                    if (strategy == DimStrategyKind.Ordinate && dimType == null)
                        result.Warnings.Add(
                            "GridDim: Ordinate strategy requested but no Ordinate DimensionType is loaded — fell back to Linear. " +
                            "Author an Ordinate-style DimensionType in Manage > Additional Settings > Dimension Types " +
                            "with 'ordinate' in its name (e.g. 'STING - Ordinate'), or pin a name via DrawingType.annotation.dimensionStyle.");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"GridDim NewDimension: {ex.Message}");
            }
        }

        public static bool IsDimensionable(View v)
        {
            if (v == null) return false;
            if (v is View3D) return false;
            switch (v.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.AreaPlan:
                case ViewType.EngineeringPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsHorizontal(Grid g)
        {
            try { var c = g.Curve as Line; if (c == null) return false; var d = c.Direction; return Math.Abs(d.Y) > Math.Abs(d.X); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private static bool IsVertical(Grid g)
        {
            try { var c = g.Curve as Line; if (c == null) return false; var d = c.Direction; return Math.Abs(d.X) >= Math.Abs(d.Y); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
