// ExpansionVesselSizer — BS 7074-1 expansion vessel sizing for DHW.
// Phase 179b. Pure static calculator — no Revit dependency.

using System;
using System.Linq;

namespace StingTools.Core.Plumbing
{
    public class ExpansionVesselResult
    {
        public double SystemVolumeL    { get; set; }
        public double DeltaTC          { get; set; }
        public double ExpansionCoeff   { get; set; }
        public double FillPressureBar  { get; set; }
        public double MaxPressureBar   { get; set; }
        public double VTankL           { get; set; }
        public string RecommendedFamily{ get; set; } = "";
    }

    public static class ExpansionVesselSizer
    {
        public static ExpansionVesselResult Size(
            double systemVolumeL,
            double tColdC = 10,
            double tHotC  = 60,
            double fillPressureBar = 1.0,
            double maxPressureBar  = 3.0,
            double safetyMargin    = 1.10)
        {
            var r = new ExpansionVesselResult
            {
                SystemVolumeL    = systemVolumeL,
                DeltaTC          = tHotC - tColdC,
                FillPressureBar  = fillPressureBar,
                MaxPressureBar   = maxPressureBar,
            };
            // BS 7074-1 expansion coefficient (e) at delta-T (interpolated from data file).
            r.ExpansionCoeff = ExpansionCoefficientForDeltaT(r.DeltaTC);
            double Pa = fillPressureBar + 1.0; // absolute
            double Pb = maxPressureBar  + 1.0; // absolute
            if (Pb <= Pa) Pb = Pa + 0.5;
            double acceptanceFactor = 1.0 - (Pa / Pb);
            double V = systemVolumeL * r.ExpansionCoeff / Math.Max(acceptanceFactor, 1e-3);
            r.VTankL = Math.Ceiling(V * safetyMargin);
            r.RecommendedFamily = StandardSizeAbove(r.VTankL);
            return r;
        }

        public static double ExpansionCoefficientForDeltaT(double dt)
        {
            // Linear interpolation across canonical points.
            (double t, double e)[] table = {
                (10, 0.00027), (20, 0.00079), (30, 0.00150),
                (40, 0.00250), (50, 0.00370), (60, 0.00500),
                (70, 0.00650), (80, 0.00820)
            };
            if (dt <= table[0].t) return table[0].e;
            if (dt >= table[table.Length-1].t) return table[table.Length-1].e;
            for (int i = 0; i < table.Length - 1; i++)
            {
                if (dt >= table[i].t && dt <= table[i+1].t)
                {
                    double frac = (dt - table[i].t) / (table[i+1].t - table[i].t);
                    return table[i].e + frac * (table[i+1].e - table[i].e);
                }
            }
            return table[table.Length-1].e;
        }

        private static string StandardSizeAbove(double v)
        {
            int[] sizes = { 8, 12, 18, 24, 35, 50, 80, 100, 150, 200, 250, 300, 400, 500, 600, 800, 1000 };
            foreach (var s in sizes) if (v <= s) return $"EV-{s}L";
            return $"EV-{Math.Ceiling(v / 100) * 100:F0}L";
        }
    }
}
