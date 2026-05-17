using StingTools.Core;
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

            // Phase 139.5 Q14 — for straight walls the tangent is constant;
            // for curved walls (Arc / NurbSpline) the tangent at slot 0 is
            // not the tangent at slot N. Sample the wall's location curve at
            // arc-length parameters around the frame centre so each slot's
            // position follows the wall.
            LocationCurve lc = hostWall?.Location as LocationCurve;
            Curve curve = lc?.Curve;
            bool useCurveSampling = curve != null
                && (curve is Arc || !(curve is Line));

            XYZ alongFallback = WallTangentSafe(hostWall);
            if (alongFallback == null || alongFallback.IsZeroLength()) alongFallback = XYZ.BasisX;

            if (useCurveSampling)
            {
                // Project frameCentre onto the curve to find the t value of slot 0.
                IntersectionResult proj = null;
                try { proj = curve.Project(frameCentre); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (proj == null) useCurveSampling = false;
                else
                {
                    double centreT = proj.Parameter;
                    double curveLen = curve.Length;
                    if (curveLen <= 1e-9) useCurveSampling = false;
                    else
                    {
                        // ApproximateParameter spans 0..1 of the *normalised*
                        // curve.  Convert pitchFt into a parameter delta via
                        // tangent magnitude at the centre.
                        XYZ tangentAtCentre = curve.ComputeDerivatives(centreT, false)?.BasisX;
                        double tangentMag = tangentAtCentre?.GetLength() ?? 0;
                        if (tangentMag <= 1e-9) useCurveSampling = false;
                        else
                        {
                            double dtPerFt = 1.0 / tangentMag;
                            foreach (var r in sorted)
                            {
                                int slot = Math.Max(0, r.ClusterSlotIndex);
                                double centred = slot - (totalSlots - 1) / 2.0;
                                double t = centreT + centred * pitchFt * dtPerFt;
                                // Clamp to the curve domain.
                                if (curve.IsBound)
                                {
                                    t = Math.Max(curve.GetEndParameter(0), Math.Min(curve.GetEndParameter(1), t));
                                }
                                XYZ p;
                                try { p = curve.Evaluate(t, false); }
                                catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); p = frameCentre + alongFallback.Multiply(centred * pitchFt); }
                                output.Add((r, p));
                            }
                            return output;
                        }
                    }
                }
            }

            foreach (var r in sorted)
            {
                int slot = Math.Max(0, r.ClusterSlotIndex);
                double centred = slot - (totalSlots - 1) / 2.0;
                XYZ p = frameCentre + alongFallback.Multiply(centred * pitchFt);
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }
    }
}
