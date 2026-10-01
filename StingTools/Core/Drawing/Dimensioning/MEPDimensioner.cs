// StingTools — Drawing Template Manager · Phase 175 / DTW-84
//
// MEPDimensioner dimensions pipe / duct / conduit / cable-tray runs. Two rule
// shapes:
//
//   * AutoDimMEPRun     — centre-to-centre chain along each straight line of a
//                         connected run: fitting centres (the plane through each
//                         elbow / tee / valve that is perpendicular to the line)
//                         and the run's free ends. What a fitter sets out from.
//   * AutoDimMEPToGrid  — one dimension from each straight to the nearest grid
//                         PARALLEL to it, measured across both. Coordination
//                         drawings locate services against the building grid.
//
// DTW-84 fixes, all three of which left the passes placing nothing or garbage:
//   1. Runs clustered by MEPCurve-to-MEPCurve adjacency, but pipes meet through
//      fittings — no two pipes ever clustered. MepRunPlanning.Cluster now walks
//      through fittings and inline accessories (not through equipment).
//   2. The chain referenced each pipe's centreline and ran its witness line
//      ALONG those pipes — references parallel to the line, which Revit rejects.
//      Its references are now perpendicular to the line (fitting centre planes,
//      pipe end points). The grid drop ran its line along the grid; it now runs
//      across the grid and the pipe, which are parallel to each other.
//   3. No idempotency: every re-run duplicated. Both now stamp provenance
//      (AnnotationProvenance.DimMepRun / DimMepGridDrop) and skip what they find.
//
// Linked MEP is reported, not dimensioned (DTW-85); grids may be linked.
// 3D views are skipped — Revit's Dimension API doesn't accept them.
// NOT VERIFIED IN REVIT.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Core.Storage;

namespace StingTools.Core.Drawing.Dimensioning
{
    internal static class MEPDimensioner
    {
        private static readonly BuiltInCategory[] MepCurveCats =
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_CableTray,
        };

        /// <summary>What a run may be walked THROUGH: fittings and inline accessories.</summary>
        private static readonly HashSet<long> JointCats = new HashSet<long>
        {
            (long)BuiltInCategory.OST_PipeFitting,
            (long)BuiltInCategory.OST_DuctFitting,
            (long)BuiltInCategory.OST_ConduitFitting,
            (long)BuiltInCategory.OST_CableTrayFitting,
            (long)BuiltInCategory.OST_PipeAccessory,
            (long)BuiltInCategory.OST_DuctAccessory,
        };

        public const double RunOffsetMm  = 600;   // chain offset from the centreline
        public const double GridDropOffsetMm = 300;
        public const double MaxGridDropFt = 50;   // cap on the grid search
        private const double ParallelTol = 0.02;  // |sin| — about 1.1 degrees
        private const double MinDimFt = 1.0 / 304.8;

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
            ReportLinkedMep(doc, view, rule, "MEP chain dim", result);
            if (elements.Count == 0) return;

            var byId = elements.ToDictionary(e => e.Id.Value);
            var runs = MepRunPlanning.Cluster(byId.Keys, id => Neighbours(doc, id), id => IsJoint(doc, id));

            var strategy = DimensionStrategy.Parse(pack.DimensionStrategy);
            var dimType  = DimensionStrategy.ResolveType(doc, strategy, pack.DimensionStyle);
            var stamped  = StampedHosts(doc, view, AnnotationProvenance.DimMepRun);
            int tooShort = 0;

