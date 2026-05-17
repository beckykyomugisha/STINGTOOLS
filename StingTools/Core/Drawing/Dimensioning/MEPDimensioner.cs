// StingTools — Drawing Template Manager · Phase 175
//
// MEPDimensioner walks the connector graph of pipe / duct / conduit /
// cable-tray runs and emits dimension chains keyed off connector
// origins. Two rule shapes are supported:
//
//   * AutoDimMEPRun     — chain along the run's own axis. One witness
//                         line per straight, parallel to the centreline.
//                         Best for spool plans / sections where a fitter
//                         needs continuous centre-to-centre lengths.
//   * AutoDimMEPToGrid  — perpendicular drop from each connector to the
//                         nearest grid line in the view. Used on
//                         coordination drawings where MEP elements need
//                         to be located against the building grid.
//
// 3D views are silently skipped — Revit's Dimension API doesn't accept
// them. Callers needing a "3D dim" must run this on a section view that
// projects the run, e.g. the ISO 6412 axonometric we mint in fabrication.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Drawing.Dimensioning
{
    internal static class MEPDimensioner
    {
        // Categories with a Connector graph we can walk. Cable trays and
        // conduits expose ConnectorManager via FamilyInstance fittings.
        private static readonly BuiltInCategory[] MepCurveCats = new[]
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_CableTray,
        };

        public const double RunOffsetMm  = 600;   // chain offset from centreline
        public const double GridDropOffsetMm = 300;
        public const double MaxGridDropFt = 50;   // cap perpendicular search

        public static void RunChain(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            if (!GridDimensioner.IsDimensionable(view))
            {
                result.Warnings.Add(
                    $"MEP chain dim: view '{view?.Name}' is not 2D — skipped. " +
                    "Revit's Dimension API only accepts plan / section / elevation / detail / drafting views. " +
                    "For a 3D-style spool dim use the ISO 6412 axonometric drafting view minted by AssemblyViewBuilder.");
                return;
            }

            var elements = CollectMepCurves(doc, view, rule);
            if (elements.Count == 0) return;

            // Group by connected run — adjacency-cluster via the connector
            // graph so we emit one chain per physical run rather than one
            // per straight, even if the run has fittings in between.
            var runs = ClusterRuns(elements);

            var strategy = DimensionStrategy.Parse(pack.DimensionStrategy);
            var dimType  = DimensionStrategy.ResolveType(doc, strategy, pack.DimensionStyle);

            foreach (var run in runs)
            {
                try { EmitRunChain(doc, view, run, strategy, dimType, result); }
                catch (Exception ex) { result.Warnings.Add($"MEP chain dim: {ex.Message}"); }
            }
        }

        public static void RunGridDrop(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            if (!GridDimensioner.IsDimensionable(view))
            {
                result.Warnings.Add(
                    $"MEP grid-drop dim: view '{view?.Name}' is not 2D — skipped. " +
                    "Use the coordination plan or section views, not the 3D coordination view.");
                return;
            }

            var elements = CollectMepCurves(doc, view, rule);
            if (elements.Count == 0) return;

            var grids = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Grid)).Cast<Grid>().ToList();
            if (grids.Count == 0)
            {
                result.Warnings.Add("MEP grid-drop dim: view contains no grids — skipped.");
                return;
            }

            var strategy = DimensionStrategy.Parse(pack.DimensionStrategy);
            var dimType  = DimensionStrategy.ResolveType(doc, strategy, pack.DimensionStyle);

            foreach (var el in elements)
            {
                try { EmitGridDrop(doc, view, el, grids, dimType, result); }
                catch (Exception ex) { result.Warnings.Add($"MEP grid-drop dim {el.Id}: {ex.Message}"); }
            }
        }

        // ── Connector-graph walking ──

        private static List<MEPCurve> CollectMepCurves(Document doc, View view, AutoAnnotationRule rule)
        {
            // Caller can narrow to one category with rule.Category, or "*"
            // for every MEP run category.
            var cats = new List<BuiltInCategory>();
            if (string.IsNullOrEmpty(rule?.Category) || rule.Category == "*")
            {
                cats.AddRange(MepCurveCats);
            }
            else if (Enum.TryParse<BuiltInCategory>(rule.Category, true, out var bic))
            {
                cats.Add(bic);
            }

            var els = new List<MEPCurve>();
            foreach (var bic in cats)
            {
                try
                {
                    var c = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .OfClass(typeof(MEPCurve))
                        .Cast<MEPCurve>()
                        .Where(m => m != null && m.ConnectorManager != null);
                    els.AddRange(c);
                }
                catch (Exception ex) { StingLog.Warn($"MEP collect {bic}: {ex.Message}"); }
            }

            // Min-size filter — pack uses mm, MEPCurve.Diameter / Width
            // are in feet.
            if (rule?.MinSizeMm != null)
            {
                var minFt = rule.MinSizeMm.Value / DimensionStrategy.MmPerFt;
                els = els.Where(e =>
                {
                    var size = ReadElementDiameterFt(e);
                    return size <= 0 || size >= minFt;
                }).ToList();
            }
            return els;
        }

        private static List<List<MEPCurve>> ClusterRuns(List<MEPCurve> all)
        {
            // Union-find-ish: walk each curve's connectors to neighbours of
            // the same MEP system; group transitively into runs.
            var visited = new HashSet<long>();
            var runs = new List<List<MEPCurve>>();
            var byId = all.ToDictionary(e => e.Id.Value, e => e);

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
                    var cm = cur.ConnectorManager;
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (c == null) continue;
                        ConnectorSet refs = null;
                        try { refs = c.AllRefs; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        if (refs == null) continue;
                        foreach (Connector r in refs)
                        {
                            var owner = r?.Owner;
                            if (owner == null) continue;
                            if (owner.Id.Value == cur.Id.Value) continue;
                            if (byId.TryGetValue(owner.Id.Value, out var nb) && !visited.Contains(nb.Id.Value))
                                stack.Push(nb);
                        }
                    }
                }
                if (run.Count > 0) runs.Add(run);
            }
            return runs;
        }

        // ── Chain emission ──

        private static void EmitRunChain(Document doc, View view, List<MEPCurve> run,
            DimStrategyKind strategy, DimensionType dimType, AnnotationResult result)
        {
            if (run == null || run.Count == 0) return;

            // One Reference per MEPCurve, anchored at the curve's midpoint.
            // Walking connectors instead would yield two references per
            // pipe — both pointing at the same LocationCurve.Reference —
            // which Revit collapses into a single dimension witness, so
            // every chain segment came out zero-length. Letting Revit
            // project per-pipe midpoints onto the witness line gives the
            // correct centre-to-centre lengths a fitter expects.
            var axis = ResolveRunAxis(run);
            var pts = new List<(Reference R, XYZ P, long Id)>();
            foreach (var c in run)
            {
                if (!(c.Location is LocationCurve lc) || lc.Curve == null) continue;
                var cref = lc.Curve.Reference;
                if (cref == null)
                {
                    // Some MEPCurve subclasses (e.g. flex pipe / flex duct)
                    // expose a null curve reference. Skip with a warning so
                    // the rest of the chain still ships rather than failing
                    // the whole rule.
                    result.Warnings.Add($"MEP chain: pipe/duct {c.Id} has no curve reference — skipped.");
                    continue;
                }
                var mid = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;
                pts.Add((cref, mid, c.Id.Value));
            }
            if (pts.Count < 2) return;

            // Sort along the dominant run axis so segments read in
            // geographic order; dedupe by element id (pipes shared by
            // multiple connector traversals would otherwise repeat).
            pts = pts
                .GroupBy(t => t.Id).Select(g => g.First())
                .OrderBy(t => t.P.DotProduct(axis.Normalize()))
                .ToList();
            if (pts.Count < 2) return;

            var refArr = new ReferenceArray();
            foreach (var p in pts) refArr.Append(p.R);

            var first = pts.First().P;
            var last  = pts.Last().P;
            var span  = (last - first).GetLength();
            var line  = DimensionStrategy.BuildWitnessLine(first, axis, RunOffsetMm, span + 1.0);

            try
            {
                var dim = dimType != null
                    ? doc.Create.NewDimension(view, line, refArr, dimType)
                    : doc.Create.NewDimension(view, line, refArr);
                if (dim != null) result.DimsPlaced++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"MEP chain NewDimension: {ex.Message}");
            }
        }

        private static void EmitGridDrop(Document doc, View view, MEPCurve el,
            List<Grid> grids, DimensionType dimType, AnnotationResult result)
        {
            if (!(el.Location is LocationCurve lc) || lc.Curve == null) return;

            var origin = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;
            var nearest = grids
                .Select(g => (g, dist: PerpDistanceToGrid(origin, g)))
                .Where(t => t.dist < MaxGridDropFt)
                .OrderBy(t => t.dist)
                .FirstOrDefault();
            if (nearest.g == null) return;

            var refArr = new ReferenceArray();
            refArr.Append(new Reference(nearest.g));
            refArr.Append(lc.Curve.Reference ?? new Reference(el));

            // Witness line perpendicular to the grid; offset 300mm clear so
            // the dim doesn't overlay the MEP run.
            XYZ axis = nearest.g.Curve is Line gl ? gl.Direction : XYZ.BasisX;
            var line = DimensionStrategy.BuildWitnessLine(origin, axis, GridDropOffsetMm, 1.0);

            try
            {
                var dim = dimType != null
                    ? doc.Create.NewDimension(view, line, refArr, dimType)
                    : doc.Create.NewDimension(view, line, refArr);
                if (dim != null) result.DimsPlaced++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"MEP grid-drop NewDimension: {ex.Message}");
            }
        }

        private static double PerpDistanceToGrid(XYZ p, Grid g)
        {
            try
            {
                if (!(g.Curve is Line line)) return double.MaxValue;
                var origin = line.Origin;
                var dir = line.Direction;
                var v = p - origin;
                var t = v.DotProduct(dir);
                var foot = origin + dir * t;
                return (p - foot).GetLength();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return double.MaxValue; }
        }

        private static XYZ ResolveRunAxis(List<MEPCurve> run)
        {
            // Pick the longest straight's direction as the dominant axis.
            try
            {
                var longest = run
                    .Select(c => c.Location as LocationCurve)
                    .Where(lc => lc?.Curve is Line)
                    .OrderByDescending(lc => lc.Curve.Length)
                    .FirstOrDefault();
                if (longest != null && longest.Curve is Line ln)
                    return ln.Direction;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return XYZ.BasisX;
        }

        private static double ReadElementDiameterFt(MEPCurve e)
        {
            try
            {
                if (e is Pipe p) return p.Diameter;
                if (e is Duct d) return Math.Max(d.Width, d.Height);
                var diaP = e.LookupParameter("Diameter");
                if (diaP != null && diaP.StorageType == StorageType.Double) return diaP.AsDouble();
                var wP = e.LookupParameter("Width");
                if (wP != null && wP.StorageType == StorageType.Double) return wP.AsDouble();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0.0;
        }
    }
}
