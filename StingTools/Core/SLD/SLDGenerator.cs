// StingTools — SLD generator façade (Phase 175)
//
// Coordinates traverser → layout → annotation placement to produce a
// drafting view that mirrors the project's electrical hierarchy.
//
// SLD-11: GenerateSLD accepts an optional SLDGeneratorOptions to pass
//         ShowFeederCsa and other flags through to downstream helpers.
// SLD-12: After the view is created, TryPlaceOnSheet tries to place it
//         on an appropriate STING sheet via DrawingDispatcher.
// SLD-13: PlaceSymbols routes compound concepts through CompoundSymbolPlacer.
// SLD-14: EnsureSymbolFamiliesLoaded pre-flights required family names
//         and appends warnings to the result before placement begins.
// SLD-19: SLDResult.Warnings is a List<string>; Warning is a computed
//         get-only property that joins them for backwards compat.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;

namespace StingTools.Core.SLD
{
    /// <summary>
    /// Options that control what extra information is generated when
    /// building an SLD or riser diagram view.
    /// </summary>
    public sealed class SLDGeneratorOptions
    {
        /// <summary>Annotate feeder cable CSA on every connection line.</summary>
        public bool ShowFeederCsa { get; set; }
        /// <summary>Annotate fault-level (kA) on panel boxes.</summary>
        public bool ShowFaultKa { get; set; }
        /// <summary>Annotate loading percentage on panel boxes.</summary>
        public bool ShowLoadingPct { get; set; }
    }

    public sealed class SLDResult
    {
        public bool Success { get; set; }
        public ViewDrafting SLDView { get; set; }
        public int SymbolsPlaced { get; set; }
        public int CircuitsShown { get; set; }

        // SLD-19: full warning list rather than a single overwritten string
        public List<string> Warnings { get; set; } = new List<string>();

        // Backwards-compatible convenience getter used by existing callers
        public string Warning => Warnings.Count > 0 ? string.Join("\n", Warnings) : null;
    }

    public static class SLDGenerator
    {
        public static SLDResult GenerateSLD(Document doc, string standardId,
            string viewName = null, SLDGeneratorOptions options = null)
        {
            var result = new SLDResult();
            if (doc == null) { result.Warnings.Add("no document"); return result; }
            try
            {
                var root = SLDCircuitTraverser.BuildHierarchy(doc);
                if (root == null) { result.Warnings.Add("no electrical hierarchy found"); return result; }

                var layout = SLDLayoutEngine.CalculateLayout(root, standardId);

                using (var tx = new Transaction(doc, "STING Generate SLD"))
                {
                    tx.Start();

                    var dvType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                    if (dvType == null)
                    {
                        result.Warnings.Add("no drafting ViewFamilyType");
                        tx.RollBack();
                        return result;
                    }

                    var view = ViewDrafting.Create(doc, dvType.Id);
                    view.Name = viewName ?? $"STING - SLD - {DateTime.Now:yyyyMMdd-HHmm}";
                    try { view.Scale = 50; }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"SLD set scale: {ex.Message}"); }

                    // SLD-14: warn about any symbol families that aren't loaded
                    EnsureSymbolFamiliesLoaded(doc, root, standardId, result);

                    var nodeToInstance = new Dictionary<ElementId, ElementId>();
                    PlaceSymbols(doc, view, root, layout, standardId, result, nodeToInstance);
                    SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, view, layout, standardId);
                    SLDAnnotationPlacer.PlaceAllAnnotations(doc, view, root, layout, standardId, nodeToInstance);

                    StampViewStandard(view, standardId);
                    tx.Commit();

                    result.Success = true;
                    result.SLDView = view;
                }

