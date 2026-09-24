// StingTools — Drawing Template Manager · Element dimensioning
//
// The three building-element dimension kinds the corporate catalogue has
// always declared and nothing implemented:
//
//   * AutoDimWallLength  — one linear dimension along each wall's length.
//   * AutoDimOpenings    — per host wall, one chain from the wall's end
//                          cap through every door / window centre to the
//                          far end cap. The drawing a joiner actually
//                          needs off a partition-layout plan.
//   * AutoDimColumnGrid  — perpendicular offset from each column centre
//                          to its nearest grid, i.e. the setting-out
//                          dimension a site engineer asks for.
//
// All three are idempotent: a wall / opening host / column that already
// carries a STING dimension referencing it is skipped, detected by what
// existing dimensions REFERENCE rather than by a stamped marker — the
// same strategy AnnotationRunner.ViewHasDimensionReferencing uses, for
// the same reason (no new shared parameter to provision).
//
// Reference strategy. Revit exposes no "wall end" reference directly, so
// end caps are recovered from the wall's solid geometry: the planar faces
// whose normal is parallel to the wall's location-curve direction. That
// needs ComputeReferences = true on the geometry options, and it is why
// EndCapReferences is the one genuinely fiddly helper here. Openings and
// columns are easier — FamilyInstance.GetReferences(CenterLeftRight)
// gives a plane perpendicular to the host wall / a column axis, which is
// exactly what a chain wants.
//
// 3D and other non-dimensionable views are skipped with a warning, never
// silently: GridDimensioner.IsDimensionable is the gate.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Storage;

namespace StingTools.Core.Drawing.Dimensioning
{
    internal static class ElementDimensioner
    {
        /// <summary>Offset of a wall-length dimension line from the wall centreline (mm).</summary>
        public const double WallDimOffsetMm = 800;
        /// <summary>Offset of an opening chain from the wall centreline (mm). Outboard of the wall dim.</summary>
        public const double OpeningDimOffsetMm = 1600;
        /// <summary>Offset of a column-to-grid dimension from the column centre (mm).</summary>
        public const double ColumnGridOffsetMm = 400;
        /// <summary>Cap on the perpendicular column→grid search (feet).</summary>
        public const double MaxGridSearchFt = 60;

        // ─────────────────────────────────────────────────────────────────
        //  AutoDimWallLength
        // ─────────────────────────────────────────────────────────────────

