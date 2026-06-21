using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Audit #5 — locks in that tonne is no longer aliased onto kg, so a
    // per-tonne rate can never be multiplied by a kilogram quantity (the
    // latent 1000× overcharge).
    public class BoqUnitsTests
    {
        [Theory]
        [InlineData("m²", "m2")]
        [InlineData("sqm", "m2")]
        [InlineData("M2", "m2")]
        [InlineData("m³", "m3")]
        [InlineData("cum", "m3")]
        [InlineData("lm", "m")]
        [InlineData("linear-m", "m")]
        [InlineData("kg", "kg")]
        [InlineData("t", "tonne")]
        [InlineData("tonne", "tonne")]
        [InlineData("tonnes", "tonne")]
        [InlineData("No", "each")]
        [InlineData("nr", "each")]
        public void Normalise_CanonicalisesSynonyms(string input, string expected)
        {
            Assert.Equal(expected, BoqUnits.Normalise(input));
        }

        [Fact]
        public void Normalise_TonneAndKilogramAreDistinct()
        {
            // The core of the fix: these must NOT collapse to the same token.
            Assert.NotEqual(BoqUnits.Normalise("tonne"), BoqUnits.Normalise("kg"));
        }

        [Theory]
        [InlineData("kg", true)]
        [InlineData("tonne", true)]
        [InlineData("t", true)]
        [InlineData("m2", false)]
        [InlineData("each", false)]
        public void IsMassUnit_Classifies(string unit, bool expected)
        {
            Assert.Equal(expected, BoqUnits.IsMassUnit(unit));
        }

        [Fact]
        public void MassKgToRateUnit_TonneRateDividesByThousand()
        {
            // 5000 kg priced per tonne must become 5 tonnes — not 5000.
            Assert.Equal(5.0, BoqUnits.MassKgToRateUnit(5000.0, "tonne"), 6);
            Assert.Equal(5.0, BoqUnits.MassKgToRateUnit(5000.0, "t"), 6);
        }

        [Fact]
        public void MassKgToRateUnit_KilogramRateUnchanged()
        {
            Assert.Equal(5000.0, BoqUnits.MassKgToRateUnit(5000.0, "kg"), 6);
        }
    }
}
