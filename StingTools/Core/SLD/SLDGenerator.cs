using StingTools.Core;
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
//
// Phase 179 S1–S5 additions:
//  S1 - PlaceSymbols stamps STING_VOLTAGE_TIER on each FamilyInstance.
//       PlaceBusbarsAndBranches now stamps STING_BUS_VOLTAGE_TIER on the view.
//       Line-weight variance by tier is deferred to ViewStylePack (see comment).
//  S2 - PlaceSymbols stamps STING_FEED_TYPE on emergency/dual-source nodes.
//       DrawEmergencyFeedNotation() draws dashed notation for dual-source links.
//  S3 - DrawBusCouplerNotation() draws BC/NOP markers between adjacent roots.
//  S5 - StampDiscriminationBadges() called after all annotations are placed.

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;
using StingTools.Commands.Electrical.Coordination;
using StingTools.Core.Drawing;

namespace StingTools.Core.SLD
{
    public sealed class SLDResult
    {
        public bool Success { get; set; }
        public ViewDrafting SLDView { get; set; }
        public int SymbolsPlaced { get; set; }
        public int CircuitsShown { get; set; }
        public string Warning { get; set; }
        /// <summary>Accumulated non-fatal warnings from symbol placement / annotation.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
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

                    // Collect per-root layout for S3 bus-coupler notation.
                    var rootLayouts = new List<(SLDNode root, SLDLayout layout)>();

                    foreach (var root in roots)
                    {
                        var baseLayout = SLDLayoutEngine.CalculateLayout(root, standardId, layoutOpts);
                        var layout = xOffset == 0 ? baseLayout : baseLayout.Offset(xOffset, 0);

                        PlaceSymbols(doc, view, root, layout, standardId, result, nodeToInstance);
                        SLDAnnotationPlacer.PlaceBusbarsAndBranches(doc, view, layout);
                        SLDAnnotationPlacer.PlaceAllAnnotations(doc, view, root, layout,
                            standardId, nodeToInstance, annotOpts);

                        // S2 — Emergency feed notation for dual-source nodes.
                        DrawEmergencyFeedNotation(doc, view, root, layout, nodeToInstance,
                            layoutOpts.SymbolHeightMm / 304.8, layoutOpts.LevelOffsetMm / 304.8);

                        rootLayouts.Add((root, layout));

                        if (layout.TotalWidth > 0)
                            xOffset += layout.TotalWidth + layoutOpts.RootGapMm / 304.8;
                    }

                    // S1 — Stamp dominant voltage tier on the view for busbar line-weight
                    // resolution by ViewStylePack (actual DetailCurve line-weight variation
                    // requires a GraphicsStyle lookup which is deferred to ViewStylePack binding;
                    // the view parameter gives the pack enough context to apply the right style).
                    StampBusTierOnView(doc, view, roots);

                    // S3 — Bus coupler / tie-breaker notation between adjacent roots.
                    DrawBusCouplerNotation(doc, view, rootLayouts,
                        layoutOpts.SymbolHeightMm / 304.8, layoutOpts.LevelOffsetMm / 304.8);

                    StampViewStandard(view, standardId);
                    StampDrawingType(view);
                    tx.Commit();

                    result.Success = true;
                    result.SLDView = view;
                }

