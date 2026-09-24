// StingTools — Drawing Template Manager · MEP annotation
//
// The two MEP annotation kinds the corporate catalogue declares and
// nothing implemented:
//
//   * AutoAnnotateSlope     — spot slope on every sloped pipe / duct run.
//                             Revit models this as a SpotDimension whose
//                             type lives in OST_SpotSlopes; there is no
//                             "slope tag" primitive. Placed via
//                             Document.Create.NewSpotElevation against a
//                             slope-category type, which is the documented
//                             route (the API has no NewSpotSlope).
//   * AutoAnnotateFlowArrow — a flow-direction annotation symbol at the
//                             midpoint of each connected run, rotated to
//                             the run axis and pointed the way the fluid
//                             actually travels.
//
// Flow direction is read from the run, not assumed: Revit's
// RBS_DUCT_FLOW_DIRECTION_PARAM / connector flow direction tells us
// whether the connector at the "start" of the run is In or Out, and the
// arrow is flipped accordingly. When neither is legible the arrow is
// placed along the geometric axis and the run is reported, so a drawing
// never carries a confidently-wrong arrow.
//
// Both passes are idempotent against what is already in the view.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing.Dimensioning
{
    internal static class MepAnnotator
    {
        private static readonly BuiltInCategory[] SlopeCats =
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctCurves,
        };

        private static readonly BuiltInCategory[] FlowCats =
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctCurves,
        };

        /// <summary>Slopes flatter than this are treated as level and not annotated (ratio, rise/run).</summary>
        public const double MinSlopeRatio = 0.0005;   // 1:2000

        // ─────────────────────────────────────────────────────────────────
        //  AutoAnnotateSlope
        // ─────────────────────────────────────────────────────────────────

        public static void RunSlope(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            if (!GridDimensioner.IsDimensionable(view))
            {
                result.Warnings.Add(
                    $"AutoAnnotateSlope: view '{view?.Name}' is not a dimensionable 2D view — skipped. " +
                    "Spot slopes need a plan / section / elevation; run this on the drainage section, not the 3D view.");
                return;
            }

            var slopeTypeId = ResolveSlopeTypeId(doc, rule?.TagFamily, result);

            // A slope rule on a non-MEP category is real: the catalogue carries
            // one on "Roofs", and a roof pitch is exactly what a spot slope is
            // for. Route it to the generic element path rather than quietly
            // redirecting it to pipes, which is what a pipes-only collector
            // would have done.
            var declaredBic = ElementDimensioner.ResolveBic(rule?.Category);
            if (declaredBic.HasValue && !SlopeCats.Contains(declaredBic.Value))
            {
                RunSlopeOnElements(doc, view, rule, declaredBic.Value, slopeTypeId, result);
                return;
            }

            var curves = CollectMepCurves(doc, view, rule, SlopeCats, result);
            if (curves.Count == 0)
            {
                result.Warnings.Add("AutoAnnotateSlope: no pipes or ducts in view — nothing to annotate.");
                return;
            }

            var already = SpottedIndex(doc, view, BuiltInCategory.OST_SpotSlopes, result);

            int flat = 0;
            foreach (var mc in curves)
            {
                try
                {
                    if (rule?.SkipIfTagged != false && already.Contains(mc.Id)) { result.Skipped++; continue; }
                    if (!(mc.Location is LocationCurve lc) || !(lc.Curve is Line ln)) continue;

                    var a = ln.GetEndPoint(0); var b = ln.GetEndPoint(1);
                    double run = new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength();
                    double rise = Math.Abs(b.Z - a.Z);
                    if (run < 1e-9 || rise / run < MinSlopeRatio) { flat++; continue; }

                    // Annotate at the midpoint, on the centreline.
                    var mid = (a + b) * 0.5;
                    var bend = mid + new XYZ(1.0, 1.0, 0);
                    var end  = mid + new XYZ(2.0, 1.0, 0);
                    var cref = lc.Curve.Reference ?? new Reference(mc);

                    var sd = doc.Create.NewSpotElevation(view, cref, mid, bend, end, mid, hasLeader: true);
                    if (sd == null)
                    {
                        result.Warnings.Add($"AutoAnnotateSlope: NewSpotElevation returned null for {mc.Id}.");
                        continue;
                    }
                    if (slopeTypeId != ElementId.InvalidElementId)
                    {
                        SafeWrite.Try(() => sd.ChangeTypeId(slopeTypeId),
                            "MepAnnotator.Slope",
                            $"spot-slope type on {mc.Category?.Name} {mc.Id}",
                            result?.Warnings);
                    }
                    result.SpotsPlaced++;
                    already.Add(mc.Id);
                }
                catch (Exception ex) { result.Warnings.Add($"AutoAnnotateSlope {mc.Id}: {ex.Message}"); }
            }

            if (flat > 0)
                result.Warnings.Add($"AutoAnnotateSlope: {flat} run(s) are level (flatter than 1:{(int)(1 / MinSlopeRatio)}) — no slope annotation placed on those.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  AutoAnnotateFlowArrow
        // ─────────────────────────────────────────────────────────────────

        public static void RunFlowArrow(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            // Annotation symbols can be placed in any view that accepts
            // annotation; the dimension gate is the right approximation and
            // keeps 3D views out (NewFamilyInstance(XYZ, sym, View) rejects
            // View3D for annotation symbols).
            if (!GridDimensioner.IsDimensionable(view))
            {
                result.Warnings.Add(
                    $"AutoAnnotateFlowArrow: view '{view?.Name}' is not a 2D annotation view — skipped.");
                return;
            }

            var curves = CollectMepCurves(doc, view, rule, FlowCats, result);
            if (curves.Count == 0)
            {
                result.Warnings.Add("AutoAnnotateFlowArrow: no pipes or ducts in view — nothing to annotate.");
                return;
            }

            // The arrow family is data, not a hard-code: rule.TagFamily first,
            // then the pack's tagFamilies table, then a name probe. When none
            // resolves we say so and place nothing — an invented symbol would
            // be worse than an absent one.
            var sym = ResolveArrowSymbol(doc, rule, pack);
            if (sym == null)
            {
                result.Warnings.Add(
                    "AutoAnnotateFlowArrow: no flow-arrow annotation family found. Set the rule's tagFamily "
                    + "(or the pack's tagFamilies entry for the category) to a loaded generic-annotation family, "
                    + "e.g. \"STING_ANNO_FLOW_ARROW\". No arrows placed.");
                return;
            }
            if (!sym.IsActive)
            {
                try { sym.Activate(); doc.Regenerate(); }
                catch (Exception ex) { result.Warnings.Add($"AutoAnnotateFlowArrow: could not activate '{sym.Name}': {ex.Message}"); return; }
            }

            var already = AnnotationSymbolIndex(doc, view, sym.Id);
            var runs = ClusterRuns(curves);

            foreach (var run in runs)
            {
                try
                {
                    // One arrow per connected run, at the longest straight's
                    // midpoint — a per-element arrow buries the drawing.
                    var host = run
                        .Select(c => (C: c, L: c.Location as LocationCurve))
                        .Where(t => t.L?.Curve is Line)
                        .OrderByDescending(t => t.L.Curve.Length)
                        .FirstOrDefault();
                    if (host.C == null) continue;
                    if (rule?.SkipIfTagged != false && already.Contains(host.C.Id)) { result.Skipped++; continue; }

                    var ln = (Line)host.L.Curve;
                    var mid = (ln.GetEndPoint(0) + ln.GetEndPoint(1)) * 0.5;
                    var dir = FlowDirection(host.C, ln, out bool known);
                    if (!known)
                        result.Warnings.Add(
                            $"AutoAnnotateFlowArrow: flow direction on {host.C.Category?.Name} {host.C.Id} could not be read "
                            + "from its connectors; arrow follows the geometric axis and may point the wrong way.");

                    // Revit projects an annotation symbol onto the view's own
                    // sketch plane, so the model-space midpoint is the right
                    // placement point in a plan or a section alike.
                    var inst = doc.Create.NewFamilyInstance(mid, sym, view);
                    if (inst == null)
                    {
                        result.Warnings.Add($"AutoAnnotateFlowArrow: NewFamilyInstance returned null for run at {mid}.");
                        continue;
                    }

                    // Rotate about the view's own normal, by the angle measured
                    // in the view's OWN basis.
                    //
                    // Atan2(dir.Y, dir.X) about ViewDirection is wrong: a floor
                    // plan's ViewDirection is -Z (it points out of the screen,
                    // towards the viewer), so a world-XY angle applied about it
                    // turns the arrow the opposite way — every arrow in every
                    // plan would point upstream. Measuring against
                    // RightDirection / UpDirection is correct in any view
                    // orientation, because (Right, Up, ViewDirection) is
                    // right-handed, so a positive rotation about ViewDirection
                    // carries Right towards Up.
                    try
                    {
                        var normal = view.ViewDirection ?? XYZ.BasisZ;
                        var right  = view.RightDirection ?? XYZ.BasisX;
                        var up     = view.UpDirection ?? XYZ.BasisY;
                        double angle = Math.Atan2(dir.DotProduct(up), dir.DotProduct(right));
                        if (Math.Abs(angle) > 1e-9)
                        {
                            var axis = Line.CreateBound(mid, mid + normal);
                            ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"AutoAnnotateFlowArrow: placed but not rotated for {host.C.Id}: {ex.Message}");
                    }

                    result.DecorativePlaced++;
                    already.Add(host.C.Id);
                }
                catch (Exception ex) { result.Warnings.Add($"AutoAnnotateFlowArrow run: {ex.Message}"); }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Collection + clustering
        // ─────────────────────────────────────────────────────────────────

        private static List<MEPCurve> CollectMepCurves(Document doc, View view, AutoAnnotationRule rule,
            BuiltInCategory[] defaults, AnnotationResult result)
        {
            // The catalogue writes DISPLAY names ("Pipes", "Ducts", "Roofs"),
            // so resolution has to accept both spellings — an Enum.TryParse
            // -only path warned and fell back on every shipped rule.
            var cats = new List<BuiltInCategory>();
            var declared = rule?.Category;
            if (string.IsNullOrWhiteSpace(declared) || declared == "*")
            {
                cats.AddRange(defaults);
            }
            else
            {
                var bic = ElementDimensioner.ResolveBic(declared);
                if (bic.HasValue) cats.Add(bic.Value);
                else
                {
                    result.Warnings.Add(
                        $"MEP annotation: category '{declared}' resolves to no Revit category — falling back to pipes + ducts.");
                    cats.AddRange(defaults);
                }
            }

            var els = new List<MEPCurve>();
            foreach (var bic in cats)
            {
                try
                {
                    els.AddRange(new FilteredElementCollector(doc, view.Id)
                        .OfCategory(bic).WhereElementIsNotElementType()
                        .OfClass(typeof(MEPCurve)).Cast<MEPCurve>()
                        .Where(m => m != null));
                }
                catch (Exception ex) { result.Warnings.Add($"MEP annotation collect {bic}: {ex.Message}"); }
            }

            if (rule?.MinSizeMm.HasValue == true)
            {
                double minFt = rule.MinSizeMm.Value / DimensionStrategy.MmPerFt;
                els = els.Where(e => { double s = SizeFt(e); return s <= 0 || s >= minFt; }).ToList();
            }
            return els;
        }

        private static double SizeFt(MEPCurve e)
        {
            try
            {
                var dia = e.LookupParameter("Diameter");
                if (dia != null && dia.StorageType == StorageType.Double) return dia.AsDouble();
                var w = e.LookupParameter("Width");
                if (w != null && w.StorageType == StorageType.Double) return w.AsDouble();
            }
            catch (Exception ex) { StingLog.Warn($"MepAnnotator.SizeFt: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// Transitive connector-graph clustering — one list per physically
        /// connected run, so a flow arrow lands once per run rather than once
        /// per straight.
        /// </summary>
        private static List<List<MEPCurve>> ClusterRuns(List<MEPCurve> all)
        {
            var visited = new HashSet<long>();
            var runs = new List<List<MEPCurve>>();
            var byId = new Dictionary<long, MEPCurve>();
            foreach (var e in all) byId[e.Id.Value] = e;

            foreach (var seed in all)
            {
                if (visited.Contains(seed.Id.Value)) continue;
                var run = new List<MEPCurve>();
                var stack = new Stack<MEPCurve>();
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!visited.Add(cur.Id.Value)) continue;
                    run.Add(cur);
                    ConnectorManager cm = null;
                    try { cm = cur.ConnectorManager; } catch (Exception ex) { StingLog.Warn($"ConnectorManager {cur.Id}: {ex.Message}"); }
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        ConnectorSet refs = null;
                        try { refs = c?.AllRefs; } catch (Exception ex) { StingLog.Warn($"AllRefs: {ex.Message}"); }
                        if (refs == null) continue;
                        foreach (Connector r in refs)
                        {
                            var owner = r?.Owner;
                            if (owner == null || owner.Id.Value == cur.Id.Value) continue;
                            if (byId.TryGetValue(owner.Id.Value, out var nb) && !visited.Contains(nb.Id.Value))
                                stack.Push(nb);
                        }
                    }
                }
                if (run.Count > 0) runs.Add(run);
            }
            return runs;
        }

        /// <summary>
        /// Downstream direction along <paramref name="ln"/>. Read from the
        /// connector flow directions when legible; <paramref name="known"/>
        /// reports whether it was, so the caller can warn rather than imply
        /// certainty.
        /// </summary>
        private static XYZ FlowDirection(MEPCurve mc, Line ln, out bool known)
        {
            known = false;
            var geo = ln.Direction.Normalize();
            try
            {
                var cm = mc.ConnectorManager;
                if (cm == null) return geo;
                Connector outConn = null, inConn = null;
                foreach (Connector c in cm.Connectors)
                {
                    if (c == null) continue;
                    try
                    {
                        if (c.Direction == FlowDirectionType.Out && outConn == null) outConn = c;
                        else if (c.Direction == FlowDirectionType.In && inConn == null) inConn = c;
                    }
                    catch (Exception ex) { StingLog.Warn($"Connector direction: {ex.Message}"); }
                }
                if (outConn != null && inConn != null)
                {
                    var v = outConn.Origin - inConn.Origin;
                    if (!v.IsZeroLength()) { known = true; return v.Normalize(); }
                }
                if (outConn != null)
                {
                    // One known outlet: flow runs from the far end towards it.
                    var a = ln.GetEndPoint(0); var b = ln.GetEndPoint(1);
                    var near = (outConn.Origin - a).GetLength() < (outConn.Origin - b).GetLength() ? b : a;
                    var v = outConn.Origin - near;
                    if (!v.IsZeroLength()) { known = true; return v.Normalize(); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FlowDirection {mc?.Id}: {ex.Message}"); }
            return geo;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Type / symbol resolution
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A SpotDimensionType in the spot-SLOPE category. Falls back to a
        /// name probe, then to any spot type, and says which it used — a
        /// spot-elevation type standing in for a slope type prints the wrong
        /// value, so that substitution must be visible.
        /// </summary>
        private static ElementId ResolveSlopeTypeId(Document doc, string preferred, AnnotationResult result)
        {
            try
            {
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().ToList();
                if (all.Count == 0)
                {
                    result.Warnings.Add("AutoAnnotateSlope: project has no SpotDimensionType — Revit's default is used.");
                    return ElementId.InvalidElementId;
                }

                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    var named = all.FirstOrDefault(t => string.Equals(t.Name, preferred, StringComparison.OrdinalIgnoreCase));
                    if (named != null) return named.Id;
                    result.Warnings.Add($"AutoAnnotateSlope: spot type '{preferred}' not found; falling back to a slope-category type.");
                }

                var slope = all.FirstOrDefault(t =>
                {
                    try { return t.Category?.Id.Value == (long)BuiltInCategory.OST_SpotSlopes; }
                    catch { return false; }
                });
                if (slope != null) return slope.Id;

                var byName = all.FirstOrDefault(t => (t.Name ?? "").IndexOf("slope", StringComparison.OrdinalIgnoreCase) >= 0);
                if (byName != null) return byName.Id;

                result.Warnings.Add(
                    "AutoAnnotateSlope: no spot-SLOPE type in this project — the annotation will print an ELEVATION, "
                    + "not a slope. Load or create a Spot Slope type and re-run.");
                return ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"AutoAnnotateSlope type resolve: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static FamilySymbol ResolveArrowSymbol(Document doc, AutoAnnotationRule rule, AnnotationRulePack pack)
        {
            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(rule?.TagFamily)) names.Add(rule.TagFamily.Trim());
            if (pack?.TagFamilies != null && !string.IsNullOrWhiteSpace(rule?.Category)
                && pack.TagFamilies.TryGetValue(rule.Category, out var fromPack)
                && !string.IsNullOrWhiteSpace(fromPack))
                names.Add(fromPack.Trim());

            try
            {
                var anno = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(s => s?.Family != null)
                    .ToList();

                foreach (var n in names)
                {
                    var hit = anno.FirstOrDefault(s =>
                        string.Equals(s.Family.Name, n, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) return hit;
                }

                // Last resort: a generic annotation whose name reads as a flow
                // arrow. Name-probing only — never a synthesised symbol.
                return anno.FirstOrDefault(s =>
                {
                    try
                    {
                        if (s.Category?.Id.Value != (long)BuiltInCategory.OST_GenericAnnotation) return false;
                        var fn = (s.Family.Name ?? "") + " " + (s.Name ?? "");
                        return fn.IndexOf("flow", StringComparison.OrdinalIgnoreCase) >= 0
                            && fn.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    catch { return false; }
                });
            }
            catch (Exception ex) { StingLog.Warn($"ResolveArrowSymbol: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Spot slope on a non-MEP category — roofs, floors, ramps. There is no
        /// centreline to read, so the annotation is anchored at the element's
        /// top face centre via its view bounding box, the same anchor
        /// AnnotationRunner.ProcessSpotRules uses for generic spot elevations.
        /// </summary>
        private static void RunSlopeOnElements(Document doc, View view, AutoAnnotationRule rule,
            BuiltInCategory bic, ElementId slopeTypeId, AnnotationResult result)
        {
            var already = SpottedIndex(doc, view, BuiltInCategory.OST_SpotSlopes, result);
            int placed = 0;
            List<Element> els;
            try
            {
                els = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(bic).WhereElementIsNotElementType().ToList();
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"AutoAnnotateSlope collect {bic}: {ex.Message}");
                return;
            }
            if (els.Count == 0)
            {
                result.Warnings.Add($"AutoAnnotateSlope: no {bic} elements in view — nothing to annotate.");
                return;
            }

            foreach (var el in els)
            {
                try
                {
                    if (rule?.SkipIfTagged != false && already.Contains(el.Id)) { result.Skipped++; continue; }
                    var bb = el.get_BoundingBox(view);
                    if (bb == null) continue;
                    var origin = new XYZ((bb.Min.X + bb.Max.X) * 0.5, (bb.Min.Y + bb.Max.Y) * 0.5, bb.Max.Z);
                    var bend = origin + new XYZ(1.0, 1.0, 0);
                    var end  = origin + new XYZ(2.0, 1.0, 0);

                    var sd = doc.Create.NewSpotElevation(view, new Reference(el), origin, bend, end, origin, hasLeader: true);
                    if (sd == null) continue;
                    if (slopeTypeId != ElementId.InvalidElementId)
                        SafeWrite.Try(() => sd.ChangeTypeId(slopeTypeId),
                            "MepAnnotator.Slope", $"spot-slope type on {el.Category?.Name} {el.Id}", result?.Warnings);
                    result.SpotsPlaced++;
                    already.Add(el.Id);
                    placed++;
                }
                catch (Exception ex) { result.Warnings.Add($"AutoAnnotateSlope {bic} {el.Id}: {ex.Message}"); }
            }
            if (placed == 0)
                result.Warnings.Add($"AutoAnnotateSlope: {els.Count} {bic} element(s) in view but none could take a spot slope.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Idempotency indexes
        // ─────────────────────────────────────────────────────────────────

        private static HashSet<ElementId> SpottedIndex(Document doc, View view, BuiltInCategory bic, AnnotationResult result)
        {
            var set = new HashSet<ElementId>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfCategory(bic).WhereElementIsNotElementType())
                {
                    if (!(el is SpotDimension sd)) continue;
                    try
                    {
                        if (!sd.AreReferencesAvailable) continue;
                        var refs = sd.References;
                        if (refs == null) continue;
                        foreach (Reference r in refs)
                        {
                            var host = doc.GetElement(r);
                            if (host != null && host.Id != ElementId.InvalidElementId) set.Add(host.Id);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"SpottedIndex {sd.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                result?.Warnings.Add($"Could not index existing spot annotations in '{view?.Name}' ({ex.Message}); duplicates are possible.");
            }
            return set;
        }

        /// <summary>
        /// Elements near an existing instance of the arrow symbol in this
        /// view. An annotation symbol carries no reference to its subject, so
        /// proximity is the only available signal: an arrow within one foot of
        /// a run's longest-straight midpoint counts as "already annotated".
        /// Deliberately coarse — the alternative is stacking an arrow on every
        /// re-run.
        /// </summary>
        private static HashSet<ElementId> AnnotationSymbolIndex(Document doc, View view, ElementId symbolId)
        {
            var set = new HashSet<ElementId>();
            try
            {
                var placed = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(fi => { try { return fi.Symbol?.Id == symbolId; } catch { return false; } })
                    .Select(fi => (fi.Location as LocationPoint)?.Point)
                    .Where(p => p != null)
                    .ToList();
                if (placed.Count == 0) return set;

                foreach (var mc in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(MEPCurve)).Cast<MEPCurve>())
                {
                    try
                    {
                        if (!(mc.Location is LocationCurve lc) || !(lc.Curve is Line ln)) continue;
                        var mid = (ln.GetEndPoint(0) + ln.GetEndPoint(1)) * 0.5;
                        if (placed.Any(p => (new XYZ(p.X - mid.X, p.Y - mid.Y, 0)).GetLength() < 1.0))
                            set.Add(mc.Id);
                    }
                    catch (Exception ex) { StingLog.Warn($"AnnotationSymbolIndex {mc.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"AnnotationSymbolIndex: {ex.Message}"); }
            return set;
        }
    }
}
