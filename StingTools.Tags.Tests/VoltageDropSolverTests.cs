// ══════════════════════════════════════════════════════════════════════════
//  VoltageDropSolverTests.cs — Core/Calc/VoltageDropSolver (ROADMAP ELEC-5).
//
//  History:
//   * The solver multiplied the 1-ph figure by √3 for three-phase — twice the
//     true drop. Fixed to √3/2 (RED 9.699 V / GREEN 4.850 V, 2026-09-24).
//   * Cross-check 2026-09-24: it still disagreed with the BS 7671 sizer — it
//     held the 4D2B r column for 50–150 mm² and derived 3-ph as 1-ph × √3/2
//     instead of reading the tabulated 3/4-core column. It now reads the SAME
//     bs7671Appendix4 table (z column, tabulated 3-ph) as Bs7671CableSizer.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Calc;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class VoltageDropSolverTests
    {
        private static Bs7671CapacityTable ShippedTable()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            string path = Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json");
            var t = Bs7671Data.FromJson(JObject.Parse(File.ReadAllText(path))).FindTable("Cu", "PVC70", "C");
            Assert.NotNull(t);
            return t;
        }

        // 16 mm² Cu, 40 A, 50 m, three-phase 400 V:
        //   Table 4D2B 3/4-core 3-ph column at 16 mm² = 2.4 mV/A/m (tabulated, not 2.8 × √3/2 = 2.425)
        //   Vd = 2.4 × 40 × 50 / 1000 = 4.800 V (line-to-line)
        //   %  = 4.800 / 400 × 100 = 1.200 %
        [Fact]
        public void Three_phase_reads_the_tabulated_3_and_4_core_column()
        {
            var r = VoltageDropSolver.Solve(new VoltageDropQuery
            {
                CsaMm2 = 16, LoadAmps = 40, LengthM = 50, NominalVoltageV = 400, ThreePhase = true
            });
            Assert.Equal(2.4, r.MvPerAPerM, 3);
            Assert.Equal(4.800, r.VoltDropV, 3);
            Assert.Equal(1.200, r.VoltDropPct, 3);
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

        // 50 mm², 100 A, 40 m, 1-ph 230 V. z = 0.94 mV/A/m (the r column is 0.93):
        //   Vd = 0.94 × 100 × 40 / 1000 = 3.760 V; 3.76 / 230 = 1.6348 %
        [Fact]
        public void Large_sizes_use_z_not_r()
        {
            var r = VoltageDropSolver.Solve(new VoltageDropQuery
            {
                CsaMm2 = 50, LoadAmps = 100, LengthM = 40, NominalVoltageV = 230
            });
            Assert.Equal(0.94, r.MvPerAPerM, 3);
            Assert.Equal(3.760, r.VoltDropV, 3);
            Assert.Equal(1.635, r.VoltDropPct, 3);
        }

        [Fact]
        public void Built_in_copy_equals_the_shipped_data_file()
        {
            var t = ShippedTable();
            Assert.Equal(t.Rows.Count, VoltageDropSolver.BuiltIn4D2B.Count);
            foreach (var row in t.Rows)
            {
                var b = VoltageDropSolver.BuiltIn4D2B.Single(x => Math.Abs(x.Csa - row.CsaMm2) < 1e-6);
                Assert.Equal(row.MvAm1ph, b.Mv1ph, 6);
                Assert.Equal(row.MvAm3ph, b.Mv3ph, 6);
            }
        }

        [Theory]
        [InlineData(2.5, false)]
        [InlineData(95, false)]
        [InlineData(95, true)]
        [InlineData(240, true)]
        public void Solver_and_sizer_read_the_same_mV_per_A_per_m(double csa, bool threePhase)
        {
            var t = ShippedTable();
            var row = t.Rows.Single(x => Math.Abs(x.CsaMm2 - csa) < 1e-6);
            var q = new VoltageDropQuery { CsaMm2 = csa, LoadAmps = 10, LengthM = 10, NominalVoltageV = 400, ThreePhase = threePhase };
            double expected = threePhase ? row.MvAm3ph : row.MvAm1ph;
            Assert.Equal(expected, VoltageDropSolver.Solve(q, t).MvPerAPerM, 6);
            Assert.Equal(expected, VoltageDropSolver.Solve(q).MvPerAPerM, 6);
        }

        [Fact]
        public void Aluminium_is_refused_not_multiplied()
        {
            var r = VoltageDropSolver.Solve(new VoltageDropQuery { CsaMm2 = 16, Material = "AL" });
            Assert.False(r.Computed);
            Assert.Equal(0.0, r.VoltDropV);
            Assert.Contains("aluminium", r.Refusal);
        }

        [Fact]
        public void Csa_outside_the_table_is_refused()
        {
            var r = VoltageDropSolver.Solve(new VoltageDropQuery { CsaMm2 = 300 });
            Assert.False(r.Computed);
            Assert.Contains("outside", r.Refusal);
        }
    }
}
