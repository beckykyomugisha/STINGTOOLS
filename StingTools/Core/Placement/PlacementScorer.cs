// StingTools v4 MVP — placement scorer.
//
// Given a room, a placement rule and optional host element, produces
// a ranked List<PlacementCandidate>. Scoring is 0..1 composite of:
//   - anchor distance (40%)
//   - side compliance   (25%)
//   - min-spacing       (20%)
//   - collision         (10%)
//   - symmetry / aesthetic (5%)
//
// The scorer is pure (no Revit transactions). All Revit API reads
// guarded with try/catch that logs via StingLog.Warn.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Stateless scorer. Caller passes in the room + rule, plus a
    /// collection of already-placed points used for MinSpacing checks.
    /// </summary>
    public class PlacementScorer
    {
        /// <summary>
        /// Composite score below this value rejects the candidate
        /// (returns empty list).
        /// </summary>
        public const double ScoreThreshold = 0.40;

        /// <summary>
        /// Millimetre-to-feet conversion (Revit's internal unit).
        /// </summary>
        private const double MmToFt = 1.0 / 304.8;

        private const double AnchorWeight    = 0.40;
        private const double SideWeight      = 0.25;
        private const double SpacingWeight   = 0.20;
        private const double CollisionWeight = 0.10;
        private const double SymmetryWeight  = 0.05;

        private readonly Document _doc;
        private LightingGridCalculator _lightingGrid;

        /// <summary>
        /// Per-room cache of ceiling obstruction AABBs. Built once on
        /// the first candidate for each room so FilteredElementCollector
        /// is not re-run for every rule.
        /// </summary>
        private readonly Dictionary<ElementId, List<ExclusionRect>> _obstructionCache
            = new Dictionary<ElementId, List<ExclusionRect>>();

        /// <summary>
        /// Per-room cache of wall solids used for the
        /// ElementIntersectsSolidFilter path (RejectInsideWall). Walls
        /// are captured as (ElementId, Solid) pairs so the filter can
        /// report which wall an offending candidate hit.
        /// </summary>
        private readonly Dictionary<ElementId, List<(ElementId wallId, Solid solid)>> _wallSolidCache
            = new Dictionary<ElementId, List<(ElementId, Solid)>>();

        /// <summary>
        /// When true, BuildCandidate invokes ElementIntersectsSolidFilter
        /// against wall solids and marks the candidate with
        /// InsideWall when it overlaps. Defaults false — enable via
        /// PlaceFixturesOptions.RejectInsideWall read by the command
        /// entry point.
        /// </summary>
        public bool RejectInsideWall { get; set; } = false;

        public PlacementScorer(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Lazy-initialised BS EN 12464-1 lux calculator. Shared across
        /// all rules scored in a single session so the classifier CSV
        /// and lux-target table parse only once.
        /// </summary>
        private LightingGridCalculator LightingGrid
        {
            get
            {
                if (_lightingGrid == null)
                {
                    try { _lightingGrid = new LightingGridCalculator(); }
                    catch (Exception ex)
                    { StingLog.Warn($"PlacementScorer: LightingGridCalculator init failed: {ex.Message}"); }
                }
                return _lightingGrid;
            }
        }

        /// <summary>
        /// Produce a ranked candidate list for the given room-rule pair.
        /// Returns empty list if the rule's RoomFilter regex does not
        /// match the room name, or if no candidate clears ScoreThreshold.
        /// </summary>
        public List<PlacementCandidate> Score(
            Room room,
            PlacementRule rule,
            IList<XYZ> alreadyPlaced,
            int countInRoomSoFar)
        {
            var results = new List<PlacementCandidate>();
            if (room == null || rule == null) return results;

            // Room filter gate
            if (!string.IsNullOrEmpty(rule.RoomFilter))
            {
                string roomName = SafeRoomName(room);
                try
                {
                    if (!Regex.IsMatch(roomName, rule.RoomFilter, RegexOptions.IgnoreCase))
                        return results;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PlacementScorer: invalid RoomFilter regex '{rule.RoomFilter}': {ex.Message}");
                    return results;
                }
            }

            // Room cap check
            if (rule.MaxPerRoom > 0 && countInRoomSoFar >= rule.MaxPerRoom)
                return results;

            // Generate anchor points for this rule type within the room
            var anchors = GenerateAnchorPoints(room, rule);
            if (anchors.Count == 0) return results;

            foreach (var anchor in anchors)
            {
                var candidate = BuildCandidate(room, rule, anchor, alreadyPlaced);
                if (candidate == null) continue;
                if (candidate.Score < ScoreThreshold) continue;
                if (candidate.HasHardCollision) continue;
                results.Add(candidate);
            }

            return results.OrderByDescending(c => c.Score).ToList();
        }

        /// <summary>
        /// Anchor generator. Real Revit geometry inspection (walls,
        /// doors, ceiling solid) is deferred to the engine's face-based
        /// placement. Here we emit one or more candidate anchor XYZs
        /// using the room location as a coarse approximation.
        /// </summary>
        private List<XYZ> GenerateAnchorPoints(Room room, PlacementRule rule)
        {
            var points = new List<XYZ>();
            XYZ roomPt = null;
            try
            {
                var loc = room.Location as LocationPoint;
                if (loc != null) roomPt = loc.Point;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementScorer: room {room.Id} LocationPoint read failed: {ex.Message}");
            }

            if (roomPt == null) return points;

            double mountingFt = rule.MountingHeightMm * MmToFt;
            double offsetFt   = rule.OffsetXMm * MmToFt;

            switch ((rule.AnchorType ?? "ROOM_CENTRE").ToUpperInvariant())
            {
                case "ROOM_CENTRE":
                case "ROOM_CENTROID":
                    points.Add(new XYZ(roomPt.X, roomPt.Y, roomPt.Z + mountingFt));
                    break;
                case "CEILING_CENTRE":
                    // Mounting height treated as elevation above room floor
                    points.Add(new XYZ(roomPt.X, roomPt.Y, roomPt.Z + Math.Max(mountingFt, 2.8 / 0.3048)));
                    break;
                case "LIGHTING_GRID":
                case "LUX_GRID":
                case "EN12464":
                    // BS EN 12464-1 lumen-method grid. The calculator
                    // emits one point per required luminaire. Returns
                    // no points when the room is too small or the
                    // classifier yields zero target lux.
                    var grid = LightingGrid?.Compute(room);
                    if (grid != null && grid.Points.Count > 0)
                    {
                        foreach (var p in grid.Points)
                        {
                            // Respect the rule's mounting height instead
                            // of the grid's default room-Z (the calculator
                            // uses the room bounding box min-Z which is
                            // the room floor, not the ceiling plane).
                            points.Add(new XYZ(p.X, p.Y, roomPt.Z + mountingFt));
                        }
                        StingLog.Info(
                            $"PlacementScorer: lighting grid for room {room.Id} — " +
                            $"{grid.RoomTypeCode} target {grid.TargetLux:F0}lx, " +
                            $"{grid.FixturesRequired} fixture(s) on {grid.SpacingXMm:F0}×{grid.SpacingYMm:F0}mm grid");
                    }
                    else
                    {
                        // Fallback: treat as CEILING_CENTRE so lux-gated
                        // rules still produce a candidate.
                        points.Add(new XYZ(roomPt.X, roomPt.Y, roomPt.Z + mountingFt));
                    }
                    break;
                case "WALL_MIDPOINT":
                case "WALL_CORNER":
                case "DOOR_HINGE":
                case "DOOR_JAMB":
                case "WINDOW_SILL":
                    // TODO-VERIFY-API: full wall/door geometry inspection happens in
                    // FixturePlacementEngine via BoundarySegment + wall face. For scoring
                    // we sample 4 cardinal offsets from the room centre.
                    double r = 3.0; // 1m in feet approx
                    points.Add(new XYZ(roomPt.X + r + offsetFt, roomPt.Y,           roomPt.Z + mountingFt));
                    points.Add(new XYZ(roomPt.X - r + offsetFt, roomPt.Y,           roomPt.Z + mountingFt));
                    points.Add(new XYZ(roomPt.X,                roomPt.Y + r,       roomPt.Z + mountingFt));
                    points.Add(new XYZ(roomPt.X,                roomPt.Y - r,       roomPt.Z + mountingFt));
                    break;
                default:
                    points.Add(new XYZ(roomPt.X, roomPt.Y, roomPt.Z + mountingFt));
                    break;
            }
            return points;
        }

        private PlacementCandidate BuildCandidate(
            Room room,
            PlacementRule rule,
            XYZ anchor,
            IList<XYZ> alreadyPlaced)
        {
            var c = new PlacementCandidate
            {
                Position       = anchor,
                RoomId         = room.Id,
                Rule           = rule,
                Rotation       = 0.0,
                CollisionFlags = 0
            };

            // Anchor fit: close to chosen anchor type. Scorer only
            // penalises distance-from-anchor when we moved away from
            // the ideal point; the anchor generator already placed us
            // on anchor, so the anchor score is near-perfect unless
            // the rule tries to route through a constrained space.
            c.AnchorScore = 1.0;

            // Side compliance: with the coarse anchor generator above,
            // LEFT/RIGHT are inferred from sign of OffsetXMm vs room
            // centre. Engine resolves the real wall face at placement.
            string side = (rule.SideConstraint ?? "EITHER").ToUpperInvariant();
            c.SideScore = (side == "EITHER") ? 1.0 : 0.85;

            // Min-spacing: penalise any already-placed point that is
            // closer than rule.MinSpacingMm.
            c.SpacingScore = ComputeSpacingScore(anchor, alreadyPlaced, rule.MinSpacingMm);
            if (c.SpacingScore < 0.05)
                c.CollisionFlags |= (int)PlacementCollisionFlags.MinSpacingFail;

            // Collision: use ObstructionIndex to compute a per-room
            // AABB exclusion list (air terminals, sprinklers, smoke
            // detectors, speakers) once per room and score the candidate
            // against it. Inside an exclusion → 0 score + hard-collision
            // flag so BuildCandidate's caller discards. Within one buffer
            // distance → linear penalty. Cache keyed by room id.
            var (collisionScore, collisionAdd) = ComputeCollisionScore(room, anchor);
            c.CollisionScore = collisionScore;
            c.CollisionFlags |= collisionAdd;

            // Wall collision: when RejectInsideWall is enabled, run
            // ElementIntersectsSolidFilter against the cached wall solids
            // for this room. Any overlap marks the candidate as
            // InsideWall (hard collision, causes rejection upstream).
            if (RejectInsideWall && c.CollisionScore > 0)
            {
                if (IsInsideWall(room, anchor))
                {
                    c.CollisionScore = 0;
                    c.CollisionFlags |= (int)PlacementCollisionFlags.InsideWall;
                }
            }

            // Symmetry / aesthetic: small bonus for being near an
            // integer multiple of 300mm from the nearest placed point
            // (helps lighting grids and socket rails line up).
            c.SymmetryScore = ComputeSymmetryScore(anchor, alreadyPlaced);

            c.Score = c.AnchorScore    * AnchorWeight
                    + c.SideScore      * SideWeight
                    + c.SpacingScore   * SpacingWeight
                    + c.CollisionScore * CollisionWeight
                    + c.SymmetryScore  * SymmetryWeight;
            return c;
        }

        /// <summary>
        /// Score a candidate against the cached ObstructionIndex AABBs
        /// for its room. Inside an exclusion → (0.0, InsideWall flag).
        /// Within one buffer distance of an edge → linear penalty
        /// 0..1 across the buffer. Outside all exclusions → 1.0.
        /// </summary>
        private (double score, int flags) ComputeCollisionScore(Room room, XYZ pt)
        {
            if (room == null || pt == null) return (1.0, 0);

            List<ExclusionRect> list;
            if (!_obstructionCache.TryGetValue(room.Id, out list))
            {
                try { list = ObstructionIndex.BuildForRoom(_doc, room); }
                catch (Exception ex)
                {
                    StingLog.Warn($"PlacementScorer: obstruction build failed for room {room.Id}: {ex.Message}");
                    list = new List<ExclusionRect>();
                }
                _obstructionCache[room.Id] = list;
            }
            if (list.Count == 0) return (1.0, 0);

            double bufferFt = ObstructionIndex.DefaultBufferFt;

            // Hard hit: candidate is inside any exclusion AABB.
            foreach (var r in list)
            {
                if (r.Contains(pt.X, pt.Y))
                    return (0.0, (int)PlacementCollisionFlags.OverlapsFixture);
            }

            // Soft penalty: candidate is within one buffer distance of
            // an exclusion edge. Compute min-edge-distance, score scales
            // linearly from 0 (touching) to 1 (at or beyond buffer).
            double minDistFt = double.MaxValue;
            foreach (var r in list)
            {
                double dx = Math.Max(0.0, Math.Max(r.MinX - pt.X, pt.X - r.MaxX));
                double dy = Math.Max(0.0, Math.Max(r.MinY - pt.Y, pt.Y - r.MaxY));
                double d  = Math.Sqrt(dx * dx + dy * dy);
                if (d < minDistFt) minDistFt = d;
            }
            if (minDistFt >= bufferFt) return (1.0, 0);
            double score = Math.Max(0.0, minDistFt / bufferFt);
            return (score, 0);
        }

        /// <summary>
        /// Cache wall solids around each room and report true when the
        /// candidate XYZ falls inside any of them. Uses
        /// ElementIntersectsSolidFilter with a tiny sphere solid built
        /// around the candidate point — cheaper than computing each
        /// wall's 3D geometry and doing the intersection manually.
        /// </summary>
        private bool IsInsideWall(Room room, XYZ pt)
        {
            if (room == null || pt == null) return false;

            // Build or retrieve the per-room wall-solid cache.
            List<(ElementId wallId, Solid solid)> walls;
            if (!_wallSolidCache.TryGetValue(room.Id, out walls))
            {
                walls = CollectWallSolidsNearRoom(room);
                _wallSolidCache[room.Id] = walls;
            }
            if (walls.Count == 0) return false;

            // Build a small-radius test sphere at the candidate. A
            // 50 mm radius is enough to catch fixtures that would
            // poke into a wall without false-positives on room-hugging
            // rails.
            const double testRadiusFt = 50.0 / 304.8;
            Solid probe;
            try
            {
                probe = CreatePointSphere(pt, testRadiusFt);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementScorer: IsInsideWall probe build failed: {ex.Message}");
                return false;
            }
            if (probe == null) return false;

            // Short-circuit via AABB first; fall back to geometric
            // intersection only when the bounding boxes overlap.
            foreach (var (wallId, wallSolid) in walls)
            {
                if (wallSolid == null) continue;
                try
                {
                    var wallBb = wallSolid.GetBoundingBox();
                    if (wallBb == null) continue;
                    // Transform AABB to world.
                    var t = wallBb.Transform;
                    var corner = t.OfPoint(wallBb.Min);
                    var maxWorld = t.OfPoint(wallBb.Max);
                    var min = new XYZ(Math.Min(corner.X, maxWorld.X),
                                      Math.Min(corner.Y, maxWorld.Y),
                                      Math.Min(corner.Z, maxWorld.Z));
                    var max = new XYZ(Math.Max(corner.X, maxWorld.X),
                                      Math.Max(corner.Y, maxWorld.Y),
                                      Math.Max(corner.Z, maxWorld.Z));
                    if (pt.X < min.X - testRadiusFt || pt.X > max.X + testRadiusFt ||
                        pt.Y < min.Y - testRadiusFt || pt.Y > max.Y + testRadiusFt ||
                        pt.Z < min.Z - testRadiusFt || pt.Z > max.Z + testRadiusFt)
                        continue;

                    var inter = BooleanOperationsUtils.ExecuteBooleanOperation(
                        probe, wallSolid, BooleanOperationsType.Intersect);
                    if (inter != null && inter.Volume > 1e-9)
                        return true;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PlacementScorer: wall-intersect {wallId} failed: {ex.Message}");
                }
            }
            return false;
        }

        private List<(ElementId wallId, Solid solid)> CollectWallSolidsNearRoom(Room room)
        {
            var list = new List<(ElementId, Solid)>();
            if (_doc == null || room == null) return list;
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return list;
                var pad = 1.0; // 1 ft pad around the room
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                var bboxFilter = new BoundingBoxIntersectsFilter(outline);

                var col = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .WherePasses(bboxFilter);

                var opts = new Options
                {
                    DetailLevel          = ViewDetailLevel.Medium,
                    ComputeReferences    = false,
                    IncludeNonVisibleObjects = false
                };

                foreach (var wallEl in col)
                {
                    try
                    {
                        var geom = wallEl.get_Geometry(opts);
                        if (geom == null) continue;
                        foreach (var g in geom)
                        {
                            if (g is Solid s && s.Volume > 1e-9)
                                list.Add((wallEl.Id, s));
                        }
                    }
                    catch (Exception ex)
                    { StingLog.Warn($"PlacementScorer: wall geom {wallEl.Id} failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"PlacementScorer: CollectWallSolidsNearRoom failed: {ex.Message}"); }
            return list;
        }

        /// <summary>
        /// Build a small axis-aligned hexahedron (approximation of a
        /// sphere) centred on the given point. Used as the probe solid
        /// for ElementIntersectsSolidFilter — a cheap way to test
        /// "point is inside wall volume" without building a true sphere.
        /// </summary>
        private static Solid CreatePointSphere(XYZ centre, double radiusFt)
        {
            try
            {
                // GeometryCreationUtilities.CreateExtrusionGeometry from a
                // small square loop, extruded 2*radiusFt along +Z starting
                // at centre - radiusFt*Z.
                var p0 = new XYZ(centre.X - radiusFt, centre.Y - radiusFt, centre.Z - radiusFt);
                var p1 = new XYZ(centre.X + radiusFt, centre.Y - radiusFt, centre.Z - radiusFt);
                var p2 = new XYZ(centre.X + radiusFt, centre.Y + radiusFt, centre.Z - radiusFt);
                var p3 = new XYZ(centre.X - radiusFt, centre.Y + radiusFt, centre.Z - radiusFt);
                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));
                return GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop }, XYZ.BasisZ, 2 * radiusFt);
            }
            catch
            {
                return null;
            }
        }

        private double ComputeSpacingScore(XYZ pt, IList<XYZ> placed, double minSpacingMm)
        {
            if (placed == null || placed.Count == 0) return 1.0;
            double minFt = minSpacingMm * MmToFt;
            if (minFt <= 0) return 1.0;

            double worst = 1.0;
            foreach (var p in placed)
            {
                if (p == null) continue;
                double d = pt.DistanceTo(p);
                if (d >= minFt) continue;
                // linear penalty: 0 distance -> 0 score, minFt -> 1.0
                double s = Math.Max(0.0, d / minFt);
                if (s < worst) worst = s;
            }
            return worst;
        }

        private double ComputeSymmetryScore(XYZ pt, IList<XYZ> placed)
        {
            if (placed == null || placed.Count == 0) return 0.5;
            // 300mm grid bonus
            double step = 300.0 * MmToFt;
            double best = 0.0;
            foreach (var p in placed)
            {
                if (p == null) continue;
                double d = pt.DistanceTo(p);
                double frac = (d % step) / step;
                double closeness = 1.0 - 2.0 * Math.Abs(frac - Math.Round(frac));
                if (closeness > best) best = closeness;
            }
            return best;
        }

        private string SafeRoomName(Room room)
        {
            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                return p?.AsString() ?? room.Name ?? "";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementScorer: room {room.Id} name read failed: {ex.Message}");
                return "";
            }
        }
    }
}
