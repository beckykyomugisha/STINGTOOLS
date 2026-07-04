using StingTools.Core.Cost;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>PM-3 — index-linked fluctuations (NEDO/BCIS formula + CPI).</summary>
    public class FluctuationsTests
    {
        [Fact]
        public void Formula_SingleIndex_AppliesMovementToAdjustableValue()
        {
            var b = new FluctuationsBasket
            {
                AdjustableWorkValue = 1_000_000,
                NonAdjustablePercent = 10,           // adjustable = 900,000
                Method = "formula"
            };
            b.Lines.Add(new FluctuationLine { Weight = 1, BaseIndex = 100, CurrentIndex = 110 }); // +10%
            // 900,000 × 0.10 = 90,000.
            Assert.Equal(90_000, FluctuationsEngine.Compute(b), 2);
            Assert.Equal(0.10, FluctuationsEngine.BlendedMovement(b), 4);
        }

        [Fact]
        public void Formula_WeightedBasket_BlendsMovements()
        {
            var b = new FluctuationsBasket { AdjustableWorkValue = 1_000_000, NonAdjustablePercent = 10, Method = "formula" };
            b.Lines.Add(new FluctuationLine { Label = "Steel",  Weight = 3, BaseIndex = 100, CurrentIndex = 120 }); // +20%, w3
            b.Lines.Add(new FluctuationLine { Label = "Cement", Weight = 1, BaseIndex = 100, CurrentIndex = 100 }); // 0%,   w1
            // blended = (3×0.2 + 1×0)/4 = 0.15 → 900,000 × 0.15 = 135,000.
            Assert.Equal(0.15, FluctuationsEngine.BlendedMovement(b), 4);
            Assert.Equal(135_000, FluctuationsEngine.Compute(b), 2);
        }

        [Fact]
        public void Cpi_UsesSingleIndexMovement()
        {
            var b = new FluctuationsBasket
            {
                AdjustableWorkValue = 1_000_000, NonAdjustablePercent = 10,
                Method = "cpi", CpiBaseIndex = 100, CpiCurrentIndex = 120
            };
            // 900,000 × 0.20 = 180,000.
            Assert.Equal(180_000, FluctuationsEngine.Compute(b), 2);
        }

        [Fact]
        public void Deflation_GivesNegativeFluctuation()
        {
            var b = new FluctuationsBasket { AdjustableWorkValue = 1_000_000, NonAdjustablePercent = 0, Method = "cpi", CpiBaseIndex = 100, CpiCurrentIndex = 95 };
            Assert.Equal(-50_000, FluctuationsEngine.Compute(b), 2);   // 1,000,000 × −0.05
        }

        [Fact]
        public void ZeroValue_OrZeroWeights_GivesZero()
        {
            Assert.Equal(0, FluctuationsEngine.Compute(new FluctuationsBasket { AdjustableWorkValue = 0 }));
            var noWeights = new FluctuationsBasket { AdjustableWorkValue = 1_000_000, Method = "formula" };
            Assert.Equal(0, FluctuationsEngine.Compute(noWeights));   // empty basket
        }

        [Fact]
        public void NonAdjustable_RemovesThatShare()
        {
            // 100% non-adjustable → nothing recoverable however the index moves.
            var b = new FluctuationsBasket { AdjustableWorkValue = 1_000_000, NonAdjustablePercent = 100, Method = "cpi", CpiBaseIndex = 100, CpiCurrentIndex = 200 };
            Assert.Equal(0, FluctuationsEngine.Compute(b), 2);
        }
    }
}
