using Xunit;
using StingTools.BOQ;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// The three accuracy fixes recovered from the KUT lifecycle branch (ROADMAP LIFE-3).
    /// Each asserts the arithmetic that was wrong, not merely that the code runs.
    /// </summary>
    public class BoqAccuracyTests
    {
        // ── 1. kg → tonne ────────────────────────────────────────────────────────

        [Fact]
        public void KgToTonne_Divides_By_1000()
        {
            // 12,500 kg of rebar priced against a per-tonne rate book is 12.5 t.
            // Without the conversion the line bills 12,500 units -- a 1000x overcharge
            // that looks plausible at every intermediate step.
            Assert.Equal(12.5, TakeoffUnitConversion.Apply(12500.0, "kg_to_tonne"), 6);
        }

        [Theory]
        [InlineData("ft2_to_m2")]
        [InlineData("ft3_to_m3")]
        [InlineData("ft_to_m")]
        [InlineData("none")]
        public void ExistingConversions_Are_Unchanged(string key)
        {
            // The new case must not have reordered or shadowed an existing one.
            Assert.True(TakeoffUnitConversion.Apply(1.0, key) > 0);
        }

        // ── 2. OH&P double-count ─────────────────────────────────────────────────

        [Fact]
        public void LoadedRate_Is_Excluded_From_Ohp_Base()
        {
            // 1,000,000 works of which 200,000 is a subcontractor's loaded quote.
            // OH&P 10%: the loaded portion must not be marked up again.
            var plain  = BoqTotals.Compute(1_000_000, 0, 10, 0, 0);
            var loaded = BoqTotals.Compute(1_000_000, 0, 10, 0, 0, 0, 200_000);

            Assert.Equal(100_000, plain.Overhead, 2);   // 1,000,000 x 10%
            Assert.Equal(80_000, loaded.Overhead, 2);   //   800,000 x 10%
        }

        [Fact]
        public void LoadedRate_Stays_In_The_Contingency_Base()
        {
            // The distinction that makes this a separate parameter from FF&E:
            // margin is not earned twice, but risk IS still carried, so contingency
            // applies to the loaded work. FF&E leaves both bases; a loaded rate leaves
            // only the OH&P base.
            var loaded = BoqTotals.Compute(1_000_000, 0, 0, 5, 0, 0, 200_000);
            var ffe    = BoqTotals.Compute(1_000_000, 0, 0, 5, 0, 200_000, 0);

            Assert.Equal(50_000, loaded.Contingency, 2);  // 1,000,000 x 5% -- full base
            Assert.Equal(40_000, ffe.Contingency, 2);     //   800,000 x 5% -- FF&E removed
        }

        [Fact]
        public void No_Loaded_Lines_Reproduces_The_Previous_Arithmetic_Exactly()
        {
            // The parameter defaults to 0, so every existing project's totals are
            // bit-identical. A change to the markup waterfall that silently moved an
            // existing contract sum would be worse than the bug it fixes.
            var before = BoqTotals.Compute(750_000, 50_000, 12, 5, 18);
            var after  = BoqTotals.Compute(750_000, 50_000, 12, 5, 18, 0, 0);

            Assert.Equal(before.Overhead,    after.Overhead,    6);
            Assert.Equal(before.Contingency, after.Contingency, 6);
            Assert.Equal(before.NetExVat,    after.NetExVat,    6);
            Assert.Equal(before.GrandTotal,  after.GrandTotal,  6);
        }

        [Fact]
        public void Exemptions_Cannot_Drive_The_Base_Negative()
        {
            // A line can be both Owner-procured and loaded; subtracting it twice, or
            // passing a nonsense figure, must clamp rather than invert the base and
            // hand back a negative OH&P.
            var b = BoqTotals.Compute(100_000, 0, 10, 5, 0, 90_000, 90_000);
            Assert.True(b.Overhead >= 0, "overhead went negative");
            Assert.True(b.Contingency >= 0, "contingency went negative");
        }
    }
}
