// StingTools — compound symbol placer (Phase 175)
//
// Lays out a compound symbol (e.g. SLD_RCBO = MCB + RCD) on a drafting
// view by stacking its components vertically and drawing connection
// lines between adjacent components.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public static class CompoundSymbolPlacer
    {
        private const double MmPerFoot = 304.8;
        private const double SpacingMm = 10.0;
        private static double MmToFt(double mm) => mm / MmPerFoot;

        public static List<ElementId> PlaceCompound(Document doc, View view, XYZ origin,
            string compoundConceptId, string standardId)
        {
            var placed = new List<ElementId>();
            if (doc == null || view == null || origin == null
                || string.IsNullOrEmpty(compoundConceptId)) return placed;

            try
            {
                var concept = SymbolConceptRegistry.GetConcept(compoundConceptId);
                if (concept?.CompoundComponents == null || concept.CompoundComponents.Count == 0)
                    return placed;

                double dy = MmToFt(SpacingMm);
                double y = 0;
                XYZ prevPos = null;

                foreach (var componentId in concept.CompoundComponents)
                {
                    XYZ pos = new XYZ(origin.X, origin.Y - y, 0);
                    string fam = SymbolConceptRegistry.GetAnnotationFamilyName(componentId, standardId);
                    if (string.IsNullOrEmpty(fam)) { y += dy; continue; }

                    var sym = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .FirstOrDefault(fs => string.Equals(fs.Name, fam, StringComparison.OrdinalIgnoreCase));
                    if (sym == null) { y += dy; continue; }
                    if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }

                    try
                    {
                        var inst = doc.Create.NewFamilyInstance(pos, sym, view);
                        if (inst != null) placed.Add(inst.Id);
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn($"CompoundSymbolPlacer NewFamilyInstance: {ex.Message}");
                    }

                    if (prevPos != null) DrawConnectionLine(doc, view, prevPos, pos);
                    prevPos = pos;
                    y += dy;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceCompound {compoundConceptId}: {ex.Message}");
            }
            return placed;
        }

        public static void DrawConnectionLine(Document doc, View view, XYZ from, XYZ to)
        {
            try
            {
                if (from.DistanceTo(to) < 1e-6) return;
                doc.Create.NewDetailCurve(view, Line.CreateBound(from, to));
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DrawConnectionLine: {ex.Message}"); }
        }
    }
}
