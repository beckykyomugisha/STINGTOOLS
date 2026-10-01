// StingTools — MEP run dimension planning (DTW-84). Revit-free.
//
// AutoDimMEPRun had three faults: (1) runs were clustered by MEPCurve-to-
// MEPCurve adjacency only, and pipes meet through FITTINGS, so no two pipes
// ever clustered and nothing was placed; (2) the chain referenced each pipe's
// centreline — references PARALLEL to a witness line that ran along the same
// pipes, which Revit rejects; (3) nothing stopped a re-run duplicating it.
//
// This half decides WHAT to dimension: which curves form a run (walking
// through fittings and accessories, not through equipment), which straights
// of a run lie on one line, and which stops on that line survive once stops
// at the same position are merged. The Revit half (MEPDimensioner) supplies
// the connector graph and turns stops into references perpendicular to the
// line — fitting centre planes and pipe end points.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing.Dimensioning
{
    /// <summary>One connected run: its straights, and the fittings / accessories joining them.</summary>
    public sealed class MepRun
    {
        public List<long> Curves { get; } = new List<long>();
        public List<long> Joints { get; } = new List<long>();
    }

    /// <summary>A straight in plan (feet) with the caller's key.</summary>
    public readonly struct MepSeg
    {
        public MepSeg(long key, double x0, double y0, double x1, double y1)
        { Key = key; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public long Key { get; }
        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
    }

    public static class MepRunPlanning
    {
        /// <summary>
        /// Group <paramref name="curves"/> into runs. Two curves are in one run when
        /// they connect directly or through any chain of <paramref name="isJoint"/>
        /// elements (fittings, inline accessories). Anything else — equipment,
        /// terminals, a curve outside the set — ends the walk, so a run never leaks
        /// through an AHU into the next system.
        /// </summary>
        public static List<MepRun> Cluster(IEnumerable<long> curves, Func<long, IEnumerable<long>> neighbours,
            Func<long, bool> isJoint)
        {
            var curveSet = new HashSet<long>(curves ?? Enumerable.Empty<long>());
            var visited = new HashSet<long>();
            var runs = new List<MepRun>();
            foreach (var seed in curveSet.OrderBy(x => x))
            {
                if (visited.Contains(seed)) continue;
                var run = new MepRun();
                var stack = new Stack<long>();
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!visited.Add(cur)) continue;
                    if (curveSet.Contains(cur)) run.Curves.Add(cur);
                    else run.Joints.Add(cur);
                    foreach (var nb in neighbours(cur) ?? Enumerable.Empty<long>())
                    {
                        if (nb == cur || visited.Contains(nb)) continue;
                        if (curveSet.Contains(nb) || isJoint(nb)) stack.Push(nb);
                    }
                }
                run.Curves.Sort();
                run.Joints.Sort();
                runs.Add(run);
            }
            return runs;
        }

        /// <summary>
        /// Straights of one run that lie on one line: same direction (within
        /// <paramref name="angleTolDeg"/>, either way round) and the same offset
        /// across it (within <paramref name="offsetTolFt"/>). Each group is one
        /// chain. Zero-length segments (risers in plan) are left out.
        /// </summary>
        public static List<List<long>> CollinearGroups(IReadOnlyList<MepSeg> segs,
            double angleTolDeg = 1.0, double offsetTolFt = 0.05)
        {
            var groups = new List<(double Dx, double Dy, double Off, List<long> Keys)>();
            double sinTol = Math.Sin(angleTolDeg * Math.PI / 180.0);
            foreach (var s in segs ?? Array.Empty<MepSeg>())
            {
                double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) continue;
                dx /= len; dy /= len;
                int found = -1;
                for (int i = 0; i < groups.Count && found < 0; i++)
                {
                    var g = groups[i];
                    if (Math.Abs(dx * g.Dy - dy * g.Dx) > sinTol) continue;
                    double off = s.X0 * -g.Dy + s.Y0 * g.Dx;   // across the group's direction
                    if (Math.Abs(off - g.Off) <= offsetTolFt) found = i;
                }
                if (found >= 0) groups[found].Keys.Add(s.Key);
                else groups.Add((dx, dy, s.X0 * -dy + s.Y0 * dx, new List<long> { s.Key }));
            }
            return groups.Select(g => g.Keys).ToList();
        }

        /// <summary>
        /// Indices of the stops a chain keeps: ordered by position along the line,
        /// one per position (stops within <paramref name="tolFt"/> of the last kept
        /// one are merged — a pipe end and the fitting face it meets, a coupling
        /// between two pipes). Earlier entries win a tie, so the caller lists the
        /// stops it prefers (fitting centres) first.
        /// </summary>
        public static List<int> ChainStops(IReadOnlyList<double> positions, double tolFt = 5.0 / 304.8)
        {
            var kept = new List<int>();
            if (positions == null) return kept;
            var order = Enumerable.Range(0, positions.Count)
                .OrderBy(i => positions[i]).ThenBy(i => i).ToList();
            int k = 0;
            while (k < order.Count)
            {
                double start = positions[order[k]];
                int best = order[k];
                int j = k + 1;
                while (j < order.Count && positions[order[j]] - start <= tolFt)
                {
                    best = Math.Min(best, order[j]);
                    j++;
                }
                kept.Add(best);
                k = j;
            }
            return kept;
        }
    }
}
