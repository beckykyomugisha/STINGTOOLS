// Ieee1584_2018 — IEEE 1584-2018 arc-flash model (0.208–15 kV, three-phase). Revit-free.
//
// Coefficients (Tables 1–5 and 7) were transcribed on 2026-09-27 from public
// implementations of the standard and accepted only where four independent ones agree
// digit for digit (LiaungYip/arcflash, jgrimard/arc-flash-calculator,
// Rush2088/PowerSystems_Tools, ESYSingenieria). The equations were checked the same
// way, and the model reproduces the IEEE 1584-2018 Annex D.1 and D.2 worked examples
// and a sample of the results the official IEEE spreadsheet produces (tests). The
// printed standard itself was not available, so results carry a basis that says so.
//
// Procedure (clause 4): intermediate arcing currents at 600, 2700 and 14 300 V (Eq. 1);
// ≤ 600 V: final Iarc by Eq. 25, energy and boundary by Eq. 6 / 10 (Table 3);
// > 600 V: energy and boundary at the three voltages (Tables 3–5) interpolated
// (Eq. 16–24). Enclosure-size correction CF (Eq. 11–15, Tables 6–7) for box
// configurations. The reduced arcing current Iarc·(1 − 0.5·VarCf) (Eq. 2) is a second
// case with its own arc duration; the larger energy and the larger boundary are kept.