                // S5 — Discrimination badges: called AFTER the main transaction commits so
                // StampDiscriminationBadges can open its own transaction safely.
                // Passes empty violations list here — callers that run SelectiveCoordEngine
                // should call StampDiscriminationBadges directly with real violations.
                SLDAnnotationPlacer.StampDiscriminationBadges(doc, result.SLDView,
                    roots.FirstOrDefault(),
                    Enumerable.Empty<CoordViolation>(),
                    new Dictionary<ElementId, ElementId>());
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
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
                        catch (Exception ex2) { StingLog.Warn($"Refresh delete stamped: {ex2.Message}"); }
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
                        var sym = FindOrLoadFamilySymbol(doc, fam);
                        if (sym != null)
                        {
                            if (!sym.IsActive) sym.Activate();
                            try
                            {
                                var inst = doc.Create.NewFamilyInstance(pos, sym, view);
                                if (inst != null)
                                {
                                    StampParam(inst, "STING_SYMBOL_ID", node.ConceptId,
                                        result.Warnings);
                                    StampParam(inst, "STING_SLD_ELEMENT_ID",
                                        node.ElementId.Value.ToString(), result.Warnings);
                                    // S1 — Voltage tier stamp.
                                    StampParam(inst, "STING_VOLTAGE_TIER",
                                        node.VoltageTier ?? "LV", result.Warnings);
                                    // S2 — Feed type stamp (Emergency / Both nodes only).
                                    if (!string.IsNullOrEmpty(node.FeedType)
                                        && !string.Equals(node.FeedType, "Normal",
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        StampParam(inst, "STING_FEED_TYPE", node.FeedType,
                                            result.Warnings);
                                    }
                                    result.SymbolsPlaced++;
                                    nodeToInstance?[node.ElementId] = inst.Id;
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"PlaceSymbols inst: {ex.Message}"); }
                        }
                        else
                        {
                            StingLog.Warn($"PlaceSymbols: family '{fam}' not found for concept " +
                                $"'{node.ConceptId}' (standard '{standard}'). " +
                                "Run Seeds_Build to create and load SLD symbol families.");
                            result.Warnings.Add($"Symbol family '{fam}' not loaded — " +
                                "run Seeds_Build workflow step.");
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlaceSymbols: {ex.Message}"); }

            foreach (var c in node.Children)
                PlaceSymbols(doc, view, c, layout, standard, result, nodeToInstance);
        }

        /// <summary>
        /// Finds a FamilySymbol by name in the document. When not found, attempts
        /// to load the matching .rfa from the project's _BIM_COORD/symbols/ folder
        /// (created by BuildSeedFamiliesCommand). Returns null when unavailable.
        /// </summary>
        private static FamilySymbol FindOrLoadFamilySymbol(Document doc, string symbolName)
        {
            // Fast path — already loaded.
            var sym = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.Name, symbolName,
                    StringComparison.OrdinalIgnoreCase));
            if (sym != null) return sym;

            // Fallback — try to load from _BIM_COORD/symbols/ alongside the .rvt.
            try
            {
                if (string.IsNullOrEmpty(doc.PathName)) return null;
                string symbolsDir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(doc.PathName),
                    "_BIM_COORD", "symbols");
                if (!System.IO.Directory.Exists(symbolsDir)) return null;

                // Try exact match first, then case-insensitive.
                string rfaPath = System.IO.Path.Combine(symbolsDir, symbolName + ".rfa");
                if (!System.IO.File.Exists(rfaPath))
                {
                    rfaPath = System.IO.Directory.EnumerateFiles(symbolsDir, "*.rfa")
                        .FirstOrDefault(f => string.Equals(
                            System.IO.Path.GetFileNameWithoutExtension(f),
                            symbolName, StringComparison.OrdinalIgnoreCase));
                }
                if (rfaPath == null || !System.IO.File.Exists(rfaPath)) return null;

