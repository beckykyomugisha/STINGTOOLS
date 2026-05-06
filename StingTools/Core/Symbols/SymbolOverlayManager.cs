// StingTools — symbol overlay manager (Phase 175)
//
// Places symbol overlays on existing model elements via IndependentTag.
// One tag per (element, view); tag head position derived from the host's
// location point.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Symbols
{
    public static class SymbolOverlayManager
    {
        public static ElementId PlaceSymbolOverlay(Document doc, View view, Element host,
            string conceptId, string standardId)
        {
            if (doc == null || view == null || host == null
                || string.IsNullOrEmpty(conceptId) || string.IsNullOrEmpty(standardId))
                return ElementId.InvalidElementId;

            try
            {
                bool schematic = SymbolViewContextResolver.IsSchematicView(view);
                string viewCtx = SymbolViewContextResolver.ToKey(SymbolViewContextResolver.Resolve(view));
                string scaleTier = SymbolScaleEngine.GetScaleTier(view);

                string famName = SymbolConceptRegistry.GetFamilyName(
                    conceptId, standardId, viewCtx, scaleTier, null);
                if (string.IsNullOrEmpty(famName))
                {
                    StingTools.Core.StingLog.Warn(
                        $"PlaceSymbolOverlay: no family for {conceptId}/{standardId}.");
                    return ElementId.InvalidElementId;
                }

                FamilySymbol sym = FindFamilySymbol(doc, famName);
                if (sym == null)
                {
                    StingTools.Core.StingLog.Warn(
                        $"PlaceSymbolOverlay: family {famName} not loaded — skip.");
                    return ElementId.InvalidElementId;
                }
                if (!sym.IsActive)
                {
                    sym.Activate();
                    doc.Regenerate();
                }

                XYZ headPos = GetHostLocation(host);
                if (headPos == null) headPos = XYZ.Zero;

                IndependentTag tag = IndependentTag.Create(
                    doc, sym.Id, view.Id, new Reference(host),
                    addLeader: false,
                    TagOrientation.Horizontal,
                    headPos);
                if (tag == null) return ElementId.InvalidElementId;

                Stamp(tag, "STING_SYMBOL_ID", conceptId);
                Stamp(tag, "STING_SYMBOL_STANDARD", standardId);
                Stamp(tag, "STING_HOST_ELEMENT_ID", host.Id.Value.ToString());

                SymbolAnnotationEngine.PlaceAnnotation(doc, view, tag, conceptId, standardId);
                return tag.Id;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceSymbolOverlay: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        public static int PlaceOverlaysForView(Document doc, View view,
            IProgress<string> progress = null)
        {
            if (doc == null || view == null) return 0;
            int placed = 0;
            try
            {
                var elements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var el in elements)
                {
                    try
                    {
                        var existing = el.LookupParameter("STING_SYMBOL_ID")?.AsString();
                        if (!string.IsNullOrEmpty(existing)) continue;

                        var concepts = SymbolConceptRegistry.GetConceptsForCategory(el.Category?.Name);
                        var concept = concepts.FirstOrDefault();
                        if (concept == null) continue;

                        string std = SymbolStandardResolver.ResolveStandard(doc, view, el);
                        if (PlaceSymbolOverlay(doc, view, el, concept.ConceptId, std) != ElementId.InvalidElementId)
                            placed++;
                        progress?.Report($"Placed {placed} overlays in view {view.Name}");
                    }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceOverlaysForView inner: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceOverlaysForView: {ex.Message}"); }
            return placed;
        }

        public static int SyncViewFilterVisibility(Document doc, View view)
        {
            if (doc == null || view == null) return 0;
            int adjusted = 0;
            try
            {
                var ogsHidden  = new OverrideGraphicSettings();
                ogsHidden.SetHalftone(true);
                var ogsVisible = new OverrideGraphicSettings();

                var tags = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in tags)
                {
                    var p = tag.LookupParameter("STING_HOST_ELEMENT_ID");
                    string hostStr = p?.AsString();
                    if (string.IsNullOrEmpty(hostStr) || !long.TryParse(hostStr, out var raw)) continue;
                    var host = doc.GetElement(new ElementId(raw));
                    if (host == null) { view.SetElementOverrides(tag.Id, ogsHidden); adjusted++; continue; }
                    bool hostHidden = host.IsHidden(view);
                    view.SetElementOverrides(tag.Id, hostHidden ? ogsHidden : ogsVisible);
                    adjusted++;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SyncViewFilterVisibility: {ex.Message}"); }
            return adjusted;
        }

        public static int SyncAllFilterVisibility(Document doc)
        {
            int total = 0;
            foreach (View v in new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate))
            {
                total += SyncViewFilterVisibility(doc, v);
            }
            return total;
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static FamilySymbol FindFamilySymbol(Document doc, string name)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => string.Equals(fs.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"FindFamilySymbol: {ex.Message}"); return null; }
        }

        private static XYZ GetHostLocation(Element host)
        {
            try
            {
                if (host?.Location is LocationPoint lp) return lp.Point;
                if (host?.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
                var bb = host?.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"GetHostLocation: {ex.Message}"); }
            return null;
        }

        private static void Stamp(IndependentTag tag, string paramName, string value)
        {
            try
            {
                var p = tag.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly) p.Set(value ?? "");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"Stamp {paramName}: {ex.Message}");
            }
        }
    }
}
