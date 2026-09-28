// ══════════════════════════════════════════════════════════════════════════
//  ConductorResistanceTests.cs — the resistance data behind fault level and Zs.
//
//  copperTables[].mohm_per_m in STING_WIRE_TABLES.json feeds FaultCurrentEngine
//  (maximum fault, 20 °C per IEC 60909-0) and BS7671ComplianceEngine.ComputeZs
//  (R1 + R2 at conductor operating temperature). It held hot / rounded values for
//  several sizes until 2026-09-24. These tests pin every shipped row to BS EN 60228
//  class 2 plain copper at 20 °C, and run a Zs example through the same code the
//  plugin uses (CableResistance, extracted Revit-free from those two engines).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.FaultCurrent;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ConductorResistanceTests
    {
        // BS EN 60228 Table 2, class 2 stranded, plain copper, max resistance at 20 °C (mΩ/m).
        private static readonly (double Csa, double Mohm)[] En60228 =
        {
            (1.5, 12.1), (2.5, 7.41), (4, 4.61), (6, 3.08), (10, 1.83), (16, 1.15),
            (25, 0.727), (35, 0.524), (50, 0.387), (70, 0.268), (95, 0.193),
            (120, 0.153), (150, 0.124), (185, 0.0991), (240, 0.0754),
        };

        private static JObject Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json")));
        }

        [Fact]
        public void Every_BS7671_copper_table_row_is_BS_EN_60228_class_2_at_20C()
        {
            var tables = (Root()["copperTables"] as JArray).OfType<JObject>()
                .Where(t => (string)t["standard"] == "BS7671").ToList();
            Assert.NotEmpty(tables);
            foreach (var t in tables)
            {
                var sizes = (t["sizes"] as JArray).OfType<JObject>().ToList();
                // Every EN 60228 size is present — an empty or truncated table must fail.
                Assert.Equal(En60228.Length, sizes.Count);
                foreach (var (csa, mohm) in En60228)
                {
                    var row = sizes.Single(s => Math.Abs((double)s["csaMm2"] - csa) < 1e-6);
                    Assert.Equal(mohm, (double)row["mohm_per_m"], 6);
                }
            }
        }

        [Fact]
        public void WireTableSet_reads_the_same_values_the_fault_engine_uses()
        {
            var ws = WireTableSet.FromJson(Root());
            Assert.Equal(En60228.Length, ws.Count);
            foreach (var (csa, mohm) in En60228)
                Assert.Equal(mohm, ws.GetMohmPerMetre(csa, "Cu"), 6);
        }

        [Fact]
        public void Zs_worked_example_2_5_over_1_5_twin_and_earth_20m()
        {
            // Ze = 0.35 Ω (TN-C-S), 2.5 mm² line / 1.5 mm² CPC Cu, 20 m, PVC 70 °C:
            //   temperature factor = 1 + 0.00393 × (70 − 20) = 1.1965
            //   R1 = 7.41 × 1.1965 × 20 = 177.321 mΩ
            //   R2 = 12.1 × 1.1965 × 20 = 289.553 mΩ
            //   Zs = 0.35 + (177.321 + 289.553) / 1000 = 0.81687 Ω
            // Cross-check: IET On-Site Guide (R1+R2) for 2.5/1.5 = 19.51 mΩ/m at 20 °C;
            //   19.51 × 1.1965 × 20 / 1000 = 0.4669 Ω → Zs 0.8169 Ω.
            var ws = WireTableSet.FromJson(Root());
            Assert.Equal(177.321, CableResistance.RunMohm(ws, 2.5, "Cu", 20, insulation: "PVC"), 3);
            Assert.Equal(289.553, CableResistance.RunMohm(ws, 1.5, "Cu", 20, insulation: "PVC"), 3);
            double zs = CableResistance.ZsOhm(0.35, 2.5, 1.5, 20, "Cu", "PVC", ws);
            Assert.Equal(0.81687, zs, 4);
            // B32 limit (BS 7671 Table 41.3, Cmin 0.95): 1.37 Ω — this circuit passes.
            Assert.True(zs < 1.37);
        }

        [Fact]
        public void Undeclared_cpc_is_taken_equal_to_the_line()
        {
            // 4 mm², 10 m, 70 °C: R1 = R2 = 4.61 × 1.1965 × 10 = 55.159 mΩ → Zs = 0.2 + 0.110317
            var ws = WireTableSet.FromJson(Root());
            Assert.Equal(0.31032, CableResistance.ZsOhm(0.2, 4, 0, 10, "Cu", "PVC", ws), 4);
        }
    }
}