                if (doc.LoadFamily(rfaPath, out Family loaded))
                {
                    StingLog.Info($"PlaceSymbols: auto-loaded '{rfaPath}'");
                    return loaded?.GetFamilySymbolIds()
                        .Select(id => doc.GetElement(id) as FamilySymbol)
                        .FirstOrDefault(s => s != null &&
                            string.Equals(s.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                        ?? loaded?.GetFamilySymbolIds()
                            .Select(id => doc.GetElement(id) as FamilySymbol)
                            .FirstOrDefault(s => s != null);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FindOrLoadFamilySymbol '{symbolName}': {ex.Message}");
            }
            return null;
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
            StampParam(el, name, value, null);
        }

        /// <summary>
        /// Stamps a string parameter on <paramref name="el"/>.
        /// When the parameter is missing, read-only, or throws, a warning is appended
        /// to <paramref name="warnings"/> (if non-null) and to the log. Never throws.
        /// </summary>
        private static void StampParam(Element el, string name, string value,
            List<string> warnings)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null)
                {
                    string msg = $"STING stamp failed: {name} on element {el?.Id} — parameter not found";
                    StingLog.Warn(msg);
                    warnings?.Add(msg);
                    return;
                }
                if (p.IsReadOnly)
                {
                    string msg = $"STING stamp failed: {name} on element {el?.Id} — parameter is read-only";
                    StingLog.Warn(msg);
                    warnings?.Add(msg);
                    return;
                }
                p.Set(value ?? "");
            }
            catch (Exception ex)
            {
                string msg = $"STING stamp failed: {name} on element {el?.Id} — {ex.Message}";
                StingLog.Warn(msg);
                warnings?.Add(msg);
            }
        }

        // ── S1 — Stamp dominant voltage tier on the view ─────────────────────

        /// <summary>
        /// S1 — Writes <c>STING_BUS_VOLTAGE_TIER</c> on the view as a view-scoped
        /// parameter. The dominant tier is the highest tier found across all root
        /// nodes (HV > MV > LV). ViewStylePack binding uses this to select the
        /// correct busbar line-weight style; actual DetailCurve line-weight variation
        /// requires a GraphicsStyle lookup which is deferred to the pack.
        /// </summary>
        private static void StampBusTierOnView(Document doc, ViewDrafting view, List<SLDNode> roots)
        {
            try
            {
                if (roots == null || roots.Count == 0) return;

                // Walk all root nodes to find dominant tier.
                string dominantTier = "LV";
                var stack = new Stack<SLDNode>();
                foreach (var r in roots) stack.Push(r);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    string t = n.VoltageTier ?? "LV";
                    if (t == "HV") { dominantTier = "HV"; break; }
                    if (t == "MV" && dominantTier != "HV") dominantTier = "MV";
                    foreach (var c in n.Children) stack.Push(c);
                }

                var p = view.LookupParameter("STING_BUS_VOLTAGE_TIER");
                if (p != null && !p.IsReadOnly) p.Set(dominantTier);
            }
            catch (Exception ex) { StingLog.Warn($"StampBusTierOnView: {ex.Message}"); }
        }

        // ── S2 — Emergency feed notation ─────────────────────────────────────

        /// <summary>
        /// S2 — For each dual-source node in the subtree, draws a dashed
        /// detail curve from the secondary parent's symbol centre to this node's
        /// symbol centre, plus a small "E" TextNote at the midpoint.
        /// No-op when the node has no secondary parent or the secondary parent
        /// has no position in <paramref name="nodeToInstance"/>.
        /// </summary>
        private static void DrawEmergencyFeedNotation(Document doc, ViewDrafting view,
            SLDNode root, SLDLayout layout,
            IDictionary<ElementId, ElementId> nodeToInstance,
            double boxHFt, double levelOffsetFt)
        {
            if (doc == null || view == null || root == null || layout == null) return;
            try
            {
                var dualNodes = SLDCircuitTraverser.FindDualSourceNodes(root);
                if (dualNodes == null || dualNodes.Count == 0) return;

                ElementId tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();

                foreach (var node in dualNodes)
                {
                    try
                    {
                        // Need position for this node and its secondary parent.
                        if (node.SecondaryParentId == null
                            || node.SecondaryParentId == ElementId.InvalidElementId)
                            continue;

                        if (!layout.SymbolPositions.TryGetValue(node.ElementId, out var nodePos))
                            continue;

                        // Try to find secondary parent position in layout.
                        if (!layout.SymbolPositions.TryGetValue(
                                node.SecondaryParentId, out var secPos))
                            continue;

                        // Draw dashed detail curve (the "dashed" quality is set via
                        // line-style; use the first available dash line style).
                        var dashLine = Line.CreateBound(secPos, nodePos);
                        if (dashLine.Length < 1e-6) continue;

                        var curve = doc.Create.NewDetailCurve(view, dashLine);

                        // Attempt to apply a dashed line style.
                        try
                        {
                            var dashStyle = new FilteredElementCollector(doc)
                                .OfClass(typeof(GraphicsStyle))
                                .Cast<GraphicsStyle>()
                                .FirstOrDefault(gs =>
                                    gs.GraphicsStyleType == GraphicsStyleType.Projection
                                    && (gs.Name.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0
                                     || gs.Name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (dashStyle != null && curve != null)
                                curve.LineStyle = dashStyle;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"DrawEmergencyFeedNotation line style: {ex.Message}");
                        }

                        // Place "E" label at midpoint.
                        if (tnt != ElementId.InvalidElementId)
                        {
                            var mid = new XYZ(
                                (secPos.X + nodePos.X) / 2.0,
                                (secPos.Y + nodePos.Y) / 2.0 + boxHFt * 0.3,
                                0);
                            try { TextNote.Create(doc, view.Id, mid, "E", tnt); }
                            catch (Exception ex2)
                            {
                                StingLog.Warn($"DrawEmergencyFeedNotation E label: {ex2.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DrawEmergencyFeedNotation node '{node?.Label}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawEmergencyFeedNotation: {ex.Message}");
            }
        }

        // ── S3 — Bus coupler / tie-breaker notation ───────────────────────────

        /// <summary>
        /// S3 — Between each adjacent pair of root distribution boards, draws a
        /// dashed detail curve connecting the right edge of the left board's busbar
        /// to the left edge of the right board's busbar, with a "BC/NOP" TextNote
        /// at the midpoint (Bus Coupler / Normally Open Point).
        /// </summary>
        private static void DrawBusCouplerNotation(Document doc, ViewDrafting view,
            List<(SLDNode root, SLDLayout layout)> rootLayouts,
            double boxHFt, double levelOffsetFt)
        {
            if (doc == null || view == null || rootLayouts == null || rootLayouts.Count < 2) return;
            try
            {
                ElementId tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();

                for (int i = 0; i < rootLayouts.Count - 1; i++)
                {
                    try
                    {
                        var (rootA, layoutA) = rootLayouts[i];
                        var (rootB, layoutB) = rootLayouts[i + 1];

                        if (!layoutA.SymbolPositions.TryGetValue(rootA.ElementId, out var posA))
                            continue;
                        if (!layoutB.SymbolPositions.TryGetValue(rootB.ElementId, out var posB))
                            continue;

                        // Right edge of rootA's top node = posA.X + half of levelOffset.
                        // Left edge of rootB's top node = posB.X - half of levelOffset.
                        double halfLevel = levelOffsetFt * 0.5;
                        var ptA = new XYZ(posA.X + halfLevel, posA.Y - boxHFt * 0.5, 0);
                        var ptB = new XYZ(posB.X - halfLevel, posB.Y - boxHFt * 0.5, 0);

                        if (ptA.DistanceTo(ptB) < 1e-6) continue;

                        // Draw dashed coupler line.
                        var couplerLine = Line.CreateBound(ptA, ptB);
                        var curve = doc.Create.NewDetailCurve(view, couplerLine);

                        // Apply dashed line style if available.
                        try
                        {
                            var dashStyle = new FilteredElementCollector(doc)
                                .OfClass(typeof(GraphicsStyle))
                                .Cast<GraphicsStyle>()
                                .FirstOrDefault(gs =>
                                    gs.GraphicsStyleType == GraphicsStyleType.Projection
                                    && (gs.Name.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0
                                     || gs.Name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (dashStyle != null && curve != null)
                                curve.LineStyle = dashStyle;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"DrawBusCouplerNotation line style: {ex.Message}");
                        }

                        // Place "BC/NOP" label at midpoint.
                        if (tnt != ElementId.InvalidElementId)
                        {
                            var mid = new XYZ(
                                (ptA.X + ptB.X) / 2.0,
                                ptA.Y + boxHFt * 0.4,
                                0);
                            try { TextNote.Create(doc, view.Id, mid, "BC/NOP", tnt); }
                            catch (Exception ex2)
                            {
                                StingLog.Warn($"DrawBusCouplerNotation TextNote: {ex2.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DrawBusCouplerNotation pair {i}/{i + 1}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawBusCouplerNotation: {ex.Message}");
            }
        }
    }
}
