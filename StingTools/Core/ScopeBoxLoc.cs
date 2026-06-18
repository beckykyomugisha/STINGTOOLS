using System.Collections.Generic;

namespace StingTools.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (A4) — scope-box LOC boundary (pure geometry).
    //
    // A scope box named STING-LOC::<locCode> (e.g. STING-LOC::BLD2) declares a
    // building footprint. Snapshotted as plan (XY) extents so containment is a
    // cheap point-in-rectangle test ignoring Z. Revit-free so the most-specific-
    // box selection can be unit-tested; SpatialAutoDetect builds the index from
    // Revit bounding boxes and feeds it here.
    //
    // STING-LOC scope boxes MUST be drawn UNROTATED — the index stores the box's
    // axis-aligned bounding extents, so a rotated box is treated as its AABB
    // envelope (larger than the drawn box).
    // ─────────────────────────────────────────────────────────────────────────
    public class ScopeBoxLoc
    {
        public string Loc;
        public double MinX, MinY, MaxX, MaxY;

        public bool Contains(double x, double y)
            => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        /// <summary>Plan area in the stored unit — used to pick the most specific
        /// (smallest) containing box when boxes overlap.</summary>
        public double Area
        {
            get
            {
                double w = MaxX - MinX, h = MaxY - MinY;
                return (w > 0 ? w : 0) * (h > 0 ? h : 0);
            }
        }

        /// <summary>
        /// The smallest-area scope box whose plan rectangle contains (x, y), or
        /// null when none do. Smallest wins so nested boxes resolve to the most
        /// specific building; ties resolve to the first in input order
        /// (deterministic).
        /// </summary>
        public static ScopeBoxLoc SmallestContaining(IEnumerable<ScopeBoxLoc> boxes, double x, double y)
        {
            ScopeBoxLoc best = null;
            if (boxes == null) return null;
            foreach (var b in boxes)
            {
                if (b == null || !b.Contains(x, y)) continue;
                if (best == null || b.Area < best.Area) best = b;
            }
            return best;
        }
    }
}
