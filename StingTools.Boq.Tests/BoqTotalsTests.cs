using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Audit #3/#4/#6 — locks in the single-source markup arithmetic: NRM
    // cascade by default, flat as opt-in, VAT + contract sum, and the OH&P
    // base excluding fully-loaded override lines.
    public class BoqTotalsTests
    {
        private const double Net = 1_000_000.0; // 1,000,000 UGX net measured works

        [Fact]
        public void ParseMode_DefaultsToCascade()
        {
            Assert.Equal(MarkupMode.Cascade, BoqTotals.ParseMode(null));
            Assert.Equal(MarkupMode.Cascade, BoqTotals.ParseMode(""));
            Assert.Equal(MarkupMode.Cascade, BoqTotals.ParseMode("nonsense"));
            Assert.Equal(MarkupMode.Flat, BoqTotals.ParseMode("flat"));
            Assert.Equal(MarkupMode.Flat, BoqTotals.ParseMode(" FLAT "));
        }

        [Fact]
        public void Flat_AddsEveryMarkupOnNet()
        {
            var t = BoqTotals.Compute(Net, Net, 12, 8, 10, 18, MarkupMode.Flat);
            Assert.Equal(120_000, t.Preliminaries, 0);   // 12% of net
            Assert.Equal(80_000, t.OverheadProfit, 0);   //  8% of net
            Assert.Equal(100_000, t.Contingency, 0);     // 10% of net
            Assert.Equal(1_300_000, t.SubTotalExclVat, 0);
            Assert.Equal(234_000, t.Vat, 0);             // 18% of 1.30M
            Assert.Equal(1_534_000, t.ContractSum, 0);
        }

        [Fact]
        public void Cascade_GrossesTheBaseUpForEachLayer()
        {
            var t = BoqTotals.Compute(Net, Net, 12, 8, 10, 18, MarkupMode.Cascade);
            Assert.Equal(120_000, t.Preliminaries, 0);            // 12% of net
            // OH&P on (net + prelims) = 8% of 1,120,000 = 89,600
            Assert.Equal(89_600, t.OverheadProfit, 0);
            // contingency on (net + prelims + ohp) = 10% of 1,209,600 = 120,960
            Assert.Equal(120_960, t.Contingency, 0);
            Assert.Equal(1_330_560, t.SubTotalExclVat, 0);
            Assert.Equal(239_500.8, t.Vat, 0);                    // rounded
        }

        [Fact]
        public void Cascade_ExceedsFlat_ForSameRates()
        {
            // The whole point of the fix: the flat method understated the works.
            var flat = BoqTotals.Compute(Net, Net, 12, 8, 10, 18, MarkupMode.Flat);
            var casc = BoqTotals.Compute(Net, Net, 12, 8, 10, 18, MarkupMode.Cascade);
            Assert.True(casc.SubTotalExclVat > flat.SubTotalExclVat);
        }

        [Fact]
        public void OhpBase_ExcludesLoadedLines_NoDoubleCount()
        {
            // 200,000 of the 1,000,000 net is a fully-loaded override rate that
            // already carries OH&P, so OH&P must only apply to the other 800,000.
            var t = BoqTotals.Compute(Net, 800_000, 12, 8, 10, 18, MarkupMode.Cascade);
            // OH&P = 8% of (800,000 × 1.12) = 8% of 896,000 = 71,680
            Assert.Equal(71_680, t.OverheadProfit, 0);
            // Prelims still on the FULL net works.
            Assert.Equal(120_000, t.Preliminaries, 0);
        }

        [Fact]
        public void NegativeAndNanInputsAreClamped()
        {
            var t = BoqTotals.Compute(Net, -1, -5, double.NaN, -10, 0, MarkupMode.Cascade);
            Assert.Equal(0, t.Preliminaries, 0);
            Assert.Equal(0, t.OverheadProfit, 0);
            Assert.Equal(0, t.Contingency, 0);
            Assert.Equal(0, t.Vat, 0);
            Assert.Equal(Net, t.SubTotalExclVat, 0);
            Assert.Equal(Net, t.ContractSum, 0);
        }
    }
}
