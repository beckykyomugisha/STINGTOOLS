using StingTools.BOQ.Rates;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// CA-1 — currency safety. These pin the invariants that close the ~3,700×
    /// silent-error class: the project base is UGX, a blank currency defaults to
    /// UGX (never GBP), and one FX pair rebases everything.
    /// </summary>
    public class RateCurrencyTests
    {
        private const double UgxPerUsd = 3700.0;
        private const double UgxPerGbp = 4700.0;

        [Fact]
        public void Base_Is_Ugx()
        {
            Assert.Equal("UGX", RateCurrency.Base);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_Currency_Defaults_To_Ugx_Not_Gbp(string code)
        {
            Assert.Equal("UGX", RateCurrency.Normalize(code));
        }

        [Fact]
        public void Material_Library_Ugx_Cost_Is_Not_Inflated()
        {
            // A UGX project material cost (entered in the MAT panel) must price at
            // its UGX magnitude — NOT ×3,700. (Regression: it was labelled USD.)
            double ugxCost = 50_000; // UGX 50k/m²
            double toUgx = RateCurrency.ToUgx(ugxCost, "UGX", UgxPerUsd, UgxPerGbp);
            Assert.Equal(50_000, toUgx, 3);
        }

        [Fact]
        public void Default_Table_Usd_Benchmark_Rebases_To_Ugx_Magnitude()
        {
            // A category served only by the default table (Walls=85 USD benchmark)
            // must price at UGX magnitude (~315,000), not 85.
            double walls = 85; // USD/m²
            double toUgx = RateCurrency.ToUgx(walls, "USD", UgxPerUsd, UgxPerGbp);
            Assert.Equal(314_500, toUgx, 3);
            Assert.True(toUgx > 100_000, "default-table rate must rebase to UGX magnitude");
        }

        [Fact]
        public void Blank_Currency_Rate_Treated_As_Ugx_Never_Times_4700()
        {
            // A rate row missing its currency must be treated as UGX (1:1), never
            // silently ×4,700 (the old GBP default).
            double rate = 120_000;
            double toUgx = RateCurrency.ToUgx(rate, null, UgxPerUsd, UgxPerGbp);
            Assert.Equal(120_000, toUgx, 3);
            Assert.NotEqual(120_000 * UgxPerGbp, toUgx, 0);
        }

        [Fact]
        public void Unknown_Currency_Treated_As_Ugx_Not_Scaled()
        {
            double rate = 1000;
            Assert.Equal(1000, RateCurrency.ToUgx(rate, "ZZZ", UgxPerUsd, UgxPerGbp), 3);
        }

        [Fact]
        public void RateUsd_Derives_From_Same_Fx_As_ToUgx()
        {
            // Round-trip: a USD benchmark → UGX → back to USD via the SAME FX must
            // return the original (no second FX source).
            double usd = 85;
            double ugx = RateCurrency.ToUgx(usd, "USD", UgxPerUsd, UgxPerGbp);
            double back = RateCurrency.FromUgx(ugx, "USD", UgxPerUsd, UgxPerGbp);
            Assert.Equal(usd, back, 6);
        }

        [Fact]
        public void Convert_Same_Currency_Is_Identity()
        {
            Assert.Equal(777, RateCurrency.Convert(777, "UGX", "UGX", UgxPerUsd, UgxPerGbp), 6);
            Assert.Equal(777, RateCurrency.Convert(777, null, "", UgxPerUsd, UgxPerGbp), 6);
        }

        [Fact]
        public void Zero_Fx_Falls_Back_To_Safe_Default_Not_DivideByZero()
        {
            // FromUgx with a zero/blank FX must not throw or return NaN.
            double usd = RateCurrency.FromUgx(370_000, "USD", 0, 0);
            Assert.Equal(100, usd, 0); // 370,000 / 3700 fallback
        }
    }
}
