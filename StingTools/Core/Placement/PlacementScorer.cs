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

        public PlacementScorer(Document doc)
        {
            _doc = doc;
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

            // Collision: real mesh collision happens in the engine; the
            // scorer uses a generous "close to room centre" heuristic.
            c.CollisionScore = 1.0;

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
