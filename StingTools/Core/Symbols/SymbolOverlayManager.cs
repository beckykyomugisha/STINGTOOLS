using StingTools.Core;
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
                // Pre-build a HashSet of host element IDs that already
                // carry a STING symbol tag in this view. Read from the
                // tag's STING_HOST_ELEMENT_ID stamp (the bug: previous
                // code read the param from the host element, but the
                // param is stamped on the tag).
                var alreadyCovered = new HashSet<long>(
                    new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(IndependentTag))
                        .Cast<IndependentTag>()
                        .Select(t => t.LookupParameter("STING_HOST_ELEMENT_ID")?.AsString())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Select(s => long.TryParse(s, out var v) ? v : 0L)
                        .Where(v => v != 0L));

                var elements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var el in elements)
                {
                    try
                    {
                        if (alreadyCovered.Contains(el.Id.Value)) continue;

                        // Pick a concept by family-name keyword match
                        // first (e.g. "pendant" → LTG_PENDANT), then
                        // fall back to first concept for the category.
                        var concepts = SymbolConceptRegistry.GetConceptsForCategory(el.Category?.Name);
                        var concept = ResolveConceptForElement(el, concepts);
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

        /// <summary>
        /// Pick the most specific concept for an element. Order:
        ///   1. Element's own STING_SYMBOL_ID stamp (Universe C —
        ///      augmented family knows its concept).
        ///   2. Project-specific alias map
        ///      (<c>&lt;project&gt;/_BIM_COORD/symbol_aliases.json</c>)
        ///      — exact and glob matches on family/type/instance name.
        ///   3. Family/type name keyword match against concept name
        ///      tokens (e.g. element with family "Pendant - Glass" maps
        ///      to LTG_PENDANT, not LTG_DOWNLIGHT_RND).
        ///   4. First concept for the category (legacy behaviour).
        /// </summary>
        private static SymbolConcept ResolveConceptForElement(Element el,
            IReadOnlyList<SymbolConcept> categoryConcepts)
        {
            if (categoryConcepts == null || categoryConcepts.Count == 0) return null;
            if (categoryConcepts.Count == 1) return categoryConcepts[0];

            // 1. Augmented-family signal.
            try
            {
                string stamped = el.LookupParameter("STING_SYMBOL_ID")?.AsString();
                if (!string.IsNullOrEmpty(stamped))
                {
                    var match = categoryConcepts.FirstOrDefault(c =>
                        string.Equals(c.ConceptId, stamped, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveConcept stamp: {ex.Message}"); }

            // 2. Project-specific alias map.
            try
            {
                string fam = "", typ = "", inst = el.Name ?? "";
                if (el is FamilyInstance fi)
                {
                    typ = fi.Symbol?.Name ?? "";
                    fam = fi.Symbol?.FamilyName ?? "";
                }
                string aliasConceptId = SymbolAliasRegistry.ResolveAlias(
                    el.Document, fam, typ, inst);
                if (!string.IsNullOrEmpty(aliasConceptId))
                {
                    var match = categoryConcepts.FirstOrDefault(c =>
                        string.Equals(c.ConceptId, aliasConceptId, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveConcept alias: {ex.Message}"); }

            // 3. Family / type / instance name keyword match.
            string haystack = BuildElementHaystack(el);
            if (!string.IsNullOrEmpty(haystack))
            {
                SymbolConcept best = null;
                int bestScore = 0;
                foreach (var c in categoryConcepts)
                {
                    int score = ScoreConceptAgainstHaystack(c, haystack);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                if (best != null && bestScore > 0) return best;
            }

            // 4. Fall back to first.
            return categoryConcepts[0];
        }

        private static string BuildElementHaystack(Element el)
        {
            try
            {
                string fam = "", typ = "", inst = el.Name ?? "";
                if (el is FamilyInstance fi)
                {
                    typ = fi.Symbol?.Name ?? "";
                    fam = fi.Symbol?.FamilyName ?? "";
                }
                return (fam + " " + typ + " " + inst).ToLowerInvariant();
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"BuildElementHaystack: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// Token-overlap score between concept name (e.g. "Ltg Pendant")
        /// and the element's family/type/instance haystack. Returns count
        /// of meaningful tokens (length ≥ 3) from the concept name that
        /// appear in the haystack.
        /// </summary>
        private static int ScoreConceptAgainstHaystack(SymbolConcept c, string haystack)
        {
            if (c == null || string.IsNullOrEmpty(haystack)) return 0;
            // Source tokens: concept ID and Name, split on _ - and space.
            var source = ((c.ConceptId ?? "") + " " + (c.Name ?? "")).ToLowerInvariant();
            var tokens = source.Split(new[] { '_', '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int score = 0;
            foreach (var t in tokens)
            {
                if (t.Length < 3) continue;
                // Skip common prefixes that match every concept in a discipline.
                if (t == "ltg" || t == "elec" || t == "hvac" || t == "plm" || t == "fp"
                    || t == "sld" || t == "pipe") continue;
                if (haystack.Contains(t)) score++;
            }
            return score;
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
