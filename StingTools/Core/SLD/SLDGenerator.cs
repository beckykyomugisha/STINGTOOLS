// StingTools — SLD generator façade (Phase 175)
//
// Coordinates traverser → layout → annotation placement to produce a
// drafting view that mirrors the project's electrical hierarchy.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;

namespace StingTools.Core.SLD
{
    public sealed class SLDResult
    {
        public bool Success { get; set; }
        public ViewDrafting SLDView { get; set; }
        public int SymbolsPlaced { get; set; }
        public int CircuitsShown { get; set; }
        public string Warning { get; set; }
    }

    public static class SLDGenerator
    {
        public static SLDResult GenerateSLD(Document doc, string standardId, string viewName = null)
        {
            var result = new SLDResult();
            if (doc == null) { result.Warning = "no document"; return result; }
            try
            {
                var root = SLDCircuitTraverser.BuildHierarchy(doc);
                if (root == null) { result.Warning = "no electrical hierarchy found"; return result; }

                var layout = SLDLayoutEngine.CalculateLayout(root, standardId);

                using (var tx = new Transaction(doc, "STING Generate SLD"))
                {
                    tx.Start();

                    var dvType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                    if (dvType == null) { result.Warning = "no drafting ViewFamilyType"; tx.RollBack(); return result; }

                    var view = ViewDrafting.Create(doc, dvType.Id);
                    view.Name = viewName ?? $"STING - SLD - {DateTime.Now:yyyyMMdd-HHmm}";
                    try { view.Scale = 50; } catch (Exception ex) { StingTools.Core.StingLog.Warn($"SLD set scale: {ex.Message}"); }

                    var nodeToInstance = new Dictionary<ElementId, ElementId>();
                    PlaceSymbols(doc, view, root, layout, standardId, result, nodeToInstance);
                    SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, view, layout);
                    SLDAnnotationPlacer.PlaceAllAnnotations(doc, view, root, layout, standardId, nodeToInstance);

                    StampViewStandard(view, standardId);
                    tx.Commit();

                    result.Success = true;
                    result.SLDView = view;
                }
            }
            catch (Exception ex)
            {
                result.Warning = ex.Message;
                StingTools.Core.StingLog.Error("SLDGenerator.GenerateSLD", ex);
            }
            return result;
        }

        /// <summary>
        /// Refresh an existing SLD view in response to a model change.
        /// <para>
        /// Two-tier update strategy:
        /// </para>
        /// <list type="bullet">
        /// <item><b>Fast path</b> — when <paramref name="changedElementId"/>
        /// matches one symbol on the view (via <c>STING_SLD_ELEMENT_ID</c>),
        /// only that symbol's label is re-rendered. O(1) work; suitable
        /// for tight IUpdater loops.</item>
        /// <item><b>Full rebuild</b> — when the changed element doesn't
        /// map to an existing symbol (added or deleted), or
        /// <paramref name="changedElementId"/> is invalid, the whole view
        /// content is regenerated.</item>
        /// </list>
        /// </summary>
        public static void UpdateSLD(Document doc, ViewDrafting sldView, ElementId changedElementId)
        {
            if (doc == null || sldView == null) return;
            try
            {
                string standard = sldView.LookupParameter("STING_VIEW_SYMBOL_STANDARD")?.AsString() ?? "IEC";

                // Fast path — try targeted update first.
                if (changedElementId != null
                    && changedElementId != ElementId.InvalidElementId
                    && doc.GetElement(changedElementId) != null
                    && TryUpdateSingleNode(doc, sldView, changedElementId, standard))
                {
                    return;
                }

                // Full rebuild fallback.
                FullRebuild(doc, sldView, standard);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("SLDGenerator.UpdateSLD", ex);
            }
        }

