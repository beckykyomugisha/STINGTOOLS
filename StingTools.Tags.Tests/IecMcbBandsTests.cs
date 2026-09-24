using StingTools.Commands.Electrical.Coordination;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Generic IEC 60898-1 band model and band selectivity check (ROADMAP ELEC-4).
    /// Replaces a synthetic linear ramp that let any pair with a rating string "pass".
    /// Expected values are the IEC 60898-1 Table 7 test points, worked by hand below.
    /// </summary>
    public class IecMcbBandsTests
    {
        private static DeviceBand Mcb(DeviceCurve c, double inA) =>
            new DeviceBand { Label = $"{c}{inA}", Curve = c, RatingA = inA };

        // ── Band edges for a C16 (In = 16 A, instantaneous 80–160 A) ─────────
        //  max-clear: < 23.2 A (1.45 In) never guaranteed; 23.2–40.8 A ≤ 1 h;
        //             40.8–160 A ≤ 60 s (In ≤ 32); ≥ 160 A < 0.1 s
        //  min-trip : < 18.08 A (1.13 In) ≥ 1 h; 18.08–40.8 A > 1 s;
        //             40.8–80 A ≥ 0.1 s; ≥ 80 A may be instantaneous (0)
        [Theory]
        [InlineData(22.0,  double.PositiveInfinity)]
        [InlineData(24.0,  3600)]
        [InlineData(48.0,  60)]
        [InlineData(159.0, 60)]
        [InlineData(160.0, 0.1)]
        [InlineData(1000,  0.1)]
        public void C16_max_clear_edge(double amps, double seconds)
            => Assert.Equal(seconds, Mcb(DeviceCurve.C, 16).MaxClearTimeS(amps));

        [Theory]
        [InlineData(17.0,  3600)]
        [InlineData(20.0,  1)]
        [InlineData(48.0,  0.1)]
        [InlineData(79.0,  0.1)]
        [InlineData(80.0,  0)]
        public void C16_min_trip_edge(double amps, double seconds)
            => Assert.Equal(seconds, Mcb(DeviceCurve.C, 16).MinTripTimeS(amps));

        [Fact]
        public void Instantaneous_bands_per_curve_letter()
        {
            Assert.Equal((3.0, 5.0),   (Mcb(DeviceCurve.B, 10).LowerInstMultiple, Mcb(DeviceCurve.B, 10).UpperInstMultiple));
            Assert.Equal((5.0, 10.0),  (Mcb(DeviceCurve.C, 10).LowerInstMultiple, Mcb(DeviceCurve.C, 10).UpperInstMultiple));
            Assert.Equal((10.0, 20.0), (Mcb(DeviceCurve.D, 10).LowerInstMultiple, Mcb(DeviceCurve.D, 10).UpperInstMultiple));
        }

        [Fact]
        public void Rating_dependent_test_times()
        {
            Assert.Equal(60,   Mcb(DeviceCurve.C, 32).TestCMaxS);
            Assert.Equal(120,  Mcb(DeviceCurve.C, 40).TestCMaxS);
            Assert.Equal(3600, Mcb(DeviceCurve.C, 63).ConventionalTimeS);
            Assert.Equal(7200, Mcb(DeviceCurve.C, 80).ConventionalTimeS);
        }

        [Fact]
        public void Band_edges_are_monotonic_non_increasing_in_current()
        {
            foreach (var c in new[] { DeviceCurve.B, DeviceCurve.C, DeviceCurve.D })
            {
                var b = Mcb(c, 20);
                double pMin = double.PositiveInfinity, pMax = double.PositiveInfinity;
                for (double i = 10; i < 5000; i *= 1.05)
                {
                    double mn = b.MinTripTimeS(i), mx = b.MaxClearTimeS(i);
                    Assert.True(mn <= pMin && mx <= pMax, $"{c} band rose at {i} A");
                    Assert.True(mn <= mx, $"{c} min edge above max edge at {i} A");
                    pMin = mn; pMax = mx;
                }
            }
        }

        [Fact]
        public void Mccb_and_acb_have_no_band()
        {
            Assert.False(IecMcbBands.Parse("250A 65kA MCCB").HasBand);
            Assert.True(double.IsNaN(IecMcbBands.Parse("400A", "ACB").MaxClearTimeS(10000)));
        }

        // ── Label parsing — never invents a curve letter ────────────────────
        [Theory]
        [InlineData("C16", null, DeviceCurve.C, 16)]
        [InlineData("B32", null, DeviceCurve.B, 32)]
        [InlineData("16A C", null, DeviceCurve.C, 16)]
        [InlineData("16A", "MCB-C", DeviceCurve.C, 16)]
        [InlineData("50A", "MCB-D", DeviceCurve.D, 50)]
        [InlineData("250A 65kA MCCB", null, DeviceCurve.Mccb, 250)]
        [InlineData("400A", "ACB", DeviceCurve.Acb, 400)]
        [InlineData("63A", null, DeviceCurve.Unknown, 63)]
        public void Parse_label(string label, string type, DeviceCurve curve, double rating)
        {
            var b = IecMcbBands.Parse(label, type);
            Assert.Equal(curve, b.Curve);
            Assert.Equal(rating, b.RatingA);
        }

        // ── Selectivity ─────────────────────────────────────────────────────

        // C6 under C63, prospective 0.30 kA. Range 1.45×6 = 8.7 A → 300 A.
        //   8.7–15.3 A   down max 3600 s  vs up min 3600 s (I < 71.2 A)   ok (touching, not overlapping)
        //   15.3–60 A    down max 60 s    vs up min 3600 s                ok
        //   60–71.2 A    down max 0.1 s   vs up min 3600 s                ok
        //   71.2–160.7 A down max 0.1 s   vs up min 1 s                   ok
        //   160.7–300 A  down max 0.1 s   vs up min 0.1 s (I < 315 A)     ok
        // → Selective. Above 315 A (5 × 63) the upstream may trip instantaneously.
        [Fact]
        public void Small_under_large_is_selective_below_upstream_instantaneous()
        {
            var r = IecMcbBands.Check(Mcb(DeviceCurve.C, 63), Mcb(DeviceCurve.C, 6), 0.30);
            Assert.Equal(SelectivityVerdict.Selective, r.Verdict);
        }

        [Fact]
        public void Fault_reaching_upstream_instantaneous_band_is_not_assured()
        {
            var r = IecMcbBands.Check(Mcb(DeviceCurve.C, 63), Mcb(DeviceCurve.C, 6), 6.0);
            Assert.Equal(SelectivityVerdict.NotAssured, r.Verdict);
            Assert.Equal(315.0, r.CriticalCurrentA, 3);
            Assert.Contains("manufacturer selectivity table required", r.Reason);
            Assert.Contains("instantaneous", r.Reason);
        }

        [Fact]
        public void Just_above_the_upstream_lower_instantaneous_is_not_assured()
        {
            Assert.Equal(SelectivityVerdict.Selective,
                IecMcbBands.Check(Mcb(DeviceCurve.C, 63), Mcb(DeviceCurve.C, 6), 0.314).Verdict);
            Assert.Equal(SelectivityVerdict.NotAssured,
                IecMcbBands.Check(Mcb(DeviceCurve.C, 63), Mcb(DeviceCurve.C, 6), 0.316).Verdict);
        }

        // C16 under C63 overload region: at 1.13×63 = 71.2 A the C63 may trip after > 1 s,
        // while the C16 (4.45 In, still in its 60 s thermal band) is only guaranteed < 60 s.
        [Fact]
        public void Overload_band_overlap_is_not_assured()
        {
            var r = IecMcbBands.Check(Mcb(DeviceCurve.C, 63), Mcb(DeviceCurve.C, 16), 0.2);
            Assert.Equal(SelectivityVerdict.NotAssured, r.Verdict);
            Assert.InRange(r.CriticalCurrentA, 71.0, 71.3);
        }

        [Fact]
        public void Identical_devices_are_never_selective()
            => Assert.NotEqual(SelectivityVerdict.Selective,
                IecMcbBands.Check(Mcb(DeviceCurve.C, 16), Mcb(DeviceCurve.C, 16), 6).Verdict);

        // Upstream C16 feeding a downstream C63: at 160 A the C16 trips < 0.1 s
        // (10 In) while the C63 (2.54 In) cannot trip before 1 s.
        [Fact]
        public void Smaller_upstream_is_not_selective()
        {
            var r = IecMcbBands.Check(Mcb(DeviceCurve.C, 16), Mcb(DeviceCurve.C, 63), 6);
            Assert.Equal(SelectivityVerdict.NotSelective, r.Verdict);
            Assert.InRange(r.CriticalCurrentA, 159.9, 160.1);
        }

        [Fact]
        public void No_curve_data_is_never_a_pass()
        {
            var mccb = IecMcbBands.Parse("250A MCCB");
            Assert.Equal(SelectivityVerdict.NoCurveData, IecMcbBands.Check(mccb, Mcb(DeviceCurve.C, 16), 6).Verdict);
            Assert.Equal(SelectivityVerdict.NoCurveData, IecMcbBands.Check(Mcb(DeviceCurve.C, 63), IecMcbBands.Parse("32A"), 6).Verdict);
        }

        [Fact]
        public void Unknown_fault_level_is_never_a_pass()
            => Assert.Equal(SelectivityVerdict.NotAssured,
                IecMcbBands.Check(Mcb(DeviceCurve.C, 63), Mcb(DeviceCurve.C, 6), 0).Verdict);

        [Fact]
        public void Basis_wording_is_explicit()
            => Assert.Equal("generic IEC 60898 bands — confirm with manufacturer selectivity tables", IecMcbBands.Basis);
    }
}
