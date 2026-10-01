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
// DTW-102: runs in loaded links the view shows are dimensioned too (they used
// to be counted and reported only). The same planning walks the LINKED
// document's connector graph (through its fittings, not its equipment);
// geometry is mapped into host coordinates with the link instance's total
// transform; each fitting-plane / pipe-end / centreline reference is made a
// link reference (Reference.CreateLinkReference) and the dimension is created
// in the host view. Provenance keys name the link instance and the linked
// element (AnnotationProvenance.LinkedHost), so re-runs are idempotent and a
// linked run never collides with a host one. A linked line whose references
// Revit will not carry through the link — or whose dimension Revit refuses —
// is counted and reported (LinkedMepTally), never dropped silently.
// Grids may be host or linked.
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

            const string label = "MEP chain dim";
            var sources = Sources(doc, view, rule, label, result);
            if (sources.Count == 0) return;

            var strategy = DimensionStrategy.Parse(pack.DimensionStrategy);
            var dimType  = DimensionStrategy.ResolveType(doc, strategy, pack.DimensionStyle);
            var stamped  = StampedHosts(doc, view, AnnotationProvenance.DimMepRun);
            int tooShort = 0;
            var linked = new LinkedMepTally();

            foreach (var (src, elements) in sources)
            {
                var byId = elements.ToDictionary(e => e.Id.Value);
                var runs = MepRunPlanning.Cluster(byId.Keys, id => Neighbours(src.Doc, id), id => IsJoint(src.Doc, id));
                foreach (var run in runs)
                {
                    try
                    {
                        var curves = run.Curves.Select(id => byId[id]).ToList();
                        var joints = run.Joints.Select(id => src.Doc.GetElement(new ElementId(id))).Where(e => e != null).ToList();
                        EmitRunChains(doc, view, src, curves, joints, dimType, stamped, rule, result, linked, ref tooShort);
                    }
                    catch (Exception ex) { result.Warnings.Add($"{label} ({src.Label}): {ex.Message}"); }
                }
            }
            if (tooShort > 0)
                result.Warnings.Add($"{label}: {tooShort} straight line(s) had fewer than two points to dimension — skipped.");
            ReportLinked(view, label, linked, result);
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

            const string label = "MEP grid-drop dim";
            var sources = Sources(doc, view, rule, label, result);
            if (sources.Count == 0) return;

            // DTW-85: host grids and the grids of loaded links the view shows — an
            // MEP model's grids usually live in the linked architectural model.
            var gridWarnings = new List<string>();
            var grids = ViewLinks.StraightGrids(doc, view, gridWarnings, out _);
            AddUnique(result.Warnings, gridWarnings);
            if (grids.Count == 0)
            {
                result.Warnings.Add("MEP grid-drop dim: view shows no straight grids, host or linked — skipped.");
                return;
            }

            var strategy = DimensionStrategy.Parse(pack.DimensionStrategy);
            var dimType  = DimensionStrategy.ResolveType(doc, strategy, pack.DimensionStyle);
            var stamped  = StampedHosts(doc, view, AnnotationProvenance.DimMepGridDrop);
            int noGrid = 0;
            var linked = new LinkedMepTally();

            foreach (var (src, elements) in sources)
            {
                foreach (var el in elements)
                {
                    try
                    {
                        if (rule?.SkipIfTagged != false && stamped.Contains(src.HostKey(el))) { result.Skipped++; continue; }
                        if (!EmitGridDrop(doc, view, src, el, grids, dimType, result, linked)) noGrid++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"{label} {src.Label}/{el.Id}: {ex.Message}"); }
                }
            }
            if (noGrid > 0)
                result.Warnings.Add($"{label}: {noGrid} run(s) have no parallel grid within {MaxGridDropFt:0} ft " +
                                    "(or sit on one, or run into the view) — not dimensioned.");
            ReportLinked(view, label, linked, result);
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
        /// DTW-102: where a pass's runs come from — the host document, or one loaded
        /// link the view shows. Elements are in their own document's coordinates; the
        /// source maps points, vectors and references into the host.
        /// </summary>
        private sealed class MepSource
        {
            public Document Doc;
            public ViewLink Link;   // null for the host
            public bool IsLinked => Link != null;
            public string Label => Link == null ? "host" : $"link '{Link.Name}'";

            public XYZ Pt(XYZ p) => Link == null ? p : Link.Transform.OfPoint(p);
            public XYZ Vec(XYZ v) => Link == null ? v : Link.Transform.OfVector(v);
            public Line LineOf(Line l) => Link == null ? l : (Line)l.CreateTransformed(Link.Transform);

            /// <summary>The provenance host: the element's UniqueId, or link instance + linked element.</summary>
            public string HostKey(Element e) => Link == null
                ? e.UniqueId
                : AnnotationProvenance.LinkedHost(Link.Instance.UniqueId, e.UniqueId);

            /// <summary>
            /// A reference the host view can dimension to: the reference itself for a host
            /// element, a link reference for a linked one. Null, with the reason, when Revit
            /// will not make the link reference.
            /// </summary>
            public Reference Ref(Reference r, string what, out string why)
            {
                why = null;
                if (r == null) { why = $"{Label}: {what} has no reference"; return null; }
                if (Link == null) return r;
                try
                {
                    var lr = r.CreateLinkReference(Link.Instance);
                    if (lr == null) why = $"{Label}: {what} — CreateLinkReference returned null";
                    return lr;
                }
                catch (Exception ex)
                {
                    why = $"{Label}: {what} — CreateLinkReference: {ex.Message}";
                    return null;
                }
            }

            /// <summary>
            /// Geometry options: the host view for host elements; the view's detail level
            /// for linked ones (Options.View must be a view of the element's own document).
            /// </summary>
            public Options GeometryOptions(View view)
            {
                var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true };
                if (Link == null) opt.View = view;
                else if (view.DetailLevel != ViewDetailLevel.Undefined) opt.DetailLevel = view.DetailLevel;
                return opt;
            }
        }

        /// <summary>
        /// The host's runs and each visible loaded link's runs, sources with none left
        /// out. A link that cannot be read is a warning (once), never a silent omission.
        /// </summary>
        private static List<(MepSource Src, List<MEPCurve> Elements)> Sources(Document doc, View view,
            AutoAnnotationRule rule, string label, AnnotationResult result)
        {
            var list = new List<(MepSource, List<MEPCurve>)>();
            var host = CollectMepCurves(doc, view, rule);
            if (host.Count > 0) list.Add((new MepSource { Doc = doc }, host));

            var warnings = new List<string>();
            foreach (var link in ViewLinks.InView(doc, view, warnings))
            {
                var els = new List<MEPCurve>();
                foreach (var bic in RuleCategories(rule))
                {
                    try
                    {
                        els.AddRange(ViewLinks.Visible(doc, view, link, bic)
                            .OfType<MEPCurve>()
                            .Where(m => m.ConnectorManager != null));
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"{label}: {bic} in link '{link.Name}' could not be read ({ex.Message}) — not dimensioned.");
                    }
                }
                els = ApplyMinSize(els, rule);
                if (els.Count > 0) list.Add((new MepSource { Doc = link.Doc, Link = link }, els));
            }
            AddUnique(result.Warnings, warnings);
            return list;
        }

        private static void AddUnique(List<string> into, IEnumerable<string> items)
        {
            foreach (var w in items)
                if (!into.Contains(w)) into.Add(w);
        }

        /// <summary>The linked-run outcome: a warning when any linked line was not dimensioned, a log line otherwise.</summary>
        private static void ReportLinked(View view, string label, LinkedMepTally linked, AnnotationResult result)
        {
            var w = linked.Warning(label);
            if (w != null) result.Warnings.Add(w);
            else if (linked.DimensionedCount > 0)
                StingLog.Info($"{label} in '{view.Name}': {linked.DimensionedCount} linked line(s) dimensioned through their link.");
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
            return ApplyMinSize(els, rule);
        }

        private static List<MEPCurve> ApplyMinSize(List<MEPCurve> els, AutoAnnotationRule rule)
        {
            // Min-size filter — pack uses mm, MEPCurve.Diameter / Width are in feet.
            if (rule?.MinSizeMm == null) return els;
            // A-2: shared size definition + gate (ElementSize / AnnotationMinSize).
            return els.Where(e => AnnotationMinSize.Keeps(
                ElementSize.SectionFt(e) * AnnotationMinSize.MmPerFt, rule.MinSizeMm)).ToList();
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
            public Reference Ref;   // host-view reference; null when a link reference was refused
            public XYZ Point;       // host coordinates
            public string Why;      // why Ref is null
        }

        /// <summary>
        /// One chain per straight line of the run (MepRunPlanning.CollinearGroups):
        /// fitting centre planes that cross the line, then the free pipe ends, merged
        /// where they coincide; the witness line runs along the line, offset across it
        /// in the view plane. All geometry is in host coordinates (DTW-102).
        /// </summary>
        private static void EmitRunChains(Document doc, View view, MepSource src, List<MEPCurve> curves,
            List<Element> joints, DimensionType dimType, HashSet<string> stamped, AutoAnnotationRule rule,
            AnnotationResult result, LinkedMepTally linked, ref int tooShort)
        {
            var viewDir = view.ViewDirection.Normalize();
            var lines = new Dictionary<long, (MEPCurve C, Line L)>();
            var segs = new List<MepSeg>();
            foreach (var c in curves)
            {
                if (!(c.Location is LocationCurve lc) || !(lc.Curve is Line local)) continue;
                var ln = src.LineOf(local);
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
                if (rule?.SkipIfTagged != false && members.Any(m => stamped.Contains(src.HostKey(m.C))))
                { result.Skipped++; continue; }

                var axis = members.OrderByDescending(m => m.L.Length).First().L.Direction.Normalize();
                var memberIds = new HashSet<long>(members.Select(m => m.C.Id.Value));

                // Preferred stops first: ChainStops keeps the earlier of two coincident ones.
                var stops = new List<Stop>();
                foreach (var j in joints)
                {
                    if (!Neighbours(src.Doc, j.Id.Value).Any(memberIds.Contains)) continue;
                    var st = FittingCentreStop(src, j, axis);
                    if (st != null) stops.Add(st);
                }
                foreach (var m in members)
                    stops.AddRange(FreeEndStops(view, src, m.C, joints));
                if (stops.Count < 2)
                    foreach (var m in members) stops.AddRange(EndStops(view, src, m.C));   // nothing better: every end

                // Positions on the line, then the ones that still have a reference. On the
                // host they are the same; through a link, a refused link reference loses one.
                int positions = MepRunPlanning.ChainStops(stops.Select(st => st.Point.DotProduct(axis)).ToList()).Count;
                var usable = stops.Where(st => st.Ref != null).ToList();
                var keep = MepRunPlanning.ChainStops(usable.Select(st => st.Point.DotProduct(axis)).ToList());
                var host = members.OrderBy(m => m.C.Id.Value).First().C;
                var outcome = MepRunPlanning.LineOutcome(positions, keep.Count);
                if (outcome == MepLineOutcome.TooShort) { tooShort++; continue; }
                if (outcome == MepLineOutcome.LinkReferencesRefused)
                {
                    linked.RefRefused(stops.FirstOrDefault(st => st.Ref == null)?.Why
                                      ?? $"{src.Label}: run at {host.Id} lost a stop");
                    continue;
                }

                var refs = new ReferenceArray();
                foreach (var i in keep) refs.Append(usable[i].Ref);

                double first = usable[keep[0]].Point.DotProduct(axis);
                double last  = usable[keep[keep.Count - 1]].Point.DotProduct(axis);
                if (last - first < MinDimFt) { tooShort++; continue; }

                var across = viewDir.CrossProduct(axis);
                if (across.GetLength() < 1e-9) { tooShort++; continue; }
                across = across.Normalize();
                var basePt = members[0].L.GetEndPoint(0);
                var p0 = basePt + axis * (first - basePt.DotProduct(axis)) + across * (RunOffsetMm / DimensionStrategy.MmPerFt);
                var line = Line.CreateBound(p0, p0 + axis * (last - first));

                var hostKey = src.HostKey(host);
                if (Emit(doc, view, line, refs, dimType, result, $"MEP run at {src.Label}/{host.Id}",
                        AnnotationProvenance.DimMepRun, hostKey, out var failure))
                {
                    stamped.Add(hostKey);
                    if (src.IsLinked) linked.Placed();
                }
                else if (src.IsLinked) linked.DimRefused(failure);
                else result.Warnings.Add(failure);
            }
        }

        /// <summary>The fitting / accessory's centre plane perpendicular to <paramref name="axis"/>, or null.</summary>
        private static Stop FittingCentreStop(MepSource src, Element joint, XYZ axis)
        {
            if (!(joint is FamilyInstance fi) || !(fi.Location is LocationPoint lp)) return null;
            try
            {
                var tf = fi.GetTransform();
                // CenterLeftRight is the plane normal to BasisX; CenterFrontBack to BasisY.
                // The basis is in the fitting's document; the axis is in the host.
                var bx = src.Vec(tf.BasisX).Normalize();
                var by = src.Vec(tf.BasisY).Normalize();
                var pick = Math.Abs(bx.DotProduct(axis)) > 1 - ParallelTol
                    ? FamilyInstanceReferenceType.CenterLeftRight
                    : Math.Abs(by.DotProduct(axis)) > 1 - ParallelTol
                        ? FamilyInstanceReferenceType.CenterFrontBack
                        : (FamilyInstanceReferenceType?)null;
                if (pick == null) return null;
                var refs = fi.GetReferences(pick.Value);
                if (refs == null || refs.Count == 0) return null;
                var r = src.Ref(refs[0], $"fitting {joint.Id} centre plane", out var why);
                return new Stop { Ref = r, Point = src.Pt(lp.Point), Why = why };
            }
            catch (Exception ex) { StingLog.Warn($"MEP fitting centre {src.Label}/{joint.Id}: {ex.Message}"); return null; }
        }

        /// <summary>Ends of a straight not joined to a fitting of the run — open ends, equipment, terminals.</summary>
        private static IEnumerable<Stop> FreeEndStops(View view, MepSource src, MEPCurve c, List<Element> joints)
        {
            var jointIds = new HashSet<long>(joints.Select(j => j.Id.Value));
            var ends = EndStops(view, src, c);
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
                        if (src.Pt(con.Origin).DistanceTo(end.Point) > 0.01) continue;
                        foreach (Connector r in con.AllRefs)
                            if (r?.Owner != null && jointIds.Contains(r.Owner.Id.Value)) joined = true;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"MEP free end {src.Label}/{c.Id}: {ex.Message}"); }
                if (!joined) free.Add(end);
            }
            return free;
        }

        /// <summary>
        /// The two end-point references of a run's centreline. Revit exposes them on
        /// the centreline curve of the element's geometry (ComputeReferences, with
        /// non-visible objects, since the centreline is hidden at fine detail). A
        /// linked end whose link reference Revit refuses is kept with a null Ref, so
        /// the line is reported rather than dimensioned without it.
        /// </summary>
        private static List<Stop> EndStops(View view, MepSource src, MEPCurve c)
        {
            var stops = new List<Stop>();
            var line = CentrelineOf(src, c, view);
            if (line == null) return stops;
            for (int i = 0; i < 2; i++)
            {
                Reference r = null;
                try { r = line.GetEndPointReference(i); }
                catch (Exception ex) { StingLog.Warn($"MEP end reference {src.Label}/{c.Id}/{i}: {ex.Message}"); }
                if (r == null) continue;
                var hr = src.Ref(r, $"run {c.Id} end {i}", out var why);
                stops.Add(new Stop { Ref = hr, Point = src.Pt(line.GetEndPoint(i)), Why = why });
            }
            return stops;
        }

        /// <summary>The centreline curve of the element's geometry, in its own document's coordinates.</summary>
        private static Line CentrelineOf(MepSource src, MEPCurve c, View view)
        {
            try
            {
                var geo = c.get_Geometry(src.GeometryOptions(view));
                if (geo == null) return null;
                foreach (GeometryObject go in geo)
                    if (go is Line l && l.Reference != null) return l;
            }
            catch (Exception ex) { StingLog.Warn($"MEP centreline {src.Label}/{c.Id}: {ex.Message}"); }
            return null;
        }

        /// <summary>A point flattened onto the view plane's 2D frame (RightDirection, UpDirection).</summary>
        private static UV ToViewPlane(XYZ p, View view)
            => new UV(p.DotProduct(view.RightDirection), p.DotProduct(view.UpDirection));

        // ── Grid drop ──

        /// <summary>
        /// Dimension one straight to the nearest grid parallel to it: the grid and the
        /// straight's centreline are parallel references, so the line runs ACROSS
        /// them (from the straight to the grid), offset along the straight. Host or
        /// linked straight, host or linked grid (DTW-102).
        /// Returns false when the straight has no such grid.
        /// </summary>
        private static bool EmitGridDrop(Document doc, View view, MepSource src, MEPCurve el,
            List<ViewGridLine> grids, DimensionType dimType, AnnotationResult result, LinkedMepTally linked)
        {
            if (!(el.Location is LocationCurve lc) || !(lc.Curve is Line local)) return false;
            var pipe = src.LineOf(local);
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

            var centreRef = CentrelineOf(src, el, view)?.Reference;
            if (centreRef == null)
            {
                result.Warnings.Add($"MEP grid-drop dim: {src.Label}/{el.Id} exposes no centreline reference — skipped.");
                return true;   // reported here, not as "no grid"
            }
            var pipeRef = src.Ref(centreRef, $"run {el.Id} centreline", out var why);
            if (pipeRef == null)
            {
                linked.RefRefused(why);   // only a link can refuse; counted in the linked report
                return true;
            }

            var refArr = new ReferenceArray();
            refArr.Append(best.Ref);
            refArr.Append(pipeRef);

            var shift = d * (GridDropOffsetMm / DimensionStrategy.MmPerFt);
            var line = Line.CreateBound(mid + shift, bestFoot + shift);
            if (Emit(doc, view, line, refArr, dimType, result, $"MEP {src.Label}/{el.Id} → grid {best.Name}",
                    AnnotationProvenance.DimMepGridDrop, src.HostKey(el), out var failure))
            {
                if (src.IsLinked) linked.Placed();
            }
            else if (src.IsLinked) linked.DimRefused(failure);
            else result.Warnings.Add(failure);
            return true;
        }

        // ── Shared ──

        /// <summary>
        /// Create the dimension and stamp it for <paramref name="hostKey"/> (a UniqueId,
        /// or AnnotationProvenance.LinkedHost for a linked run). On failure returns false
        /// with the reason; the caller reports it (a warning for a host run, the linked
        /// tally for a linked one).
        /// </summary>
        private static bool Emit(Document doc, View view, Line line, ReferenceArray refs,
            DimensionType dimType, AnnotationResult result, string label, string producer, string hostKey,
            out string failure)
        {
            failure = null;
            try
            {
                var dim = dimType != null
                    ? doc.Create.NewDimension(view, line, refs, dimType)
                    : doc.Create.NewDimension(view, line, refs);
                if (dim == null) { failure = $"NewDimension returned null for {label}."; return false; }
                result.DimsPlaced++;
                StingAnnotationProvenanceSchema.Stamp(dim, producer, AnnotationProvenance.Key(hostKey));
                return true;
            }
            catch (Exception ex) { failure = $"NewDimension {label}: {ex.Message}"; return false; }
        }

        /// <summary>
        /// Host keys this view's <paramref name="producer"/> dimensions were stamped for:
        /// element UniqueIds for host runs, AnnotationProvenance.LinkedHost for linked ones.
        /// </summary>
        private static HashSet<string> StampedHosts(Document doc, View view, string producer)
            => new HashSet<string>(
                StingAnnotationProvenanceSchema.Index(doc, view, typeof(Dimension), producer).Keys
                    .Select(AnnotationProvenance.HostOf)
                    .Where(u => !string.IsNullOrEmpty(u)),
                StringComparer.Ordinal);
    }
}
