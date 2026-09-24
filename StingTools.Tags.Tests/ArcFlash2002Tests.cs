using System;
using StingTools.Commands.Electrical.ArcFlash;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// IEEE 1584-2002 LV arc-flash engine (ROADMAP ELEC-1). The previous engine claimed
    /// "IEEE 1584-2018" and its energy FELL as fault current rose. These tests pin a
    /// hand-computed worked example and the physical monotonicity the old code broke.
    /// </summary>
    public class ArcFlash2002Tests
    {
        // ── Worked example ──────────────────────────────────────────────────
        // Ibf = 25 kA bolted, V = 0.4 kV, box (panelboard), solidly grounded,
        // G = 25 mm, t = 0.1 s, D = 455 mm, x = 1.641, Cf = 1.5.
        //
        // 1) Arcing current, lg Ia = K + 0.662 lgIbf + 0.0966 V + 0.000526 G
        //                            + 0.5588 V lgIbf − 0.00304 G lgIbf
        //    lg 25                       = 1.397940
        //    K (box)                     = −0.097
        //    0.662 × 1.397940            =  0.925436
        //    0.0966 × 0.4                =  0.038640
        //    0.000526 × 25               =  0.013150
        //    0.5588 × 0.4 × 1.397940     =  0.312468
        //    −0.00304 × 25 × 1.397940    = −0.106243
        //    lg Ia                       =  1.086451  → Ia = 12.2025 kA
        //
        // 2) Normalized energy, lg En = K1 + K2 + 1.081 lg Ia + 0.0011 G
        //    K1 (box) = −0.555, K2 (grounded) = −0.113
        //    1.081 × 1.086451 = 1.174454 ; 0.0011 × 25 = 0.0275
        //    lg En = −0.668 + 1.174454 + 0.0275 = 0.533954 → En = 3.41943 J/cm²
        //
        // 3) E = 4.184 Cf En (t/0.2) (610/D)^x
        //    4.184 × 1.5 = 6.276 ; × 3.41943 = 21.46034 ; × (0.1/0.2) = 10.73017
        //    (610/455)^1.641 = 1.3406593^1.641 = e^(0.293162 × 1.641) = e^0.481079 = 1.617819
        //    E = 10.73017 × 1.617819 = 17.3595 J/cm² = 17.3595 / 4.184 = 4.1490 cal/cm²
        //    → PPE category 2 (4 < 4.149 ≤ 8)
        //
        // 4) DB = [4.184 Cf En (t/0.2) (610^x / EB)]^(1/x), EB = 5.0 J/cm²
        //       = D × (E / EB)^(1/x) = 455 × (17.3595/5)^(1/1.641)
        //       = 455 × 3.4719^0.609385 = 455 × e^(1.244702 × 0.609385)
        //       = 455 × e^0.758503 = 455 × 2.135077 = 971.5 mm
        private const double HandIaKa   = 12.2025;
        private const double HandEnJcm2 = 3.41943;
        private const double HandEJcm2  = 17.3595;
        private const double HandECal   = 4.1490;
        private const double HandDbMm   = 971.46;

        private static void Within1Pct(double expected, double actual)
            => Assert.True(Math.Abs(actual - expected) <= 0.01 * Math.Abs(expected),
                $"expected {expected} ±1 %, got {actual}");

        [Fact]
        public void Worked_example_step_by_step_matches_hand_calculation()
        {
            double ia = ArcFlashEngine.ArcingCurrentKa(25, 0.4, 25, box: true);
            Within1Pct(HandIaKa, ia);

            double en = ArcFlashEngine.NormalizedEnergyJcm2(ia, 25, box: true, solidlyGrounded: true);
            Within1Pct(HandEnJcm2, en);

            double e = ArcFlashEngine.IncidentEnergyJcm2(en, 0.1, 455, 1.641);
            Within1Pct(HandEJcm2, e);
            Within1Pct(HandECal, e / 4.184);

            double db = ArcFlashEngine.BoundaryMm(en, 0.1, 1.641);
            Within1Pct(HandDbMm, db);
        }

        [Fact]
        public void Worked_example_through_Calculate_matches_hand_calculation()
        {
            var r = ArcFlashEngine.Calculate(new ArcFlashInput
            {
                BoltedFaultKa = 25, VoltageV = 400, EquipmentClass = ArcEquipmentClass.PanelMcc,
                WorkingDistanceMm = 455, GapMm = 25, SolidlyGrounded = true, ClearingTimeS = 0.1
            });
            Assert.True(r.Calculated, r.NotCalculatedReason);
            Within1Pct(HandIaKa, r.ArcingCurrentKa);
            Within1Pct(HandECal, r.IncidentEnergyCalCm2);
            Within1Pct(HandDbMm, r.BoundaryMm);
            Assert.Equal(2, r.PpeCategory);
            Assert.False(r.ReducedCaseGoverns);   // same t at 0.85·Ia → lower energy
            Assert.Equal(1.641, r.DistanceExponent);
        }

        [Fact]
        public void Energy_at_the_boundary_is_exactly_the_boundary_energy()
        {
            double en = ArcFlashEngine.NormalizedEnergyJcm2(12.2, 25, true, true);
            foreach (double x in new[] { 1.473, 1.641, 2.0 })
            {
                double db = ArcFlashEngine.BoundaryMm(en, 0.1, x);
                Within1Pct(5.0, ArcFlashEngine.IncidentEnergyJcm2(en, 0.1, db, x));
            }
        }

        [Fact]
        public void Ungrounded_is_more_conservative_than_grounded_by_10_pow_0113()
        {
            double g = ArcFlashEngine.NormalizedEnergyJcm2(12.2, 25, true, true);
            double u = ArcFlashEngine.NormalizedEnergyJcm2(12.2, 25, true, false);
            Within1Pct(Math.Pow(10, 0.113), u / g);   // 1.297
        }

        // ── Monotonicity (the old engine failed the first of these) ─────────

        [Theory]
        [InlineData(208, 13, true)]
        [InlineData(400, 25, true)]
        [InlineData(480, 32, true)]
        [InlineData(1000, 152, true)]
        [InlineData(400, 25, false)]
        public void Energy_rises_with_bolted_fault_current(double v, double gap, bool box)
        {
            double prev = 0;
            for (double ibf = 0.7; ibf <= 106; ibf *= 1.25)
            {
                double ia = ArcFlashEngine.ArcingCurrentKa(ibf, v / 1000.0, gap, box);
                double e = ArcFlashEngine.IncidentEnergyJcm2(
                    ArcFlashEngine.NormalizedEnergyJcm2(ia, gap, box, true), 0.1, 455, 1.641);
                Assert.True(e > prev, $"E fell at Ibf={ibf:0.###} kA ({e} <= {prev})");
                prev = e;
            }
        }

        [Fact]
        public void Energy_rises_with_clearing_time_and_falls_with_distance()
        {
            double en = ArcFlashEngine.NormalizedEnergyJcm2(12.2, 25, true, true);
            double prev = 0;
            foreach (double t in new[] { 0.01, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0 })
            {
                double e = ArcFlashEngine.IncidentEnergyJcm2(en, t, 455, 1.641);
                Assert.True(e > prev);
                prev = e;
            }
            Assert.True(ArcFlashEngine.IncidentEnergyJcm2(en, 0.1, 455, 1.641)
                      > ArcFlashEngine.IncidentEnergyJcm2(en, 0.1, 610, 1.641));
        }

        [Fact]
        public void Calculate_energy_rises_with_fault_current_and_time()
        {
            double prevE = 0;
            foreach (double ibf in new[] { 1.0, 5.0, 10.0, 25.0, 50.0, 100.0 })
            {
                var r = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = ibf, VoltageV = 400, ClearingTimeS = 0.1 });
                Assert.True(r.Calculated);
                Assert.True(r.IncidentEnergyCalCm2 > prevE, $"E fell at {ibf} kA");
                prevE = r.IncidentEnergyCalCm2;
            }
            var fast = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400, ClearingTimeS = 0.05 });
            var slow = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400, ClearingTimeS = 0.5 });
            Assert.True(slow.IncidentEnergyCalCm2 > fast.IncidentEnergyCalCm2);
        }

        [Fact]
        public void Boundary_rises_with_energy()
        {
            double prevDb = 0, prevE = 0;
            foreach (double t in new[] { 0.02, 0.05, 0.1, 0.3, 1.0, 2.0 })
            {
                var r = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400, ClearingTimeS = t });
                Assert.True(r.IncidentEnergyCalCm2 > prevE);
                Assert.True(r.BoundaryMm > prevDb);
                prevE = r.IncidentEnergyCalCm2; prevDb = r.BoundaryMm;
            }
        }

        // ── 85 % arcing-current case ────────────────────────────────────────

        [Fact]
        public void Reduced_current_case_governs_when_the_device_is_slower_there()
        {
            // Device clears in 50 ms at full Ia but 500 ms at 0.85·Ia (e.g. drops out of
            // its instantaneous band). Energy ∝ Ia^1.081 · t, so the reduced case is
            // ~0.85^1.081 × 10 ≈ 8.4× worse and must be the one reported.
            double iaFull = ArcFlashEngine.ArcingCurrentKa(25, 0.4, 25, true);
            var r = ArcFlashEngine.Calculate(
                new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400, SolidlyGrounded = true },
                ia => ia >= iaFull * 0.99 ? 0.05 : 0.5);
            Assert.True(r.Calculated);
            Assert.True(r.ReducedCaseGoverns);
            Assert.Equal(0.5, r.GoverningClearingTimeS, 6);
            Within1Pct(0.85 * iaFull, r.ReducedArcingCurrentKa);
        }

        [Fact]
        public void Clearing_time_is_capped_at_two_seconds_with_a_note()
        {
            var capped = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400, ClearingTimeS = 60 });
            var two    = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400, ClearingTimeS = 2 });
            Assert.Equal(two.IncidentEnergyCalCm2, capped.IncidentEnergyCalCm2, 9);
            Assert.Contains(capped.Notes, n => n.Contains("capped"));
        }

        // ── Refusals: no number is better than a wrong one ─────────────────

        [Theory]
        [InlineData(11000, 25, 0.1)]   // MV — not implemented
        [InlineData(120, 25, 0.1)]     // below 208 V
        [InlineData(0, 25, 0.1)]       // voltage unknown (the old code defaulted to 240 V)
        [InlineData(400, 0.5, 0.1)]    // below 0.7 kA
        [InlineData(400, 120, 0.1)]    // above 106 kA
        [InlineData(400, 25, 0)]       // clearing time unknown
        public void Out_of_scope_inputs_are_not_calculated(double v, double ibf, double t)
        {
            var r = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = ibf, VoltageV = v, ClearingTimeS = t });
            Assert.False(r.Calculated);
            Assert.False(string.IsNullOrWhiteSpace(r.NotCalculatedReason));
            Assert.Equal(0, r.IncidentEnergyCalCm2);
        }

        [Fact]
        public void Unknown_clearing_time_from_lookup_is_not_calculated()
        {
            var r = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400 }, _ => double.NaN);
            Assert.False(r.Calculated);
        }

        // ── PPE + labels ────────────────────────────────────────────────────

        [Theory]
        [InlineData(1.0, 0)]
        [InlineData(1.2, 0)]
        [InlineData(3.9, 1)]
        [InlineData(4.149, 2)]
        [InlineData(8.0, 2)]
        [InlineData(24.9, 3)]
        [InlineData(40.0, 4)]
        [InlineData(40.1, -1)]
        public void Ppe_category_by_energy_threshold(double cal, int cat)
            => Assert.Equal(cat, ArcFlashEngine.PpeCategory(cal));

        [Fact]
        public void Every_label_carries_the_2002_indicative_basis()
        {
            var ok = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400, ClearingTimeS = 0.1 });
            var bad = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 0, ClearingTimeS = 0.1 });
            string a = ArcFlashEngine.FormatLabel("DB-1", 400, ArcEquipmentClass.PanelMcc, ok, "fixed");
            string b = ArcFlashEngine.FormatLabel("DB-2", 0, ArcEquipmentClass.PanelMcc, bad, "");
            foreach (var s in new[] { a, b })
            {
                Assert.Contains("IEEE 1584-2002", s);
                Assert.Contains("indicative", s);
                Assert.DoesNotContain("IEEE 1584-2018", s);   // the old, false claim
            }
            Assert.Contains("NOT CALCULATED", b);
            Assert.DoesNotContain("cal/cm²", b);
        }
    }
}
