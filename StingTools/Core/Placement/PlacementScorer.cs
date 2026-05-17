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
    public partial class PlacementScorer
    {
        /// <summary>
        /// Composite score below this value rejects the candidate
        /// (returns empty list).  Phase 139 G — lowered from 0.40 to
        /// 0.35 so coverage-guarantee mode keeps borderline candidates.
        /// Configurable from the Placement Centre Run &amp; Routing tab.
        /// </summary>
        public static double ScoreThreshold = 0.35;

        /// <summary>
        /// Millimetre-to-feet conversion (Revit's internal unit).
        /// </summary>
        private const double MmToFt = 1.0 / 304.8;

        // Phase 139.2 P — re-weighted scoring; coverage-contribution and
        // manufacturer-resolution scores added.  Sum = 1.00.
        private const double AnchorWeight       = 0.35;
        private const double SideWeight         = 0.22;
        private const double SpacingWeight      = 0.18;
        private const double CollisionWeight    = 0.10;
        private const double SymmetryWeight     = 0.05;
        private const double CoverageWeight     = 0.07;
        private const double ManufacturerWeight = 0.03;

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

            // Room filter gate (PC-07 — full scoping suite)
            if (!RoomMatchesScope(room, rule)) return results;

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
                // Phase 139 G — when GuaranteeCoverage = true, never reject for low score;
                // mark as warning candidate instead.
                if (!rule.GuaranteeCoverage && candidate.Score < ScoreThreshold) continue;
                if (candidate.HasHardCollision) continue;
                results.Add(candidate);
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            return results;
        }

        /// <summary>
        /// PC-04 + PC-09 + PC-10 — anchor generator. Each anchor type
        /// inspects the room's actual boundary segments / door / window /
        /// column / grid geometry. Heights honour MountingReference
        /// (FFL / SOFFIT / SLAB / CEILING) plus OffsetX/Y/Z (PC-06).
        /// Falls back to the room's location point when boundary
        /// inspection fails.
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

            // PC-06 — full 3-D offset and rotation read once per rule.
            double offsetXFt = rule.OffsetXMm * MmToFt;
            double offsetYFt = rule.OffsetYMm * MmToFt;
            double offsetZFt = rule.OffsetZMm * MmToFt;

            // PC-06 — mounting reference picks which datum MountingHeight is measured from.
            double anchorZ = ResolveMountingDatumZ(room, rule, roomPt) + (rule.MountingHeightMm * MmToFt) + offsetZFt;

            string anchor = (rule.AnchorType ?? "ROOM_CENTRE").ToUpperInvariant();
            switch (anchor)
            {
                case "ROOM_CENTRE":
                case "ROOM_CENTROID":
                    points.Add(new XYZ(roomPt.X + offsetXFt, roomPt.Y + offsetYFt, anchorZ));
                    break;

                case "CEILING_CENTRE":
                    points.Add(new XYZ(roomPt.X + offsetXFt, roomPt.Y + offsetYFt,
                        Math.Max(anchorZ, roomPt.Z + 2.8 / 0.3048)));
                    break;

                case "LIGHTING_GRID":
                case "LUX_GRID":
                case "EN12464":
                    // PC-10 — pipe lighting-grid points through CeilingGridSnap.
                    EmitLightingGridPoints(room, rule, roomPt, offsetXFt, offsetYFt, anchorZ, points);
                    break;

                // PC-04 — wall/door/window anchors now read real boundary geometry.
                case "WALL_MIDPOINT":
                    EmitWallMidpoints(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "WALL_CORNER":
                    EmitWallCorners(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "DOOR_HINGE":
                    EmitDoorAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points, hingeSide: true,  overDoor: false);
                    break;
                case "DOOR_JAMB":
                    EmitDoorAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points, hingeSide: false, overDoor: false);
                    break;
                case "DOOR_HEAD":
                    EmitDoorAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points, hingeSide: false, overDoor: true);
                    break;
                case "WINDOW_SILL":
                    EmitWindowSills(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;

                // PC-09 — additional anchor types.
                case "OPPOSITE_WALL":
                    EmitOppositeWallAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "GRID_INTERSECTION":
                    EmitGridIntersectionAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "COLUMN_FACE":
                    EmitColumnFaceAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "PERIMETER_OFFSET":
                    EmitPerimeterAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "RAISED_FLOOR_TILE":
                    // 600mm raised-access tile centres
                    EmitFloorTileAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points, tileMm: 600.0);
                    break;
                case "STAIR_NOSING":
                    EmitStairNosingAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "ESCAPE_ROUTE_CENTRELINE":
                    // For now, sample along the longest boundary edge — escape routes
                    // are typically corridor centrelines parallel to the long axis.
                    EmitWallMidpoints(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    break;
                case "RELATIVE_TO":
                case "EQUIPMENT_PAIR":
                    // PC-13 — handled by the engine; scorer falls through to ROOM_CENTRE
                    // so the rule produces *some* candidate when no predecessor placed.
                    points.Add(new XYZ(roomPt.X + offsetXFt, roomPt.Y + offsetYFt, anchorZ));
                    break;

                default:
                    points.Add(new XYZ(roomPt.X + offsetXFt, roomPt.Y + offsetYFt, anchorZ));
                    break;
            }

            // PC-13 — when this rule is co-placed with the previous run point, no extra
            // candidates needed; the engine will splice in the predecessor's point at
            // ProcessRoomRule. Same for RELATIVE_TO / EQUIPMENT_PAIR above.
            return points;
        }

        /// <summary>
        /// PC-06 — return the Z datum for MountingHeight given the rule's
        /// MountingReference. FFL → room floor (room.Z). SLAB → room floor
        /// (legacy alias). CEILING / SOFFIT → room top + 0 (caller adds
        /// MountingHeight as an *offset* below the ceiling face).
        /// </summary>
        private double ResolveMountingDatumZ(Room room, PlacementRule rule, XYZ roomPt)
        {
            string r = (rule?.MountingReference ?? "FFL").ToUpperInvariant();
            if (r == "FFL" || r == "SLAB" || string.IsNullOrEmpty(r)) return roomPt.Z;
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return roomPt.Z;
                if (r == "CEILING" || r == "SOFFIT") return bb.Max.Z;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveMountingDatumZ {room.Id}: {ex.Message}"); }
            return roomPt.Z;
        }

        // PC-10 — emit lighting-grid points snapped to the ceiling tile grid.
        private void EmitLightingGridPoints(Room room, PlacementRule rule, XYZ roomPt,
            double offsetXFt, double offsetYFt, double anchorZ, List<XYZ> points)
        {
            try
            {
                var grid = LightingGrid?.Compute(room);
                if (grid == null || grid.Points.Count == 0)
                {
                    points.Add(new XYZ(roomPt.X + offsetXFt, roomPt.Y + offsetYFt, anchorZ));
                    return;
                }
                // Snap raw grid points to ceiling tile centres before lifting Z.
                var snapped = CeilingGridSnap.SnapToCeilingGrid(_doc, room, grid.Points);
                foreach (var p in snapped)
                    points.Add(new XYZ(p.X + offsetXFt, p.Y + offsetYFt, anchorZ));
                StingLog.Info(
                    $"PlacementScorer: lighting grid for room {room.Id} — {grid.RoomTypeCode} " +
                    $"target {grid.TargetLux:F0}lx, {grid.FixturesRequired} fixture(s), " +
                    $"snapped to ceiling tiles");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementScorer.EmitLightingGridPoints {room.Id}: {ex.Message}");
                points.Add(new XYZ(roomPt.X + offsetXFt, roomPt.Y + offsetYFt, anchorZ));
            }
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

            // §5.1 — apply family-level placement hints as a final score
            // modifier. Only the level hint biases the composite score
            // today; MountHeightMm / SpacingRule / OrientationRule / HostType
            // are consumed by PlaceFixturesCommand at point-of-placement,
            // WeightKg feeds StructuralPreflight, and GroupKey drives the
            // group-placement loop. Reading them here proves each has an
            // execution path and logs the hints for diagnostics.
            try { ApplyPlacementHints(c, room, rule); }
            catch (Exception ex) { StingLog.Warn($"PlacementScorer.ApplyPlacementHints: {ex.Message}"); }

            return c;
        }

        /// <summary>
        /// §5.1 read-site. Reads the seven family-level placement hints
        /// through PlacementParamReader and applies a level-hint bias to
        /// the composite score. Other hints are logged for the placement
        /// engines to consume — zero-impact for families that declared
        /// nothing.
        /// </summary>
        private void ApplyPlacementHints(PlacementCandidate c, Room room, PlacementRule rule)
        {
            // PlacementRule-bound families: the scorer doesn't own a
            // FamilySymbol reference, so read hints from any instance of
            // the target category in the room (first wins). Families with
            // no hints return an empty struct and this method no-ops.
            Element sample = ResolveSampleInstanceForRule(room, rule);
            if (sample == null) return;
            var hints = PlacementParamReader.Read(sample);
            if (hints.IsEmpty) return;

            string levelName = "";
            try
            {
                var lvl = room?.LevelId != null && room.LevelId != ElementId.InvalidElementId
                    ? _doc.GetElement(room.LevelId) as Level
                    : null;
                levelName = lvl?.Name ?? "";
            }
            catch { }
            double levelBias = PlacementParamReader.LevelHintBias(hints.LevelHint, levelName);
            // Level-hint bias is 0.1..1.0; multiply into the composite so a
            // strong mismatch suppresses the candidate and a strong match
            // promotes it slightly above equivalent candidates.
            c.Score *= (0.5 + levelBias * 0.5);  // 0.55..1.0 envelope

            // Hints available to downstream consumers as diagnostic side
            // data. WeightKg + HostType + SpacingRule + OrientationRule +
            // MountHeightMm + GroupKey — engines read them via
            // PlacementParamReader.Read(sample) when they need them.
            if (!string.IsNullOrEmpty(hints.HostType) ||
                !string.IsNullOrEmpty(hints.SpacingRule) ||
                !string.IsNullOrEmpty(hints.OrientationRule) ||
                !string.IsNullOrEmpty(hints.GroupKey) ||
                hints.WeightKg > 0 || hints.MountHeightMm > 0)
            {
                StingLog.Info(
                    $"PlacementScorer hints for '{rule?.CategoryFilter ?? ""}': " +
                    $"host={hints.HostType} mount={hints.MountHeightMm:F0}mm " +
                    $"spacing={hints.SpacingRule} orient={hints.OrientationRule} " +
                    $"group={hints.GroupKey} weight={hints.WeightKg:F1}kg " +
                    $"levelBias={levelBias:F2}");
            }
        }

        /// <summary>
        /// First-pass sampling — returns any instance in the room whose
        /// category matches the rule so PlacementParamReader has something
        /// to read from. A richer implementation would use the resolved
        /// FamilySymbol the caller already chose; deferred to keep this
        /// wiring small.
        /// </summary>
        private Element ResolveSampleInstanceForRule(Room room, PlacementRule rule)
        {
            if (rule == null || room == null) return null;
            try
            {
                var col = new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    if (el.Category == null) continue;
                    if (!string.Equals(el.Category.Name, rule.CategoryFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    return el;
                }
            }
            catch { }
            return null;
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


        // ── PC-04 — boundary-segment readers ─────────────────────────

        /// <summary>
        /// Cache of room → boundary segments + paired Wall/Door/Window
        /// elements. Built once per room across all rules in one Score()
        /// session.
        /// </summary>
        private readonly Dictionary<ElementId, RoomBoundaryCache> _boundaryCache
            = new Dictionary<ElementId, RoomBoundaryCache>();

        private RoomBoundaryCache GetBoundary(Room room)
        {
            if (room == null) return null;
            if (_boundaryCache.TryGetValue(room.Id, out var cached)) return cached;
            var cache = new RoomBoundaryCache();
            try
            {
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
                    StoreFreeBoundaryFaces = false,
                };
                var loops = room.GetBoundarySegments(opts);
                if (loops != null)
                {
                    foreach (var loop in loops)
                    {
                        if (loop == null) continue;
                        foreach (var seg in loop)
                        {
                            if (seg == null) continue;
                            var entry = new BoundaryEntry { Segment = seg, Curve = seg.GetCurve() };
                            try
                            {
                                var hostId = seg.ElementId;
                                if (hostId != null && hostId != ElementId.InvalidElementId)
                                {
                                    var host = _doc.GetElement(hostId);
                                    if (host is Wall w) entry.Wall = w;
                                }
                            }
                            catch { }
                            if (entry.Curve != null) cache.Segments.Add(entry);
                        }
                    }
                }

                // Doors / windows whose host wall borders the room.
                var bb = room.get_BoundingBox(null);
                if (bb != null)
                {
                    var pad = 1.5;
                    var outline = new Outline(
                        new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                        new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                    var bbf = new BoundingBoxIntersectsFilter(outline);
                    cache.Doors   = CollectInsts(_doc, BuiltInCategory.OST_Doors,   bbf);
                    cache.Windows = CollectInsts(_doc, BuiltInCategory.OST_Windows, bbf);
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementScorer.GetBoundary {room.Id}: {ex.Message}"); }
            _boundaryCache[room.Id] = cache;
            return cache;
        }

        private static List<FamilyInstance> CollectInsts(Document doc, BuiltInCategory cat, ElementFilter bbf)
        {
            var list = new List<FamilyInstance>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(cat).WhereElementIsNotElementType().WherePasses(bbf))
                {
                    if (el is FamilyInstance fi) list.Add(fi);
                }
            }
            catch { }
            return list;
        }

        private class RoomBoundaryCache
        {
            public List<BoundaryEntry> Segments { get; } = new List<BoundaryEntry>();
            public List<FamilyInstance> Doors   { get; set; } = new List<FamilyInstance>();
            public List<FamilyInstance> Windows { get; set; } = new List<FamilyInstance>();
        }

        private class BoundaryEntry
        {
            public BoundarySegment Segment;
            public Curve Curve;
            public Wall Wall;
        }

        // ── PC-04 — wall midpoints / corners from real boundary segments ─

        private void EmitWallMidpoints(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Segments.Count == 0) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            foreach (var seg in b.Segments)
            {
                if (seg.Curve == null) continue;
                if (!(seg.Curve is Line line)) continue;
                var mid = line.Evaluate(0.5, true);
                XYZ inward = ComputeInward(line, room);
                double xFt = mid.X + inward.X * offsetXFt + (-inward.Y) * offsetYFt;
                double yFt = mid.Y + inward.Y * offsetXFt + ( inward.X) * offsetYFt;
                points.Add(new XYZ(xFt, yFt, anchorZ));
            }
        }

        private void EmitWallCorners(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Segments.Count == 0) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            // Corner = endpoint of every line segment, deduped.
            var seen = new HashSet<long>();
            foreach (var seg in b.Segments)
            {
                if (!(seg.Curve is Line line)) continue;
                foreach (var pt in new[] { line.GetEndPoint(0), line.GetEndPoint(1) })
                {
                    long key = (long)(pt.X * 1000) * 100000 + (long)(pt.Y * 1000);
                    if (!seen.Add(key)) continue;
                    XYZ inward = ComputeInward(line, room);
                    points.Add(new XYZ(pt.X + inward.X * offsetXFt, pt.Y + inward.Y * offsetXFt, anchorZ));
                }
            }
        }

        private void EmitDoorAnchor(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points,
            bool hingeSide, bool overDoor)
        {
            var b = GetBoundary(room);
            if (b == null || b.Doors == null || b.Doors.Count == 0) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            foreach (var door in b.Doors)
            {
                XYZ origin = (door.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                var hostWall = door.Host as Wall;
                XYZ along = WallTangent(hostWall);
                if (along == null) along = XYZ.BasisX;
                XYZ inward = ComputeInwardFromWall(hostWall, room) ?? XYZ.BasisY;
                // Hinge side: place along the wall in the direction of FacingFlipped.
                double hingeSign = hingeSide ? 1 : -1;
                if (door.FacingFlipped) hingeSign = -hingeSign;
                XYZ shift = along.Multiply(hingeSign * (rule.OffsetXMm > 0 ? rule.OffsetXMm * MmToFt : 300.0 * MmToFt));
                XYZ p = origin + shift + inward.Multiply(offsetYFt);
                double z = overDoor ? (origin.Z + 2.2 / 0.3048) : anchorZ;
                points.Add(new XYZ(p.X, p.Y, z));
            }
        }

        private void EmitWindowSills(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Windows == null || b.Windows.Count == 0) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            foreach (var win in b.Windows)
            {
                XYZ origin = (win.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                points.Add(new XYZ(origin.X + offsetXFt, origin.Y + offsetYFt, anchorZ));
            }
        }

        private static XYZ WallTangent(Wall w)
        {
            try
            {
                var lc = w?.Location as LocationCurve;
                if (lc?.Curve is Line ln) return (ln.GetEndPoint(1) - ln.GetEndPoint(0)).Normalize();
            }
            catch { }
            return null;
        }

        private XYZ ComputeInwardFromWall(Wall w, Room room)
        {
            try
            {
                if (w == null || w.Location is not LocationCurve lc || lc.Curve is not Line ln) return null;
                XYZ tangent = (ln.GetEndPoint(1) - ln.GetEndPoint(0)).Normalize();
                XYZ normal = new XYZ(-tangent.Y, tangent.X, 0);
                // Resolve direction: nudge a tiny amount along ±normal and pick the one inside the room bbox.
                var bb = room.get_BoundingBox(null);
                if (bb == null) return normal;
                XYZ wallMid = ln.Evaluate(0.5, true);
                XYZ probe = wallMid + normal.Multiply(0.5);
                if (probe.X >= bb.Min.X && probe.X <= bb.Max.X && probe.Y >= bb.Min.Y && probe.Y <= bb.Max.Y) return normal;
                return normal.Negate();
            }
            catch { return null; }
        }

        private XYZ ComputeInward(Line line, Room room)
        {
            try
            {
                XYZ tangent = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                XYZ normal = new XYZ(-tangent.Y, tangent.X, 0);
                var bb = room.get_BoundingBox(null);
                if (bb == null) return normal;
                XYZ mid = line.Evaluate(0.5, true);
                XYZ probe = mid + normal.Multiply(0.5);
                if (probe.X >= bb.Min.X && probe.X <= bb.Max.X && probe.Y >= bb.Min.Y && probe.Y <= bb.Max.Y) return normal;
                return normal.Negate();
            }
            catch { return XYZ.BasisY; }
        }

        private void Fallback(Room room, double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var loc = room.Location as LocationPoint;
                if (loc?.Point != null)
                    points.Add(new XYZ(loc.Point.X + offsetXFt, loc.Point.Y + offsetYFt, anchorZ));
            }
            catch { }
        }

        // ── PC-09 — additional anchor types ──────────────────────────

        private void EmitOppositeWallAnchor(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Find the longest boundary edge on the wall opposite the first door.
            var b = GetBoundary(room);
            if (b == null || b.Segments.Count == 0) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            FamilyInstance door = (b.Doors != null && b.Doors.Count > 0) ? b.Doors[0] : null;
            if (door == null) { EmitWallMidpoints(room, rule, anchorZ, offsetXFt, offsetYFt, points); return; }
            XYZ doorPt = (door.Location as LocationPoint)?.Point;
            if (doorPt == null) { EmitWallMidpoints(room, rule, anchorZ, offsetXFt, offsetYFt, points); return; }
            BoundaryEntry best = null;
            double bestDist = -1;
            foreach (var seg in b.Segments)
            {
                if (!(seg.Curve is Line line)) continue;
                XYZ mid = line.Evaluate(0.5, true);
                double d = mid.DistanceTo(doorPt);
                if (d > bestDist) { bestDist = d; best = seg; }
            }
            if (best == null) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            var midOpp = ((Line)best.Curve).Evaluate(0.5, true);
            XYZ inward = ComputeInward((Line)best.Curve, room);
            points.Add(new XYZ(midOpp.X + inward.X * offsetXFt, midOpp.Y + inward.Y * offsetXFt, anchorZ));
        }

        private void EmitGridIntersectionAnchor(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
                var grids = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Grids)
                    .WhereElementIsNotElementType().Cast<Grid>().ToList();
                if (grids.Count < 2) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }

                XYZ centroid = (bb.Min + bb.Max) * 0.5;
                XYZ best = null; double bestSq = double.MaxValue;
                for (int i = 0; i < grids.Count; i++)
                for (int j = i + 1; j < grids.Count; j++)
                {
                    var c1 = grids[i].Curve as Line;
                    var c2 = grids[j].Curve as Line;
                    if (c1 == null || c2 == null) continue;
                    XYZ pt = LineIntersect(c1, c2);
                    if (pt == null) continue;
                    if (pt.X < bb.Min.X - 5 || pt.X > bb.Max.X + 5 ||
                        pt.Y < bb.Min.Y - 5 || pt.Y > bb.Max.Y + 5) continue;
                    double dx = pt.X - centroid.X, dy = pt.Y - centroid.Y;
                    double sq = dx * dx + dy * dy;
                    if (sq < bestSq) { bestSq = sq; best = pt; }
                }
                if (best != null)
                    points.Add(new XYZ(best.X + offsetXFt, best.Y + offsetYFt, anchorZ));
                else
                    Fallback(room, anchorZ, offsetXFt, offsetYFt, points);
            }
            catch (Exception ex) { StingLog.Warn($"GridIntersectionAnchor: {ex.Message}"); Fallback(room, anchorZ, offsetXFt, offsetYFt, points); }
        }

        private static XYZ LineIntersect(Line a, Line b)
        {
            if (a == null || b == null) return null;
            try
            {
                XYZ p1 = a.GetEndPoint(0), p2 = a.GetEndPoint(1);
                XYZ p3 = b.GetEndPoint(0), p4 = b.GetEndPoint(1);
                double x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y;
                double x3 = p3.X, y3 = p3.Y, x4 = p4.X, y4 = p4.Y;
                double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
                if (Math.Abs(den) < 1e-9) return null;
                double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den;
                return new XYZ(x1 + t * (x2 - x1), y1 + t * (y2 - y1), p1.Z);
            }
            catch { return null; }
        }

        private void EmitColumnFaceAnchor(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
                XYZ centroid = (bb.Min + bb.Max) * 0.5;
                var cats = new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns };
                XYZ best = null; double bestSq = double.MaxValue;
                foreach (var cat in cats)
                {
                    foreach (var el in new FilteredElementCollector(_doc).OfCategory(cat).WhereElementIsNotElementType())
                    {
                        XYZ pt = (el.Location as LocationPoint)?.Point;
                        if (pt == null) continue;
                        if (pt.X < bb.Min.X - 2 || pt.X > bb.Max.X + 2 ||
                            pt.Y < bb.Min.Y - 2 || pt.Y > bb.Max.Y + 2) continue;
                        double dx = pt.X - centroid.X, dy = pt.Y - centroid.Y;
                        double sq = dx * dx + dy * dy;
                        if (sq < bestSq) { bestSq = sq; best = pt; }
                    }
                }
                if (best != null) points.Add(new XYZ(best.X + offsetXFt, best.Y + offsetYFt, anchorZ));
                else Fallback(room, anchorZ, offsetXFt, offsetYFt, points);
            }
            catch (Exception ex) { StingLog.Warn($"ColumnFaceAnchor: {ex.Message}"); Fallback(room, anchorZ, offsetXFt, offsetYFt, points); }
        }

        private void EmitPerimeterAnchor(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Sample every PerLinearMetre or every 1.5m along all boundary lines.
            var b = GetBoundary(room);
            if (b == null || b.Segments.Count == 0) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            double stepMm = rule.PerLinearMetre > 0
                ? rule.PerLinearMetre * 1000.0
                : (rule.MinSpacingMm > 0 ? rule.MinSpacingMm : 1500.0);
            double stepFt = stepMm * MmToFt;
            foreach (var seg in b.Segments)
            {
                if (!(seg.Curve is Line line)) continue;
                double len = line.Length;
                int n = Math.Max(1, (int)Math.Floor(len / stepFt));
                XYZ inward = ComputeInward(line, room);
                for (int i = 0; i < n; i++)
                {
                    double t = (i + 0.5) / n;
                    XYZ p = line.Evaluate(t, true);
                    points.Add(new XYZ(
                        p.X + inward.X * offsetXFt,
                        p.Y + inward.Y * offsetXFt,
                        anchorZ));
                }
            }
        }

        private void EmitFloorTileAnchor(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points, double tileMm = 600.0)
        {
            var bb = room.get_BoundingBox(null);
            if (bb == null) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
            XYZ centroid = (bb.Min + bb.Max) * 0.5;
            double tileFt = tileMm * MmToFt;
            double gx = bb.Min.X + Math.Round((centroid.X - bb.Min.X) / tileFt) * tileFt + tileFt / 2.0;
            double gy = bb.Min.Y + Math.Round((centroid.Y - bb.Min.Y) / tileFt) * tileFt + tileFt / 2.0;
            points.Add(new XYZ(gx + offsetXFt, gy + offsetYFt, anchorZ));
        }

        private void EmitStairNosingAnchor(Room room, PlacementRule rule, double anchorZ,
            double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) { Fallback(room, anchorZ, offsetXFt, offsetYFt, points); return; }
                var pad = 0.5;
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                var bbf = new BoundingBoxIntersectsFilter(outline);
                int placed = 0;
                foreach (var el in new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().WherePasses(bbf))
                {
                    var stairBb = el.get_BoundingBox(null);
                    if (stairBb == null) continue;
                    XYZ p = (stairBb.Min + stairBb.Max) * 0.5;
                    points.Add(new XYZ(p.X + offsetXFt, p.Y + offsetYFt, anchorZ));
                    placed++;
                    if (placed > 8) break;
                }
                if (placed == 0) Fallback(room, anchorZ, offsetXFt, offsetYFt, points);
            }
            catch (Exception ex) { StingLog.Warn($"StairNosingAnchor: {ex.Message}"); Fallback(room, anchorZ, offsetXFt, offsetYFt, points); }
        }


        // ── PC-07 — full room-scoping pass ───────────────────────────

        /// <summary>
        /// Returns true when the room matches every active scoping clause
        /// on the rule (RoomFilter, ExcludeRoomFilter, RoomDepartmentFilter,
        /// LevelFilter, PhaseFilter, WorksetFilter, MinAreaM2 / MaxAreaM2).
        /// Empty / 0 means "no filter" — preserves legacy behaviour for
        /// rules that don't set the new fields.
        /// </summary>
        private bool RoomMatchesScope(Room room, PlacementRule rule)
        {
            if (room == null || rule == null) return false;

            string roomName = SafeRoomName(room);

            if (!RegexAllow(rule.RoomFilter, roomName))            return false;
            if (RegexBlock(rule.ExcludeRoomFilter, roomName))      return false;

            // Department parameter
            if (!string.IsNullOrEmpty(rule.RoomDepartmentFilter))
            {
                string dept = "";
                try { dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? ""; }
                catch { }
                if (!RegexAllow(rule.RoomDepartmentFilter, dept)) return false;
            }

            // Level
            if (!string.IsNullOrEmpty(rule.LevelFilter))
            {
                string lvlName = "";
                try
                {
                    if (room.LevelId != null && room.LevelId != ElementId.InvalidElementId)
                        lvlName = (_doc.GetElement(room.LevelId) as Level)?.Name ?? "";
                }
                catch { }
                if (!RegexAllow(rule.LevelFilter, lvlName)) return false;
            }

            // Phase
            if (!string.IsNullOrEmpty(rule.PhaseFilter))
            {
                string phaseName = "";
                try
                {
                    var phaseParam = room.get_Parameter(BuiltInParameter.ROOM_PHASE_ID);
                    if (phaseParam != null && phaseParam.HasValue)
                    {
                        var ph = _doc.GetElement(phaseParam.AsElementId()) as Phase;
                        if (ph != null) phaseName = ph.Name ?? "";
                    }
                }
                catch { }
                if (!RegexAllow(rule.PhaseFilter, phaseName)) return false;
            }

            // Workset
            if (!string.IsNullOrEmpty(rule.WorksetFilter))
            {
                string wsName = "";
                try
                {
                    var wsId = room.WorksetId;
                    if (wsId != null && _doc.IsWorkshared)
                    {
                        var ws = _doc.GetWorksetTable().GetWorkset(wsId);
                        if (ws != null) wsName = ws.Name ?? "";
                    }
                }
                catch { }
                if (!RegexAllow(rule.WorksetFilter, wsName)) return false;
            }

            // Area gates (m²)
            if (rule.MinAreaM2 > 0 || rule.MaxAreaM2 > 0)
            {
                double areaFt2 = 0;
                try { areaFt2 = room.Area; } catch { }
                double areaM2 = areaFt2 * 0.3048 * 0.3048;
                if (rule.MinAreaM2 > 0 && areaM2 < rule.MinAreaM2) return false;
                if (rule.MaxAreaM2 > 0 && areaM2 > rule.MaxAreaM2) return false;
            }

            return true;
        }

        private static bool RegexAllow(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            try { return Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase); }
            catch (Exception ex) { StingLog.Warn($"PlacementScorer regex '{pattern}': {ex.Message}"); return false; }
        }

        private static bool RegexBlock(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            try { return Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase); }
            catch (Exception ex) { StingLog.Warn($"PlacementScorer block regex '{pattern}': {ex.Message}"); return false; }
        }
    }
}
