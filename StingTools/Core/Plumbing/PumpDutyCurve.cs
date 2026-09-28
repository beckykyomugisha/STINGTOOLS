// PumpDutyCurve — duty-point checks against a manufacturer's head/flow curve.
// Revit-free (tested in StingTools.Mep.Tests).
//
// A catalogue entry may carry curve points (flow, head, optional efficiency)
// read off the manufacturer's published curve. The head the pump delivers at
// the duty flow is interpolated linearly between points; a pump is a
// candidate only if that head meets the duty head AND the duty flow lies on
// the published part of the curve — a rated point alone cannot say either.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Plumbing
{
    public sealed class PumpCurvePoint
    {
        public double FlowLps { get; set; }
        public double HeadM { get; set; }
        /// <summary>0 = not published at this point.</summary>
        public double EfficiencyPct { get; set; }
    }

    public sealed class PumpCurveCheck
    {
        /// <summary>Duty flow lies between the first and last published points.</summary>
        public bool WithinCurve { get; set; }
        /// <summary>Head the pump delivers at the duty flow, m (NaN outside the curve).</summary>
        public double HeadAtDutyM { get; set; } = double.NaN;
        /// <summary>Efficiency at the duty flow, % (NaN when the curve publishes none).</summary>
        public double EfficiencyAtDutyPct { get; set; } = double.NaN;
        /// <summary>(head at duty − duty head) / duty head × 100.</summary>
        public double HeadMarginPct { get; set; } = double.NaN;
        public bool Meets { get; set; }
    }

    public static class PumpDutyCurve
    {
        public static List<PumpCurvePoint> Clean(IEnumerable<PumpCurvePoint> pts)
            => (pts ?? Enumerable.Empty<PumpCurvePoint>())
               .Where(p => p != null && p.FlowLps >= 0 && p.HeadM > 0)
               .GroupBy(p => p.FlowLps).Select(g => g.First())
               .OrderBy(p => p.FlowLps).ToList();

        public static double Interpolate(IList<PumpCurvePoint> pts, double q, Func<PumpCurvePoint, double> y)
        {
            if (pts == null || pts.Count == 0) return double.NaN;
            if (q < pts[0].FlowLps || q > pts[pts.Count - 1].FlowLps) return double.NaN;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                if (q < a.FlowLps || q > b.FlowLps) continue;
                double ya = y(a), yb = y(b);
                if (double.IsNaN(ya) || double.IsNaN(yb)) return double.NaN;
                double t = b.FlowLps > a.FlowLps ? (q - a.FlowLps) / (b.FlowLps - a.FlowLps) : 0;
                return ya + t * (yb - ya);
            }
            return y(pts[pts.Count - 1]);
        }

        public static PumpCurveCheck Check(IEnumerable<PumpCurvePoint> curve, double dutyFlowLps, double dutyHeadM)
        {
            var pts = Clean(curve);
            var c = new PumpCurveCheck();
            if (pts.Count < 2 || dutyFlowLps <= 0 || dutyHeadM <= 0) return c;
            double h = Interpolate(pts, dutyFlowLps, p => p.HeadM);
            c.WithinCurve = !double.IsNaN(h);
            if (!c.WithinCurve) return c;
            c.HeadAtDutyM = h;
            c.HeadMarginPct = (h - dutyHeadM) / dutyHeadM * 100.0;
            c.EfficiencyAtDutyPct = Interpolate(pts, dutyFlowLps, p => p.EfficiencyPct > 0 ? p.EfficiencyPct : double.NaN);
            c.Meets = h >= dutyHeadM;
            return c;
        }
    }
}
