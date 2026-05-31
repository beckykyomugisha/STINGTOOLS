using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Z-21 — locks in that the BOQ legacy-fallback path applies a wastage
    // allowance to measured material quantities (it applied 0% before — audit
    // §6.3). WasteFactor is the pure helper DeriveQuantity's fallback now calls.
    public class WasteFactorTests
    {
        [Theory]
        [InlineData("m²")]
        [InlineData("m2")]
        [InlineData("sqm")]
        [InlineData("m³")]
        [InlineData("m3")]
        [InlineData("cum")]
        [InlineData("m")]
        [InlineData("kg")]
        [InlineData("tonne")]
        [InlineData("tonnes")]
        public void Apply_GrossesUpMeasuredUnits(string unit)
        {
            // 100 units @ 5% waste => 105.
            Assert.Equal(105.0, WasteFactor.Apply(100.0, unit, 5.0), 6);
            Assert.True(WasteFactor.AppliesTo(unit));
        }

        [Theory]
        [InlineData("each")]
        [InlineData("item")]
        [InlineData("nr")]
        [InlineData("")]
        [InlineData(null)]
        public void Apply_LeavesCountedAndUnknownUnitsUntouched(string unit)
        {
            // You do not waste 5% of a pump.
            Assert.Equal(100.0, WasteFactor.Apply(100.0, unit, 5.0), 6);
            Assert.False(WasteFactor.AppliesTo(unit));
        }

        [Fact]
        public void Apply_IsCaseAndWhitespaceInsensitive()
        {
            Assert.Equal(110.0, WasteFactor.Apply(100.0, " M3 ", 10.0), 6);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(double.NaN)]
        public void Apply_NonPositiveWasteNeverReducesQuantity(double wastePct)
        {
            Assert.Equal(100.0, WasteFactor.Apply(100.0, "m3", wastePct), 6);
        }

        [Fact]
        public void Apply_DefaultFivePercentMatchesFallbackKnob()
        {
            // COST_DEFAULT_WASTE_PCT default is 5.0 in BOQCostManager.
            Assert.Equal(52.5, WasteFactor.Apply(50.0, "m3", 5.0), 6);
        }
    }
}