        private static bool TryUpdateSingleNode(Document doc, ViewDrafting sldView,
            ElementId changedElementId, string standard)
        {
            try
            {
                // Find the SLD symbol that maps to this model element.
                string targetIdStr = changedElementId.Value.ToString();
                var sldSymbol = new FilteredElementCollector(doc, sldView.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault(fi => string.Equals(
                        fi.LookupParameter("STING_SLD_ELEMENT_ID")?.AsString(),
                        targetIdStr,
                        StringComparison.Ordinal));
                if (sldSymbol == null) return false;

                // Re-read circuit data from the live model.
                var live = doc.GetElement(changedElementId) as FamilyInstance;
                if (live == null) return false;

                var node = new StingTools.Core.SLD.SLDNode
                {
                    ElementId = changedElementId,
                    RevitElement = live,
                    Label = live.Name,
                    ConceptId = sldSymbol.LookupParameter("STING_SYMBOL_ID")?.AsString(),
                };

                // Pull rating/circuit/poles/load from any electrical system
                // this element belongs to.
                try
                {
                    var systems = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Electrical.ElectricalSystem))
                        .Cast<Autodesk.Revit.DB.Electrical.ElectricalSystem>()
                        .Where(s =>
                        {
                            try
                            {
                                if (s.BaseEquipment?.Id == changedElementId) return true;
                                foreach (Element e in s.Elements)
                                    if (e.Id == changedElementId) return true;
                            }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                            return false;
                        }).ToList();
                    if (systems.Count > 0)
                        StingTools.Core.SLD.SLDCircuitTraverser.ReadCircuitData(systems[0], node);
                }
                catch (Exception ex)
                { StingTools.Core.StingLog.Warn($"TryUpdateSingleNode read circuit: {ex.Message}"); }

                // Locate the stamped TextNote ID. If present, refresh that
                // exact label. Otherwise fall back to a spatial scan.
                var rules = StingTools.Core.Symbols.SymbolStandardRegistry.GetAnnotationRules(standard);
                XYZ pos = (sldSymbol.Location as LocationPoint)?.Point ?? XYZ.Zero;
                ElementId stampedLabelId = ResolveStampedLabelId(sldSymbol);

                using (var tx = new Transaction(doc, "STING Refresh SLD Node"))
                {
                    tx.Start();
                    if (stampedLabelId != ElementId.InvalidElementId
                        && doc.GetElement(stampedLabelId) is TextNote)
                    {
                        try { doc.Delete(stampedLabelId); }
                        catch (Exception ex) { StingTools.Core.StingLog.Warn($"Refresh delete stamped: {ex.Message}"); }
                    }
                    else
                    {
                        // No stamped link — fall back to deleting any
                        // TextNote sitting next to the symbol.
                        DeleteAdjacentTextNotes(doc, sldView, pos);
                    }

                    var newLabelId = SLDAnnotationPlacer.PlaceCircuitAnnotation(
                        doc, sldView, node, pos, rules);
                    if (newLabelId != ElementId.InvalidElementId)
                    {
                        var p = sldSymbol.LookupParameter("STING_SYMBOL_LABEL_ID");
                        if (p != null && !p.IsReadOnly)
                            p.Set(newLabelId.Value.ToString());
                    }
                    tx.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"TryUpdateSingleNode: {ex.Message}");
                return false;
            }
        }

        private static ElementId ResolveStampedLabelId(FamilyInstance symbol)
        {
            try
            {
                var p = symbol?.LookupParameter("STING_SYMBOL_LABEL_ID");
                string s = p?.AsString();
                if (string.IsNullOrEmpty(s)) return ElementId.InvalidElementId;
                if (long.TryParse(s, out var raw))
                    return new ElementId(raw);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveStampedLabelId: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static void DeleteAdjacentTextNotes(Document doc, View view, XYZ near, double maxDistFt = 0.2)
        {
            try
            {
                var toDelete = new System.Collections.Generic.List<ElementId>();
                foreach (TextNote n in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>())
                {
                    try
                    {
                        if (n.Coord != null && n.Coord.DistanceTo(near) <= maxDistFt)
                            toDelete.Add(n.Id);
                    }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"DeleteAdjacentTextNotes inner: {ex.Message}"); }
                }
                foreach (var id in toDelete)
                {
                    try { doc.Delete(id); } catch (Exception ex) { StingTools.Core.StingLog.Warn($"DeleteAdjacentTextNotes del: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DeleteAdjacentTextNotes: {ex.Message}"); }
        }

        private static void FullRebuild(Document doc, ViewDrafting sldView, string standard)
        {
            using (var tx = new Transaction(doc, "STING Rebuild SLD"))
            {
                tx.Start();
                var ids = new FilteredElementCollector(doc, sldView.Id).ToElementIds();
                foreach (var id in ids)
                {
                    try { doc.Delete(id); } catch (Exception ex) { StingTools.Core.StingLog.Warn($"Rebuild del {id}: {ex.Message}"); }
                }
                tx.Commit();
            }

            var root = SLDCircuitTraverser.BuildHierarchy(doc);
            if (root == null) return;
            var layout = SLDLayoutEngine.CalculateLayout(root, standard);

            using (var tx = new Transaction(doc, "STING Rebuild SLD content"))
            {
                tx.Start();
                var stub = new SLDResult();
                var nodeToInstance = new Dictionary<ElementId, ElementId>();
                PlaceSymbols(doc, sldView, root, layout, standard, stub, nodeToInstance);
                SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, sldView, layout);
                SLDAnnotationPlacer.PlaceAllAnnotations(doc, sldView, root, layout, standard, nodeToInstance);
                tx.Commit();
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static void PlaceSymbols(Document doc, ViewDrafting view, SLDNode node,
            SLDLayout layout, string standard, SLDResult result,
            IDictionary<ElementId, ElementId> nodeToInstance = null)
        {
            if (node == null) return;
            try
            {
                if (layout.SymbolPositions.TryGetValue(node.ElementId, out var pos)
                    && !string.IsNullOrEmpty(node.ConceptId))
                {
                    var fam = SymbolConceptRegistry.GetAnnotationFamilyName(node.ConceptId, standard);
                    if (!string.IsNullOrEmpty(fam))
                    {
                        var sym = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(s => string.Equals(s.Name, fam, StringComparison.OrdinalIgnoreCase));
                        if (sym != null)
                        {
                            if (!sym.IsActive) sym.Activate();
                            try
                            {
                                var inst = doc.Create.NewFamilyInstance(pos, sym, view);
                                if (inst != null)
                                {
                                    StampParam(inst, "STING_SYMBOL_ID", node.ConceptId);
                                    StampParam(inst, "STING_SLD_ELEMENT_ID", node.ElementId.Value.ToString());
                                    result.SymbolsPlaced++;
                                    if (nodeToInstance != null)
                                        nodeToInstance[node.ElementId] = inst.Id;
                                }
                            }
                            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceSymbols inst: {ex.Message}"); }
                        }
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceSymbols: {ex.Message}"); }

            foreach (var c in node.Children) PlaceSymbols(doc, view, c, layout, standard, result, nodeToInstance);
        }

        private static void StampViewStandard(View view, string standard)
        {
            try
            {
                var p = view.LookupParameter("STING_VIEW_SYMBOL_STANDARD");
                if (p != null && !p.IsReadOnly) p.Set(standard ?? "");
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"StampViewStandard: {ex.Message}"); }
        }

        private static void StampParam(Element el, string name, string value)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p != null && !p.IsReadOnly) p.Set(value ?? "");
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"StampParam {name}: {ex.Message}"); }
        }
    }
}