            foreach (var run in runs)
            {
                try
                {
                    var curves = run.Curves.Select(id => byId[id]).ToList();
                    var joints = run.Joints.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null).ToList();
                    EmitRunChains(doc, view, curves, joints, dimType, stamped, rule, result, ref tooShort);
                }
                catch (Exception ex) { result.Warnings.Add($"MEP chain dim: {ex.Message}"); }
            }
            if (tooShort > 0)
                result.Warnings.Add($"MEP chain dim: {tooShort} straight line(s) had fewer than two points to dimension — skipped.");
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
            if (!(view is ViewPlan))
            {
                // Grids are plan lines; measuring a run to one is a plan dimension.
                result.Warnings.Add($"MEP grid-drop dim: '{view.Name}' is not a plan view — skipped. " +
                                    "Runs are set out against grids on plans.");
                return;
            }

            var elements = CollectMepCurves(doc, view, rule);
            ReportLinkedMep(doc, view, rule, "MEP grid-drop dim", result);
            if (elements.Count == 0) return;

            // DTW-85: host grids and the grids of loaded links the view shows — an
            // MEP model's grids usually live in the linked architectural model.
            var grids = ViewLinks.StraightGrids(doc, view, result.Warnings, out _);
            if (grids.Count == 0)
            {
                result.Warnings.Add("MEP grid-drop dim: view shows no straight grids, host or linked — skipped.");
                return;
            }

            var strategy = DimensionStrategy.Parse(pack.DimensionStrategy);
            var dimType  = DimensionStrategy.ResolveType(doc, strategy, pack.DimensionStyle);
            var stamped  = StampedHosts(doc, view, AnnotationProvenance.DimMepGridDrop);
            int noGrid = 0;

            foreach (var el in elements)
            {
                try
                {
                    if (rule?.SkipIfTagged != false && stamped.Contains(el.UniqueId)) { result.Skipped++; continue; }
                    if (!EmitGridDrop(doc, view, el, grids, dimType, result)) noGrid++;
                }
                catch (Exception ex) { result.Warnings.Add($"MEP grid-drop dim {el.Id}: {ex.Message}"); }
            }
            if (noGrid > 0)
                result.Warnings.Add($"MEP grid-drop dim: {noGrid} run(s) have no parallel grid within {MaxGridDropFt:0} ft " +
                                    "(or sit on one, or run into the view) — not dimensioned.");
        }

        // ── Collection ──

        /// <summary>The run categories a rule covers: its own, or every MEP run category for "*".</summary>
        private static List<BuiltInCategory> RuleCategories(AutoAnnotationRule rule)
        {
            var cats = new List<BuiltInCategory>();
            if (string.IsNullOrEmpty(rule?.Category) || rule.Category == "*")
                cats.AddRange(MepCurveCats);
            else
            {
                var bic = ElementDimensioner.ResolveBic(rule.Category);
                if (bic.HasValue) cats.Add(bic.Value);
            }
            return cats;
        }

        /// <summary>
        /// DTW-85: this pass dimensions host runs only — a dimension to a linked
        /// pipe's centreline or end needs geometry references through the link,
        /// which it does not build. Linked runs the view shows are counted and
        /// reported, so an MEP-in-link drawing does not read as "nothing to do".
        /// </summary>
        private static void ReportLinkedMep(Document doc, View view, AutoAnnotationRule rule, string label,
            AnnotationResult result)
        {
            int n = 0;
            foreach (var link in ViewLinks.InView(doc, view, null))
            {
                foreach (var bic in RuleCategories(rule))
                {
                    try { n += ViewLinks.Visible(doc, view, link, bic).Count; }
                    catch (Exception ex) { StingLog.Warn($"{label}: link {link.Name} {bic}: {ex.Message}"); }
                }
            }
            if (n > 0)
                result.Warnings.Add($"{label}: {n} MEP run(s) in linked models not dimensioned — " +
                                    "dimensioning linked MEP is not supported by this pass; dimension them in the MEP model.");
        }

        private static List<MEPCurve> CollectMepCurves(Document doc, View view, AutoAnnotationRule rule)
        {
            var els = new List<MEPCurve>();
            foreach (var bic in RuleCategories(rule))
            {
                try
                {
                    els.AddRange(new FilteredElementCollector(doc, view.Id)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .OfClass(typeof(MEPCurve))
                        .Cast<MEPCurve>()
                        .Where(m => m != null && m.ConnectorManager != null));
                }
                catch (Exception ex) { StingLog.Warn($"MEP collect {bic}: {ex.Message}"); }
            }

            // Min-size filter — pack uses mm, MEPCurve.Diameter / Width are in feet.
            if (rule?.MinSizeMm != null)
            {
                // A-2: shared size definition + gate (ElementSize / AnnotationMinSize).
                els = els.Where(e => AnnotationMinSize.Keeps(
                    ElementSize.SectionFt(e) * AnnotationMinSize.MmPerFt, rule.MinSizeMm)).ToList();
            }
            return els;
        }

        // ── Connector graph ──

        private static ConnectorManager ConnectorsOf(Element e)
        {
            try
            {
                if (e is MEPCurve c) return c.ConnectorManager;
                if (e is FamilyInstance fi) return fi.MEPModel?.ConnectorManager;
            }
            catch (Exception ex) { StingLog.Warn($"MEP connectors {e?.Id}: {ex.Message}"); }
            return null;
        }

        /// <summary>Elements physically joined to <paramref name="id"/> (logical / system connections excluded).</summary>
        private static IEnumerable<long> Neighbours(Document doc, long id)
        {
            var result = new List<long>();
            var cm = ConnectorsOf(doc.GetElement(new ElementId(id)));
            if (cm == null) return result;
            foreach (Connector c in cm.Connectors)
            {
                if (c == null || c.ConnectorType == ConnectorType.Logical) continue;
                ConnectorSet refs = null;
                try { refs = c.AllRefs; }
                catch (Exception ex) { StingLog.Warn($"MEP connector refs on {id}: {ex.Message}"); }
                if (refs == null) continue;
                foreach (Connector r in refs)
                {
                    if (r?.Owner == null || r.ConnectorType == ConnectorType.Logical) continue;
                    long o = r.Owner.Id.Value;
                    if (o != id) result.Add(o);
                }
            }
            return result;
        }

        private static bool IsJoint(Document doc, long id)
        {
            var e = doc.GetElement(new ElementId(id));
            return e is FamilyInstance && e.Category != null && JointCats.Contains(e.Category.Id.Value);
        }

        // ── Run chain ──

        private sealed class Stop
        {
            public Reference Ref;
            public XYZ Point;
        }

        /// <summary>
        /// One chain per straight line of the run (MepRunPlanning.CollinearGroups):
        /// fitting centre planes that cross the line, then the free pipe ends, merged
        /// where they coincide; the witness line runs along the line, offset across it
        /// in the view plane.
        /// </summary>
        private static void EmitRunChains(Document doc, View view, List<MEPCurve> curves, List<Element> joints,
            DimensionType dimType, HashSet<string> stamped, AutoAnnotationRule rule, AnnotationResult result,
            ref int tooShort)
        {
            var viewDir = view.ViewDirection.Normalize();
            var lines = new Dictionary<long, (MEPCurve C, Line L)>();
            var segs = new List<MepSeg>();
            foreach (var c in curves)
            {
                if (!(c.Location is LocationCurve lc) || !(lc.Curve is Line ln)) continue;
                // A straight running into the view (a riser on plan) has no length to chain.
                if (Math.Abs(ln.Direction.Normalize().DotProduct(viewDir)) > 1 - ParallelTol) continue;
                var a = ToViewPlane(ln.GetEndPoint(0), view); var b = ToViewPlane(ln.GetEndPoint(1), view);
                lines[c.Id.Value] = (c, ln);
                segs.Add(new MepSeg(c.Id.Value, a.U, a.V, b.U, b.V));
            }

            foreach (var group in MepRunPlanning.CollinearGroups(segs))
            {
                var members = group.Select(id => lines[id]).ToList();
                // One stamp per line, keyed by its lowest-id straight — skip when ANY
                // straight of the line already carries this pass's chain.
                if (rule?.SkipIfTagged != false && members.Any(m => stamped.Contains(m.C.UniqueId)))
                { result.Skipped++; continue; }

                var axis = members.OrderByDescending(m => m.L.Length).First().L.Direction.Normalize();
                var memberIds = new HashSet<long>(members.Select(m => m.C.Id.Value));

                // Preferred stops first: ChainStops keeps the earlier of two coincident ones.
                var stops = new List<Stop>();
                foreach (var j in joints)
                {
                    if (!Neighbours(doc, j.Id.Value).Any(memberIds.Contains)) continue;
                    var s = FittingCentreStop(j, axis);
                    if (s != null) stops.Add(s);
                }
                foreach (var m in members)
                    stops.AddRange(FreeEndStops(doc, view, m.C, m.L, joints));
                if (stops.Count < 2)
                    foreach (var m in members) stops.AddRange(EndStops(view, m.C));   // nothing better: every end

                var keep = MepRunPlanning.ChainStops(stops.Select(s => s.Point.DotProduct(axis)).ToList());
                if (keep.Count < 2) { tooShort++; continue; }

                var refs = new ReferenceArray();
                foreach (var i in keep) refs.Append(stops[i].Ref);

                double first = stops[keep[0]].Point.DotProduct(axis);
                double last  = stops[keep[keep.Count - 1]].Point.DotProduct(axis);
                if (last - first < MinDimFt) { tooShort++; continue; }

                var across = viewDir.CrossProduct(axis);
                if (across.GetLength() < 1e-9) { tooShort++; continue; }
                across = across.Normalize();
                var basePt = members[0].L.GetEndPoint(0);
                var p0 = basePt + axis * (first - basePt.DotProduct(axis)) + across * (RunOffsetMm / DimensionStrategy.MmPerFt);
                var line = Line.CreateBound(p0, p0 + axis * (last - first));

                var host = members.OrderBy(m => m.C.Id.Value).First().C;
                if (Emit(doc, view, line, refs, dimType, result, $"MEP run at {host.Id}",
                        AnnotationProvenance.DimMepRun, host))
                    stamped.Add(host.UniqueId);
            }
        }

        /// <summary>The fitting / accessory's centre plane perpendicular to <paramref name="axis"/>, or null.</summary>
        private static Stop FittingCentreStop(Element joint, XYZ axis)
        {
            if (!(joint is FamilyInstance fi) || !(fi.Location is LocationPoint lp)) return null;
            try
            {
                var tf = fi.GetTransform();
                // CenterLeftRight is the plane normal to BasisX; CenterFrontBack to BasisY.
                var pick = Math.Abs(tf.BasisX.Normalize().DotProduct(axis)) > 1 - ParallelTol
                    ? FamilyInstanceReferenceType.CenterLeftRight
                    : Math.Abs(tf.BasisY.Normalize().DotProduct(axis)) > 1 - ParallelTol
                        ? FamilyInstanceReferenceType.CenterFrontBack
                        : (FamilyInstanceReferenceType?)null;
                if (pick == null) return null;
                var refs = fi.GetReferences(pick.Value);
                if (refs == null || refs.Count == 0) return null;
                return new Stop { Ref = refs[0], Point = lp.Point };
            }
            catch (Exception ex) { StingLog.Warn($"MEP fitting centre {joint.Id}: {ex.Message}"); return null; }
        }

        /// <summary>Ends of a straight not joined to a fitting of the run — open ends, equipment, terminals.</summary>
        private static IEnumerable<Stop> FreeEndStops(Document doc, View view, MEPCurve c, Line ln, List<Element> joints)
        {
            var jointIds = new HashSet<long>(joints.Select(j => j.Id.Value));
            var ends = EndStops(view, c);
            if (ends.Count < 2) return Enumerable.Empty<Stop>();
            var free = new List<Stop>();
            foreach (var end in ends)
            {
                bool joined = false;
                try
                {
                    foreach (Connector con in c.ConnectorManager.Connectors)
                    {
                        if (con == null || con.ConnectorType == ConnectorType.Logical) continue;
                        if (con.Origin.DistanceTo(end.Point) > 0.01) continue;
                        foreach (Connector r in con.AllRefs)
                            if (r?.Owner != null && jointIds.Contains(r.Owner.Id.Value)) joined = true;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"MEP free end {c.Id}: {ex.Message}"); }
                if (!joined) free.Add(end);
            }
            return free;
        }

        /// <summary>
        /// The two end-point references of a run's centreline. Revit exposes them on
        /// the centreline curve of the element's geometry (ComputeReferences, with
        /// non-visible objects, since the centreline is hidden at fine detail).
        /// </summary>
        private static List<Stop> EndStops(View view, MEPCurve c)
        {
            var stops = new List<Stop>();
            var line = CentrelineOf(c, view);
            if (line == null) return stops;
            for (int i = 0; i < 2; i++)
            {
                Reference r = null;
                try { r = line.GetEndPointReference(i); }
                catch (Exception ex) { StingLog.Warn($"MEP end reference {c.Id}/{i}: {ex.Message}"); }
                if (r != null) stops.Add(new Stop { Ref = r, Point = line.GetEndPoint(i) });
            }
            return stops;
        }

        private static Line CentrelineOf(MEPCurve c, View view)
        {
            try
            {
                var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, View = view };
                var geo = c.get_Geometry(opt);
                if (geo == null) return null;
                foreach (GeometryObject go in geo)
                    if (go is Line l && l.Reference != null) return l;
            }
            catch (Exception ex) { StingLog.Warn($"MEP centreline {c.Id}: {ex.Message}"); }
            return null;
        }

        /// <summary>A point flattened onto the view plane's 2D frame (RightDirection, UpDirection).</summary>
        private static UV ToViewPlane(XYZ p, View view)
            => new UV(p.DotProduct(view.RightDirection), p.DotProduct(view.UpDirection));

        // ── Grid drop ──

        /// <summary>
        /// Dimension one straight to the nearest grid parallel to it: the grid and the
        /// straight's centreline are parallel references, so the line runs ACROSS
        /// them (from the straight to the grid), offset along the straight.
        /// Returns false when the straight has no such grid.
        /// </summary>
        private static bool EmitGridDrop(Document doc, View view, MEPCurve el,
            List<ViewGridLine> grids, DimensionType dimType, AnnotationResult result)
        {
            if (!(el.Location is LocationCurve lc) || !(lc.Curve is Line pipe)) return false;
            var viewDir = view.ViewDirection.Normalize();
            var d = pipe.Direction.Normalize();
            if (Math.Abs(d.DotProduct(viewDir)) > 1 - ParallelTol) return false;   // running into the view

            var mid = (pipe.GetEndPoint(0) + pipe.GetEndPoint(1)) * 0.5;
            ViewGridLine best = null; XYZ bestFoot = null; double bestDist = double.MaxValue;
            foreach (var g in grids)
            {
                var gd = g.Line.Direction.Normalize();
                // Parallel in the view plane: the grid's direction is the pipe's, either way round.
                var cross = gd.CrossProduct(d);
                if (Math.Abs(cross.DotProduct(viewDir)) > ParallelTol) continue;
                // Foot of the perpendicular from the pipe midpoint, in the view plane.
                var o = g.Line.Origin;
                var foot = o + gd * (mid - o).DotProduct(gd);
                var v = foot - mid;
                v -= viewDir * v.DotProduct(viewDir);
                double dist = v.GetLength();
                if (dist < MinDimFt || dist > MaxGridDropFt || dist >= bestDist) continue;
                best = g; bestDist = dist; bestFoot = mid + v;
            }
            if (best == null) return false;

            var pipeRef = CentrelineOf(el, view)?.Reference;
            if (pipeRef == null)
            {
                result.Warnings.Add($"MEP grid-drop dim: {el.Id} exposes no centreline reference — skipped.");
                return true;   // reported here, not as "no grid"
            }

            var refArr = new ReferenceArray();
            refArr.Append(best.Ref);
            refArr.Append(pipeRef);

            var shift = d * (GridDropOffsetMm / DimensionStrategy.MmPerFt);
            var line = Line.CreateBound(mid + shift, bestFoot + shift);
            Emit(doc, view, line, refArr, dimType, result, $"MEP {el.Id} → grid {best.Name}",
                AnnotationProvenance.DimMepGridDrop, el);
            return true;
        }

        // ── Shared ──

        private static bool Emit(Document doc, View view, Line line, ReferenceArray refs,
            DimensionType dimType, AnnotationResult result, string label, string producer, Element host)
        {
            try
            {
                var dim = dimType != null
                    ? doc.Create.NewDimension(view, line, refs, dimType)
                    : doc.Create.NewDimension(view, line, refs);
                if (dim == null) { result.Warnings.Add($"NewDimension returned null for {label}."); return false; }
                result.DimsPlaced++;
                StingAnnotationProvenanceSchema.Stamp(dim, producer, AnnotationProvenance.Key(host.UniqueId));
                return true;
            }
            catch (Exception ex) { result.Warnings.Add($"NewDimension {label}: {ex.Message}"); return false; }
        }

        /// <summary>UniqueIds of the hosts this view's <paramref name="producer"/> dimensions were stamped for.</summary>
        private static HashSet<string> StampedHosts(Document doc, View view, string producer)
            => new HashSet<string>(
                StingAnnotationProvenanceSchema.Index(doc, view, typeof(Dimension), producer).Keys
                    .Select(AnnotationProvenance.HostOf)
                    .Where(u => !string.IsNullOrEmpty(u)),
                StringComparer.Ordinal);
    }
}
