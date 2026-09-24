// ══════════════════════════════════════════════════════════════════════════
//  VoltageDropSolverTests.cs — Core/Calc/VoltageDropSolver three-phase factor
//  (ROADMAP ELEC-5).
//
//  BS 7671 Appendix 4 tabulates the 3-/4-core three-phase mV/A/m at about
//  0.866 (= √3/2) of the 2-core single-phase value: the single-phase figure
//  is a two-conductor loop, the three-phase line-to-line drop is √3 × one
//  conductor. The solver multiplied by √3 instead - twice the true drop.
//
//  RED / GREEN, recorded 2026-09-24:
//    ×√3 factor     RED   9.699 V, expected 4.850 V
//    ×√3/2 factor   GREEN
// ══════════════════════════════════════════════════════════════════════════
using StingTools.Core.Calc;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class VoltageDropSolverTests
    {
        // 16 mm² Cu, 40 A, 50 m, three-phase 400 V:
        //   mV/A/m (1-ph, 2-core) = 2.8
        //   3-ph mV/A/m = 2.8 × 0.8660254 = 2.42487
        //   Vd = 2.42487 × 40 × 50 / 1000 = 4.84974 V (line-to-line)
        //   %  = 4.84974 / 400 × 100 = 1.2124 %
        [Fact]
        public void Three_phase_uses_root3_over_2_of_the_single_phase_figure()
        {
            var r = VoltageDropSolver.Solve(new VoltageDropQuery
            {
                CsaMm2 = 16, LoadAmps = 40, LengthM = 50, NominalVoltageV = 400, ThreePhase = true
            });
            Assert.Equal(4.850, r.VoltDropV, 3);
            Assert.Equal(1.212, r.VoltDropPct, 3);
        }

        // Same cable single-phase 230 V: Vd = 2.8 × 40 × 50 / 1000 = 5.600 V; 5.6/230 = 2.4348 %
        [Fact]
        public void Single_phase_is_the_tabulated_loop_figure()
        {
            var r = VoltageDropSolver.Solve(new VoltageDropQuery
            {
                CsaMm2 = 16, LoadAmps = 40, LengthM = 50, NominalVoltageV = 230, ThreePhase = false
            });
            Assert.Equal(5.600, r.VoltDropV, 3);
            Assert.Equal(2.435, r.VoltDropPct, 3);
        }

        [Fact]
        public void Three_phase_factor_matches_the_BS7671_table_ratio_at_16mm2()
        {
            // BS 7671 App 4: 16 mm² 2-core 2.8 mV/A/m, 3-/4-core 2.4 mV/A/m -> ratio 0.857;
            // √3/2 = 0.866 reproduces the tabulated 3-phase value to its printed precision.
            Assert.Equal(2.4, 2.8 * VoltageDropSolver.ThreePhaseFactor, 1);
        }
    }
}
