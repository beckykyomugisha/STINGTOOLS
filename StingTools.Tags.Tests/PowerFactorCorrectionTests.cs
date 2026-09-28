// ══════════════════════════════════════════════════════════════════════════
//  PowerFactorCorrectionTests.cs — capacitor-bank sizing (ROADMAP ELEC-6).
//
//  Q = P·(tan φ1 − tan φ2). The kvarPerKwAtPf table this replaced had
//  0.85 → 0.31 and 0.95 → 0.10; the true tan(acos) values are 0.620 and 0.329,
//  so 100 kW from 0.85 to 0.95 came out 21 kVAR instead of 29.1 kVAR.
//
//  RED / GREEN, recorded 2026-09-24:
//    table interpolation (0.31 − 0.10) × 100    RED   21.0 kVAR, expected 29.106
//    P·(tan φ1 − tan φ2)                        GREEN
// ══════════════════════════════════════════════════════════════════════════
using System;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PowerFactorCorrectionTests
    {
        // tan(acos 0.85) = √(1 − 0.7225) / 0.85 = √0.2775 / 0.85 = 0.526783 / 0.85 = 0.619744
        // tan(acos 0.95) = √(1 − 0.9025) / 0.95 = √0.0975 / 0.95 = 0.312250 / 0.95 = 0.328684
        [Theory]
        [InlineData(0.85, 0.619744)]
        [InlineData(0.95, 0.328684)]
        [InlineData(1.00, 0.0)]
        public void KvarPerKw_is_tan_acos_pf(double pf, double expected)
        {
            Assert.Equal(expected, PowerFactorCorrection.KvarPerKw(pf), 5);
        }

        // 100 kW, 0.85 → 0.95: Q = 100 × (0.619744 − 0.328684) = 100 × 0.291060 = 29.106 kVAR
        [Fact]
        public void Hundred_kW_from_085_to_095_needs_29_kvar()
        {
            Assert.Equal(29.106, PowerFactorCorrection.RequiredKvar(100, 0.85, 0.95), 3);
        }

        // 200 kVA apparent at 0.80: P = 200 × 0.80 = 160 kW
        //   tan(acos 0.80) = 0.6 / 0.8 = 0.75;  tan(acos 0.95) = 0.328684
        //   Q = 160 × (0.75 − 0.328684) = 160 × 0.421316 = 67.411 kVAR
        [Fact]
        public void From_apparent_power_converts_to_active_first()
        {
            Assert.Equal(67.411, PowerFactorCorrection.RequiredKvarFromKva(200, 0.80, 0.95), 3);
        }

        [Fact]
        public void No_bank_when_already_at_target()
        {
            Assert.Equal(0, PowerFactorCorrection.RequiredKvar(100, 0.96, 0.95));
        }

        [Fact]
        public void Nonsense_power_factor_is_refused_not_guessed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PowerFactorCorrection.KvarPerKw(1.2));
            Assert.Throws<ArgumentOutOfRangeException>(() => PowerFactorCorrection.KvarPerKw(0));
        }
    }
}
