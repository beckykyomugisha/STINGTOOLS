// StingTools — Drawing Template Manager · keeping placed match lines true to the model
//
// Two decisions MatchLineEngine made too coarsely:
//
//   1. Is an existing pair still right? It was "right" when the sheet references on its
//      curves still matched, so a box moved after the lines were drawn kept its old
//      lines forever: Generate skipped the pair, and only a forced Sync redrew it.
//      SegmentsMatch compares the placed curves with the boundary as it is now.
//
//   2. Is a stamped curve an orphan? Only when its SCOPE-BOX pair was no longer
//      adjacent. A curve keyed to a view that has since been deleted, or retyped so it
//      no longer pairs, belonged to a live box pair and was never pruned — every run
//      added the new pair's curves beside it. ShouldPrune also drops a view-pair key
//      that this run did not pair.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class MatchLineUpkeep
    {
        /// <summary>A trailing ":segN" dropped: the key every segment of one pair shares.</summary>
        public static string BaseKey(string stampedKey)
        {
            if (string.IsNullOrEmpty(stampedKey)) return stampedKey;
            int i = stampedKey.LastIndexOf(":seg", StringComparison.OrdinalIgnoreCase);
            if (i > 0 && int.TryParse(stampedKey.Substring(i + 4), out _)) return stampedKey.Substring(0, i);
            return stampedKey;
        }

        /// <summary>The scope-box pair GUID a key starts with.</summary>
        public static string ScopePairOf(string key)
        {
            var k = key ?? "";
            int sep = k.IndexOf(':');
            return sep > 0 ? k.Substring(0, sep) : k;
        }

        /// <summary>
        /// True when a stamped curve (or caption) keyed <paramref name="stampedKey"/> should
        /// go: its box pair is no longer adjacent, or — for a key naming two views — this run
        /// did not pair those views. A pair whose placement failed this run is in
        /// <paramref name="keepScopePairs"/> and is never pruned on the strength of a
        /// partial run.
        /// </summary>
        public static bool ShouldPrune(string stampedKey, ISet<string> liveScopePairs,
            ISet<string> liveViewPairKeys, ISet<string> keepScopePairs = null)
        {
            var key = BaseKey(stampedKey ?? "");
            var scope = ScopePairOf(key);
            if (keepScopePairs != null && keepScopePairs.Contains(scope)) return false;
            if (liveScopePairs == null || !liveScopePairs.Contains(scope)) return true;
            bool namesViews = key.Split(':').Length >= 3;
            if (!namesViews || liveViewPairKeys == null) return false;
            return !liveViewPairKeys.Contains(key);
        }

        /// <summary>
        /// True when the placed segments are the expected ones, within
        /// <paramref name="tolerance"/>, in any order and either direction. A different
        /// count is a mismatch.
        /// </summary>
        public static bool SegmentsMatch(IList<(double X0, double Y0, double X1, double Y1)> placed,
            IList<(double X0, double Y0, double X1, double Y1)> expected, double tolerance)
        {
            placed = placed ?? new List<(double, double, double, double)>();
            expected = expected ?? new List<(double, double, double, double)>();
            if (placed.Count != expected.Count) return false;
            var left = expected.ToList();
            foreach (var p in placed)
            {
                int hit = left.FindIndex(e => Same(p, e, tolerance));
                if (hit < 0) return false;
                left.RemoveAt(hit);
            }
            return true;
        }

        private static bool Same((double X0, double Y0, double X1, double Y1) a,
            (double X0, double Y0, double X1, double Y1) b, double tol)
        {
            bool Near(double x0, double y0, double x1, double y1) => Math.Abs(x0 - x1) <= tol && Math.Abs(y0 - y1) <= tol;
            return (Near(a.X0, a.Y0, b.X0, b.Y0) && Near(a.X1, a.Y1, b.X1, b.Y1))
                || (Near(a.X0, a.Y0, b.X1, b.Y1) && Near(a.X1, a.Y1, b.X0, b.Y0));
        }
    }
}