        public static void RunWallLength(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            if (!Gate(view, "AutoDimWallLength", result)) return;

            var walls = CollectWalls(doc, view, rule);
            if (walls.Count == 0)
            {
                result.Warnings.Add("AutoDimWallLength: no walls in view — nothing to dimension.");
                return;
            }

            var dimType = ResolveDimType(doc, pack);
            var already = DimensionedHostIndex(doc, view, result, AnnotationProvenance.DimWallLength);

            foreach (var wall in walls)
            {
                try
                {
                    if (rule?.SkipIfTagged != false && already.Contains(wall.Id)) { result.Skipped++; continue; }
                    if (!TryWallAxis(wall, out var start, out var end, out var dir)) continue;

                    // Minimum-length gate so a plan isn't buried in 100mm
                    // dimensions for every stub and reveal.
                    double lengthFt = (end - start).GetLength();
                    if (rule?.MinSizeMm.HasValue == true &&
                        lengthFt * DimensionStrategy.MmPerFt < rule.MinSizeMm.Value)
                    { result.Skipped++; continue; }

                    var caps = EndCapReferences(wall, dir, result);
                    if (caps.Count < 2)
                    {
                        // Not a failure worth shouting about per wall — curved
                        // and stacked walls legitimately have no planar caps —
                        // but it must not be silent either.
                        result.Warnings.Add($"AutoDimWallLength: wall {wall.Id} has no pair of planar end faces (curved or joined?) — skipped.");
                        continue;
                    }

                    var refs = new ReferenceArray();
                    refs.Append(caps[0]); refs.Append(caps[1]);

                    // BuildWitnessLine runs FROM the origin it is given, so the
                    // origin is the wall start, not its midpoint — a midpoint
                    // origin overshoots the wall by half its length.
                    var line = DimensionStrategy.BuildWitnessLine(start, dir, WallDimOffsetMm, lengthFt);
                    if (Emit(doc, view, line, refs, dimType, result, $"wall {wall.Id}",
                            AnnotationProvenance.DimWallLength, wall))
                        already.Add(wall.Id);
                }
                catch (Exception ex) { result.Warnings.Add($"AutoDimWallLength wall {wall.Id}: {ex.Message}"); }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  AutoDimOpenings
        // ─────────────────────────────────────────────────────────────────

        public static void RunOpenings(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            if (!Gate(view, "AutoDimOpenings", result)) return;

            // Which opening categories participate. The rule's category
            // narrows it; "*" / empty means doors AND windows, which is what
            // every shipped type that declares this rule wants (they declare
            // it twice, once per category — the host-keyed grouping below
            // collapses that into one chain per wall either way).
            var cats = new List<BuiltInCategory>();
            var declared = rule?.Category;
            if (string.IsNullOrWhiteSpace(declared) || declared == "*")
            { cats.Add(BuiltInCategory.OST_Doors); cats.Add(BuiltInCategory.OST_Windows); }
            else
            {
                var id = ResolveBic(declared);
                if (id.HasValue) cats.Add(id.Value);
                else { result.Warnings.Add($"AutoDimOpenings: category '{declared}' is not a built-in category — skipped."); return; }
            }

            // Group openings by host wall so each wall gets ONE chain
            // carrying every opening in it, rather than one dimension per
            // opening (which is what a naive per-element loop produces and
            // what a joiner cannot read).
            var byHost = new Dictionary<ElementId, List<FamilyInstance>>();
            foreach (var bic in cats)
            {
                try
                {
                    foreach (var el in new FilteredElementCollector(doc, view.Id)
                        .OfCategory(bic).WhereElementIsNotElementType())
                    {
                        if (!(el is FamilyInstance fi)) continue;
                        var host = fi.Host;
                        if (!(host is Wall)) continue;
                        if (!byHost.TryGetValue(host.Id, out var list))
                            byHost[host.Id] = list = new List<FamilyInstance>();
                        list.Add(fi);
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"AutoDimOpenings collect {bic}: {ex.Message}"); }
            }

            if (byHost.Count == 0)
            {
                result.Warnings.Add("AutoDimOpenings: no wall-hosted doors or windows in view — nothing to dimension.");
                return;
            }

            var dimType = ResolveDimType(doc, pack);
            var already = DimensionedHostIndex(doc, view, result, null);
            // Exact: walls whose opening chain STING stamped. The reference test
            // below stays for chains placed before stamping existed.
            var stampedChains = StampedHosts(doc, view, AnnotationProvenance.DimOpeningChain);

            foreach (var kv in byHost)
            {
                try
                {
                    if (!(doc.GetElement(kv.Key) is Wall wall)) continue;
                    if (rule?.SkipIfTagged != false
                        && (stampedChains.Contains(wall.Id)
                            || (already.Contains(wall.Id) && kv.Value.All(o => already.Contains(o.Id)))))
                    { result.Skipped++; continue; }

                    if (!TryWallAxis(wall, out var start, out var end, out var dir)) continue;

                    // Openings ordered along the wall so the chain reads left
                    // to right rather than in collector order.
                    var ordered = kv.Value
                        .Select(o => (Inst: o, Pos: ProjectOnto(OriginOf(o), start, dir)))
                        .Where(t => t.Inst != null)
                        .OrderBy(t => t.Pos)
                        .ToList();

                    var refs = new ReferenceArray();
                    var caps = EndCapReferences(wall, dir, result);
                    if (caps.Count >= 1) refs.Append(caps[0]);

                    int opened = 0;
                    foreach (var t in ordered)
                    {
                        var r = CentreReference(t.Inst);
                        if (r == null)
                        {
                            result.Warnings.Add($"AutoDimOpenings: {t.Inst.Category?.Name} {t.Inst.Id} exposes no CenterLeftRight reference — omitted from the chain.");
                            continue;
                        }
                        refs.Append(r); opened++;
                    }
                    if (caps.Count >= 2) refs.Append(caps[1]);

                    if (opened == 0 || refs.Size < 2)
                    {
                        result.Warnings.Add($"AutoDimOpenings: wall {wall.Id} yielded fewer than two usable references — no chain placed.");
                        continue;
                    }

                    double lengthFt = (end - start).GetLength();
                    var line = DimensionStrategy.BuildWitnessLine(start, dir, OpeningDimOffsetMm, lengthFt);
                    if (Emit(doc, view, line, refs, dimType, result, $"openings in wall {wall.Id}",
                            AnnotationProvenance.DimOpeningChain, wall))
                    {
                        already.Add(wall.Id);
                        foreach (var t in ordered) already.Add(t.Inst.Id);
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"AutoDimOpenings host {kv.Key}: {ex.Message}"); }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  AutoDimColumnGrid
        // ─────────────────────────────────────────────────────────────────

        public static void RunColumnToGrid(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            if (!Gate(view, "AutoDimColumnGrid", result)) return;

            var cats = new List<BuiltInCategory>();
            var declared = rule?.Category;
            if (string.IsNullOrWhiteSpace(declared) || declared == "*")
            { cats.Add(BuiltInCategory.OST_StructuralColumns); cats.Add(BuiltInCategory.OST_Columns); }
            else
            {
                var id = ResolveBic(declared);
                if (id.HasValue) cats.Add(id.Value);
                else { result.Warnings.Add($"AutoDimColumnGrid: category '{declared}' is not a built-in category — skipped."); return; }
            }

            var columns = new List<FamilyInstance>();
            foreach (var bic in cats)
            {
                try
                {
                    columns.AddRange(new FilteredElementCollector(doc, view.Id)
                        .OfCategory(bic).WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>());
                }
                catch (Exception ex) { result.Warnings.Add($"AutoDimColumnGrid collect {bic}: {ex.Message}"); }
            }
            if (columns.Count == 0)
            {
                result.Warnings.Add("AutoDimColumnGrid: no columns in view — nothing to dimension.");
                return;
            }

            var grids = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Grid)).Cast<Grid>()
                .Where(g => g?.Curve is Line).ToList();
            if (grids.Count == 0)
            {
                result.Warnings.Add("AutoDimColumnGrid: view contains no straight grids — skipped.");
                return;
            }

            var dimType = ResolveDimType(doc, pack);
            var already = DimensionedHostIndex(doc, view, result, AnnotationProvenance.DimColumnGrid);

            foreach (var col in columns)
            {
                try
                {
                    if (rule?.SkipIfTagged != false && already.Contains(col.Id)) { result.Skipped++; continue; }

                    var origin = OriginOf(col);
                    if (origin == null) continue;

                    // Grids by distance, the one the column SITS on included. The
                    // old filter dropped distance ~0, so a column set out on a grid
                    // was dimensioned to the next grid over — a number that is
                    // correct and useless.
                    var ranked = grids
                        .Select(g => (G: g, D: PerpDistanceFt(origin, (Line)g.Curve)))
                        .Where(t => t.D < MaxGridSearchFt)
                        .OrderBy(t => t.D)
                        .ToList();
                    var nearest = PickSettingOutGrid(ranked);
                    if (nearest.G == null) { result.Skipped++; continue; }

                    var gridDir = ((Line)nearest.G.Curve).Direction;
                    // The dimension measures ACROSS the grid: its line runs along the
                    // grid's in-plane normal, and the column reference must be the
                    // centre plane PARALLEL to the grid — the one whose normal is
                    // that same across direction. Both used to be built from the grid
                    // direction itself, which draws the line along the grid and picks
                    // the plane perpendicular to it.
                    var across = view.ViewDirection.CrossProduct(gridDir);
                    if (across.GetLength() < 1e-9) { result.Skipped++; continue; }
                    across = across.Normalize();
                    var colRef = BestAlignedReference(col, across);
                    if (colRef == null)
                    {
                        result.Warnings.Add($"AutoDimColumnGrid: column {col.Id} exposes no usable centre reference — skipped.");
                        continue;
                    }

                    var refs = new ReferenceArray();
                    refs.Append(new Reference(nearest.G));
                    refs.Append(colRef);

                    var line = DimensionStrategy.BuildWitnessLine(origin, across, ColumnGridOffsetMm, Math.Max(nearest.D, 1.0));
                    if (Emit(doc, view, line, refs, dimType, result, $"column {col.Id} → grid {nearest.G.Name}",
                            AnnotationProvenance.DimColumnGrid, col))
                        already.Add(col.Id);
                }
                catch (Exception ex) { result.Warnings.Add($"AutoDimColumnGrid column {col.Id}: {ex.Message}"); }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Shared helpers
        // ─────────────────────────────────────────────────────────────────

        private static bool Gate(View view, string label, AnnotationResult result)
        {
            if (GridDimensioner.IsDimensionable(view)) return true;
            result.Warnings.Add(
                $"{label}: view '{view?.Name}' is not a dimensionable 2D view — skipped. " +
                "Revit's Dimension API accepts plan / section / elevation / detail / drafting views only.");
            return false;
        }

        private static DimensionType ResolveDimType(Document doc, AnnotationRulePack pack)
        {
            try
            {
                var kind = DimensionStrategy.Parse(pack?.DimensionStrategy);
                return DimensionStrategy.ResolveType(doc, kind, pack?.DimensionStyle);
            }
            catch (Exception ex) { StingLog.Warn($"ElementDimensioner.ResolveDimType: {ex.Message}"); return null; }
        }

        private static bool Emit(Document doc, View view, Line line, ReferenceArray refs,
            DimensionType dimType, AnnotationResult result, string label,
            string producer = null, Element host = null)
        {
            try
            {
                var dim = dimType != null
                    ? doc.Create.NewDimension(view, line, refs, dimType)
                    : doc.Create.NewDimension(view, line, refs);
                if (dim != null)
                {
                    result.DimsPlaced++;
                    // Provenance: the next run finds this dimension by what it is FOR,
                    // even when Revit can no longer read its references.
                    if (producer != null && host != null)
                        StingAnnotationProvenanceSchema.Stamp(dim, producer, AnnotationProvenance.Key(host.UniqueId));
                    return true;
                }
                result.Warnings.Add($"NewDimension returned null for {label}.");
            }
            catch (Exception ex) { result.Warnings.Add($"NewDimension {label}: {ex.Message}"); }
            return false;
        }

        private static List<Wall> CollectWalls(Document doc, View view, AutoAnnotationRule rule)
        {
            try
            {
                var col = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(Wall)).Cast<Wall>()
                    .Where(w => w != null);

                // A rule pinned to a category other than Walls is a config
                // error; return empty and let the runner's warning stand.
                var declared = rule?.Category;
                if (!string.IsNullOrWhiteSpace(declared) && declared != "*")
                {
                    var bic = ResolveBic(declared);
                    if (bic.HasValue && bic.Value != BuiltInCategory.OST_Walls) return new List<Wall>();
                }
                return col.ToList();
            }
            catch (Exception ex) { StingLog.Warn($"CollectWalls: {ex.Message}"); return new List<Wall>(); }
        }

        /// <summary>
        /// A rule's category, as either an <c>OST_</c> enum name or a
        /// localised display name.
        ///
        /// The shipped catalogue writes DISPLAY names — "Doors", "Windows",
        /// "Structural Columns", "Walls" — not BIC strings. An Enum.TryParse
        /// -only resolver therefore returned null for every shipped rule, and
        /// these engines would have collected nothing while reporting
        /// "category is not a built-in category". RevitCategoryTree already
        /// holds the display-name → BIC table the rest of the drawing layer
        /// uses, so both spellings resolve through the same one place.
        /// </summary>
        internal static BuiltInCategory? ResolveBic(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var k = key.Trim();
            if (Enum.TryParse<BuiltInCategory>(k, true, out var bic)) return bic;

            var byName = RevitCategoryTree.FindByDisplayName(k);
            if (byName != null && !string.IsNullOrWhiteSpace(byName.Bic)
                && Enum.TryParse<BuiltInCategory>(byName.Bic, true, out var mapped))
                return mapped;

            return null;
        }

        private static bool TryWallAxis(Wall wall, out XYZ start, out XYZ end, out XYZ dir)
        {
            start = end = dir = null;
            try
            {
                if (!(wall.Location is LocationCurve lc) || !(lc.Curve is Line ln)) return false;
                start = ln.GetEndPoint(0); end = ln.GetEndPoint(1);
                dir = ln.Direction;
                return dir != null && dir.GetLength() > 1e-9;
            }
            catch (Exception ex) { StingLog.Warn($"TryWallAxis {wall?.Id}: {ex.Message}"); return false; }
        }

        /// <summary>
        /// The wall's two end-cap face references — the planar faces whose
        /// normal runs along the wall. Revit has no API for "the end of a
        /// wall", so they come out of the solid with ComputeReferences on.
        /// Returns fewer than 2 for curved / stacked / joined-away walls,
        /// which the callers report rather than swallow.
        /// </summary>
        private static List<Reference> EndCapReferences(Wall wall, XYZ dir, AnnotationResult result)
        {
            var found = new List<(Reference R, double Dot)>();
            try
            {
                var opt = new Options
                {
                    ComputeReferences = true,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine,
                };
                var geo = wall.get_Geometry(opt);
                if (geo == null) return new List<Reference>();
                var n = dir.Normalize();
                foreach (GeometryObject go in geo)
                {
                    if (!(go is Solid solid) || solid.Faces == null) continue;
                    foreach (Face f in solid.Faces)
                    {
                        if (!(f is PlanarFace pf) || pf.Reference == null) continue;
                        double dot = Math.Abs(pf.FaceNormal.Normalize().DotProduct(n));
                        // Parallel-to-wall-direction normal ⇒ this is an end cap.
                        if (dot > 0.985) found.Add((pf.Reference, pf.Origin.DotProduct(n)));
                    }
                }
            }
            catch (Exception ex)
            {
                result?.Warnings.Add($"EndCapReferences wall {wall?.Id}: {ex.Message}");
                return new List<Reference>();
            }
            if (found.Count < 2) return found.Select(t => t.R).ToList();
            // Extreme pair along the wall axis — a wall with inserts can
            // expose more than two axis-normal faces.
            var lo = found.OrderBy(t => t.Dot).First();
            var hi = found.OrderByDescending(t => t.Dot).First();
            return new List<Reference> { lo.R, hi.R };
        }

        /// <summary>CenterLeftRight reference of a family instance — the plane a chain wants.</summary>
        private static Reference CentreReference(FamilyInstance fi)
        {
            try
            {
                var refs = fi.GetReferences(FamilyInstanceReferenceType.CenterLeftRight);
                if (refs != null && refs.Count > 0) return refs[0];
                refs = fi.GetReferences(FamilyInstanceReferenceType.CenterFrontBack);
                if (refs != null && refs.Count > 0) return refs[0];
            }
            catch (Exception ex) { StingLog.Warn($"CentreReference {fi?.Id}: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Of a family instance's two centre planes, the one whose own
        /// direction best matches <paramref name="alongDir"/> — used so a
        /// column→grid dimension picks the plane perpendicular to the grid
        /// rather than the one parallel to it (which yields a zero-length or
        /// "references are not parallel" failure).
        /// </summary>
        private const double OnGridTolFt = 1.0 / 304.8;   // 1 mm: set out ON the grid

        /// <summary>
        /// The grid a column should be dimensioned to. A column ON a grid needs no
        /// dimension in that direction, so the answer is the nearest grid in the
        /// OTHER direction — unless it is on one of those too (a grid intersection),
        /// in which case there is nothing to set out and null is returned.
        /// </summary>
        private static (Grid G, double D) PickSettingOutGrid(List<(Grid G, double D)> ranked)
        {
            if (ranked == null || ranked.Count == 0) return default;
            var first = ranked[0];
            if (first.D > OnGridTolFt) return first;
            var onDir = ((Line)first.G.Curve).Direction;
            bool Parallel((Grid G, double D) t) =>
                Math.Abs(((Line)t.G.Curve).Direction.CrossProduct(onDir).GetLength()) < 1e-3;
            var other = ranked.Where(t => !Parallel(t)).ToList();
            if (other.Count == 0 || other[0].D <= OnGridTolFt) return default;   // at an intersection
            return other[0];
        }

        private static Reference BestAlignedReference(FamilyInstance fi, XYZ alongDir)
        {
            try
            {
                var candidates = new List<Reference>();
                foreach (var t in new[] { FamilyInstanceReferenceType.CenterLeftRight,
                                          FamilyInstanceReferenceType.CenterFrontBack })
                {
                    try
                    {
                        var rs = fi.GetReferences(t);
                        if (rs != null) candidates.AddRange(rs.Where(r => r != null));
                    }
                    catch (Exception ex) { StingLog.Warn($"GetReferences {t} on {fi?.Id}: {ex.Message}"); }
                }
                if (candidates.Count == 0) return null;
                if (candidates.Count == 1) return candidates[0];

                // Score by the instance transform's basis alignment: the
                // plane normal we want is parallel to alongDir.
                var tf = fi.GetTransform();
                var n = alongDir.Normalize();
                double sx = Math.Abs(tf.BasisX.Normalize().DotProduct(n));
                double sy = Math.Abs(tf.BasisY.Normalize().DotProduct(n));
                // CenterLeftRight is normal to BasisX; CenterFrontBack to BasisY.
                return sx >= sy ? candidates[0] : candidates[candidates.Count - 1];
            }
            catch (Exception ex) { StingLog.Warn($"BestAlignedReference {fi?.Id}: {ex.Message}"); return null; }
        }

        private static XYZ OriginOf(Element el)
        {
            try
            {
                if (el?.Location is LocationPoint lp) return lp.Point;
                if (el?.Location is LocationCurve lc && lc.Curve != null)
                    return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;
                var bb = el?.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch (Exception ex) { StingLog.Warn($"OriginOf {el?.Id}: {ex.Message}"); }
            return null;
        }

        /// <summary>Signed distance of p along dir from origin — orders openings along a wall.</summary>
        internal static double ProjectOnto(XYZ p, XYZ origin, XYZ dir)
        {
            if (p == null || origin == null || dir == null) return 0;
            return (p - origin).DotProduct(dir.Normalize());
        }

        internal static double PerpDistanceFt(XYZ p, Line line)
        {
            try
            {
                var o = line.Origin; var d = line.Direction;
                var v = p - o;
                var foot = o + d * v.DotProduct(d);
                return (p - foot).GetLength();
            }
            catch (Exception ex) { StingLog.Warn($"PerpDistanceFt: {ex.Message}"); return double.MaxValue; }
        }

        /// <summary>
        /// Element ids already referenced by a dimension in this view. One
        /// scan, shared across every element in the pass. Fails OPEN (empty
        /// set ⇒ place everything) with a warning, so an index failure never
        /// silently withholds requested dimensions.
        /// </summary>
        private static HashSet<ElementId> DimensionedHostIndex(Document doc, View view, AnnotationResult result,
            string producer)
        {
            // Stamped hosts first: exact, and immune to unreadable references.
            var set = producer != null ? StampedHosts(doc, view, producer) : new HashSet<ElementId>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Dimension)).WhereElementIsNotElementType())
                {
                    if (!(el is Dimension dim)) continue;
                    try
                    {
                        if (!dim.AreReferencesAvailable) continue;
                        var refs = dim.References;
                        if (refs == null) continue;
                        foreach (Reference r in refs)
                        {
                            var host = doc.GetElement(r);
                            if (host != null && host.Id != ElementId.InvalidElementId) set.Add(host.Id);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"DimensionedHostIndex dim {dim.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                result?.Warnings.Add($"Could not index existing dimensions in '{view?.Name}' ({ex.Message}); duplicate dimensions are possible on this view.");
            }
            return set;
        }

        /// <summary>Hosts of every dimension in the view that <paramref name="producer"/> stamped.</summary>
        private static HashSet<ElementId> StampedHosts(Document doc, View view, string producer)
        {
            var set = new HashSet<ElementId>();
            foreach (var key in StingAnnotationProvenanceSchema.Index(doc, view, typeof(Dimension), producer).Keys)
            {
                var host = doc.GetElement(AnnotationProvenance.HostOf(key));
                if (host != null) set.Add(host.Id);
            }
            return set;
        }
    }
}
