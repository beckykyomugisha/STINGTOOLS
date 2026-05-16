// StingTools — SLD generator façade (Phase 175 + Phase 179 enhancements)
//
// Coordinates traverser → layout → annotation to produce a drafting view
// that mirrors the project's electrical hierarchy.
//
// Phase 179 changes:
//  - GenerateSLD accepts optional SLDLayoutOptions + SLDAnnotationOptions.
//  - Multi-root: all independent distribution hierarchies are rendered
//    side-by-side in the same view, separated by a configurable gap.
//  - DrawingType stamp written on every generated view so Browser Organizer
//    and drift detection can classify it.
//  - UpdateSLD / FullRebuild propagate options through.

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
        private const string DrawingTypeId = "elec-sld-A1-1to100";

        public static SLDResult GenerateSLD(Document doc, string standardId,
            string viewName = null,
            SLDLayoutOptions layoutOpts = null,
            SLDAnnotationOptions annotOpts = null)
        {
            layoutOpts = layoutOpts ?? SLDLayoutOptions.Default;
            annotOpts  = annotOpts  ?? SLDAnnotationOptions.Default;

            var result = new SLDResult();
            if (doc == null) { result.Warning = "no document"; return result; }
            try
            {
                var roots = SLDCircuitTraverser.BuildHierarchyAll(doc);
                if (roots == null || roots.Count == 0)
                { result.Warning = "no electrical hierarchy found"; return result; }

                using (var tx = new Transaction(doc, "STING Generate SLD"))
                {
                    tx.Start();

                    var dvType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                    if (dvType == null)
                    { result.Warning = "no drafting ViewFamilyType"; tx.RollBack(); return result; }

                    var view = ViewDrafting.Create(doc, dvType.Id);
                    view.Name = viewName ?? $"STING - SLD - {DateTime.Now:yyyyMMdd-HHmm}";
                    try { view.Scale = 50; } catch (Exception ex) { StingLog.Warn($"SLD set scale: {ex.Message}"); }

                    var nodeToInstance = new Dictionary<ElementId, ElementId>();
                    double xOffset = 0;

                    foreach (var root in roots)
                    {
                        var baseLayout = SLDLayoutEngine.CalculateLayout(root, standardId, layoutOpts);
                        var layout = xOffset == 0 ? baseLayout : baseLayout.Offset(xOffset, 0);

                        PlaceSymbols(doc, view, root, layout, standardId, result, nodeToInstance);
                        SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, view, layout);
                        SLDAnnotationPlacer.PlaceAllAnnotations(doc, view, root, layout,
                            standardId, nodeToInstance, annotOpts);

                        if (layout.TotalWidth > 0)
                            xOffset += layout.TotalWidth + layoutOpts.RootGapMm / 304.8;
                    }

                    StampViewStandard(view, standardId);
                    StampDrawingType(view);
                    tx.Commit();

                    result.Success = true;
                    result.SLDView = view;
                }
            }
            catch (Exception ex)
            {
                result.Warning = ex.Message;
                StingLog.Error("SLDGenerator.GenerateSLD", ex);
            }
            return result;
        }

        /// <summary>
        /// Two-tier update strategy:
        /// - Fast path when <paramref name="changedElementId"/> maps to an existing
        ///   symbol via <c>STING_SLD_ELEMENT_ID</c>: only that label is refreshed (O(1)).
        /// - Full rebuild fallback for structural changes (add / delete).
        /// </summary>
        public static void UpdateSLD(Document doc, ViewDrafting sldView,
            ElementId changedElementId,
            SLDLayoutOptions layoutOpts = null,
            SLDAnnotationOptions annotOpts = null)
        {
            if (doc == null || sldView == null) return;
            try
            {
                string standard = sldView.LookupParameter("STING_VIEW_SYMBOL_STANDARD")?.AsString() ?? "IEC";

                if (changedElementId != null
                    && changedElementId != ElementId.InvalidElementId
                    && doc.GetElement(changedElementId) != null
                    && TryUpdateSingleNode(doc, sldView, changedElementId, standard,
                        annotOpts ?? SLDAnnotationOptions.Default))
                {
                    return;
                }

                FullRebuild(doc, sldView, standard,
                    layoutOpts ?? SLDLayoutOptions.Default,
                    annotOpts  ?? SLDAnnotationOptions.Default);
            }
            catch (Exception ex)
            {
                StingLog.Error("SLDGenerator.UpdateSLD", ex);
            }
        }

        private static bool TryUpdateSingleNode(Document doc, ViewDrafting sldView,
            ElementId changedElementId, string standard, SLDAnnotationOptions annotOpts)
        {
            try
            {
                string targetIdStr = changedElementId.Value.ToString();
                var sldSymbol = new FilteredElementCollector(doc, sldView.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault(fi => string.Equals(
                        fi.LookupParameter("STING_SLD_ELEMENT_ID")?.AsString(),
                        targetIdStr, StringComparison.Ordinal));
                if (sldSymbol == null) return false;

                var live = doc.GetElement(changedElementId) as FamilyInstance;
                if (live == null) return false;

                var node = new SLDNode
                {
                    ElementId    = changedElementId,
                    RevitElement = live,
                    Label        = live.Name,
                    ConceptId    = sldSymbol.LookupParameter("STING_SYMBOL_ID")?.AsString(),
                };

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
                            catch { }
                            return false;
                        }).ToList();
                    if (systems.Count > 0)
                        SLDCircuitTraverser.ReadCircuitData(systems[0], node);
                }
                catch (Exception ex) { StingLog.Warn($"TryUpdateSingleNode read circuit: {ex.Message}"); }

                var rules = SymbolStandardRegistry.GetAnnotationRules(standard);
                XYZ pos = (sldSymbol.Location as LocationPoint)?.Point ?? XYZ.Zero;
                ElementId stampedLabelId = ResolveStampedLabelId(sldSymbol);

                using (var tx = new Transaction(doc, "STING Refresh SLD Node"))
                {
                    tx.Start();
                    if (stampedLabelId != ElementId.InvalidElementId
                        && doc.GetElement(stampedLabelId) is TextNote)
                    {
                        try { doc.Delete(stampedLabelId); }
                        catch (Exception ex) { StingLog.Warn($"Refresh delete stamped: {ex.Message}"); }
                    }
                    else
                    {
                        DeleteAdjacentTextNotes(doc, sldView, pos);
                    }

                    var newLabelId = SLDAnnotationPlacer.PlaceCircuitAnnotation(
                        doc, sldView, node, pos, rules, annotOpts);
                    if (newLabelId != ElementId.InvalidElementId)
                    {
                        var p = sldSymbol.LookupParameter("STING_SYMBOL_LABEL_ID");
                        if (p != null && !p.IsReadOnly) p.Set(newLabelId.Value.ToString());
                    }
                    tx.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryUpdateSingleNode: {ex.Message}");
                return false;
            }
        }

        private static void FullRebuild(Document doc, ViewDrafting sldView, string standard,
            SLDLayoutOptions layoutOpts, SLDAnnotationOptions annotOpts)
        {
            using (var tx = new Transaction(doc, "STING Rebuild SLD"))
            {
                tx.Start();
                var ids = new FilteredElementCollector(doc, sldView.Id).ToElementIds();
                foreach (var id in ids)
                {
                    try { doc.Delete(id); }
                    catch (Exception ex) { StingLog.Warn($"Rebuild del {id}: {ex.Message}"); }
                }
                tx.Commit();
            }

            var roots = SLDCircuitTraverser.BuildHierarchyAll(doc);
            if (roots == null || roots.Count == 0) return;

            using (var tx = new Transaction(doc, "STING Rebuild SLD content"))
            {
                tx.Start();
                var stub = new SLDResult();
                var nodeToInstance = new Dictionary<ElementId, ElementId>();
                double xOffset = 0;

                foreach (var root in roots)
                {
                    var baseLayout = SLDLayoutEngine.CalculateLayout(root, standard, layoutOpts);
                    var layout = xOffset == 0 ? baseLayout : baseLayout.Offset(xOffset, 0);

                    PlaceSymbols(doc, sldView, root, layout, standard, stub, nodeToInstance);
                    SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, sldView, layout);
                    SLDAnnotationPlacer.PlaceAllAnnotations(doc, sldView, root, layout,
                        standard, nodeToInstance, annotOpts);

                    if (layout.TotalWidth > 0)
                        xOffset += layout.TotalWidth + layoutOpts.RootGapMm / 304.8;
                }
                tx.Commit();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

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
                            .FirstOrDefault(s => string.Equals(s.Name, fam,
                                StringComparison.OrdinalIgnoreCase));
                        if (sym != null)
                        {
                            if (!sym.IsActive) sym.Activate();
                            try
                            {
                                var inst = doc.Create.NewFamilyInstance(pos, sym, view);
                                if (inst != null)
                                {
                                    StampParam(inst, "STING_SYMBOL_ID", node.ConceptId);
                                    StampParam(inst, "STING_SLD_ELEMENT_ID",
                                        node.ElementId.Value.ToString());
                                    result.SymbolsPlaced++;
                                    nodeToInstance?[node.ElementId] = inst.Id;
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"PlaceSymbols inst: {ex.Message}"); }
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlaceSymbols: {ex.Message}"); }

            foreach (var c in node.Children)
                PlaceSymbols(doc, view, c, layout, standard, result, nodeToInstance);
        }

        private static ElementId ResolveStampedLabelId(FamilyInstance symbol)
        {
            try
            {
                var p = symbol?.LookupParameter("STING_SYMBOL_LABEL_ID");
                string s = p?.AsString();
                if (string.IsNullOrEmpty(s)) return ElementId.InvalidElementId;
                if (long.TryParse(s, out var raw)) return new ElementId(raw);
            }
            catch (Exception ex) { StingLog.Warn($"ResolveStampedLabelId: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static void DeleteAdjacentTextNotes(Document doc, View view, XYZ near,
            double maxDistFt = 0.2)
        {
            try
            {
                var toDelete = new List<ElementId>();
                foreach (TextNote n in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote)).Cast<TextNote>())
                {
                    try
                    {
                        if (n.Coord != null && n.Coord.DistanceTo(near) <= maxDistFt)
                            toDelete.Add(n.Id);
                    }
                    catch (Exception ex) { StingLog.Warn($"DeleteAdjacentTextNotes inner: {ex.Message}"); }
                }
                foreach (var id in toDelete)
                {
                    try { doc.Delete(id); }
                    catch (Exception ex) { StingLog.Warn($"DeleteAdjacentTextNotes del: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeleteAdjacentTextNotes: {ex.Message}"); }
        }

        private static void StampViewStandard(View view, string standard)
        {
            try
            {
                var p = view.LookupParameter("STING_VIEW_SYMBOL_STANDARD");
                if (p != null && !p.IsReadOnly) p.Set(standard ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"StampViewStandard: {ex.Message}"); }
        }

        private static void StampDrawingType(ViewDrafting view)
        {
            try
            {
                var t = Type.GetType("StingTools.Core.Drawing.DrawingTypeStamper");
                t?.GetMethod("Stamp", new[] { typeof(Element), typeof(string) })
                  ?.Invoke(null, new object[] { view, DrawingTypeId });
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }

        private static void StampParam(Element el, string name, string value)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p != null && !p.IsReadOnly) p.Set(value ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"StampParam {name}: {ex.Message}"); }
        }
    }
}
