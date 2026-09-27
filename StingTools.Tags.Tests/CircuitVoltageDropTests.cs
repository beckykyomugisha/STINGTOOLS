// ══════════════════════════════════════════════════════════════════════════
//  CircuitVoltageDropTests.cs — ELEC-22.
//
//  One resolver owns a circuit's voltage drop (Core/Electrical/CircuitVoltageDrop.cs).
//  These pin its precedence against the SHIPPED tables: a missing input is NONE
//  (never 0), the recorded cable's own Appendix 4 table wins, with no record the
//  figure is the highest mV/A/m any loaded table gives (an upper bound), and the
//  Circuit Check can pass on that bound but never fail on it.
//
//  Hand-worked: 2.5 mm² 1-ph, Ib 20 A, 25 m, 230 V.
//    4D2B (PVC70 multicore, method C) 18 mV/A/m → 18 × 20 × 25 / 1000 = 9.0 V = 3.913 %
//    highest 2.5 mm² 1-ph mV/A/m shipped: 4E2B (XLPE90) 19 → 9.5 V = 4.130 %
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CircuitVoltageDropTests
    {
        private static Bs7671Data Data()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Bs7671Data.FromJson(JObject.Parse(File.ReadAllText(
                Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json"))));
        }

        private static CircuitVdInput Circuit(bool recorded = true, double csa = 2.5) => new CircuitVdInput
        {
            CurrentA = 20, LengthM = 25, VoltageV = 230, Phases = 1, CsaMm2 = csa, Material = "Cu", Standard = "BS7671",
            Insulation = recorded ? "PVC70" : null, InstallMethod = recorded ? "C" : null, CableType = recorded ? "Multicore" : null,
        };

        [Fact]
        public void The_recorded_cables_table_gives_the_figure()
        {
            var r = CircuitVoltageDrop.Resolve(Circuit(), Data());
            Assert.Equal(VdMethod.Appendix4Recorded, r.Method);
            Assert.Equal(18.0, r.MvAm, 3);
            Assert.Equal(3.913, r.Pct, 3);
            Assert.False(r.UpperBound);
            Assert.StartsWith("A4 Table 4D2B", r.Stamp);
        }

        [Fact]
        public void With_no_cable_recorded_the_figure_is_the_highest_loaded_mV_A_m()
        {
            var r = CircuitVoltageDrop.Resolve(Circuit(recorded: false), Data());
            Assert.Equal(VdMethod.Appendix4Envelope, r.Method);
            Assert.True(r.UpperBound);
            Assert.Equal(19.0, r.MvAm, 3);
            Assert.Equal(4.130, r.Pct, 3);
            Assert.StartsWith("A4-MAX upper bound", r.Stamp);
            // Never below the recorded figure: it bounds every loaded table.
            Assert.True(r.Pct >= CircuitVoltageDrop.Resolve(Circuit(), Data()).Pct);
        }

        [Fact]
        public void A_partial_record_is_treated_as_no_record()
        {
            var i = Circuit(); i.CableType = "";
            Assert.Equal(VdMethod.Appendix4Envelope, CircuitVoltageDrop.Resolve(i, Data()).Method);
        }

        [Theory]
        [InlineData(0, 25, 230, 2.5, "current")]
        [InlineData(20, 0, 230, 2.5, "length")]
        [InlineData(20, 25, 0, 2.5, "voltage")]
        [InlineData(20, 25, 230, 0, "conductor size")]
        public void A_missing_input_is_NONE_never_zero(double i, double l, double v, double csa, string what)
        {
            var c = Circuit(); c.CurrentA = i; c.LengthM = l; c.VoltageV = v; c.CsaMm2 = csa;
            var r = CircuitVoltageDrop.Resolve(c, Data());
            Assert.Equal(VdMethod.None, r.Method);
            Assert.False(r.HasValue);
            Assert.Contains(what, r.Detail);
            Assert.StartsWith("NONE missing", r.Stamp);
        }

        [Fact]
        public void A_size_not_in_the_recorded_table_is_NONE_not_the_envelope()
        {
            var r = CircuitVoltageDrop.Resolve(Circuit(csa: 3.0), Data());
            Assert.Equal(VdMethod.None, r.Method);
            Assert.Contains("4D2B", r.Detail);
        }

        [Fact]
        public void Aluminium_is_NONE()
        {
            var c = Circuit(); c.Material = "Al";
            Assert.Equal(VdMethod.None, CircuitVoltageDrop.Resolve(c, Data()).Method);
        }

        [Fact]
        public void No_tables_is_NONE_never_a_built_in_copy()
        {
            Assert.Equal(VdMethod.None, CircuitVoltageDrop.Resolve(Circuit(), null).Method);
            var bad = new Bs7671Data { LoadError = "override invalid" };
            var r = CircuitVoltageDrop.Resolve(Circuit(), bad);
            Assert.Equal(VdMethod.None, r.Method);
            Assert.Contains("override invalid", r.Detail);
        }

        [Fact]
        public void NEC_uses_the_resistance_method_it_is_given()
        {
            var c = Circuit(); c.Standard = "NEC2023";
            Assert.Equal(VdMethod.None, CircuitVoltageDrop.Resolve(c, Data()).Method);
            var r = CircuitVoltageDrop.Resolve(c, Data(), _ => 2.5);
            Assert.Equal(VdMethod.Resistance60228, r.Method);
            Assert.Equal(2.5, r.Pct, 6);
            Assert.False(CircuitVoltageDrop.IsAppendix4(r.Method));
        }

        [Fact]
        public void Three_phase_uses_the_three_phase_column()
        {
            var c = Circuit(); c.Phases = 3; c.VoltageV = 400;
            var r = CircuitVoltageDrop.Resolve(c, Data());
            Assert.Equal(15.0, r.MvAm, 3);                        // 4D2B 2.5 mm² 3-ph
            Assert.Equal(15.0 * 20 * 25 / 1000 / 400 * 100, r.Pct, 6);
        }

        [Fact]
        public void Minimum_size_for_the_limit_is_the_smallest_that_complies()
        {
            // 3.913 % at 2.5 mm² fails a 3 % limit; 4 mm² (11 mV/A/m) gives 2.391 %.
            double? s = CircuitVoltageDrop.MinimumCsaForLimit(Circuit(), Data(), 3.0,
                new[] { 2.5, 4.0, 6.0 }, out var at);
            Assert.Equal(4.0, s);
            Assert.Equal(2.391, at.Pct, 3);
            Assert.Null(CircuitVoltageDrop.MinimumCsaForLimit(Circuit(), Data(), 0.1, new[] { 2.5, 4.0 }, out _));
        }

        [Theory]
        [InlineData("", VdMethod.Legacy)]
        [InlineData("A4 Table 4D2B …", VdMethod.Appendix4Recorded)]
        [InlineData("A4-MAX upper bound …", VdMethod.Appendix4Envelope)]
        [InlineData("A4-SIZED feeder …", VdMethod.Appendix4Sized)]
        [InlineData("R60228 conductor resistance", VdMethod.Resistance60228)]
        [InlineData("IMPORT Amtech (method not known to STING)", VdMethod.Imported)]
        [InlineData("NONE missing length", VdMethod.None)]
        public void The_stamped_basis_parses_back_to_its_method(string stamp, VdMethod m)
            => Assert.Equal(m, CircuitVoltageDrop.ParseMethod(stamp));

        [Fact]
        public void Every_method_round_trips_through_its_code()
        {
            foreach (VdMethod m in Enum.GetValues(typeof(VdMethod)))
            {
                string stamp = new CircuitVdResult { Method = m, Detail = "x" }.Stamp;
                Assert.Equal(m, CircuitVoltageDrop.ParseMethod(m == VdMethod.Legacy ? "" : stamp));
            }
        }

        [Theory]
        [InlineData("ASNZS3000")]
        public void A_standard_with_no_tables_gets_no_figure(string std)
        {
            // ELEC-24: never BS 7671's figure under another standard's name.
            var c = Circuit(); c.Standard = std;
            var r = CircuitVoltageDrop.Resolve(c, Data());
            Assert.Equal(VdMethod.None, r.Method);
            Assert.False(string.IsNullOrWhiteSpace(r.Detail));
        }

        [Fact]
        public void IEC_60364_uses_the_harmonised_Appendix_4_tables()
        {
            var c = Circuit(); c.Standard = "IEC60364";
            Assert.Equal(VdMethod.Appendix4Recorded, CircuitVoltageDrop.Resolve(c, Data()).Method);
        }

        [Fact]
        public void Schedule_text_marks_bounds_and_never_shows_a_number_for_NONE()
        {
            // ELEC-26: the mirror always carries the current state.
            Assert.Equal("3.91", CircuitVoltageDrop.DisplayText(CircuitVoltageDrop.Resolve(Circuit(), Data())));
            Assert.Equal("≤4.13", CircuitVoltageDrop.DisplayText(CircuitVoltageDrop.Resolve(Circuit(recorded: false), Data())));
            var missing = Circuit(); missing.LengthM = 0;
            Assert.Equal("—", CircuitVoltageDrop.DisplayText(CircuitVoltageDrop.Resolve(missing, Data())));
            Assert.Equal("—", CircuitVoltageDrop.DisplayText(null));
        }

        // ── the Circuit Check on an upper bound ─────────────────────────────

        private static CircuitCheckInput Check(double vd, bool bound) => new CircuitCheckInput
        {
            IbA = 14, InA = 16, IzA = 27, VdPct = vd, VdLimitPct = 3, VdIsUpperBound = bound,
            ProspectiveFaultKa = 4.2, BreakingCapacityKa = 6,
        };

        [Fact]
        public void An_upper_bound_under_the_limit_is_a_pass()
        {
            var r = CircuitComplianceRule.Evaluate(Check(2.4, bound: true));
            Assert.False(r.Failed);
            Assert.DoesNotContain(r.NotChecked, x => x.StartsWith("VD"));
        }

        [Fact]
        public void An_upper_bound_over_the_limit_is_not_checked_never_a_fail()
        {
            var r = CircuitComplianceRule.Evaluate(Check(4.13, bound: true));
            Assert.False(r.Failed);
            Assert.Contains(r.NotChecked, x => x.Contains("upper bound 4.13 %"));
            Assert.StartsWith("UNVERIFIED", r.Summary);
        }

        [Fact]
        public void An_exact_figure_over_the_limit_fails()
            => Assert.True(CircuitComplianceRule.Evaluate(Check(3.91, bound: false)).Failed);

        [Fact]
        public void No_figure_says_why()
        {
            var c = Check(0, false); c.VdPct = null; c.VdNotCheckedReason = "missing length";
            Assert.Contains("VD (missing length)", CircuitComplianceRule.Evaluate(c).NotChecked);
        }
    }
}
