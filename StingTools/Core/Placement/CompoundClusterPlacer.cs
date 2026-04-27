// Phase 139.2 — Compound cluster placer.
//
// MK Grid Plus assemblies are mounted in a single faceplate frame but
// each module is a separate device. Rules sharing a ClusterGroupId map
// onto the same frame: the engine resolves one frame-centre point and
// this helper distributes module slots along the wall horizontal axis
// at the rule's ModulePitchMm.
//
// All inputs in Revit internal units (feet). Pitch is mm — converted
// per-call.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    public static class CompoundClusterPlacer
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>
        /// Group rules by ClusterGroupId. Rules without a ClusterGroupId
        /// are emitted as singleton groups keyed by RuleId so callers can
        /// iterate uniformly.
        /// </summary>
        public static IEnumerable<IGrouping<string, PlacementRule>> GroupByCluster(IList<PlacementRule> rules)
        {
            if (rules == null) return Array.Empty<IGrouping<string, PlacementRule>>();
            return rules
                .Where(r => r != null)
                .GroupBy(r => string.IsNullOrEmpty(r.ClusterGroupId)
                    ? "::single::" + (r.RuleId ?? r.MergeKey ?? Guid.NewGuid().ToString())
                    : r.ClusterGroupId,
                    StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Compute per-slot insertion XYZs for a cluster sharing the same
        /// frame centre. Slot offsets are spread symmetrically along the
        /// wall horizontal axis; spread = (n - (totalSlots-1)/2) × pitch.
        /// Returns a list of (rule, position) ordered by ClusterSlotIndex.
        /// Falls back to wall-tangent inference when hostWall is null.
        /// </summary>
        public static List<(PlacementRule rule, XYZ position)> ComputeClusterPositions(
            IList<PlacementRule> clusterRules,
            XYZ frameCentre,
            Wall hostWall)
        {
            var output = new List<(PlacementRule, XYZ)>();
            if (clusterRules == null || clusterRules.Count == 0 || frameCentre == null) return output;

            // Pitch — first non-zero ModulePitchMm in the group wins.
            double pitchMm = 0.0;
            int totalSlotsFromRules = 0;
            foreach (var r in clusterRules)
            {
                if (r == null) continue;
                if (pitchMm <= 0 && r.ModulePitchMm > 0) pitchMm = r.ModulePitchMm;
                if (r.ClusterTotalSlots > totalSlotsFromRules) totalSlotsFromRules = r.ClusterTotalSlots;
            }
            int totalSlots = totalSlotsFromRules > 0 ? totalSlotsFromRules : clusterRules.Count;

            // Sort by ClusterSlotIndex once (in-place on a fresh list — caller's
            // collection is left untouched).
            var sorted = new List<PlacementRule>(clusterRules);
            sorted.Sort((a, b) => a.ClusterSlotIndex.CompareTo(b.ClusterSlotIndex));

            if (pitchMm <= 0)
            {
                foreach (var r in sorted) output.Add((r, frameCentre));
                return output;
            }

            double pitchFt = pitchMm * MmToFt;
            XYZ along = WallTangentSafe(hostWall);
            if (along == null || along.IsZeroLength()) along = XYZ.BasisX;

            foreach (var r in sorted)
            {
                int slot = Math.Max(0, r.ClusterSlotIndex);
                double centred = slot - (totalSlots - 1) / 2.0;
                XYZ p = frameCentre + along.Multiply(centred * pitchFt);
                output.Add((r, p));
            }
            return output;
        }

        private static XYZ WallTangentSafe(Wall wall)
        {
            if (wall == null) return null;
            try
            {
                if (wall.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ a = lc.Curve.GetEndPoint(0);
                    XYZ b = lc.Curve.GetEndPoint(1);
                    XYZ d = b - a;
                    if (d.IsZeroLength()) return null;
                    return d.Normalize();
                }
            }
            catch { }
            return null;
        }
    }
}
