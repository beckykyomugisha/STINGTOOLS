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

        // ── Z-21b — single-surface waste (no rate × quantity double-count) ──

        [Fact]
        public void ResolveWastePercent_ExplicitOverrideWins()
        {
            // Element carrying StingCostRateOverride.WastePercent = 8 → 8% governs.
            Assert.Equal(8.0, WasteFactor.ResolveWastePercent(8.0, 5.0), 6);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        public void ResolveWastePercent_NoOverrideFallsToProjectDefault(double overrideWaste)
        {
            Assert.Equal(5.0, WasteFactor.ResolveWastePercent(overrideWaste, 5.0), 6);
        }

        [Fact]
        public void LineTotal_RateOverrideElement_WastesExactlyOnce()
        {
            // Element: 10 m³, base rate 100, explicit rate-override waste 8%.
            // Z-21b convention: waste applies on the QUANTITY only; the rate
            // carries OH&P (none here) but NOT waste.
            const double rawQty = 10.0, baseRate = 100.0, ovrWaste = 8.0;

            double wastePct = WasteFactor.ResolveWastePercent(ovrWaste, 5.0);
            double qty = WasteFactor.Apply(rawQty, "m3", wastePct);   // 10 × 1.08
            double rate = baseRate;                                    // NOT inflated by waste
            double lineTotal = qty * rate;

            // Wasted exactly once: 10 × 1.08 × 100 = 1080.
            Assert.Equal(1080.0, lineTotal, 6);

            // And NOT the old double-count (waste on both rate and qty):
            double doubleCounted = WasteFactor.Apply(rawQty, "m3", ovrWaste)
                                 * (baseRate * (1.0 + ovrWaste / 100.0)); // 10.8 × 108 = 1166.4
            Assert.NotEqual(doubleCounted, lineTotal);
            Assert.Equal(1166.4, doubleCounted, 6); // documents the bug that no longer happens
        }

        [Fact]
        public void LineTotal_NonOverrideElement_UnchangedFromZ21()
        {
            // No rate-override → project default 5% on quantity, rate untouched.
            const double rawQty = 10.0, baseRate = 100.0;
            double wastePct = WasteFactor.ResolveWastePercent(0.0, 5.0);   // → 5
            double lineTotal = WasteFactor.Apply(rawQty, "m3", wastePct) * baseRate;
            Assert.Equal(1050.0, lineTotal, 6); // identical to Z-21 behaviour
        }
    }
}
