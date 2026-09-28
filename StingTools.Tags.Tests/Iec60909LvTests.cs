// ══════════════════════════════════════════════════════════════════════════
//  Iec60909LvTests.cs — holds the LV fault-level arithmetic to hand-worked
//  IEC 60909-0:2016 examples (ROADMAP ELEC-2).
//
//  Every expected value is computed by hand in the comment above its test.
//  The old FaultCurrentEngine estimate (resistance-only, no c-factor, 240 V
//  line-to-neutral pushed through a sqrt3 line-to-line formula, cable R at
//  70 °C, magnitudes added as scalars) gave 4.85 kA for the first example
//  below, against 9.51 kA by IEC 60909 - it understated the fault level by
//  half, the unsafe direction for breaking capacity.
//
//  RED / GREEN, recorded 2026-09-24:
//    Downstream3PhKa carrying the old formula   RED   4.855 kA, expected 9.509
//    IEC 60909 implementation                   GREEN
// ══════════════════════════════════════════════════════════════════════════
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Iec60909LvTests
    {
        // 400 V L-L, 25 kA upstream, zero-length feeder:
        //   ZQ = c·Un/(√3·Ik) = 1.10 × 400 / (1.7320508 × 25) = 440 / 43.30127 = 10.16136 mΩ
        //   Ik = c·Un/(√3·|ZQ|) = 440 / (1.7320508 × 10.16136) = 25.000 kA
        // i.e. the source impedance round-trips: no cable, no change.
        [Fact]
        public void Zero_length_feeder_returns_the_upstream_fault_level()
        {
            double ka = Iec60909Lv.Downstream3PhKa(25.0, 400.0, new ImpedanceMohm(0, 0));
            Assert.Equal(25.0, ka, 6);
        }

        // Source impedance split, IEC 60909-0 §6.2 default RQ = 0.1·XQ:
        //   ZQ = 10.16136 mΩ (above)
        //   XQ = ZQ / √(1 + 0.1²) = 10.16136 / 1.0049876 = 10.11094 mΩ   (IEC prints 0.995·ZQ = 10.11055)
        //   RQ = 0.1 × 10.11094 = 1.01109 mΩ
        //   check |ZQ| = √(1.01109² + 10.11094²) = √(1.02230 + 102.23111) = 10.16136 mΩ
        [Fact]
        public void Source_impedance_uses_the_IEC_60909_default_R_X_split()
        {
            var zq = Iec60909Lv.SourceImpedance3Ph(400.0, 25.0);
            Assert.Equal(10.11094, zq.X, 4);
            Assert.Equal(1.01109, zq.R, 4);
            Assert.Equal(10.16136, zq.Magnitude, 4);
            Assert.Equal(0.995, Iec60909Lv.SourceXOverZ, 3);
        }

        // 400 V L-L, 25 kA upstream, 20 m of 16 mm² Cu (IEC 60228: 1.15 mΩ/m at 20 °C),
        // X assumed 0.08 mΩ/m, c = 1.10:
        //   source  RQ = 1.01109,  XQ = 10.11094 mΩ
        //   cable   RL = 1.15 × 20 = 23.000, XL = 0.08 × 20 = 1.600 mΩ
        //   Zk = (1.01109 + 23.000) + j(10.11094 + 1.600) = 24.01109 + j11.71094 mΩ
        //   |Zk| = √(24.01109² + 11.71094²) = √(576.533 + 137.146) = √713.679 = 26.71476 mΩ
        //   I"k3 = 1.10 × 400 / (1.7320508 × 26.71476) = 440 / 46.27132 = 9.509 kA
        [Fact]
        public void Three_phase_fault_after_20m_of_16mm2()
        {
            var cable = Iec60909Lv.CableConductor(1.15, 20.0);
            Assert.Equal(23.0, cable.R, 6);
            Assert.Equal(1.6, cable.X, 6);

            double ka = Iec60909Lv.Downstream3PhKa(25.0, 400.0, cable);
            Assert.Equal(9.509, ka, 3);
        }

        // Single-phase 230 V, 6 kA line-to-neutral PFC at the origin, 20 m of 16 mm²:
        //   Zloop,src = c·U0/Ik1 = 1.10 × 230 / 6 = 42.16667 mΩ
        //     X = 42.16667 / 1.0049876 = 41.95740,  R = 0.1 × 41.95740 = 4.19574 mΩ
        //   cable loop (phase + neutral) = 2 × (23.000 + j1.600) = 46.000 + j3.200 mΩ
        //   Zloop = 50.19574 + j45.15740 mΩ,  |Zloop| = √(2519.612 + 2039.191) = 67.51891 mΩ
        //   Ik1 = 1.10 × 230 / 67.51891 = 253 / 67.51891 = 3.747 kA
        [Fact]
        public void Single_phase_fault_counts_phase_and_neutral()
        {
            double ka = Iec60909Lv.Downstream1PhKa(6.0, 230.0, Iec60909Lv.CableConductor(1.15, 20.0));
            Assert.Equal(3.747, ka, 3);
        }

        // Same 400 V / 25 kA / 20 m / 16 mm² case with cmin = 0.95 in BOTH halves:
        //   ZQ = 0.95 × 400 / (√3 × 25) = 380 / 43.30127 = 8.77572 mΩ
        //     XQ = 8.77572 / 1.0049876 = 8.73217, RQ = 0.87322 mΩ
        //   Zk = 23.87322 + j10.33217,  |Zk| = √(569.930 + 106.754) = 26.01316 mΩ
        //   Ik = 380 / (1.7320508 × 26.01316) = 380 / 45.05612 = 8.434 kA
        [Fact]
        public void Minimum_voltage_factor_is_honoured()
        {
            double ka = Iec60909Lv.Downstream3PhKa(25.0, 400.0, Iec60909Lv.CableConductor(1.15, 20.0),
                Iec60909Lv.CMinLv);
            Assert.Equal(8.434, ka, 3);
        }

        [Fact]
        public void Voltage_factors_are_the_IEC_60909_Table_1_LV_values()
        {
            Assert.Equal(1.10, Iec60909Lv.CMaxLv);
            Assert.Equal(1.05, Iec60909Lv.CMaxLv6PctTolerance);
            Assert.Equal(0.95, Iec60909Lv.CMinLv);
        }

        [Fact]
        public void Unknown_inputs_give_zero_not_a_guess()
        {
            Assert.Equal(0, Iec60909Lv.Downstream3PhKa(0, 400, new ImpedanceMohm(1, 0)));
            Assert.Equal(0, Iec60909Lv.Downstream3PhKa(25, 0, new ImpedanceMohm(1, 0)));
            Assert.Equal(0, Iec60909Lv.CableConductor(1.15, 0).Magnitude);
        }
    }
}
