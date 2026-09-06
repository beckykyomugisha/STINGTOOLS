using StingTools.BOQ;
using Xunit;

namespace StingTools.Cost.Tests
{
    // Owner-procured FF&E is bought direct from the supplier's register. It is real
    // money in the bill, but a main contractor does not earn overhead, profit or
    // contingency on goods it never bought — so it comes out of the markup BASE while
    // staying in the works total. These tests pin that distinction and, just as
    // importantly, pin that a project which never sets an FF&E treatment sees
    // arithmetic identical to before.
    public class BoqTotalsFfeExemptionTests
    {
        private const double Works = 100_000_000.0;
        private const double PrelimPct = 12.0, OhpPct = 8.0, ContPct = 10.0, VatPct = 18.0;

        private static BoqMarkupBreakdown Compute(double exempt) =>
            BoqTotals.Compute(Works, Works * PrelimPct / 100.0, OhpPct, ContPct, VatPct, exempt);

        [Fact]
        public void ZeroExemption_Reproduces_The_Previous_Arithmetic_Exactly()
        {
            var oldWay = BoqTotals.Compute(Works, Works * PrelimPct / 100.0, OhpPct, ContPct, VatPct);
            var withZero = Compute(0);

            Assert.Equal(oldWay.Works, withZero.Works, 6);
            Assert.Equal(oldWay.Prelims, withZero.Prelims, 6);
            Assert.Equal(oldWay.Overhead, withZero.Overhead, 6);
            Assert.Equal(oldWay.Contingency, withZero.Contingency, 6);
            Assert.Equal(oldWay.NetExVat, withZero.NetExVat, 6);
            Assert.Equal(oldWay.Vat, withZero.Vat, 6);
            Assert.Equal(oldWay.GrandTotal, withZero.GrandTotal, 6);
        }

        [Fact]
        public void ExemptWorks_Leave_The_Bill_But_Leave_The_Markup_Base()
        {
            const double exempt = 20_000_000.0;
            var b = Compute(exempt);

            // Prelims keep the FULL works base: site establishment, storage and handling
            // are earned on Owner-supplied goods too.
            Assert.Equal(Works * 0.12, b.Prelims, 6);

            // OH&P and contingency are computed on (works + prelims - exempt).
            double ohpBase = Works + b.Prelims - exempt;
            Assert.Equal(ohpBase * 0.08, b.Overhead, 6);
            Assert.Equal((ohpBase + b.Overhead) * 0.10, b.Contingency, 6);

            // The exempt value is still IN the bill — it is money the Owner spends.
            Assert.Equal(Works + b.Prelims + b.Overhead + b.Contingency, b.NetExVat, 6);
            Assert.Equal(Works, b.Works, 6);
        }

        [Fact]
        public void MoreExemption_Always_Lowers_The_Contract_Sum_Never_The_Works()
        {
            var none = Compute(0);
            var some = Compute(20_000_000);
            var most = Compute(80_000_000);

            Assert.True(some.NetExVat < none.NetExVat);
            Assert.True(most.NetExVat < some.NetExVat);
            Assert.Equal(none.Works, most.Works, 6);   // the works subtotal never moves
        }

        [Theory]
        [InlineData(-5_000_000.0)]                     // nonsense negative
        [InlineData(500_000_000.0)]                    // larger than the works
        public void OutOfRangeExemption_Is_Clamped_Not_Trusted(double exempt)
        {
            var b = Compute(exempt);
            // Clamped into [0, works], so no markup can ever go negative and no
            // exemption can invert the base into a bigger markup than none at all.
            Assert.True(b.Overhead >= 0);
            Assert.True(b.Contingency >= 0);
            Assert.True(b.Overhead <= Compute(0).Overhead);
            Assert.True(b.NetExVat >= b.Works);        // prelims are never exempted
        }
    }
}