using System;
using System.Collections.Generic;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>IEEE 1584-2018 electrode configuration.</summary>
    public enum ElectrodeConfiguration
    {
        /// <summary>Vertical conductors in a box.</summary>
        VCB,
        /// <summary>Vertical conductors terminated in an insulating barrier, in a box.</summary>
        VCBB,
        /// <summary>Horizontal conductors in a box.</summary>
        HCB,
        /// <summary>Vertical conductors in open air.</summary>
        VOA,
        /// <summary>Horizontal conductors in open air.</summary>
        HOA
    }

    public sealed class Ieee1584Case
    {
        public double ArcingCurrentKa { get; set; }
        public double ArcDurationMs { get; set; }
        public double IncidentEnergyJcm2 { get; set; }
        public double BoundaryMm { get; set; }
    }

    public sealed class Ieee1584Result
    {
        public bool Calculated { get; set; }
        public string NotCalculatedReason { get; set; } = "";
        public Ieee1584Case Full { get; set; }
        public Ieee1584Case Reduced { get; set; }
        public double VarCf { get; set; }
        /// <summary>Enclosure-size correction factor (1 for open-air configurations).</summary>
        public double EnclosureCf { get; set; } = 1.0;
        /// <summary>"Typical" or "Shallow" (box configurations); "" for open air.</summary>
        public string EnclosureType { get; set; } = "";
        public double EquivalentSizeIn { get; set; }
        /// <summary>The larger of the two cases' energy, J/cm².</summary>
        public double IncidentEnergyJcm2 { get; set; }
        /// <summary>The larger of the two cases' boundary, mm.</summary>
        public double BoundaryMm { get; set; }
        public bool ReducedCaseGovernsEnergy { get; set; }
        public List<string> Notes { get; } = new List<string>();
    }

    public static class Ieee1584_2018
    {
        public const double MinVoltageKv = 0.208, MaxVoltageKv = 15.0;
        public const double MinWorkingDistanceMm = 305.0;

        /// <summary>Clause 4.2 ranges: (Ibf kA min, max, gap mm min, max) for ≤ 600 V and above.</summary>
        public static (double ibfMin, double ibfMax, double gapMin, double gapMax) Range(double vocKv)
            => vocKv <= 0.6 ? (0.5, 106.0, 6.35, 76.2) : (0.2, 65.0, 19.05, 254.0);

        // Table 1 — Eq. 1 intermediate arcing current, [config][600 / 2700 / 14300 V][k1..k10].
        private static readonly double[][][] Table1 =
        {
            new[] { // VCB
                new double[] { -0.04287, 1.035, -0.083, 0.0, 0.0, -4.783e-09, 1.962e-06, -0.000229, 0.003141, 1.092 },
                new double[] { 0.0065, 1.001, -0.024, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729 },
                new double[] { 0.005795, 1.015, -0.011, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729 },
            },
            new[] { // VCBB
                new double[] { -0.017432, 0.98, -0.05, 0.0, 0.0, -5.767e-09, 2.524e-06, -0.00034, 0.01187, 1.013 },
                new double[] { 0.002823, 0.995, -0.0125, 0.0, -9.204e-11, 2.901e-08, -3.262e-06, 0.0001569, -0.004003, 0.9825 },
                new double[] { 0.014827, 1.01, -0.01, 0.0, -9.204e-11, 2.901e-08, -3.262e-06, 0.0001569, -0.004003, 0.9825 },
            },
            new[] { // HCB
                new double[] { 0.054922, 0.988, -0.11, 0.0, 0.0, -5.382e-09, 2.316e-06, -0.000302, 0.0091, 0.9725 },
                new double[] { 0.001011, 1.003, -0.0249, 0.0, 0.0, 4.859e-10, -1.814e-07, -9.128e-06, -0.0007, 0.9881 },
                new double[] { 0.008693, 0.999, -0.02, 0.0, -5.043e-11, 2.233e-08, -3.046e-06, 0.000116, -0.001145, 0.9839 },
            },
            new[] { // VOA
                new double[] { 0.043785, 1.04, -0.18, 0.0, 0.0, -4.783e-09, 1.962e-06, -0.000229, 0.003141, 1.092 },
                new double[] { -0.02395, 1.006, -0.0188, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729 },
                new double[] { 0.005371, 1.0102, -0.029, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729 },
            },
            new[] { // HOA
                new double[] { 0.111147, 1.008, -0.24, 0.0, 0.0, -3.895e-09, 1.641e-06, -0.000197, 0.002615, 1.1 },
                new double[] { 0.000435, 1.006, -0.038, 0.0, 0.0, 7.859e-10, -1.914e-07, -9.128e-06, -0.0007, 0.9981 },
                new double[] { 0.000904, 0.999, -0.02, 0.0, 0.0, 7.859e-10, -1.914e-07, -9.128e-06, -0.0007, 0.9981 },
            },
        };

        // Table 2 — VarCf polynomial in Voc (kV), [config][k1..k7].
        private static readonly double[][] Table2 =
        {
            new double[] { 0.0, -1.4269e-06, 8.3137e-05, -0.0019382, 0.022366, -0.12645, 0.30226 }, // VCB
            new double[] { 1.138e-06, -6.0287e-05, 0.0012758, -0.013778, 0.080217, -0.24066, 0.33524 }, // VCBB
            new double[] { 0.0, -3.097e-06, 0.00016405, -0.0033609, 0.033308, -0.16182, 0.34627 }, // HCB
            new double[] { 9.5606e-07, -5.1543e-05, 0.0011161, -0.01242, 0.075125, -0.23584, 0.33696 }, // VOA
            new double[] { 0.0, -3.1555e-06, 0.0001682, -0.0034607, 0.034124, -0.1599, 0.34629 }, // HOA
        };

        // Table3 — incident energy / boundary at 600 V, [config][k1..k13].
        private static readonly double[][] Table3 =
        {
            new double[] { 0.753364, 0.566, 1.752636, 0.0, 0.0, -4.783e-09, 1.962e-06, -0.000229, 0.003141, 1.092, 0.0, -1.598, 0.957 }, // VCB
            new double[] { 3.068459, 0.26, -0.098107, 0.0, 0.0, -5.767e-09, 2.524e-06, -0.00034, 0.01187, 1.013, -0.06, -1.809, 1.19 }, // VCBB
            new double[] { 4.073745, 0.344, -0.370259, 0.0, 0.0, -5.382e-09, 2.316e-06, -0.000302, 0.0091, 0.9725, 0.0, -2.03, 1.036 }, // HCB
            new double[] { 0.679294, 0.746, 1.222636, 0.0, 0.0, -4.783e-09, 1.962e-06, -0.000229, 0.003141, 1.092, 0.0, -1.598, 0.997 }, // VOA
            new double[] { 3.470417, 0.465, -0.261863, 0.0, 0.0, -3.895e-09, 1.641e-06, -0.000197, 0.002615, 1.1, 0.0, -1.99, 1.04 }, // HOA
        };

        // Table4 — incident energy / boundary at 2700 V, [config][k1..k13].
        private static readonly double[][] Table4 =
        {
            new double[] { 2.40021, 0.165, 0.354202, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729, 0.0, -1.569, 0.9778 }, // VCB
            new double[] { 3.870592, 0.185, -0.736618, 0.0, -9.204e-11, 2.901e-08, -3.262e-06, 0.0001569, -0.004003, 0.9825, 0.0, -1.742, 1.09 }, // VCBB
            new double[] { 3.486391, 0.177, -0.193101, 0.0, 0.0, 4.859e-10, -1.814e-07, -9.128e-06, -0.0007, 0.9881, 0.027, -1.723, 1.055 }, // HCB
            new double[] { 3.880724, 0.105, -1.906033, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729, 0.0, -1.515, 1.115 }, // VOA
            new double[] { 3.616266, 0.149, -0.761561, 0.0, 0.0, 7.859e-10, -1.914e-07, -9.128e-06, -0.0007, 0.9981, 0.0, -1.639, 1.078 }, // HOA
        };

        // Table5 — incident energy / boundary at 14300 V, [config][k1..k13].
        private static readonly double[][] Table5 =
        {
            new double[] { 3.825917, 0.11, -0.999749, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729, 0.0, -1.568, 0.99 }, // VCB
            new double[] { 3.644309, 0.215, -0.585522, 0.0, -9.204e-11, 2.901e-08, -3.262e-06, 0.0001569, -0.004003, 0.9825, 0.0, -1.677, 1.06 }, // VCBB
            new double[] { 3.044516, 0.125, 0.245106, 0.0, -5.043e-11, 2.233e-08, -3.046e-06, 0.000116, -0.001145, 0.9839, 0.0, -1.655, 1.084 }, // HCB
            new double[] { 3.405454, 0.12, -0.93245, -1.557e-12, 4.556e-10, -4.186e-08, 8.346e-07, 5.482e-05, -0.003191, 0.9729, 0.0, -1.534, 0.979 }, // VOA
            new double[] { 2.04049, 0.177, 1.005092, 0.0, 0.0, 7.859e-10, -1.914e-07, -9.128e-06, -0.0007, 0.9981, -0.05, -1.633, 1.151 }, // HOA
        };

        // Table 7 — enclosure-size correction [b1, b2, b3] for VCB, VCBB, HCB.
        private static readonly double[][] Table7Typical =
        {
            new double[] { -0.000302, 0.03441, 0.4325 }, // VCB
            new double[] { -0.0002976, 0.032, 0.479 }, // VCBB
            new double[] { -0.0001923, 0.01935, 0.6899 }, // HCB
        };
        private static readonly double[][] Table7Shallow =
        {
            new double[] { 0.002222, -0.02556, 0.6222 }, // VCB
            new double[] { -0.002778, 0.1194, -0.2778 }, // VCBB
            new double[] { -0.0005556, 0.03722, 0.4778 }, // HCB
        };


        private static int Ix(ElectrodeConfiguration c) => (int)c;
        private static bool IsBox(ElectrodeConfiguration c)
            => c == ElectrodeConfiguration.VCB || c == ElectrodeConfiguration.VCBB || c == ElectrodeConfiguration.HCB;

        /// <summary>Eq. 1: intermediate arcing current (kA) at 600, 2700 or 14 300 V (row 0, 1, 2).</summary>
        public static double IntermediateArcingCurrentKa(ElectrodeConfiguration c, int voltageRow, double ibfKa, double gapMm)
        {
            var k = Table1[Ix(c)][voltageRow];
            double poly = k[3] * Math.Pow(ibfKa, 6) + k[4] * Math.Pow(ibfKa, 5) + k[5] * Math.Pow(ibfKa, 4)
                        + k[6] * Math.Pow(ibfKa, 3) + k[7] * ibfKa * ibfKa + k[8] * ibfKa + k[9];
            return Math.Pow(10, k[0] + k[1] * Math.Log10(ibfKa) + k[2] * Math.Log10(gapMm)) * poly;
        }

        /// <summary>Eq. 2: arcing-current variation correction factor (Voc in kV).</summary>
        public static double VarCf(ElectrodeConfiguration c, double vocKv)
        {
            var k = Table2[Ix(c)];
            double v = vocKv;
            return k[0] * Math.Pow(v, 6) + k[1] * Math.Pow(v, 5) + k[2] * Math.Pow(v, 4)
                 + k[3] * Math.Pow(v, 3) + k[4] * v * v + k[5] * v + k[6];
        }

        /// <summary>Eq. 25: final arcing current for Voc ≤ 0.6 kV.</summary>
        public static double LowVoltageArcingCurrentKa(double iarc600Ka, double vocKv, double ibfKa)
            => 1.0 / Math.Sqrt(Math.Pow(0.6 / vocKv, 2)
                               * (1.0 / (iarc600Ka * iarc600Ka) - (0.36 - vocKv * vocKv) / (0.36 * ibfKa * ibfKa)));

        /// <summary>
        /// Eq. 11–15 with the Table 6 rules: the enclosure-size correction factor. Open-air
        /// configurations return 1. Enclosure dimensions in mm; Voc in kV.
        /// </summary>
        public static double EnclosureCorrection(ElectrodeConfiguration c, double vocKv, double heightMm, double widthMm,
            double depthMm, out string type, out double eesIn)
        {
            type = ""; eesIn = 0;
            if (!IsBox(c)) return 1.0;
            bool shallow = vocKv < 0.6 && heightMm < 508 && widthMm < 508 && depthMm <= 203.2;
            type = shallow ? "Shallow" : "Typical";
            double a = c == ElectrodeConfiguration.VCB ? 4 : 10;
            double b = c == ElectrodeConfiguration.VCB ? 20 : c == ElectrodeConfiguration.VCBB ? 24 : 22;
            // The standard prints 0.03937 for mm → in in Table 6 (not 1/25.4); Annex D uses it.
            double Eq(double x) => (660.4 + (x - 660.4) * ((vocKv + a) / b)) / 25.4;
            double Dim(double x, bool isHeight)
            {
                if (x < 508) return shallow ? 0.03937 * x : 20.0;
                if (x <= 660.4) return 0.03937 * x;
                bool vcbHeight = isHeight && c == ElectrodeConfiguration.VCB;
                if (x <= 1244.6) return vcbHeight ? 0.03937 * x : Eq(x);
                return vcbHeight ? 49.0 : Eq(1244.6);
            }
            eesIn = (Dim(heightMm, true) + Dim(widthMm, false)) / 2.0;
            var bb = (shallow ? Table7Shallow : Table7Typical)[Ix(c)];
            double p = bb[0] * eesIn * eesIn + bb[1] * eesIn + bb[2];
            return shallow ? 1.0 / p : p;
        }

        // Eq. 3–10 share one exponent: k1 + k2·lg G + k3·Iarc_k3 / poly(Ibf) + k11·lg Ibf + k13·lg Iarc + lg(1/CF).
        private static double Exponent(double[] k, double gapMm, double iarcK3, double iarc, double ibf, double cf)
        {
            double den = k[3] * Math.Pow(ibf, 7) + k[4] * Math.Pow(ibf, 6) + k[5] * Math.Pow(ibf, 5) + k[6] * Math.Pow(ibf, 4)
                       + k[7] * Math.Pow(ibf, 3) + k[8] * ibf * ibf + k[9] * ibf;
            return k[0] + k[1] * Math.Log10(gapMm) + k[2] * iarcK3 / den + k[10] * Math.Log10(ibf)
                 + k[12] * Math.Log10(iarc) + Math.Log10(1.0 / cf);
        }

        private static double Energy(double[] k, double gapMm, double iarcK3, double iarc, double ibf, double cf, double tMs, double dMm)
            => 12.552 / 50.0 * tMs * Math.Pow(10, Exponent(k, gapMm, iarcK3, iarc, ibf, cf) + k[11] * Math.Log10(dMm));

        // Boundary at 1.2 cal/cm² (5.0208 J/cm², the "20" in lg(20/T)).
        private static double Boundary(double[] k, double gapMm, double iarcK3, double iarc, double ibf, double cf, double tMs)
            => Math.Pow(10, (Exponent(k, gapMm, iarcK3, iarc, ibf, cf) - Math.Log10(20.0 / tMs)) / -k[11]);

        /// <summary>Eq. 16–24: interpolation between the 600, 2700 and 14 300 V values.</summary>
        public static double Interpolate(double vocKv, double x600, double x2700, double x14300)
        {
            double x1 = (x2700 - x600) / 2.1 * (vocKv - 2.7) + x2700;
            double x2 = (x14300 - x2700) / 11.6 * (vocKv - 14.3) + x14300;
            double x3 = x1 * (2.7 - vocKv) / 2.1 + x2 * (vocKv - 0.6) / 2.1;
            return vocKv <= 2.7 ? x3 : x2;
        }

        /// <summary>Arcing current (kA) for the full (reduced = false) or reduced case.</summary>
        public static double ArcingCurrentKa(ElectrodeConfiguration c, double vocKv, double ibfKa, double gapMm, bool reduced)
        {
            double m = reduced ? 1.0 - 0.5 * VarCf(c, vocKv) : 1.0;
            if (vocKv <= 0.6)
                return LowVoltageArcingCurrentKa(IntermediateArcingCurrentKa(c, 0, ibfKa, gapMm), vocKv, ibfKa) * m;
            return Interpolate(vocKv,
                IntermediateArcingCurrentKa(c, 0, ibfKa, gapMm) * m,
                IntermediateArcingCurrentKa(c, 1, ibfKa, gapMm) * m,
                IntermediateArcingCurrentKa(c, 2, ibfKa, gapMm) * m);
        }

        /// <summary>One case: energy (J/cm²) and boundary (mm) for a given arc duration (ms).</summary>
        public static Ieee1584Case Case(ElectrodeConfiguration c, double vocKv, double ibfKa, double gapMm, double distMm,
            double cf, double tMs, bool reduced)
        {
            double m = reduced ? 1.0 - 0.5 * VarCf(c, vocKv) : 1.0;
            int i = Ix(c);
            var r = new Ieee1584Case { ArcDurationMs = tMs };
            if (vocKv <= 0.6)
            {
                // The k3 term keeps the full 600 V intermediate current in both cases.
                double i600 = IntermediateArcingCurrentKa(c, 0, ibfKa, gapMm);
                double iarc = LowVoltageArcingCurrentKa(i600, vocKv, ibfKa) * m;
                r.ArcingCurrentKa = iarc;
                r.IncidentEnergyJcm2 = Energy(Table3[i], gapMm, i600, iarc, ibfKa, cf, tMs, distMm);
                r.BoundaryMm = Boundary(Table3[i], gapMm, i600, iarc, ibfKa, cf, tMs);
                return r;
            }
            var tables = new[] { Table3, Table4, Table5 };
            var iv = new double[3]; var ev = new double[3]; var bv = new double[3];
            for (int n = 0; n < 3; n++)
            {
                iv[n] = IntermediateArcingCurrentKa(c, n, ibfKa, gapMm) * m;
                ev[n] = Energy(tables[n][i], gapMm, iv[n], iv[n], ibfKa, cf, tMs, distMm);
                bv[n] = Boundary(tables[n][i], gapMm, iv[n], iv[n], ibfKa, cf, tMs);
            }
            r.ArcingCurrentKa = Interpolate(vocKv, iv[0], iv[1], iv[2]);
            r.IncidentEnergyJcm2 = Interpolate(vocKv, ev[0], ev[1], ev[2]);
            r.BoundaryMm = Interpolate(vocKv, bv[0], bv[1], bv[2]);
            return r;
        }

        /// <summary>
        /// Full procedure. <paramref name="arcDurationMs"/> gives the protective device's
        /// clearing time (ms) at an arcing current (kA); it is evaluated at the full and at
        /// the reduced arcing current. Return NaN or ≤ 0 for "unknown".
        /// </summary>
        public static Ieee1584Result Calculate(ElectrodeConfiguration c, double vocKv, double ibfKa, double gapMm,
            double distMm, double heightMm, double widthMm, double depthMm, Func<double, double> arcDurationMs)
        {
            var r = new Ieee1584Result();
            if (!(vocKv >= MinVoltageKv - 1e-9 && vocKv <= MaxVoltageKv + 1e-9))
                return No(r, $"{vocKv * 1000:0} V is outside the IEEE 1584-2018 range (208 V – 15 kV)");
            var (ibfMin, ibfMax, gMin, gMax) = Range(vocKv);
            if (!(ibfKa >= ibfMin && ibfKa <= ibfMax))
                return No(r, $"bolted fault {ibfKa:0.###} kA is outside the IEEE 1584-2018 range at this voltage ({ibfMin}–{ibfMax} kA)");
            if (!(gapMm >= gMin && gapMm <= gMax))
                return No(r, $"gap {gapMm:0.#} mm is outside the IEEE 1584-2018 range at this voltage ({gMin}–{gMax} mm)");
            if (!(distMm >= MinWorkingDistanceMm))
                return No(r, $"working distance {distMm:0} mm is below the IEEE 1584-2018 minimum ({MinWorkingDistanceMm:0} mm)");
            if (IsBox(c))
            {
                if (!(heightMm > 0 && widthMm > 0 && depthMm > 0)) return No(r, "enclosure size unknown");
                if (widthMm < 4 * gapMm) return No(r, $"enclosure width {widthMm:0} mm is less than 4 × the gap — outside the model");
            }
            if (arcDurationMs == null) return No(r, "protective-device clearing time unknown");

            r.EnclosureCf = EnclosureCorrection(c, vocKv, heightMm, widthMm, depthMm, out string type, out double ees);
            r.EnclosureType = type;
            r.EquivalentSizeIn = ees;
            r.VarCf = VarCf(c, vocKv);

            double iFull = ArcingCurrentKa(c, vocKv, ibfKa, gapMm, false);
            double iRed = ArcingCurrentKa(c, vocKv, ibfKa, gapMm, true);
            double tFull = arcDurationMs(iFull), tRed = arcDurationMs(iRed);
            if (double.IsNaN(tFull) || double.IsNaN(tRed) || tFull <= 0 || tRed <= 0)
                return No(r, "protective-device clearing time unknown");

            r.Full = Case(c, vocKv, ibfKa, gapMm, distMm, r.EnclosureCf, tFull, false);
            r.Reduced = Case(c, vocKv, ibfKa, gapMm, distMm, r.EnclosureCf, tRed, true);
            r.ReducedCaseGovernsEnergy = r.Reduced.IncidentEnergyJcm2 > r.Full.IncidentEnergyJcm2;
            r.IncidentEnergyJcm2 = Math.Max(r.Full.IncidentEnergyJcm2, r.Reduced.IncidentEnergyJcm2);
            r.BoundaryMm = Math.Max(r.Full.BoundaryMm, r.Reduced.BoundaryMm);
            r.Calculated = true;
            return r;
        }

        private static Ieee1584Result No(Ieee1584Result r, string why)
        {
            r.Calculated = false;
            r.NotCalculatedReason = why;
            return r;
        }
    }
}
