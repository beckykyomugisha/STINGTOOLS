// StingTools — SLD generator façade (Phase 175)
//
// Coordinates traverser → layout → annotation placement to produce a
// drafting view that mirrors the project's electrical hierarchy.

using System;
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

                    PlaceSymbols(doc, view, root, layout, standardId, result);
                    SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, view, layout);
                    SLDAnnotationPlacer.PlaceAllAnnotations(doc, view, root, layout, standardId);

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

        public static void UpdateSLD(Document doc, ViewDrafting sldView, ElementId changedElementId)
        {
            // Lightweight update — re-runs the full generator, deleting prior
            // contents on the same view first.
            if (doc == null || sldView == null) return;
            try
            {
                using (var tx = new Transaction(doc, "STING Update SLD"))
                {
                    tx.Start();
                    var ids = new FilteredElementCollector(doc, sldView.Id).ToElementIds();
                    foreach (var id in ids)
                    {
                        try { doc.Delete(id); } catch (Exception ex) { StingTools.Core.StingLog.Warn($"UpdateSLD del {id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }
                // Now regenerate symbols/annotations onto the same view.
                var standard = sldView.LookupParameter("STING_VIEW_SYMBOL_STANDARD")?.AsString() ?? "IEC";
                var root = SLDCircuitTraverser.BuildHierarchy(doc);
                if (root == null) return;
                var layout = SLDLayoutEngine.CalculateLayout(root, standard);
                using (var tx = new Transaction(doc, "STING Update SLD content"))
                {
                    tx.Start();
                    var stub = new SLDResult();
                    PlaceSymbols(doc, sldView, root, layout, standard, stub);
                    SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, sldView, layout);
                    SLDAnnotationPlacer.PlaceAllAnnotations(doc, sldView, root, layout, standard);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("SLDGenerator.UpdateSLD", ex);
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static void PlaceSymbols(Document doc, ViewDrafting view, SLDNode node,
            SLDLayout layout, string standard, SLDResult result)
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
                                    StampParam(inst, "STING_SLD_ELEMENT_ID", node.ElementId.IntegerValue.ToString());
                                    result.SymbolsPlaced++;
                                }
                            }
                            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceSymbols inst: {ex.Message}"); }
                        }
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceSymbols: {ex.Message}"); }

            foreach (var c in node.Children) PlaceSymbols(doc, view, c, layout, standard, result);
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
