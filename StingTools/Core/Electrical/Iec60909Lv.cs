// Iec60909Lv — Revit-free short-circuit arithmetic for LV distribution,
// following the IEC 60909-0:2016 equivalent-voltage-source method.
//
// Replaces the resistance-only estimate that FaultCurrentEngine used until
// 2026-09 (ROADMAP ELEC-2). That estimate had no voltage factor, no R/X split
// (so a reactive source and a resistive cable were added as scalars, which
// overstates |Z| and UNDERSTATES the fault level - the non-conservative
// direction for breaking capacity), and it put phase-to-neutral volts into a
// line-to-line formula.
//
// Units throughout: volts, kiloamperes, milliohms. V / kA = mOhm, so
//   Z[mOhm] = c * U[V] / (sqrt3 * Ik[kA])  and  Ik[kA] = c * U[V] / (sqrt3 * |Z|[mOhm]).
//
// What this is NOT: a full IEC 60909 study. There is no transformer model
// (the upstream fault level stands in for everything above the first board),
// no motor contribution, no peak current ip, and no zero-sequence network for
// line-to-earth faults. Every default below that is not an IEC 60909 value is
// named as an ASSUMPTION so the caller can surface it.

using System;

namespace StingTools.Core.Electrical
{
    /// <summary>A series impedance in milliohms (R + jX).</summary>
    public readonly struct ImpedanceMohm
    {
        public ImpedanceMohm(double r, double x) { R = r; X = x; }
        public double R { get; }
        public double X { get; }
        public double Magnitude => Math.Sqrt(R * R + X * X);
        public static ImpedanceMohm operator +(ImpedanceMohm a, ImpedanceMohm b)
            => new ImpedanceMohm(a.R + b.R, a.X + b.X);
        public static ImpedanceMohm operator *(double k, ImpedanceMohm z)
            => new ImpedanceMohm(k * z.R, k * z.X);
        public override string ToString() => $"{R:0.###} + j{X:0.###} mOhm";
    }

    public static class Iec60909Lv
    {
        public const double Sqrt3 = 1.7320508075688772;

        /// <summary>
        /// Voltage factor c for MAXIMUM short-circuit current, low voltage
        /// (100 V - 1000 V), systems with a +10 % voltage tolerance.
        /// IEC 60909-0:2016 Table 1. 230/400 V to BS EN 60038 / IEC 60038 is
        /// declared +10 % / -10 %, so this is the value that applies; the
        /// alternative 1.05 is for systems with a +6 % tolerance.
        /// </summary>
        public const double CMaxLv = 1.10;

        /// <summary>IEC 60909-0:2016 Table 1: cmax for LV systems with a +6 % tolerance.</summary>
        public const double CMaxLv6PctTolerance = 1.05;

        /// <summary>IEC 60909-0:2016 Table 1: cmin for low voltage (100 V - 1000 V).</summary>
        public const double CMinLv = 0.95;

        /// <summary>
        /// IEC 60909-0:2016 §6.2 (network feeders): where the feeder's R and X
        /// are not known, take RQ = 0.1·XQ with XQ = 0.995·ZQ. IEC states this for
        /// feeders above 35 kV; applying it to an LV upstream fault level is an
        /// ASSUMPTION. It makes the source almost purely reactive, which gives
        /// close to the highest downstream Ik for a given |ZQ| when the cable
        /// is mostly resistive - the conservative side for breaking capacity.
        /// </summary>
        public const double SourceROverX = 0.1;

        /// <summary>
        /// XQ/ZQ implied by <see cref="SourceROverX"/>: 1/√(1 + 0.1²) = 0.99504,
        /// which IEC 60909-0 §6.2 prints rounded as 0.995. The unrounded value is
        /// used so that |RQ + jXQ| = ZQ exactly and a zero-length feeder returns
        /// the upstream fault level unchanged (with 0.995 it drifts by 0.004 %).
        /// </summary>
        public static readonly double SourceXOverZ = 1.0 / Math.Sqrt(1.0 + SourceROverX * SourceROverX);

        /// <summary>
        /// ASSUMPTION, not a standard value: reactance of one conductor of an LV
        /// multicore cable, mOhm/m. Manufacturer data for PVC/XLPE multicore Cu
        /// cables sits around 0.07 - 0.09 mOhm/m; replace with catalogue data
        /// when known. Single-core cables spaced apart run higher.
        /// </summary>
        public const double AssumedCableReactanceMohmPerM = 0.08;

