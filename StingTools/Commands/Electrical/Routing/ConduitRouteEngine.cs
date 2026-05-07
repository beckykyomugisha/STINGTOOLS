using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Electrical;

namespace StingTools.Commands.Electrical.Routing
{
    /// <summary>
    /// Pure rectilinear routing engine — no Revit transactions, no model
    /// writes. Computes a Manhattan-style L/Z path from one XYZ to another,
    /// staying at the source's elevation until the final drop. Conduit
    /// diameter selection follows BS 7671 Appendix E + IEC 61386 by sizing
    /// to ≤40 % cross-section fill. The MEP Routing API isn't used (it
    /// isn't enabled on every Revit configuration); production hardening
    /// could swap in NavMesh / ray-casting clash avoidance — Phase 179
    /// honestly delivers the simple rectilinear path.
    /// </summary>
    public class RouteSegment
    {
        public XYZ Start { get; set; }
        public XYZ End   { get; set; }
        public double DiameterMm { get; set; }
        public string Label { get; set; } = "";
        public RouteSegment() { }
        public RouteSegment(XYZ start, XYZ end, double diameterMm, string label)
        { Start = start; End = end; DiameterMm = diameterMm; Label = label ?? ""; }
    }

    public static class ConduitRouteEngine
    {
        private static readonly double[] StandardConduitMm =
            { 16, 20, 25, 32, 40, 50, 63, 75, 100 };

        public static List<RouteSegment> ComputeRoute(XYZ start, XYZ end,
            double diameterMm, string label)
        {
            var segs = new List<RouteSegment>();
            if (start == null || end == null) return segs;
            // L/Z: horizontal at start elevation → drop to end elevation.
            var mid1 = new XYZ(end.X, start.Y, start.Z);
            var mid2 = new XYZ(end.X, start.Y, end.Z);
            if (mid1.DistanceTo(start) > 0.01) segs.Add(new RouteSegment(start, mid1, diameterMm, label));
            if (mid2.DistanceTo(mid1)  > 0.01) segs.Add(new RouteSegment(mid1,  mid2, diameterMm, label));
            if (end.DistanceTo(mid2)   > 0.01) segs.Add(new RouteSegment(mid2,  end,  diameterMm, label));
            return segs;
        }

        public static double SelectConduitDiameterMm(IEnumerable<StingCable> cables)
        {
            if (cables == null) return 20;
            var list = cables.ToList();
            if (list.Count == 0) return 20;
            double totalAreaMm2 = list.Sum(c =>
            {
                double od = c.OuterDiameterMm > 0 ? c.OuterDiameterMm : EstimateCableOdMm(c.CsaMm2);
                return Math.PI * od * od * 0.25 * Math.Max(1, c.CoreCount);
            });
            double requiredAreaMm2 = totalAreaMm2 / 0.40;     // ≤40 % fill
            double requiredDiamMm  = 2.0 * Math.Sqrt(requiredAreaMm2 / Math.PI);
            foreach (var d in StandardConduitMm)
                if (d >= requiredDiamMm) return d;
            return StandardConduitMm[StandardConduitMm.Length - 1];
        }

        public static double EstimateCableOdMm(double csaMm2)
        {
            if (csaMm2 <= 1.5)   return 6.5;
            if (csaMm2 <= 2.5)   return 7.5;
            if (csaMm2 <= 4)     return 8.5;
            if (csaMm2 <= 6)     return 9.5;
            if (csaMm2 <= 10)    return 11.5;
            if (csaMm2 <= 16)    return 13.5;
            if (csaMm2 <= 25)    return 16.5;
            if (csaMm2 <= 35)    return 19.0;
            if (csaMm2 <= 50)    return 22.0;
            if (csaMm2 <= 70)    return 26.0;
            if (csaMm2 <= 95)    return 30.0;
            if (csaMm2 <= 120)   return 34.0;
            if (csaMm2 <= 150)   return 38.0;
            if (csaMm2 <= 185)   return 42.0;
            return 50.0;
        }
    }
}
