using System;
using System.Globalization;
using StingTools.Core.Units;
using Xunit;
using Q = StingTools.Core.Units.FlowDisplayUnits.Quantity;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-6 — the presentation layer for MEP flows and pressures.
    ///
    /// <para>The Owner reads CFM and GPM; the engines work in l/s and Pa and were not
    /// touched. <b>The round trip is the whole risk</b>: a value entered as CFM, stored as
    /// l/s and redisplayed must return the same CFM, and the obvious implementation does not
    /// do that.</para>
    /// </summary>
    public class FlowDisplayUnitsTests
    {
        // ── The round trip, which is the requirement ─────────────────────────────

        [Theory]
        [InlineData(Q.AirFlow, 850)]
        [InlineData(Q.AirFlow, 1000)]
        [InlineData(Q.AirFlow, 1)]
        [InlineData(Q.AirFlow, 12345.678)]
        [InlineData(Q.AirFlow, 0.0001)]
        [InlineData(Q.WaterFlow, 12.5)]
        [InlineData(Q.WaterFlow, 300)]
        [InlineData(Q.WaterFlow, 0.75)]
        [InlineData(Q.Pressure, 0.25)]
        [InlineData(Q.Pressure, 4.0)]
        [InlineData(Q.Pressure, 1234.5)]
        public void EnteredThenStoredThenRedisplayedReturnsTheSameValue(Q q, double entered)
        {
            double si = FlowDisplayUnits.ToSi(entered, q, imperial: true);
            double back = FlowDisplayUnits.FromSi(si, q, imperial: true);
            // Exact to within double precision — not "close enough to round to the same
            // display", which is the weaker claim a two-constant implementation can make.
            Assert.Equal(entered, back, 12);
        }

        /// <summary>
        /// <b>The trap, made explicit.</b> The usual implementation uses two published
        /// constants — 0.4719 l/s per CFM and 2.119 CFM per l/s — which are NOT reciprocals.
        /// This test reproduces that pair and shows it failing the round trip, so the reason
        /// for the single-factor design is recorded rather than assumed.
        /// </summary>
        [Fact]
        public void TheTwoConstantImplementationDoesNotRoundTrip()
        {
            const double naiveToSi = 0.4719;    // l/s per CFM, as published to 4 s.f.
            const double naiveFromSi = 2.119;   // CFM per l/s, as published to 4 s.f.

            // The pair is not reciprocal: the product is 0.99995610, not 1. The error is
            // RELATIVE, so it is invisible on a small duct and grows with the flow.
            double product = naiveToSi * naiveFromSi;
            Assert.NotEqual(1.0, product, 6);
            Assert.True(Math.Abs(1.0 - product) > 1e-5,
                $"expected the published pair to be non-reciprocal; product was {product}");

            // At an air-handling-unit flow that is over half a CFM out — a value the
            // engineer typed, coming back different.
            double big = 12345.678;
            Assert.True(Math.Abs(big * product - big) > 0.5,
                $"expected visible drift at {big} CFM; got {big * product}");

            // And at a terminal flow it still moves the third decimal, which is enough to
            // make a stored value disagree with the one on the drawing.
            double entered = 850.0;
            Assert.NotEqual(entered, entered * product, 3);

            // …and the shipped implementation does not.
            double ours = FlowDisplayUnits.FromSi(
                FlowDisplayUnits.ToSi(entered, Q.AirFlow, true), Q.AirFlow, true);
            Assert.Equal(entered, ours, 12);
        }

        // ── The factors are the defined ones ─────────────────────────────────────

        /// <summary>1 ft = 0.3048 m exactly (international foot), so 1 CFM is exactly
        /// 0.4719474432 l/s. Pinned because a rounded factor is what breaks the round
        /// trip.</summary>
        [Fact]
        public void OneCfmIsExactlyPointFourSevenOneNineFourSevenFourFourThreeTwoLitresPerSecond()
            => Assert.Equal(0.4719474432, FlowDisplayUnits.LitrePerSecondPerCfm, 12);

        /// <summary>1 US liquid gallon = 3.785411784 L exactly (231 in³), so 1 GPM is
        /// exactly 0.0630901964 l/s.</summary>
        [Fact]
        public void OneUsGpmIsExactlyPointZeroSixThreeZeroNineLitresPerSecond()
            => Assert.Equal(3.785411784 / 60.0, FlowDisplayUnits.LitrePerSecondPerUsGpm, 12);

        /// <summary>Sanity against figures an engineer can check by eye.</summary>
        [Fact]
        public void KnownConversions()
        {
            // 1000 l/s is about 2119 CFM.
            Assert.Equal(2118.88, FlowDisplayUnits.FromSi(1000, Q.AirFlow, true), 2);
            // 1 l/s is about 15.85 US GPM.
            Assert.Equal(15.850, FlowDisplayUnits.FromSi(1, Q.WaterFlow, true), 3);
            // 250 Pa is about 1 in.w.g.
            Assert.Equal(1.0047, FlowDisplayUnits.FromSi(250, Q.Pressure, true), 4);
        }

        // ── Metric projects are bit-identical to before ──────────────────────────

        [Theory]
        [InlineData(Q.AirFlow)]
        [InlineData(Q.WaterFlow)]
        [InlineData(Q.Pressure)]
        public void MetricIsTheIdentity(Q q)
        {
            foreach (double v in new[] { 0.0, 1.0, 850.0, 12345.678 })
            {
                Assert.Equal(v, FlowDisplayUnits.FromSi(v, q, imperial: false));
                Assert.Equal(v, FlowDisplayUnits.ToSi(v, q, imperial: false));
            }
        }

        /// <summary>Air and water are BOTH l/s in SI and different units in imperial. A
        /// single "flow" conversion would silently price an air flow in GPM; the quantity is
        /// part of the call for that reason.</summary>
        [Fact]
        public void AirAndWaterDivergeOnlyInImperial()
        {
            Assert.Equal(FlowDisplayUnits.FromSi(100, Q.AirFlow, false),
                         FlowDisplayUnits.FromSi(100, Q.WaterFlow, false));
            Assert.NotEqual(FlowDisplayUnits.FromSi(100, Q.AirFlow, true),
                            FlowDisplayUnits.FromSi(100, Q.WaterFlow, true));
        }

        // ── Formatting and parsing ───────────────────────────────────────────────

        [Fact]
        public void FormatCarriesTheRightSymbol()
        {
            Assert.EndsWith("CFM", FlowDisplayUnits.Format(100, Q.AirFlow, true));
            Assert.EndsWith("l/s", FlowDisplayUnits.Format(100, Q.AirFlow, false));
            Assert.EndsWith("GPM", FlowDisplayUnits.Format(10, Q.WaterFlow, true));
            Assert.EndsWith("in.w.g.", FlowDisplayUnits.Format(250, Q.Pressure, true));
            Assert.EndsWith("Pa", FlowDisplayUnits.Format(250, Q.Pressure, false));
        }

        [Theory]
        [InlineData("850 CFM", 850)]
        [InlineData("850", 850)]
        [InlineData("1,200 CFM", 1200)]
        [InlineData("  1200  ", 1200)]
        public void ParseAcceptsWhatPeopleActuallyType(string typed, double expectedCfm)
        {
            Assert.True(FlowDisplayUnits.TryParseToSi(typed, Q.AirFlow, true, out double si));
            Assert.Equal(expectedCfm, FlowDisplayUnits.FromSi(si, Q.AirFlow, true), 9);
        }

        /// <summary>Unparseable input must FAIL, not return zero. A silent 0 in a flow field
        /// sizes a duct to nothing, and nothing downstream can tell that apart from a duct
        /// that genuinely carries no air.</summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("CFM")]
        public void UnparseableInputFailsRatherThanReturningZero(string typed)
        {
            Assert.False(FlowDisplayUnits.TryParseToSi(typed, Q.AirFlow, true, out double si));
            Assert.Equal(0, si);   // the out value is 0, but the CALLER was told false
        }

        /// <summary>The full user round trip: type CFM, parse to SI, redisplay. This is the
        /// sentence in the gap, executed.</summary>
        [Theory]
        [InlineData("850")]
        [InlineData("1,250")]
        [InlineData("12.5")]
        public void TypedCfmSurvivesStorageAsLitresPerSecond(string typed)
        {
            Assert.True(FlowDisplayUnits.TryParseToSi(typed, Q.AirFlow, true, out double storedLps));
            string shown = FlowDisplayUnits.FormatValue(storedLps, Q.AirFlow, true, 4);
            double reparsed = double.Parse(shown, NumberStyles.Float | NumberStyles.AllowThousands,
                                           CultureInfo.InvariantCulture);
            double original = double.Parse(typed, NumberStyles.Float | NumberStyles.AllowThousands,
                                           CultureInfo.InvariantCulture);
            Assert.Equal(original, reparsed, 4);
        }
    }
}
