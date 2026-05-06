// StingTools — compound symbol placer (Phase 175)
//
// Lays out a compound symbol (e.g. SLD_RCBO = MCB + RCD; or a complete
// motor starter = MCCB + contactor + thermal overload + motor) on a
// drafting view. Three layout modes:
//
//   * VerticalStack    — components stacked top to bottom with a single
//                        spine line connecting them. Good for compact
//                        legend keys.
//   * HorizontalSeries — components placed left to right with horizontal
//                        connection lines between them. Good for
//                        circuit-flow (inline) representations.
//   * Ladder           — two vertical rails (supply / neutral) with
//                        components on horizontal rungs between them.
//                        One rung per component by default; group
//                        adjacent components onto one rung when they
//                        share a `rung_*` discriminator in the concept
//                        tree (extension hook for future).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public enum CompoundLayoutMode
    {
        VerticalStack,
        HorizontalSeries,
        Ladder,
    }

    public static class CompoundSymbolPlacer
    {
        private const double MmPerFoot = 304.8;
        private const double SpacingMm = 10.0;
        private const double LadderRailGapMm = 60.0;
        private const double LadderRungSpacingMm = 12.0;
        private static double MmToFt(double mm) => mm / MmPerFoot;

        public static List<ElementId> PlaceCompound(Document doc, View view, XYZ origin,
            string compoundConceptId, string standardId,
            CompoundLayoutMode mode = CompoundLayoutMode.VerticalStack)
        {
            var placed = new List<ElementId>();
            if (doc == null || view == null || origin == null
                || string.IsNullOrEmpty(compoundConceptId)) return placed;

            try
            {
                var concept = SymbolConceptRegistry.GetConcept(compoundConceptId);
                if (concept?.CompoundComponents == null || concept.CompoundComponents.Count == 0)
                    return placed;

                switch (mode)
                {
                    case CompoundLayoutMode.HorizontalSeries:
                        return PlaceHorizontalSeries(doc, view, origin, concept, standardId);
                    case CompoundLayoutMode.Ladder:
                        return PlaceLadder(doc, view, origin, concept, standardId);
                    case CompoundLayoutMode.VerticalStack:
                    default:
                        return PlaceVerticalStack(doc, view, origin, concept, standardId);
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceCompound {compoundConceptId}: {ex.Message}");
                return placed;
            }
        }

        // ── VerticalStack ───────────────────────────────────────────────

        private static List<ElementId> PlaceVerticalStack(Document doc, View view, XYZ origin,
            SymbolConcept concept, string standardId)
        {
            var placed = new List<ElementId>();
            double dy = MmToFt(SpacingMm);
            double y = 0;
            XYZ prev = null;
            foreach (var componentId in concept.CompoundComponents)
            {
                var pos = new XYZ(origin.X, origin.Y - y, 0);
                var id = PlaceOne(doc, view, pos, componentId, standardId);
                if (id != ElementId.InvalidElementId) placed.Add(id);
                if (prev != null) DrawConnectionLine(doc, view, prev, pos);
                prev = pos;
                y += dy;
            }
            return placed;
        }

        // ── HorizontalSeries ────────────────────────────────────────────

        private static List<ElementId> PlaceHorizontalSeries(Document doc, View view, XYZ origin,
            SymbolConcept concept, string standardId)
        {
            var placed = new List<ElementId>();
            double dx = MmToFt(SpacingMm);
            double x = 0;
            XYZ prev = null;
            foreach (var componentId in concept.CompoundComponents)
            {
                var pos = new XYZ(origin.X + x, origin.Y, 0);
                var id = PlaceOne(doc, view, pos, componentId, standardId);
                if (id != ElementId.InvalidElementId) placed.Add(id);
                if (prev != null) DrawConnectionLine(doc, view, prev, pos);
                prev = pos;
                x += dx;
            }
            return placed;
        }

        // ── Ladder logic ────────────────────────────────────────────────

        private static List<ElementId> PlaceLadder(Document doc, View view, XYZ origin,
            SymbolConcept concept, string standardId)
        {
            var placed = new List<ElementId>();
            double rail = MmToFt(LadderRailGapMm);
            double rung = MmToFt(LadderRungSpacingMm);
            int rungCount = concept.CompoundComponents.Count;
            if (rungCount == 0) return placed;

            // Rail extents — top to bottom of rung stack with a small margin.
            double topY = origin.Y + rung * 0.5;
            double bottomY = origin.Y - rung * (rungCount - 0.5);
            XYZ leftRailTop  = new XYZ(origin.X,         topY,    0);
            XYZ leftRailBot  = new XYZ(origin.X,         bottomY, 0);
            XYZ rightRailTop = new XYZ(origin.X + rail,  topY,    0);
            XYZ rightRailBot = new XYZ(origin.X + rail,  bottomY, 0);

            DrawConnectionLine(doc, view, leftRailTop, leftRailBot);
            DrawConnectionLine(doc, view, rightRailTop, rightRailBot);

            for (int i = 0; i < rungCount; i++)
            {
                double yi = origin.Y - i * rung;
                XYZ rungLeft  = new XYZ(origin.X,        yi, 0);
                XYZ rungRight = new XYZ(origin.X + rail, yi, 0);
                XYZ centre    = new XYZ(origin.X + rail * 0.5, yi, 0);

                var id = PlaceOne(doc, view, centre, concept.CompoundComponents[i], standardId);
                if (id != ElementId.InvalidElementId) placed.Add(id);

                // Connection lines from rails to component centre on each side.
                DrawConnectionLine(doc, view, rungLeft,  centre);
                DrawConnectionLine(doc, view, centre,    rungRight);
            }

            return placed;
        }

        // ── Shared helpers ──────────────────────────────────────────────

        private static ElementId PlaceOne(Document doc, View view, XYZ pos,
            string componentId, string standardId)
        {
            try
            {
                string fam = SymbolConceptRegistry.GetAnnotationFamilyName(componentId, standardId)
                    ?? SymbolConceptRegistry.GetFamilyName(componentId, standardId, "Schematic", "standard", null);
                if (string.IsNullOrEmpty(fam)) return ElementId.InvalidElementId;

                var sym = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs =>
                        string.Equals(fs.Name, fam, StringComparison.OrdinalIgnoreCase));
                if (sym == null) return ElementId.InvalidElementId;
                if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }

                var inst = doc.Create.NewFamilyInstance(pos, sym, view);
                if (inst != null)
                {
                    StampParam(inst, "STING_SYMBOL_ID", componentId);
                    return inst.Id;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"CompoundSymbolPlacer.PlaceOne {componentId}: {ex.Message}");
            }
            return ElementId.InvalidElementId;
        }

        public static void DrawConnectionLine(Document doc, View view, XYZ from, XYZ to)
        {
            try
            {
                if (from == null || to == null) return;
                if (from.DistanceTo(to) < 1e-6) return;
                doc.Create.NewDetailCurve(view, Line.CreateBound(from, to));
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DrawConnectionLine: {ex.Message}"); }
        }

        private static void StampParam(Element el, string name, string value)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p != null && !p.IsReadOnly) p.Set(value ?? "");
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"CompoundSymbolPlacer Stamp {name}: {ex.Message}"); }
        }
    }
}
