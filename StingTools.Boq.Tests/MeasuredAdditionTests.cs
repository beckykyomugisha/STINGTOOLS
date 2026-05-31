using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Z-23b — NRM2 measured additions (rebar lap, concrete over-order) as opt-in
    // knobs that CANNOT double-count the general COST_DEFAULT_WASTE_PCT.
    public class MeasuredAdditionTests
    {
        // ── Default OFF: zero delivered-number change ──
        [Theory]
        [InlineData("kg")]
        [InlineData("m3")]
        public void KnobsDefaultZero_NoChangeVsWasteOnly(string unit)
        {
            const double baseQ = 1000.0, waste = 5.0;
            double lap   = MeasuredAddition.RebarLapPercent(isRebar: true, knobPercent: 0.0);      // default OFF
            double buf   = MeasuredAddition.ConcreteOverOrderPercent(isConcrete: true, knobPercent: 0.0); // default OFF
            Assert.Equal(0.0, lap);
            Assert.Equal(0.0, buf);
            // GrossUp with 0 addition == WasteFactor.Apply (waste only) — identical.
            Assert.Equal(WasteFactor.Apply(baseQ, unit, waste),
                         MeasuredAddition.GrossUp(baseQ, unit, waste, lap), 6);
        }

        // ── Knobs only fire for the right discipline ──
        [Fact]
        public void RebarLap_OnlyOnRebar()
        {
            Assert.Equal(10.0, MeasuredAddition.RebarLapPercent(isRebar: true,  knobPercent: 10.0));
            Assert.Equal(0.0,  MeasuredAddition.RebarLapPercent(isRebar: false, knobPercent: 10.0)); // steel section, not rebar
        }

        [Fact]
        public void ConcreteBuffer_OnlyOnConcrete()
        {
            Assert.Equal(5.0, MeasuredAddition.ConcreteOverOrderPercent(isConcrete: true,  knobPercent: 5.0));
            Assert.Equal(0.0, MeasuredAddition.ConcreteOverOrderPercent(isConcrete: false, knobPercent: 5.0));
        }

        // ── THE anti-double-count proof ──
        [Fact]
        public void RebarWastePlusLap_EachAppliedOnce_NotCompoundedAsSecondWaste()
        {
            const double baseKg = 1000.0, waste = 5.0, lap = 10.0;
            double result = MeasuredAddition.GrossUp(baseKg, "kg", waste, lap);

            // CORRECT: each distinct allowance applied ONCE, additively:
            //   1000 × (1 + (5 + 10)/100) = 1150.
            Assert.Equal(1150.0, result, 6);

            // NOT a second waste pass (would be 1000 × 1.05 × 1.10 = 1155):
            double compounded = baseKg * (1 + waste / 100.0) * (1 + lap / 100.0);
            Assert.NotEqual(compounded, result);

            // NOT the waste re-applied as the lap (would be 1000 × 1.05 × 1.05 = 1102.5):
            double doubleWaste = baseKg * (1 + waste / 100.0) * (1 + waste / 100.0);
            Assert.NotEqual(doubleWaste, result);

            // Waste-only baseline is strictly less (lap genuinely adds, once):
            Assert.Equal(1050.0, WasteFactor.Apply(baseKg, "kg", waste), 6);
        }

        [Fact]
        public void ConcreteWastePlusBuffer_EachOnce()
        {
            // 10 m³ concrete, 5% waste + 5% over-order = 10 × 1.10 = 11.0 (not 1.05²).
            Assert.Equal(11.0, MeasuredAddition.GrossUp(10.0, "m3", 5.0, 5.0), 6);
        }

        // ── Guards ──
        [Theory]
        [InlineData(-3.0)]
        [InlineData(double.NaN)]
        public void NegativeOrNaN_Addition_TreatedAsZero(double bad)
        {
            Assert.Equal(1050.0, MeasuredAddition.GrossUp(1000.0, "kg", 5.0, bad), 6); // waste only
        }

        [Fact]
        public void NonMeasuredUnit_Untouched()
        {
            Assert.Equal(7.0, MeasuredAddition.GrossUp(7.0, "each", 5.0, 10.0), 6);
        }
    }
}
