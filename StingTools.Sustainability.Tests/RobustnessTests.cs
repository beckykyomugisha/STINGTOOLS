using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS F — savings-% NaN/divide-by-zero guards + clear over-baseline phrasing,
    // and ASHRAE 169 climate-zone auto-derivation from degree-days.
    public class RobustnessTests
    {
        // ── Savings guards ────────────────────────────────────────────────────
        [Fact]
        public void Savings_ValidInputs_MatchFormula()
        {
            Assert.Equal((200.0 - 150.0) / 200.0 * 100.0, SustainSavings.Pct(200, 150), 6);
        }

        [Fact]
        public void Savings_NonPositiveBaseline_OrNonFinite_ReturnZero()
        {
            Assert.Equal(0, SustainSavings.Pct(0, 100), 6);
            Assert.Equal(0, SustainSavings.Pct(-5, 100), 6);
            Assert.Equal(0, SustainSavings.Pct(double.NaN, 100), 6);
            Assert.Equal(0, SustainSavings.Pct(200, double.NaN), 6);
            Assert.Equal(0, SustainSavings.Pct(200, double.PositiveInfinity), 6);
        }

        [Fact]
        public void Savings_Describe_DistinguishesReductionFromOverBaseline()
        {
            Assert.Contains("reduction", SustainSavings.Describe(200, 150, "kWh/m²·yr"));
            Assert.Contains("over baseline", SustainSavings.Describe(150, 400));
            Assert.Contains("baseline not available", SustainSavings.Describe(0, 100));
        }

        // ── ASHRAE 169 zone classification ────────────────────────────────────
        [Theory]
        [InlineData(6500, 0, 0)]     // very hot — Bangui-like
        [InlineData(5500, 0, 1)]
        [InlineData(4000, 0, 2)]
        [InlineData(3000, 0, 3)]
        [InlineData(1500, 2500, 4)]  // cool cdd + modest hdd
        [InlineData(800, 3500, 5)]
        [InlineData(400, 4500, 6)]
        [InlineData(100, 6000, 7)]
        [InlineData(50, 8000, 8)]    // very cold
        public void AshraeZone_Number_FollowsDegreeDayThresholds(double cdd, double hdd, int expected)
        {
            Assert.Equal(expected, AshraeClimateZone.Number(cdd, hdd));
        }

        [Fact]
        public void AshraeZone_Classify_AddsMoistureForZeroToSix_NoneForSevenEight()
        {
            Assert.Equal("0A", AshraeClimateZone.Classify(6500, 0));        // very hot humid
            Assert.Equal("4B", AshraeClimateZone.Classify(1500, 2500, "B")); // dry sub-type
            Assert.Equal("7", AshraeClimateZone.Classify(100, 6000));        // no letter
            Assert.Equal("8", AshraeClimateZone.Classify(50, 8000));
        }
    }
}
