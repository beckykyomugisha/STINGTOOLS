// StingTools v4 MVP — placement candidate scoring record.
//
// PlacementScorer produces a ranked List<PlacementCandidate> for every
// room-rule pair. FixturePlacementEngine consumes the top N candidates
// whose Score exceeds ScoreThreshold (default 0.40) and that pass
// CollisionFlags=0 (no hard clash).

using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// CollisionFlags bit mask. 0 = OK; bits combine for diagnostics.
    /// </summary>
    [System.Flags]
    public enum PlacementCollisionFlags
    {
        None              = 0,
        InsideWall        = 1 << 0,
        OverlapsFixture   = 1 << 1,
        OutsideRoom       = 1 << 2,
        TooCloseToDoor    = 1 << 3,
        TooCloseToWindow  = 1 << 4,
        BelowMountingMin  = 1 << 5,
        SideConstraintFail= 1 << 6,
        MinSpacingFail    = 1 << 7,
        RoomCapReached    = 1 << 8
    }

    /// <summary>
    /// Scored placement candidate. Immutable-ish: scorer fills all
    /// fields then the engine consumes the top-ranked candidates.
    /// </summary>
    public class PlacementCandidate
    {
        /// <summary>World-space placement point (Revit internal feet).</summary>
        public XYZ Position { get; set; }

        /// <summary>
        /// Composite score 0..1. 1.0 is a perfect candidate; values
        /// below PlacementScorer.ScoreThreshold (0.40) are rejected.
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Host element (wall, ceiling, face). Null when rule anchors
        /// to free space such as ROOM_CENTRE.
        /// </summary>
        public ElementId HostElementId { get; set; }

        /// <summary>
        /// Rotation about +Z in radians. 0 = aligned to host face.
        /// </summary>
        public double Rotation { get; set; }

        /// <summary>
        /// Bitfield of collision diagnostics. None = clean placement.
        /// Any non-zero value is surfaced in the result panel but only
        /// a subset cause outright rejection (see PlacementScorer).
        /// </summary>
        public int CollisionFlags { get; set; }

        /// <summary>
        /// The rule that produced this candidate; surfaced for audit.
        /// </summary>
        public PlacementRule Rule { get; set; }

        /// <summary>Room the candidate lives in.</summary>
        public ElementId RoomId { get; set; }

        /// <summary>
        /// Per-axis scoring breakdown (anchor, side, spacing, collision,
        /// symmetry). Used for diagnostic display and rule learning.
        /// </summary>
        public double AnchorScore   { get; set; }
        public double SideScore     { get; set; }
        public double SpacingScore  { get; set; }
        public double CollisionScore{ get; set; }
        public double SymmetryScore { get; set; }

        /// <summary>Human-readable diagnostic shown in the result panel.</summary>
        public string DiagnosticNote { get; set; } = "";

        public bool HasHardCollision
            => (CollisionFlags & (int)(PlacementCollisionFlags.InsideWall
                                     | PlacementCollisionFlags.OverlapsFixture
                                     | PlacementCollisionFlags.OutsideRoom
                                     | PlacementCollisionFlags.RoomCapReached)) != 0;
    }
}
