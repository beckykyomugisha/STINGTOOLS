// CurveCompat — curve/curve intersection points across Revit versions (ROADMAP ELEC-29).
//
// Curve.Intersect(Curve, out IntersectionResultArray) was deprecated in Revit 2026 and
// removed in 2027; its replacement Curve.Intersect(Curve, CurveIntersectResultOption)
// does not exist in 2025. REVIT2026_OR_GREATER comes from StingTools.csproj (RevitYear).

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    internal static class CurveCompat
    {
        /// <summary>The points where two curves intersect (empty when they do not, or overlap only).</summary>
        public static List<XYZ> IntersectionPoints(Curve a, Curve b)
        {
            var pts = new List<XYZ>();
            if (a == null || b == null) return pts;
#if REVIT2026_OR_GREATER
            CurveIntersectResult r = a.Intersect(b, CurveIntersectResultOption.Detailed);
            if (r == null || r.Result != SetComparisonResult.Overlap) return pts;
            foreach (CurveOverlapPoint o in r.GetOverlaps())
                if (o.Type == CurveOverlapPointType.Intersection && o.Point != null) pts.Add(o.Point);
#else
            SetComparisonResult cmp = a.Intersect(b, out IntersectionResultArray results);
            if (cmp != SetComparisonResult.Overlap || results == null) return pts;
            for (int i = 0; i < results.Size; i++) pts.Add(results.get_Item(i).XYZPoint);
#endif
            return pts;
        }
    }
}
