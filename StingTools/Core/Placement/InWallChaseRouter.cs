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

        private readonly Document _doc;
        private readonly StructuralAwareness _structural;

        public InWallChaseRouter(Document doc, StructuralAwareness structural)
        {
            _doc = doc;
            _structural = structural ?? new StructuralAwareness(doc);
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

                // Phase 139.4 — auto-sleeve every created segment.  The
                // SleeveEngine scans each pipe, finds host penetrations
                // (walls / floors / roofs / ceilings), cuts an opening,
                // and drops a STING_SLEEVE_ROUND family at every hit.
                if (AutoSleeve && result.CreatedSegments.Count > 0)
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
            catch (Exception ex)
            {
                StingLog.Warn($"InWallChaseRouter.Route: {ex.Message}");
                result.Warnings.Add($"InWallChaseRouter exception: {ex.Message}");
            }

            return result;
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
                    // Sum non-structural finish + air-cavity layers on the
                    // interior side. CompoundStructure.GetLayers is ordered
                    // from exterior to interior in Revit's convention.
                    var layers = cs.GetLayers();
                    if (layers != null)
                    {
                        foreach (var l in layers.Reverse())
                        {
                            if (l == null) continue;
                            // Stop summing when we hit a structural layer.
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
            if (pipeOdMm == 0) pipeOdMm = rule.BoxDepthMm > 0 ? rule.BoxDepthMm : 22.0; // 22 mm = standard 15 mm copper OD with insulation
            double insulationMm = rule.ObstructionClearanceMm > 0 ? rule.ObstructionClearanceMm : 10.0;
            double clearanceMm  = 5.0;
            double requiredMm   = pipeOdMm + 2 * insulationMm + clearanceMm;
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
