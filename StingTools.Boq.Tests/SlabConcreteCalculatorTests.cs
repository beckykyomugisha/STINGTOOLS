using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-4.1 — the parameter-driven slab net-concrete calculator (the DEFAULT
    /// method, replacing the flat solid-fraction). Validates the per-system
    /// formulas + the precast exclusion for maxspan/beam-block.
    /// </summary>
    public class SlabConcreteCalculatorTests
    {
        [Fact]
        public void HollowPot_Net_Concrete_Matches_Formula_And_Fraction_In_Range()
        {
            // topping 50, rib 125, depth 225, pot 350 → pitch = pot + rib = 475.
            var c = SlabConcreteCalculator.Compute(new SlabCalcInput
            {
                ToppingMm = 50, RibWidthMm = 125, RibSpacingMm = 475, RibDepthMm = 225,
                PotWidthMm = 350, PotLengthMm = 300, TwoWay = false, RibsArePrecast = false
            });
            Assert.True(c.Valid);
            double ribFrac = 0.125 / 0.475;
            double expected = 0.050 + ribFrac * 0.225;   // topping + rib concrete
            Assert.Equal(expected, c.InsituConcreteM3PerM2, 5);
            // Solid fraction (net ÷ gross depth) is a genuine void reduction. Wide
            // pots put the accurate fraction ~0.40 — BELOW the optimistic 0.62 flat
            // fallback — so the calculator is the more accurate default.
            Assert.InRange(c.SolidFraction, 0.35, 0.70);
            Assert.True(c.SolidFraction < 0.999);
            Assert.True(c.InfillBlockCountPerM2 > 0);      // pots counted
        }

        [Fact]
        public void Ribbed_OneWay_Formula()
        {
            var c = SlabConcreteCalculator.Compute(new SlabCalcInput
            {
                ToppingMm = 75, RibWidthMm = 125, RibSpacingMm = 600, RibDepthMm = 300,
                TwoWay = false, RibsArePrecast = false
            });
            double expected = 0.075 + (0.125 / 0.600) * 0.300;
            Assert.Equal(expected, c.InsituConcreteM3PerM2, 5);
        }

        [Fact]
        public void Waffle_TwoWay_Subtracts_Rib_Intersection()
        {
            double w = 0.150, s = 0.750, dep = 0.325, top = 0.075;
            var c = SlabConcreteCalculator.Compute(new SlabCalcInput
            {
                ToppingMm = 75, RibWidthMm = 150, RibSpacingMm = 750, RibDepthMm = 325,
                PotWidthMm = 600, PotLengthMm = 600, TwoWay = true, RibsArePrecast = false
            });
            double f = w / s;
            double expected = top + (2 * f - f * f) * dep;   // two-way minus crossing
            Assert.Equal(expected, c.InsituConcreteM3PerM2, 5);
            // Two-way rib concrete exceeds the one-way equivalent.
            double oneWay = top + f * dep;
            Assert.True(c.InsituConcreteM3PerM2 > oneWay);
        }

        [Fact]
        public void Maxspan_InSitu_Is_Topping_Only_And_Ribs_Are_Precast()
        {
            var c = SlabConcreteCalculator.Compute(new SlabCalcInput
            {
                ToppingMm = 65, RibWidthMm = 125, RibSpacingMm = 625, RibDepthMm = 200,
                PotWidthMm = 500, PotLengthMm = 600, TwoWay = false, RibsArePrecast = true
            });
            // In-situ concrete = topping only.
            Assert.Equal(0.065, c.InsituConcreteM3PerM2, 5);
            Assert.Equal(0, c.InsituRibM3PerM2, 6);
            // Precast rib concrete + length are reported separately (excluded).
            Assert.True(c.PrecastRibConcreteM3PerM2 > 0);
            Assert.Equal(1.0 / 0.625, c.PrecastRibLengthMPerM2, 4);
            // Blocks counted.
            Assert.True(c.InfillBlockCountPerM2 > 0);
            // Solid fraction is low (most depth is precast + void).
            Assert.True(c.SolidFraction < 0.5);
        }

        [Fact]
        public void Maxspan_Does_Not_Leave_Precast_In_The_InSitu_Line()
        {
            // Regression: a solid measure would be topping+ribDepth = 265mm; in-situ
            // must be topping (65mm) only — ~4× less — so precast isn't mis-billed.
            var c = SlabConcreteCalculator.Compute(new SlabCalcInput
            {
                ToppingMm = 65, RibWidthMm = 125, RibSpacingMm = 625, RibDepthMm = 200,
                RibsArePrecast = true
            });
            double solidDepth = 0.065 + 0.200;
            Assert.True(c.InsituConcreteM3PerM2 < solidDepth / 3.0);
        }

        [Fact]
        public void Insufficient_Dims_Returns_Invalid()
        {
            var c = SlabConcreteCalculator.Compute(new SlabCalcInput()); // all zero
            Assert.False(c.Valid);
        }
    }
}