        /// <summary>
        /// Upstream (network feeder) impedance for a 3-phase fault level:
        /// ZQ = c·Un / (√3·I"kQ), IEC 60909-0:2016 §6.2, split R/X per the §6.2 default.
        /// <paramref name="unLineToLineV"/> is the nominal LINE-TO-LINE voltage.
        /// </summary>
        public static ImpedanceMohm SourceImpedance3Ph(double unLineToLineV, double ikqKa, double c = CMaxLv)
        {
            if (unLineToLineV <= 0 || ikqKa <= 0) return new ImpedanceMohm(0, 0);
            double z = c * unLineToLineV / (Sqrt3 * ikqKa);
            return SplitSource(z);
        }

        /// <summary>
        /// Upstream LOOP impedance (phase + neutral) for a single-phase system
        /// whose upstream fault level is the line-to-neutral prospective fault
        /// current at the origin: Zloop = c·U0 / Ik1. Same R/X split assumption.
        /// </summary>
        public static ImpedanceMohm SourceLoopImpedance1Ph(double u0V, double ik1Ka, double c = CMaxLv)
        {
            if (u0V <= 0 || ik1Ka <= 0) return new ImpedanceMohm(0, 0);
            return SplitSource(c * u0V / ik1Ka);
        }

        private static ImpedanceMohm SplitSource(double zMohm)
        {
            double x = SourceXOverZ * zMohm;
            return new ImpedanceMohm(SourceROverX * x, x);
        }

        /// <summary>
        /// One conductor of a cable run. For MAXIMUM fault current IEC 60909-0
        /// takes conductor resistance at 20 °C, so pass the 20 °C mOhm/m
        /// (IEC 60228 values) unchanged.
        /// </summary>
        public static ImpedanceMohm CableConductor(double rMohmPerM20C, double lengthM,
            double xMohmPerM = AssumedCableReactanceMohmPerM)
        {
            if (lengthM <= 0 || rMohmPerM20C <= 0) return new ImpedanceMohm(0, 0);
            return new ImpedanceMohm(rMohmPerM20C * lengthM, Math.Max(0, xMohmPerM) * lengthM);
        }

        /// <summary>Initial symmetrical 3-phase short-circuit current I"k3 = c·Un / (√3·|Zk|), kA.</summary>
        public static double Ik3Ka(double unLineToLineV, ImpedanceMohm zk, double c = CMaxLv)
        {
            double z = zk.Magnitude;
            if (unLineToLineV <= 0 || z <= 0) return 0;
            return c * unLineToLineV / (Sqrt3 * z);
        }

        /// <summary>Line-to-neutral fault current through a loop impedance: c·U0 / |Zloop|, kA.</summary>
        public static double Ik1LoopKa(double u0V, ImpedanceMohm zLoop, double c = CMaxLv)
        {
            double z = zLoop.Magnitude;
            if (u0V <= 0 || z <= 0) return 0;
            return c * u0V / z;
        }

        /// <summary>
        /// 3-phase fault level at the end of a feeder, from the fault level at
        /// its supply end. The source impedance is recovered with the SAME c,
        /// so a zero-length feeder returns the upstream value exactly.
        /// </summary>
        public static double Downstream3PhKa(double upstreamKa, double unLineToLineV,
            ImpedanceMohm feederConductor, double c = CMaxLv)
        {
            if (upstreamKa <= 0 || unLineToLineV <= 0) return 0;
            var zk = SourceImpedance3Ph(unLineToLineV, upstreamKa, c) + feederConductor;
            return Ik3Ka(unLineToLineV, zk, c);
        }

        /// <summary>
        /// Single-phase (line-to-neutral) fault level at the end of a feeder.
        /// The feeder contributes its phase AND neutral conductor, taken as
        /// equal (2 × <paramref name="feederConductor"/>) - an ASSUMPTION where
        /// a reduced neutral is used.
        /// </summary>
        public static double Downstream1PhKa(double upstreamKa, double u0V,
            ImpedanceMohm feederConductor, double c = CMaxLv)
        {
            if (upstreamKa <= 0 || u0V <= 0) return 0;
            var zLoop = SourceLoopImpedance1Ph(u0V, upstreamKa, c) + 2.0 * feederConductor;
            return Ik1LoopKa(u0V, zLoop, c);
        }
    }
}
