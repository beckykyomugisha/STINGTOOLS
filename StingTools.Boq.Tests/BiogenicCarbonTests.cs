using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Z-25b — locks the WLCA fossil/biogenic split convention used by the
    // embodied-carbon report: HEADLINE = A1-A3 fossil (gross upfront); biogenic
    // is a separate ≤0 line; net = fossil + biogenic. (RICS WLCA 2nd ed 2023 /
    // RIBA 2030 / LETI.) BiogenicCarbon is the pure helper the report calls.
    public class BiogenicCarbonTests
    {
        [Theory]
        [InlineData("Timber Board Ceiling")]
        [InlineData("SOFTWOOD PAINTED SKIRTING")]
        [InlineData("Oak Hardwood Floor")]
        [InlineData("Plywood Panel")]
        [InlineData("Glulam Beam")]
        [InlineData("CLT Slab")]
        public void Timber_IsBiogenic_WithSequestration(string mat)
        {
            Assert.True(BiogenicCarbon.IsBiogenic(mat));
            Assert.Equal(-1.64, BiogenicCarbon.BiogenicFactorPerKg(mat));     // ICE v3.0 sequestration
            Assert.Equal(0.263, BiogenicCarbon.FossilFactorPerKg(mat, 0.31)); // fossil overrides gross keyword
        }

        [Theory]
        [InlineData("Galvanized Steel", 1.55)]
        [InlineData("Concrete C30", 0.13)]
        [InlineData("Float Glass", 1.44)]
        [InlineData("Copper Pipe", 3.50)]
        public void NonTimber_BiogenicZero_FossilUnchanged(string mat, double gross)
        {
            Assert.False(BiogenicCarbon.IsBiogenic(mat));
            Assert.Equal(0.0, BiogenicCarbon.BiogenicFactorPerKg(mat));        // separate line = 0
            Assert.Equal(gross, BiogenicCarbon.FossilFactorPerKg(mat, gross)); // fossil = gross (no regression)
        }

        [Fact]
        public void Headline_IsFossil_NotNet_ForTimberRichElement()
        {
            // Sample: 1 m³ timber, density 480 kg/m³ → 480 kg.
            const double mass = 480.0;
            double fossil   = mass * BiogenicCarbon.FossilFactorPerKg("Timber", 0.31);
            double biogenic = mass * BiogenicCarbon.BiogenicFactorPerKg("Timber");
            double net      = fossil + biogenic;

            Assert.Equal(126.24, fossil, 2);    // HEADLINE — gross upfront, positive
            Assert.Equal(-787.2, biogenic, 2);  // separate, negative
            Assert.Equal(-660.96, net, 2);      // whole-life context

            // The headline must be the fossil (positive) figure, never the net.
            Assert.True(fossil > 0);
            Assert.True(net < 0);
            Assert.NotEqual(net, fossil);
        }

        [Fact]
        public void NonTimber_NetEqualsFossil()
        {
            const double mass = 1000.0;
            double fossil   = mass * BiogenicCarbon.FossilFactorPerKg("Steel", 1.55);
            double biogenic = mass * BiogenicCarbon.BiogenicFactorPerKg("Steel");
            Assert.Equal(0.0, biogenic);
            Assert.Equal(fossil, fossil + biogenic); // net == fossil when no sequestration
        }
    }
}
