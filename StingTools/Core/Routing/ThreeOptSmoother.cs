// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Routing/ThreeOptSmoother.cs — S3.10.
//
// 3-opt local search for routing paths. Given a list of waypoints
// (VoxelCell centres converted to XYZ), repeatedly tries removing 3
// edges and reconnecting the resulting 3 segments in each of the
// seven alternative orderings. Keeps any reordering that strictly
// reduces total length. Terminates when a full pass yields no
// improvement.
//
// Used after ACO (S3.9) and before Bezier fitting-snap (S3.11) so
// path crossings and zig-zags get straightened before the final
// fitting placement.
//
// Operates on IList<XYZ> so it can be reused by routing engines that
// don't go through the voxel grid (e.g. the AutoPipeDrop vertical
// drop engine).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public static class ThreeOptSmoother
    {
        /// <summary>
        /// Run 3-opt until no further improvement. Returns a new list;
        /// the input is not mutated.
        /// </summary>
        public static List<XYZ> Smooth(IList<XYZ> path, int maxPasses = 25)
        {
            if (path == null || path.Count < 4) return path == null ? new List<XYZ>() : new List<XYZ>(path);
            var best = new List<XYZ>(path);
            bool improved;
            int pass = 0;

            do
            {
                improved = false;
                pass++;
                for (int i = 0; i + 3 < best.Count; i++)
                {
                    for (int j = i + 1; j + 2 < best.Count; j++)
                    {
                        for (int k = j + 1; k + 1 < best.Count; k++)
                        {
                            var candidate = TryReconnect(best, i, j, k);
                            if (candidate != null && Length(candidate) + 1e-6 < Length(best))
                            {
                                best = candidate;
                                improved = true;
                            }
                        }
                    }
                }
            } while (improved && pass < maxPasses);
            return best;
        }

        /// <summary>
        /// Try each of the 7 non-trivial 3-opt reconnections for
        /// indices (i, j, k). Returns the shortest that's valid, or
        /// null if none beats the input.
        /// </summary>
        private static List<XYZ> TryReconnect(List<XYZ> path, int i, int j, int k)
        {
            // Segments: A = path[0..i], B = path[i+1..j], C = path[j+1..k], D = path[k+1..]
            var a = path.GetRange(0, i + 1);
            var b = path.GetRange(i + 1, j - i);
            var c = path.GetRange(j + 1, k - j);
            var d = path.GetRange(k + 1, path.Count - k - 1);

            var br = new List<XYZ>(b); br.Reverse();
            var cr = new List<XYZ>(c); cr.Reverse();

            var options = new List<List<XYZ>>
            {
                Concat(a, br, c,  d),
                Concat(a, b,  cr, d),
                Concat(a, br, cr, d),
                Concat(a, c,  b,  d),
                Concat(a, c,  br, d),
                Concat(a, cr, b,  d),
                Concat(a, cr, br, d),
            };

            List<XYZ> shortest = null;
            double shortestLen = Length(path);
            foreach (var o in options)
            {
                double len = Length(o);
                if (len < shortestLen - 1e-6)
                {
                    shortestLen = len;
                    shortest    = o;
                }
            }
            return shortest;
        }

        private static List<XYZ> Concat(params List<XYZ>[] segments)
        {
            var result = new List<XYZ>();
            foreach (var s in segments) result.AddRange(s);
            return result;
        }

        private static double Length(IList<XYZ> path)
        {
            double len = 0;
            for (int i = 1; i < path.Count; i++) len += path[i - 1].DistanceTo(path[i]);
            return len;
        }
    }
}