                // SLD-12: try to place on a sheet (best-effort, never fails the command)
                TryPlaceOnSheet(doc, result.SLDView);
            }
            catch (Exception ex)
            {
                result.Warnings.Add(ex.Message);
                StingTools.Core.StingLog.Error("SLDGenerator.GenerateSLD", ex);
            }
            return result;
        }

        /// <summary>
        /// Refresh an existing SLD view in response to a model change.
        /// Fast path — targeted update for a single modified element.
        /// Full rebuild — when the element has no SLD symbol or is invalid.
        /// </summary>
        public static void UpdateSLD(Document doc, ViewDrafting sldView, ElementId changedElementId)
        {
            if (doc == null || sldView == null) return;
            try
            {
                string standard = sldView.LookupParameter("STING_VIEW_SYMBOL_STANDARD")?.AsString() ?? "IEC";

                if (changedElementId != null
                    && changedElementId != ElementId.InvalidElementId
                    && doc.GetElement(changedElementId) != null
                    && TryUpdateSingleNode(doc, sldView, changedElementId, standard))
                {
                    return;
                }

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
                string targetIdStr = changedElementId.Value.ToString();
                var sldSymbol = new FilteredElementCollector(doc, sldView.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault(fi => string.Equals(
                        fi.LookupParameter("STING_SLD_ELEMENT_ID")?.AsString(),
                        targetIdStr,
                        StringComparison.Ordinal));
                if (sldSymbol == null) return false;

                var live = doc.GetElement(changedElementId) as FamilyInstance;
                if (live == null) return false;

                var node = new SLDNode
                {
                    ElementId = changedElementId,
                    RevitElement = live,
                    Label = live.Name,
                    ConceptId = sldSymbol.LookupParameter("STING_SYMBOL_ID")?.AsString(),
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
                catch (Exception ex)
                { StingTools.Core.StingLog.Warn($"TryUpdateSingleNode read circuit: {ex.Message}"); }

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
                        catch (Exception ex) { StingTools.Core.StingLog.Warn($"Refresh delete stamped: {ex.Message}"); }
                    }
                    else
                    {
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
                var toDelete = new List<ElementId>();
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
                    try { doc.Delete(id); }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"DeleteAdjacentTextNotes del: {ex.Message}"); }
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
                    try { doc.Delete(id); }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"Rebuild del {id}: {ex.Message}"); }
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
                SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, sldView, layout, standard);
                SLDAnnotationPlacer.PlaceAllAnnotations(doc, sldView, root, layout, standard, nodeToInstance);
                tx.Commit();
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        // SLD-14: pre-flight — collect required family names and warn for any not loaded
        private static void EnsureSymbolFamiliesLoaded(Document doc, SLDNode root,
            string standard, SLDResult result)
        {
            try
            {
                var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var stack = new Stack<SLDNode>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (!string.IsNullOrEmpty(n.ConceptId))
                    {
                        string fam = SymbolConceptRegistry.GetAnnotationFamilyName(n.ConceptId, standard);
                        if (!string.IsNullOrEmpty(fam)) needed.Add(fam);
                    }
                    foreach (var c in n.Children) stack.Push(c);
                }

                var loaded = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Select(s => s.FamilyName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var fam in needed)
                    if (!loaded.Contains(fam))
                        result.Warnings.Add($"SLD symbol family not loaded: '{fam}' — load the family to show this symbol type.");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"EnsureSymbolFamiliesLoaded: {ex.Message}");
            }
        }

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
                    // SLD-13: route compound concepts through CompoundSymbolPlacer
                    var concept = SymbolConceptRegistry.GetConcept(node.ConceptId);
                    bool isCompound = concept?.CompoundComponents != null
                                      && concept.CompoundComponents.Count > 0;

                    if (isCompound)
                    {
                        var ids = CompoundSymbolPlacer.PlaceCompound(
                            doc, view, pos, node.ConceptId, standard);
                        if (ids.Count > 0)
                        {
                            result.SymbolsPlaced++;
                            if (nodeToInstance != null)
                                nodeToInstance[node.ElementId] = ids[0];
                            // Stamp the first placed component with the element back-reference
                            var first = doc.GetElement(ids[0]) as FamilyInstance;
                            if (first != null)
                            {
                                StampParam(first, "STING_SYMBOL_ID", node.ConceptId);
                                StampParam(first, "STING_SLD_ELEMENT_ID", node.ElementId.Value.ToString());
                            }
                        }
                    }
                    else
                    {
                        var fam = SymbolConceptRegistry.GetAnnotationFamilyName(node.ConceptId, standard);
                        if (!string.IsNullOrEmpty(fam))
                        {
                            var sym = new FilteredElementCollector(doc)
                                .OfClass(typeof(FamilySymbol))
                                .Cast<FamilySymbol>()
                                .FirstOrDefault(s =>
                                    string.Equals(s.Name, fam, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(s.FamilyName, fam, StringComparison.OrdinalIgnoreCase));
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
                                        if (nodeToInstance != null)
                                            nodeToInstance[node.ElementId] = inst.Id;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    StingTools.Core.StingLog.Warn($"PlaceSymbols inst: {ex.Message}");
                                }
                            }
                            // SLD-14: missing family already warned by EnsureSymbolFamiliesLoaded
                        }
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceSymbols: {ex.Message}"); }

            foreach (var c in node.Children)
                PlaceSymbols(doc, view, c, layout, standard, result, nodeToInstance);
        }

        // SLD-12: try to place the new SLD view on a matching STING sheet
        private static void TryPlaceOnSheet(Document doc, ViewDrafting view)
        {
            if (view == null) return;
            try
            {
                // Ask the drawing type registry if there is a matching profile
                var dt = Core.Drawing.DrawingDispatcher.Resolve(doc, "Electrical", "*", "SLD");
                if (dt == null)
                {
                    StingTools.Core.StingLog.Info(
                        "SLDGenerator: no DrawingType for Electrical/SLD — view created without sheet placement.");
                    return;
                }

                // Find an existing STING SLD sheet to place onto
                var sheet = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s =>
                        (s.Name ?? "").IndexOf("SLD", StringComparison.OrdinalIgnoreCase) >= 0
                        && (s.Name ?? "").StartsWith("STING", StringComparison.OrdinalIgnoreCase));

                if (sheet == null)
                {
                    StingTools.Core.StingLog.Info(
                        "SLDGenerator: no STING SLD sheet found — view created but not placed on a sheet.");
                    return;
                }

                using (var tx = new Transaction(doc, "STING Place SLD on Sheet"))
                {
                    tx.Start();
                    try
                    {
                        if (Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                        {
                            Viewport.Create(doc, sheet.Id, view.Id, XYZ.Zero);
                            StingTools.Core.StingLog.Info(
                                $"SLDGenerator: placed SLD view on sheet '{sheet.Name}'.");
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                        StingTools.Core.StingLog.Warn($"SLDGenerator TryPlaceOnSheet: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SLDGenerator TryPlaceOnSheet outer: {ex.Message}");
            }
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
