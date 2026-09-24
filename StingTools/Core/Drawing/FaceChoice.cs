using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// Which face of an element a MATERIAL tag should point at.
    ///
    /// A material tag labels the material of a face, so it needs a face
    /// reference; the runner only ever passed the whole element, which a
    /// material tag cannot use. On an elevation the face you are looking at is
    /// the one to label, so faces turned towards the viewer win, and among those
    /// the larger one (the wall's face, not a reveal). A face seen edge-on — a
    /// wall's side in plan — still scores a little, so a plan still gets a tag
    /// rather than none; a face turned away scores the same small amount.
    /// </summary>
    public static class FaceChoice
    {
        /// <param name="facingDots">Per face: dot(face normal, view direction). The view
        /// direction points towards the viewer, so +1 faces the viewer head-on.</param>
        /// <param name="areas">Per face: area (any unit, the same for all).</param>
        /// <returns>Index of the face to tag, or -1 when there is none.</returns>
        public static int Best(IList<double> facingDots, IList<double> areas)
        {
            int n = Math.Min(facingDots?.Count ?? 0, areas?.Count ?? 0);
            int best = -1;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                double a = areas[i];
                if (!(a > 0)) continue;
                double score = a * (0.05 + Math.Max(0.0, facingDots[i]));
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }
}
