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

        /// <summary>
        /// Lay out the concept on a two-rail ladder. Prefers
        /// <see cref="SymbolConcept.CompoundRungs"/> when present (one
        /// row per rung, multiple components in series along the rung);
        /// falls back to <see cref="SymbolConcept.CompoundComponents"/>
        /// (one component per rung) for legacy concepts.
        /// </summary>
        private static List<ElementId> PlaceLadder(Document doc, View view, XYZ origin,
            SymbolConcept concept, string standardId)
        {
            var placed = new List<ElementId>();

            // Build the canonical rung list once, regardless of source.
            List<List<string>> rungs = ResolveRungs(concept);
            if (rungs.Count == 0) return placed;

            // Width of a rail bay scales with the widest rung so multi-
            // component rungs don't overlap the rails.
            double rungSpace = MmToFt(LadderRungSpacingMm);
            double minBay    = MmToFt(LadderRailGapMm);
            int    maxOnRung = rungs.Max(r => r.Count);
            double bayWidth  = Math.Max(minBay, maxOnRung * MmToFt(SpacingMm * 1.6));

            int rungCount = rungs.Count;
            double topY    = origin.Y + rungSpace * 0.5;
            double bottomY = origin.Y - rungSpace * (rungCount - 0.5);

            XYZ leftRailTop  = new XYZ(origin.X,             topY,    0);
            XYZ leftRailBot  = new XYZ(origin.X,             bottomY, 0);
            XYZ rightRailTop = new XYZ(origin.X + bayWidth,  topY,    0);
            XYZ rightRailBot = new XYZ(origin.X + bayWidth,  bottomY, 0);

            DrawConnectionLine(doc, view, leftRailTop,  leftRailBot);
            DrawConnectionLine(doc, view, rightRailTop, rightRailBot);

            for (int i = 0; i < rungCount; i++)
            {
                var components = rungs[i];
                if (components == null || components.Count == 0) continue;

                double yi = origin.Y - i * rungSpace;
                XYZ rungLeft  = new XYZ(origin.X,            yi, 0);
                XYZ rungRight = new XYZ(origin.X + bayWidth, yi, 0);

                // Distribute components evenly across the bay.
                int n = components.Count;
                XYZ prev = rungLeft;
                for (int j = 0; j < n; j++)
                {
                    // Position at fraction (j + 1) / (n + 1) — gives even
                    // spacing with margin on both sides of the bay.
                    double frac = (j + 1.0) / (n + 1.0);
                    var pos = new XYZ(origin.X + bayWidth * frac, yi, 0);
                    var id  = PlaceOne(doc, view, pos, components[j], standardId);
                    if (id != ElementId.InvalidElementId) placed.Add(id);

                    DrawConnectionLine(doc, view, prev, pos);
                    prev = pos;
                }
                DrawConnectionLine(doc, view, prev, rungRight);
            }
            return placed;
        }

        /// <summary>
        /// Resolve the rung list. <see cref="SymbolConcept.CompoundRungs"/>
        /// wins when present and non-empty; otherwise each entry in
        /// <see cref="SymbolConcept.CompoundComponents"/> becomes its own
        /// single-component rung.
        /// </summary>
        private static List<List<string>> ResolveRungs(SymbolConcept concept)
        {
            var rungs = new List<List<string>>();
            if (concept.CompoundRungs != null && concept.CompoundRungs.Count > 0)
            {
                foreach (var r in concept.CompoundRungs)
                    if (r?.Components != null && r.Components.Count > 0)
                        rungs.Add(new List<string>(r.Components));
            }
            else if (concept.CompoundComponents != null)
            {
                foreach (var cid in concept.CompoundComponents)
                    if (!string.IsNullOrEmpty(cid))
                        rungs.Add(new List<string> { cid });
            }
            return rungs;
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
