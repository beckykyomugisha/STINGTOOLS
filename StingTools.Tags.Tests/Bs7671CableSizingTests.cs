// ══════════════════════════════════════════════════════════════════════════
//  Bs7671CableSizingTests.cs — ELEC-3 / ELEC-5.
//
//  The BS 7671 cable sizer used an uncited ladder (2.5 mm² = 17 A) and a flat
//  XLPE ×1.18 multiplier, and never read STING_WIRE_TABLES.json. These tests run
//  the replacement (Core/Electrical/Bs7671CableSizing.cs) against the SHIPPED data
//  file, so a wrong number in the data fails here rather than on a drawing.
//
//  Reference values are BS 7671:2018 Appendix 4 Table 4D2A (70 °C thermoplastic
//  multicore, non-armoured, Cu), reference method C, and its voltage-drop
//  companion Table 4D2B.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Bs7671CableSizingTests
    {
        private static readonly int[] Mcb = { 6, 10, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125 };

        private static Bs7671Data Data()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            string path = Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json");
            return Bs7671Data.FromJson(JObject.Parse(File.ReadAllText(path)));
        }

        private static Bs7671SizingInput Pvc(double ib, double lengthM, double vdLimit,
            int phases = 1, double volts = 230.0) => new Bs7671SizingInput
        {
            DesignCurrentA = ib, VoltageV = volts, Phases = phases, LengthM = lengthM,
            InstallMethod = "C", Insulation = "PVC70", Material = "Cu",
            AmbientTempC = 30, VdLimitPct = vdLimit, DeviceRatingsA = Mcb,
        };

        // ── the shipped data, row by row ─────────────────────────────────────

        [Theory]
        // csa, It 2-core 1-ph, It 3/4-core 3-ph, mV/A/m 1-ph, mV/A/m 3-ph   (Tables 4D2A / 4D2B, method C)
        [InlineData(1.5, 19.5, 17.5, 29.0, 25.0)]
        [InlineData(2.5, 27.0, 24.0, 18.0, 15.0)]
        [InlineData(4.0, 36.0, 32.0, 11.0, 9.5)]
        [InlineData(6.0, 46.0, 41.0, 7.3, 6.4)]
        [InlineData(10.0, 63.0, 57.0, 4.4, 3.8)]
        [InlineData(16.0, 85.0, 76.0, 2.8, 2.4)]
        public void Table_4D2A_method_C_rows_match_BS7671(double csa, double it1, double it3, double mv1, double mv3)
        {
            var table = Data().FindTable("Cu", "PVC70", "C");
            Assert.NotNull(table);
            Assert.Equal("4D2A", table.Id);
            var row = table.Rows.Single(r => Math.Abs(r.CsaMm2 - csa) < 0.01);
            Assert.Equal(it1, row.It1ph, 3);
            Assert.Equal(it3, row.It3ph, 3);
            Assert.Equal(mv1, row.MvAm1ph, 3);
            Assert.Equal(mv3, row.MvAm3ph, 3);
            Assert.True(row.Verified);
        }

        [Fact]
        public void Large_rows_have_checked_capacity_but_unchecked_voltage_drop()
        {
            var table = Data().FindTable("Cu", "PVC70", "C");
            Assert.NotNull(table);
            // Assert.All over an empty sequence passes, so pin the row count first:
            // 25, 35, 50, 70, 95, 120, 150, 185, 240 mm² = 9 rows.
            var large = table.Rows.Where(r => r.CsaMm2 >= 25).ToList();
            Assert.Equal(9, large.Count);
            // It agrees with IEC 60364-5-52 column C (2026-09-27); 4D2B mV/A/m has no second source.
            Assert.All(large, r => Assert.True(r.Verified));
            Assert.All(large, r => Assert.False(r.MvVerified));
        }

        [Fact]
        public void Every_shipped_table_is_well_formed()
        {
            var d = Data();
            Assert.Equal(20, d.Tables.Count);
            foreach (var t in d.Tables)
            {
                Assert.False(string.IsNullOrEmpty(t.VoltDropTable), t.Id);
                Assert.Contains(t.CableType, new[] { "Multicore", "SingleCore", "ArmouredMulticore" });
                Assert.NotEmpty(t.Rows);
                // Sorted, positive, and capacity rising with size in both columns.
                for (int i = 1; i < t.Rows.Count; i++)
                {
                    Assert.True(t.Rows[i].CsaMm2 > t.Rows[i - 1].CsaMm2, $"{t.Id} {t.InstallMethod} order");
                    Assert.True(t.Rows[i].It1ph > t.Rows[i - 1].It1ph, $"{t.Id} {t.InstallMethod} {t.Rows[i].CsaMm2} It_1ph");
                    Assert.True(t.Rows[i].It3ph > t.Rows[i - 1].It3ph, $"{t.Id} {t.InstallMethod} {t.Rows[i].CsaMm2} It_3ph");
                }
                // A (table, method, cable type) appears once, so lookup is unambiguous.
                Assert.Single(d.Tables, x => x.Insulation == t.Insulation && x.CableType == t.CableType && x.InstallMethod == t.InstallMethod);
            }
        }

        // ── the worked example ───────────────────────────────────────────────

        [Fact]
        public void Worked_example_32A_30m_at_5pct_gives_4mm2()
        {
            // Ib = 32 A, 230 V single-phase, 30 m, PVC/PVC multicore Cu, method C,
            // 30 °C (Ca = 1.00), not grouped (Cg = 1.00), Ci = 1.00, MCB (Cf = 1.00).
            //   In  = 32 A (next BS EN 60898 rating ≥ Ib)
            //   It ≥ In / (Ca·Cg·Ci·Cf) = 32 / 1 = 32 A
            //   2.5 mm²: It 27 A < 32 → fails on current
            //   4 mm²:   It 36 A ≥ 32 → Iz 36 A ≥ In 32 A ✓
            //   VD = 11 mV/A/m × 32 A × 30 m / 1000 = 10.56 V = 10.56 / 230 = 4.59 % ≤ 5 % ✓
            // → 4 mm²
            var r = Bs7671CableSizer.Size(Pvc(32, 30, 5.0), Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(4.0, r.CsaMm2);
            Assert.Equal(32, r.DeviceRatingA);
            Assert.Equal(36.0, r.TabulatedItA);
            Assert.Equal(36.0, r.IzA, 3);
            Assert.Equal(10.56, r.VoltDropV, 2);
            Assert.Equal(4.591, r.VoltDropPct, 2);
            Assert.Equal(4.0, r.CapacityOnlyCsaMm2);
            Assert.Contains("4D2A", r.Basis);
            Assert.Contains("4D2B", r.Basis);
            Assert.Contains("Ca=1.00", r.Basis);
        }

        [Fact]
        public void Worked_example_at_3pct_upsizes_to_10mm2()
        {
            // Same circuit, VD limit 3 %:
            //   4 mm²:  11  × 32 × 30 / 1000 = 10.56 V = 4.59 % > 3 %  ✗
            //   6 mm²:  7.3 × 32 × 30 / 1000 =  7.008 V = 3.05 % > 3 % ✗
            //   10 mm²: 4.4 × 32 × 30 / 1000 =  4.224 V = 1.84 % ≤ 3 % ✓
            // → 10 mm² (current alone needed only 4 mm²)
            var r = Bs7671CableSizer.Size(Pvc(32, 30, 3.0), Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(10.0, r.CsaMm2);
            Assert.Equal(4.0, r.CapacityOnlyCsaMm2);
            Assert.Equal(4.224, r.VoltDropV, 3);
            Assert.Equal(1.837, r.VoltDropPct, 2);
            Assert.Contains("upsized for voltage drop", r.Basis);
        }

        [Fact]
        public void Three_phase_uses_the_3_and_4_core_columns()
        {
            // Ib 30 A, 400 V 3-ph, 50 m, 5 %: In 32 → It ≥ 32; 3-core 2.5 mm² 24 A ✗, 4 mm² 32 A ✓.
            // VD = 9.5 × 30 × 50 / 1000 = 14.25 V = 3.56 % of 400 V ✓.
            var r = Bs7671CableSizer.Size(Pvc(30, 50, 5.0, phases: 3, volts: 400), Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(4.0, r.CsaMm2);
            Assert.Equal(32.0, r.TabulatedItA);
            Assert.Equal(14.25, r.VoltDropV, 2);
            Assert.Equal(3.5625, r.VoltDropPct, 3);
        }

        // ── correction factors ───────────────────────────────────────────────

        [Fact]
        public void Grouping_factor_Cg_from_Table_4C1_is_applied()
        {
            // Ib 20 A → In 20 A. 3 circuits bunched: Cg 0.70 → It ≥ 20 / 0.70 = 28.6 A.
            // 2.5 mm² (27 A) ✗ → 4 mm² (36 A) ✓; Iz = 36 × 0.70 = 25.2 A ≥ 20 ✓.
            var i = Pvc(20, 5, 5.0);
            i.GroupedCircuits = 3;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(0.70, r.Cg, 3);
            Assert.Equal(28.57, r.RequiredItA, 2);
            Assert.Equal(4.0, r.CsaMm2);
            Assert.Equal(25.2, r.IzA, 3);

            // Ungrouped the same circuit is 2.5 mm² (27 A ≥ 20 A).
            Assert.Equal(2.5, Bs7671CableSizer.Size(Pvc(20, 5, 5.0), Data()).CsaMm2);
        }

        [Fact]
        public void Grouping_count_between_rows_reads_the_larger_count()
        {
            // 10 circuits bunched: no 10 row; the 12-circuit row (0.45) is used.
            var i = Pvc(6, 5, 5.0);
            i.GroupedCircuits = 10;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.Equal(0.45, r.Cg, 3);
        }

        [Fact]
        public void Semi_enclosed_fuse_applies_Cf_0_725()
        {
            // Ib 20 A, In 20 A BS 3036: It ≥ 20 / 0.725 = 27.6 A → 2.5 mm² (27) ✗ → 4 mm².
            var i = Pvc(20, 5, 5.0);
            i.SemiEnclosedFuse = true;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.Equal(0.725, r.Cf, 3);
            Assert.Equal(27.59, r.RequiredItA, 2);
            Assert.Equal(4.0, r.CsaMm2);
        }

        [Fact]
        public void Semi_enclosed_fuse_picks_In_from_the_BS3036_ratings()
        {
            // Ib 22 A on a BS 3036 fuse: ratings 5/15/20/30/45/60/100 → In 30 A (an MCB list
            // would give 25 A, which is not a BS 3036 rating).
            // It ≥ 30 / 0.725 = 41.38 A → 4 mm² (36) ✗ → 6 mm² (46) ✓.
            var i = Pvc(22, 5, 5.0);
            i.SemiEnclosedFuse = true;
            i.DeviceRatingsA = ProtectiveDeviceSelection.Bs3036SemiEnclosedFuseRatingsA;
            i.DeviceLabel = ProtectiveDeviceSelection.Bs3036Label;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(30, r.DeviceRatingA);
            Assert.Equal(41.38, r.RequiredItA, 2);
            Assert.Equal(6.0, r.CsaMm2);
            Assert.Contains("BS 3036 semi-enclosed fuse", r.Basis);
            Assert.DoesNotContain("MCB", r.Basis);
        }

        [Fact]
        public void Ambient_uses_Table_4B1_and_the_hotter_row()
        {
            // 37 °C → read at the 40 °C row, PVC Ca = 0.87. Ib 25 → In 25 → It ≥ 28.7 A → 4 mm².
            var i = Pvc(25, 5, 5.0);
            i.AmbientTempC = 37;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.Equal(0.87, r.Ca, 3);
            Assert.Equal(4.0, r.CsaMm2);
            Assert.Contains("40 °C row", r.Basis);
        }

        [Fact]
        public void Below_30C_takes_no_uplift()
        {
            var i = Pvc(25, 5, 5.0);
            i.AmbientTempC = 20;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.Equal(1.00, r.Ca, 3);
            Assert.Contains("no uplift", r.Basis);
        }

        // ── refusals ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Cu", "XLPE90", "C", "SingleCore")]   // Table 4E1A not shipped
        [InlineData("Cu", "PVC70", "E", "SingleCore")]    // 4D1A has no method E
        [InlineData("Cu", "PVC70", "D1", "Multicore")]    // 4D2A has no method D
        [InlineData("Al", "PVC70", "C", "Multicore")]     // no aluminium table
        public void Missing_table_is_refused_not_approximated(string mat, string ins, string method, string cableType)
        {
            var i = Pvc(20, 10, 5.0);
            i.Material = mat; i.Insulation = ins; i.InstallMethod = method; i.CableType = cableType;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.False(r.Sized);
            Assert.Equal(0.0, r.CsaMm2);
            Assert.Contains("No BS 7671 Appendix 4 capacity table", r.Refusal);
        }

        [Fact]
        public void No_data_section_is_refused()
        {
            var r = Bs7671CableSizer.Size(Pvc(20, 10, 5.0), Bs7671Data.FromJson(new JObject()));
            Assert.False(r.Sized);
        }

        [Fact]
        public void Large_rows_carry_a_verify_flag_for_voltage_drop()
        {
            // Ib 100 A → In 100 A → It ≥ 100 → 16 mm² (85) ✗ → 25 mm² (112): It checked, mV/A/m not.
            var r = Bs7671CableSizer.Size(Pvc(100, 10, 5.0), Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(25.0, r.CsaMm2);
            Assert.True(r.UnverifiedRow);
            Assert.False(r.UnverifiedCapacity);
            Assert.True(r.UnverifiedVoltDrop);
            Assert.Contains("VERIFY", r.Basis);
            Assert.Contains("mV/A/m (Table 4D2B)", r.Basis);
        }

        // ── the 2026-09-27 tables ────────────────────────────────────────────

        [Theory]
        [InlineData("B")]
        [InlineData("B2")]   // IEC sub-code for multicore in conduit on a wall = BS method B of 4D2A
        public void Method_B_multicore_uses_4D2A_method_B(string method)
        {
            // Ib 20 → In 20 → It ≥ 20: 4D2A method B 1.5 mm² 16.5 A ✗ → 2.5 mm² 23 A ✓.
            var i = Pvc(20, 10, 5.0);
            i.InstallMethod = method;
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(2.5, r.CsaMm2);
            Assert.Equal(23.0, r.TabulatedItA);
            Assert.False(r.UnverifiedRow);
        }

        [Fact]
        public void Single_core_selects_Table_4D1A()
        {
            var t = Data().FindTable("Cu", "PVC70", "A1", "SingleCore");
            Assert.NotNull(t);
            Assert.Equal("4D1A", t.Id);
            Assert.Equal("A", t.InstallMethod);
            Assert.Equal("4D2A", Data().FindTable("Cu", "PVC70", "A2").Id);
        }

        [Fact]
        public void Xlpe_sizes_on_capacity_but_is_refused_for_want_of_voltage_drop_data()
        {
            // 4E2A method C: 1.0 mm² 19 A ✗, 1.5 mm² 24 A ✓ — but Table 4E2B is not carried,
            // so no size can be checked for voltage drop and none is selected.
            var i = Pvc(20, 10, 5.0);
            i.Insulation = "XLPE90";
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.False(r.Sized);
            Assert.Equal(1.5, r.CapacityOnlyCsaMm2);
            Assert.Contains("4E2B mV/A/m is not carried", r.Refusal);
            Assert.Contains("will not estimate voltage drop", r.Refusal);
        }

        [Fact]
        public void Armoured_single_source_rows_are_flagged_on_capacity()
        {
            // 4D4A method C: 1.5 mm² 21 A ≥ 20 A; single-source row.
            var i = Pvc(20, 5, 5.0);
            i.CableType = "ArmouredMulticore";
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(1.5, r.CsaMm2);
            Assert.Equal(21.0, r.TabulatedItA);
            Assert.True(r.UnverifiedCapacity);
            Assert.Contains("It (Table 4D4A) and mV/A/m (Table 4D4B)", r.Basis);
        }

        [Fact]
        public void Ladder_two_circuits_read_the_three_circuit_row()
        {
            // The 2-circuit ladder factor is not carried (sources disagree), so the lower 3-circuit value is used.
            var i = Pvc(6, 5, 5.0);
            i.GroupedCircuits = 2;
            i.GroupingArrangement = "SingleLayerLadderCleats";
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.Equal(0.82, r.Cg, 3);
            Assert.Contains("read at the 3-circuit row", r.Basis);
        }

        [Fact]
        public void Perforated_tray_beyond_nine_circuits_takes_no_further_reduction()
        {
            var i = Pvc(6, 5, 5.0);
            i.GroupedCircuits = 14;
            i.GroupingArrangement = "SingleLayerPerforatedTray";
            var r = Bs7671CableSizer.Size(i, Data());
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(0.72, r.Cg, 3);
        }

        [Fact]
        public void A_single_verified_flag_covers_both_columns_in_older_data()
        {
            var d = Bs7671Data.FromJson(JObject.Parse(
                "{ \"bs7671Appendix4\": { \"capacityTables\": [ { \"id\": \"4D2A\", \"conductor\": \"Cu\", \"insulation\": \"PVC70\", " +
                "\"installMethod\": \"C\", \"sizes\": [ { \"csaMm2\": 2.5, \"It_1ph\": 27, \"It_3ph\": 24, \"mVAm_1ph\": 18, \"mVAm_3ph\": 15, \"verified\": true } ] } ] } }"));
            var row = d.FindTable("Cu", "PVC70", "C").Rows.Single();
            Assert.True(row.Verified);
            Assert.True(row.MvVerified);
        }

        [Fact]
        public void Voltage_drop_unreachable_is_refused_with_the_capacity_size_named()
        {
            // 32 A over 1000 m at 3 %: even 240 mm² (0.23 mV/A/m) gives 7.36 V = 3.2 %.
            var r = Bs7671CableSizer.Size(Pvc(32, 1000, 3.0), Data());
            Assert.False(r.Sized);
            Assert.Contains("4 mm² carries the current", r.Refusal);
        }

        // ── protective device ────────────────────────────────────────────────

        [Fact]
        public void BS7671_does_not_apply_the_NEC_125pct_continuous_factor()
        {
            // Ib 26 A continuous. BS: In 32 A (≥ 26). NEC ×1.25 = 32.5 → 40 A would be chosen.
            var bs = ProtectiveDeviceSelection.Select(26, isNec: false, continuous: true, Mcb, izA: null);
            Assert.Equal(32, bs.ProposedA);
            Assert.Contains("not applied", bs.Note);
            var nec = ProtectiveDeviceSelection.Select(26, isNec: true, continuous: true, Mcb, izA: null);
            Assert.Equal(40, nec.ProposedA);
        }

        [Fact]
        public void Device_larger_than_Iz_is_blocked()
        {
            // Ib 30 A → In 32 A, cable 2.5 mm² Iz 27 A: 32 > 27 → must not be applied.
            var s = ProtectiveDeviceSelection.Select(30, false, false, Mcb, izA: 27);
            Assert.True(s.Blocked);
            Assert.Equal(32, s.ProposedA);
            var ok = ProtectiveDeviceSelection.Select(30, false, false, Mcb, izA: 36);
            Assert.False(ok.Blocked);
        }

        [Fact]
        public void No_rating_large_enough_is_blocked_not_capped()
        {
            // The old sizer returned the LARGEST MCB (125 A) for a 150 A load.
            var s = ProtectiveDeviceSelection.Select(150, false, false, Mcb, izA: null);
            Assert.True(s.Blocked);
            Assert.Equal(0, s.ProposedA);
        }

        // ── wire-size parser ────────────────────────────────────────────────

        [Theory]
        [InlineData("2 x 2.5mm²", 2.5)]
        [InlineData("3-2.5mm²", 2.5)]
        [InlineData("2.5mm2", 2.5)]
        [InlineData("3C 10 mm²", 10.0)]
        [InlineData("2,5 mm²", 2.5)]
        [InlineData("4", 4.0)]
        [InlineData("3x16", 16.0)]
        [InlineData("#12", 3.31)]
        [InlineData("3-#12, 1-#12G", 3.31)]
        [InlineData("12 AWG", 3.31)]
        [InlineData("#10", 5.26)]
        [InlineData("#1/0", 53.48)]
        [InlineData("250 kcmil", 126.68)]
        [InlineData("", 0.0)]
        [InlineData(null, 0.0)]
        public void Wire_size_parser_reads_the_cross_section_not_the_conductor_count(string text, double expected)
        {
            Assert.Equal(expected, WireSizeParser.ParseCsaMm2(text), 2);
        }

        [Theory]
        // ExternalExportEngine took the FIRST digit run ("2 x 2.5mm²" → 2);
        // WireProfile joined EVERY digit ("2x2.5mm2" → 22.52). Both now delegate here.
        [InlineData("2 x 2.5mm²", 2.5)]
        [InlineData("2x2.5mm2", 2.5)]
        public void Former_duplicate_parser_failure_strings_read_the_csa(string text, double expected)
            => Assert.Equal(expected, WireSizeParser.ParseCsaMm2(text), 3);

        // ── voltage-drop engine (ELEC-5) ────────────────────────────────────

        [Theory]
        [InlineData(1.5, 12.1)]
        [InlineData(2.5, 7.41)]
        [InlineData(4.0, 4.61)]
        [InlineData(6.0, 3.08)]
        [InlineData(10.0, 1.83)]
        public void Resistance_is_BS_EN_60228_at_20C(double csa, double mohm)
        {
            Assert.Equal(mohm, VoltageDropEngine.BaseResistanceMohmPerM(csa, "Cu"), 3);
        }

        [Fact]
        public void Resistive_VD_agrees_with_tabulated_mVAm_within_one_percent()
        {
            // 4 mm², 32 A, 30 m, 230 V, 70 °C: R = 4.61 × (1 + 0.00393 × 50) = 5.516 mΩ/m;
            // VD = 2 × 32 × 30 × 5.516 / 1000 = 10.59 V = 4.605 %. Table 4D2B: 11 mV/A/m → 4.591 %.
            double vd = VoltageDropEngine.CalculateVoltDropPercent(32, 30, 4.0, "Cu", 230, 1, 70);
            Assert.InRange(vd, 4.591 * 0.99, 4.591 * 1.01);
        }

        [Fact]
        public void Default_limits_are_BS7671_Appendix_12()
        {
            Assert.Equal(3.0, VoltageDropEngine.LimitFor(true, 0, 0));
            Assert.Equal(5.0, VoltageDropEngine.LimitFor(false, 0, 0));
            // User-configured limits still win.
            Assert.Equal(2.5, VoltageDropEngine.LimitFor(true, 2.5, 4));
            Assert.Equal(4.0, VoltageDropEngine.LimitFor(false, 2.5, 4));
        }
    }
}
