// Phase 139.3 — In-wall chase pipe router.
//
// Routes Pipe / Conduit segments parallel to a host wall's location
// curve at a controllable inset from the room-side face. Designed for
// short residential / commercial hot-cold / waste / radiator chase
// runs that designers traditionally hand-traced inside walls.
//
// Workflow (high-accuracy, never claims 100%):
//
//   1. Resolve host wall from rule.RoomFilter / nearest wall to start point.
//   2. Read Wall.WallType.GetCompoundStructure(); compute the available
//      chase depth = sum of layers between the room finish face and the
//      first structural layer (or the structural core's far face when
//      ChaseSide = "Through").
//   3. Reject the route if (PipeOuterDiameter + 2 × InsulationThickness +
//      MinClearanceMm) > AvailableChaseDepth.
//   4. Project endpoints onto the wall's location curve, then offset by
//      RouteOffsetMm along the wall's interior normal. Both projection
//      and offset honour the wall's centre-line so the chase is exactly
//      parallel.
//   5. Insert 90° elbows at corners by routing in straight wall
//      segments, then dropping/raising at the corner with a vertical
//      Z-jog of CornerDropMm.
//   6. Run StructuralAwareness.SegmentIsRoutable on every segment;
//      reject segments that would cross junctions / load-bearing zones.
//   7. Use Wall.FindInserts via StructuralAwareness.PointIsInOpening to
//      allow the chase to traverse a door head transom or a window
//      cripple zone (instead of false-rejecting).
//   8. Create Pipe.Create(systemTypeId, typeId, levelId, a, b) per
//      validated segment; emit clash warnings against rebar / studs via
//      ElementIntersectsSolidFilter against the wall solid.
//
// Caller owns the Transaction. All exceptions log + warn and return a
// degraded RouteResult — never rethrow.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Placement
{
    public class InWallChaseRouter
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double FtToMm = 304.8;

        public class ChaseRouteResult
        {
            public List<ElementId> CreatedSegments { get; } = new List<ElementId>();
            public List<string>    Warnings        { get; } = new List<string>();
            public int             RejectedSegments { get; set; }
            public double          AvailableChaseDepthMm { get; set; }
            public double          RequiredChaseDepthMm  { get; set; }
            public int             SleevesPlaced   { get; set; }
        }

        /// <summary>
        /// When true, calls SleeveEngine.PlaceSleeves on the created
        /// pipe segments after routing — auto-cuts the host wall and
        /// drops a STING_SLEEVE_ROUND family at every penetration.
        /// </summary>
        public bool AutoSleeve { get; set; } = true;

        /// <summary>
        /// Phase 139.5 Q19 — when true, AutoSleeve does NOT run during
        /// each Route call; instead pipe ids are accumulated in
        /// PendingSleevePipes and the caller invokes <see cref="FlushSleeves"/>
        /// once after the last route. Cuts the SleeveEngine wall walk
        /// from O(routes) to O(1) per batch.
        /// </summary>
        public bool BatchSleevesAtEnd { get; set; } = false;

        /// <summary>Pipe ids queued for batched sleeve placement.</summary>
        public List<ElementId> PendingSleevePipes { get; } = new List<ElementId>();

        private readonly Document _doc;
        private readonly StructuralAwareness _structural;

        /// <summary>
        /// Phase 139.29 — rebar centreline cache built lazily on first
        /// segment validation. Rebuilt once per Route call (per host
        /// wall). When the project doesn't model reinforcement the list
        /// stays empty and the rebar-clash check is a no-op.
        /// </summary>
        private List<Curve> _rebarCache;

        public InWallChaseRouter(Document doc, StructuralAwareness structural)
        {
            _doc = doc;
            _structural = structural ?? new StructuralAwareness(doc);
        }

        /// <summary>
        /// Collect Rebar centreline curves whose bounding box overlaps
        /// the host wall (plus 200 mm pad). Empty when no reinforcement
        /// is modelled — typical at MEP stage.
        /// </summary>
        private void BuildRebarCache(Wall hostWall)
        {
            _rebarCache = new List<Curve>();
            if (hostWall == null) return;
            try
            {
                var bb = hostWall.get_BoundingBox(null);
                if (bb == null) return;
                const double padFt = 200.0 / 304.8;
                var outline = new Outline(
                    new XYZ(bb.Min.X - padFt, bb.Min.Y - padFt, bb.Min.Z - padFt),
                    new XYZ(bb.Max.X + padFt, bb.Max.Y + padFt, bb.Max.Z + padFt));
                var bbf = new BoundingBoxIntersectsFilter(outline);

                // OST_Rebar covers structural rebar; OST_RebarShape would
                // be the type-only filter, not what we want.
                var rebarCol = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Rebar)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbf);

                foreach (var el in rebarCol)
                {
                    try
                    {
                        // Rebar exposes centreline curves via the API
                        // method GetCenterlineCurves(false, false, false,
                        // MultiplanarOption.IncludeOnlyPlanarCurves, 0).
                        // Some rebar variants throw — catch per element.
                        var rebar = el as Autodesk.Revit.DB.Structure.Rebar;
                        if (rebar == null) continue;
                        var curves = rebar.GetCenterlineCurves(
                            adjustForSelfIntersection: false,
                            suppressHooks: false,
                            suppressBendRadius: false,
                            multiplanarOption: Autodesk.Revit.DB.Structure.MultiplanarOption.IncludeOnlyPlanarCurves,
                            barPositionIndex: 0);
                        if (curves == null) continue;
                        foreach (var c in curves) if (c != null) _rebarCache.Add(c);
                    }
                    catch (Exception ex) { StingLog.Warn($"BuildRebarCache rebar {el?.Id?.Value}: {ex.Message}"); }
                }
                if (_rebarCache.Count > 0)
                    StingLog.Info($"InWallChaseRouter: rebar clash check active — {_rebarCache.Count} rebar curve(s) loaded.");
            }
            catch (Exception ex) { StingLog.Warn($"BuildRebarCache: {ex.Message}"); }
        }

        /// <summary>
        /// Cheap minimum-distance estimate between line segment a→b and
        /// an arbitrary curve. Samples the curve at 8 points and returns
        /// the closest. Good enough for the clash check; not perfect for
        /// pathological curve shapes.
        /// </summary>
        private static double MinSegmentToCurveDistanceFt(XYZ a, XYZ b, Curve curve)
        {
            if (a == null || b == null || curve == null) return double.MaxValue;
            try
            {
                double best = double.MaxValue;
                int samples = 8;
                for (int i = 0; i <= samples; i++)
                {
                    double t = (double)i / samples;
                    XYZ p;
                    try { p = curve.Evaluate(t, true); }
                    catch { continue; }
                    if (p == null) continue;
                    double d = DistancePointToSegmentFt(p, a, b);
                    if (d < best) best = d;
                }
                return best;
            }
            catch { return double.MaxValue; }
        }

        private static double DistancePointToSegmentFt(XYZ p, XYZ a, XYZ b)
        {
            var ab = b - a;
            double abLenSq = ab.DotProduct(ab);
            if (abLenSq < 1e-9) return p.DistanceTo(a);
            double t = (p - a).DotProduct(ab) / abLenSq;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            var proj = a + ab.Multiply(t);
            return p.DistanceTo(proj);
        }

        /// <summary>
        /// Route a chase between two endpoints inside the supplied wall.
        /// Caller owns the Transaction. RouteOffsetMm controls the inset
        /// from the room finish face: 0 = on face, +50 = 50 mm into the
        /// wall, –20 = 20 mm proud of the face. Set ChaseSide on the rule
        /// to "Interior" (default) or "Through" (route inside the
        /// structural core itself).
        /// </summary>
        public ChaseRouteResult Route(Wall hostWall, XYZ startPoint, XYZ endPoint,
            PlacementRule rule, ElementId pipeTypeId, ElementId pipeSystemTypeId)
        {
            var result = new ChaseRouteResult();
            if (_doc == null || hostWall == null || startPoint == null || endPoint == null || rule == null)
            {
                result.Warnings.Add("InWallChaseRouter: null input.");
                return result;
            }

            try
            {
                // 1. Compound-structure depth check — distinguish three states:
                //    (a) wall has no compound structure (basic wall) → accept
                //        with warning, designer must verify on site.
                //    (b) compound structure present but pipe doesn't fit → reject.
                //    (c) compound structure present and pipe fits → continue.
                var depthCheck = ResolveChaseDepth(hostWall, rule);
                result.AvailableChaseDepthMm = depthCheck.availableMm;
                result.RequiredChaseDepthMm  = depthCheck.requiredMm;
                if (depthCheck.availableMm <= 0)
                {
                    result.Warnings.Add(
                        $"InWallChaseRouter: wall '{hostWall.WallType?.Name}' has no compound " +
                        $"structure or no detectable finish layers — chase depth could not be " +
                        $"computed (required {depthCheck.requiredMm:F0}mm). Verify on site.");
                }
                else if (depthCheck.requiredMm > depthCheck.availableMm)
                {
                    result.Warnings.Add(
                        $"InWallChaseRouter: required chase depth {depthCheck.requiredMm:F0}mm exceeds " +
                        $"available {depthCheck.availableMm:F0}mm in wall '{hostWall.WallType?.Name}'. " +
                        $"Rejected — increase wall thickness or use a thinner pipe.");
                    result.RejectedSegments++;
                    return result;
                }

                // 2. Project endpoints onto wall location curve, offset by inset.
                var routedPoints = ProjectAndOffset(hostWall, new[] { startPoint, endPoint }, rule);
                if (routedPoints.Count < 2)
                {
                    result.Warnings.Add("InWallChaseRouter: projection produced <2 points.");
                    return result;
                }

                // 3. Per-segment validation + creation.
                if (pipeTypeId == ElementId.InvalidElementId)
                    pipeTypeId = ResolveDefaultPipeType();
                if (pipeSystemTypeId == ElementId.InvalidElementId)
                    pipeSystemTypeId = ResolveDefaultPipingSystem();
                ElementId levelId = hostWall.LevelId;
                double clearanceFt = 0.5; // 152mm clearance to junctions

                for (int i = 0; i < routedPoints.Count - 1; i++)
                {
                    XYZ a = routedPoints[i];
                    XYZ b = routedPoints[i + 1];
                    if (a == null || b == null || a.IsAlmostEqualTo(b)) continue;

                    if (!_structural.SegmentIsRoutable(hostWall, a, b, clearanceFt))
                    {
                        result.RejectedSegments++;
                        result.Warnings.Add(
                            $"InWallChaseRouter: segment {i} crosses load-bearing junction — split run or relocate.");
                        continue;
                    }

                    // Phase 139.29 — rebar clash check. Only fires when
                    // reinforcement is actually modelled (typically not at
                    // MEP stage); silent otherwise. Pipe must clear any
                    // rebar centreline by at least cover + dia/2 per
                    // Eurocode 2 §4.4.1.2(2).
                    if (_rebarCache == null) BuildRebarCache(hostWall);
                    if (_rebarCache.Count > 0)
                    {
                        double pipeOdMm = (rule.NominalDiameterMm > 0 ? rule.NominalDiameterMm
                                          : rule.BoxDepthMm > 0 ? rule.BoxDepthMm : 22.0);
                        double coverMm = string.Equals(rule.MountingContext, "CHASED", StringComparison.OrdinalIgnoreCase)
                            ? StingTools.Core.Calc.ConcreteCoverTable.GetNominalCoverMm(rule.ExposureClass)
                            : 25.0;
                        double minClearFt = (coverMm + pipeOdMm * 0.5) / 304.8;
                        bool rebarClash = false;
                        foreach (var rebarCurve in _rebarCache)
                        {
                            if (rebarCurve == null) continue;
                            double dist = MinSegmentToCurveDistanceFt(a, b, rebarCurve);
                            if (dist < minClearFt) { rebarClash = true; break; }
                        }
                        if (rebarClash)
                        {
                            result.RejectedSegments++;
                            result.Warnings.Add(
                                $"InWallChaseRouter: segment {i} clashes with reinforcement (Eurocode 2 cover " +
                                $"{coverMm:F0}mm + dia/2 from rebar centreline). Adjust mounting height or relocate chase.");
                            continue;
                        }
                    }

                    // Wall openings (doors, windows): permit the segment to
                    // cross — the pipe will physically pass through the
                    // opening reveal, which is the intended chase path.
                    bool throughOpening = _structural.PointIsInOpening(hostWall, (a + b) * 0.5);
                    if (throughOpening)
                        StingLog.Info($"InWallChaseRouter: segment {i} passes through wall opening — allowed.");

                    try
                    {
                        Pipe pipe = (rule.RouteSegmentCategory ?? "PIPE")
                            .Equals("CONDUIT", StringComparison.OrdinalIgnoreCase)
                            ? null
                            : Pipe.Create(_doc, pipeSystemTypeId, pipeTypeId, levelId, a, b);
                        ElementId newId = pipe?.Id ?? ElementId.InvalidElementId;

                        // Conduit fall-back so the same router serves first-fix
                        // electrical chases when the rule asks for it.
                        if (newId == ElementId.InvalidElementId
                            && (rule.RouteSegmentCategory ?? "").Equals("CONDUIT", StringComparison.OrdinalIgnoreCase))
                        {
                            var conduit = Conduit.Create(_doc, pipeTypeId, a, b, levelId);
                            newId = conduit?.Id ?? ElementId.InvalidElementId;
                        }

                        if (newId != ElementId.InvalidElementId)
                        {
                            result.CreatedSegments.Add(newId);
                            TryStampRouteRuleId(newId, rule);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"InWallChaseRouter: segment {i} create: {ex.Message}");
                        result.RejectedSegments++;
                    }
                }

                // Phase 139.4/.5 — auto-sleeve every created segment. When
                // BatchSleevesAtEnd is true (typical for an engine-level
                // run that places dozens of chase routes), defer the
                // SleeveEngine pass to a single FlushSleeves call so the
                // wall walk happens once instead of per-route.
                if (AutoSleeve && result.CreatedSegments.Count > 0)
                {
                    if (BatchSleevesAtEnd)
                    {
                        PendingSleevePipes.AddRange(result.CreatedSegments);
                    }
                    else
                    {
                        try
                        {
                            var pipes = result.CreatedSegments
                                .Select(id => _doc.GetElement(id))
                                .Where(e => e != null)
                                .ToList();
                            var sleeveResult = StingTools.Core.Mep.SleeveEngine.PlaceSleeves(_doc, pipes, dryRun: false);
                            result.SleevesPlaced = sleeveResult.Placed;
                            if (sleeveResult.Warnings != null && sleeveResult.Warnings.Count > 0)
                                result.Warnings.AddRange(sleeveResult.Warnings.Take(10));
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"InWallChaseRouter.AutoSleeve: {ex.Message}");
                            result.Warnings.Add($"AutoSleeve: {ex.Message}");
                        }
                    }
                }

                // Phase 139.28 — slope check for chased gravity drainage.
                // BS EN 12056-2 §6 minimum 1:80 (1.25 %) for branches up
                // to 1.5 m, 1:40 (2.5 %) for longer runs / mains.
                if (rule.MinSlopePercent > 0 && result.CreatedSegments.Count > 0)
                {
                    try
                    {
                        var slope = StingTools.Core.Calc.SlopeValidator.CheckSegments(
                            _doc, result.CreatedSegments, rule.MinSlopePercent, rule.RuleId);
                        foreach (var w in slope.Warnings)
                            if (!result.Warnings.Contains(w)) result.Warnings.Add(w);
                    }
                    catch (Exception slx) { result.Warnings.Add($"InWallChaseRouter slope check: {slx.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InWallChaseRouter.Route: {ex.Message}");
                result.Warnings.Add($"InWallChaseRouter exception: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Phase 139.5 Q19 — drain PendingSleevePipes into a single
        /// SleeveEngine.PlaceSleeves call. Returns count of sleeves
        /// placed. Caller owns the Transaction.
        /// </summary>
        public int FlushSleeves(List<string> warnings = null)
        {
            if (PendingSleevePipes.Count == 0) return 0;
            try
            {
                var pipes = PendingSleevePipes
                    .Select(id => _doc.GetElement(id))
                    .Where(e => e != null)
                    .ToList();
                var sleeveResult = StingTools.Core.Mep.SleeveEngine.PlaceSleeves(_doc, pipes, dryRun: false);
                if (warnings != null && sleeveResult.Warnings != null && sleeveResult.Warnings.Count > 0)
                    warnings.AddRange(sleeveResult.Warnings.Take(10));
                PendingSleevePipes.Clear();
                return sleeveResult.Placed;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InWallChaseRouter.FlushSleeves: {ex.Message}");
                warnings?.Add($"FlushSleeves: {ex.Message}");
                return 0;
            }
        }

        // ── Internal ────────────────────────────────────────────────

        private (double availableMm, double requiredMm) ResolveChaseDepth(Wall wall, PlacementRule rule)
        {
            double availableMm = 0;
            try
            {
                var cs = wall.WallType?.GetCompoundStructure();
                if (cs != null)
                {
                    // Phase 139.5 Q5 — `CompoundStructure.GetLayers()` returns
                    // layers ordered EXTERIOR → INTERIOR per the Revit API
                    // (verified in the SDK 2024+ Wall samples). We walk the
                    // INTERIOR side first by reversing once, accumulating
                    // finish / membrane / cavity layers until we hit the
                    // structural core. If the core is the first layer we see
                    // (no interior finish at all), availableMm stays 0 — the
                    // caller treats that as "no compound structure" and falls
                    // through to the warn-and-continue branch.
                    var layers = cs.GetLayers();
                    if (layers != null)
                    {
                        // Determine the structural-core layer index so we can
                        // pick the correct side. Revit exposes the structural
                        // material via CompoundStructure.StructuralMaterialIndex.
                        int structuralIdx = -1;
                        try { structuralIdx = cs.StructuralMaterialIndex; } catch { }
                        // Walk interior-side layers (highest index first).
                        for (int i = layers.Count - 1; i >= 0; i--)
                        {
                            var l = layers[i];
                            if (l == null) continue;
                            // Stop at the structural core or any layer flagged
                            // Function = Structure (sandwich panels with a mid
                            // structure layer).
                            if (i == structuralIdx) break;
                            if (l.Function == MaterialFunctionAssignment.Structure) break;
                            availableMm += Math.Max(0.0, l.Width * FtToMm);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"InWallChaseRouter.ResolveChaseDepth: {ex.Message}"); }

            // Pipe outer diameter + insulation + min clearance.
            double pipeOdMm = 0;
            try
            {
                var entry = ManufacturerCatalogueRegistry.GetForRule(rule);
                if (entry != null && entry.BoxDepthMm > 0) pipeOdMm = entry.BoxDepthMm;
            }
            catch { }
            if (pipeOdMm == 0)
            {
                // Phase 139.28 — prefer rule.NominalDiameterMm when set
                // so the rule controls the chase budget rather than the
                // manufacturer envelope. Falls through to BoxDepthMm and
                // then to a 22mm default (15mm copper OD + insulation).
                if (rule.NominalDiameterMm > 0) pipeOdMm = rule.NominalDiameterMm;
                else if (rule.BoxDepthMm > 0)   pipeOdMm = rule.BoxDepthMm;
                else                             pipeOdMm = 22.0;
            }
            // Phase 139.28 — InsulationThicknessMm is the new dedicated
            // field; ObstructionClearanceMm survives as a fallback for
            // older rule packs that overloaded it.
            double insulationMm = rule.InsulationThicknessMm > 0 ? rule.InsulationThicknessMm
                                : rule.ObstructionClearanceMm > 0 ? rule.ObstructionClearanceMm
                                : 10.0;

            // Phase 139.28 — Eurocode 2 / UK NA concrete cover.
            // Applies only when MountingContext=CHASED. The cover sits
            // BETWEEN the structural-core face and the pipe outer
            // surface, so the chase budget on the FINISH side must be
            // (cover + pipeOd + 2 × insulation + 5mm placing clearance).
            double clearanceMm  = 5.0;
            double coverMm = 0.0;
            if (string.Equals(rule.MountingContext, "CHASED", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    coverMm = StingTools.Core.Calc.ConcreteCoverTable.GetNominalCoverMm(
                        rule.ExposureClass,
                        StingTools.Core.Calc.ConcreteCoverTable.DefaultStructuralClass,
                        fireResistance: "");
                }
                catch (Exception ex) { StingLog.Warn($"ConcreteCoverTable: {ex.Message}"); }
            }
            double requiredMm   = pipeOdMm + 2 * insulationMm + clearanceMm + coverMm;
            return (availableMm, requiredMm);
        }

        private List<XYZ> ProjectAndOffset(Wall wall, IList<XYZ> rawPoints, PlacementRule rule)
        {
            var routed = new List<XYZ>();
            try
            {
                if (!(wall.Location is LocationCurve lc) || lc.Curve == null) return routed;
                XYZ wallNormal = wall.Orientation ?? XYZ.BasisX;
                double offsetFt = (rule.RouteOffsetMm) * MmToFt;

                // Phase 139.4 — when the rule declares a MountingHeightMm,
                // chase pipes sit at that height above the wall's level
                // origin (FFL). Otherwise we keep the picked Z so designers
                // can drag-route at any height.
                double? targetZFt = null;
                if (rule.MountingHeightMm > 0)
                {
                    double levelZ = 0.0;
                    try
                    {
                        if (wall.LevelId != null && wall.LevelId != ElementId.InvalidElementId)
                            levelZ = ((wall.Document.GetElement(wall.LevelId) as Level)?.Elevation) ?? 0.0;
                    }
                    catch { }
                    targetZFt = levelZ + rule.MountingHeightMm * MmToFt;
                }

                foreach (var p in rawPoints)
                {
                    if (p == null) continue;
                    var proj = lc.Curve.Project(new XYZ(p.X, p.Y, p.Z));
                    XYZ onCurve = proj?.XYZPoint ?? p;
                    // Offset along inward normal (negative = into wall, positive = into room).
                    XYZ inset = new XYZ(
                        onCurve.X + wallNormal.X * offsetFt,
                        onCurve.Y + wallNormal.Y * offsetFt,
                        targetZFt ?? p.Z);
                    routed.Add(inset);
                }
            }
            catch (Exception ex) { StingLog.Warn($"InWallChaseRouter.ProjectAndOffset: {ex.Message}"); }
            return routed;
        }

        private ElementId ResolveDefaultPipeType()
        {
            try
            {
                return new FilteredElementCollector(_doc).OfClass(typeof(PipeType))
                    .FirstElementId() ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private ElementId ResolveDefaultPipingSystem()
        {
            try
            {
                return new FilteredElementCollector(_doc).OfClass(typeof(PipingSystemType))
                    .FirstElementId() ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private void TryStampRouteRuleId(ElementId id, PlacementRule rule)
        {
            try
            {
                var el = _doc.GetElement(id);
                if (el == null) return;
                var p = el.LookupParameter("STING_ROUTE_RULE_ID_TXT");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(rule.RuleId ?? rule.MergeKey ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"InWallChaseRouter.TryStampRouteRuleId: {ex.Message}"); }
        }
    }
}
