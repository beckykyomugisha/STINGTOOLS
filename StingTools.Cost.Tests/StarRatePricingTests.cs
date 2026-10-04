using StingTools.Core.Variation;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>
    /// DSCH-46b - a star rate is the new rate agreed for varied work the BOQ has no
    /// rate for. It was built and saved but never priced anything: VariationItem
    /// reserved RateSource "StarRate" + StarRateId and nothing set them.
    /// StarRatePricing.NewItem is the one place a star rate becomes a VO item.
    /// </summary>
    public class StarRatePricingTests
    {
        private static StarRate Rate(string currency = "UGX", string unit = "m3")
        {
            var r = new StarRate { Description = "Mass concrete to new pit", Unit = unit, Currency = currency,
                                   OverheadPercent = 10, ProfitPercent = 5 };
            r.MaterialsLines.Add(new StarRateLine { Resource = "Concrete C20", Unit = "m3", Quantity = 1, UnitRate = 400_000 });
            r.LabourLines.Add(new StarRateLine { Resource = "Labourer", Unit = "hr", Hours = 2, UnitRate = 10_000 });
            return r;
        }

        private static VariationInstruction Vo(VariationStatus s = VariationStatus.Draft, string currency = "UGX")
            => new VariationInstruction { Number = "VO-EI-0001", Status = s, Currency = currency };

        [Fact]
        public void PricesANewItemAtTheFinalRate_AndLinksIt()
        {
            var vo = Vo(); var r = Rate();
            var item = StarRatePricing.NewItem(vo, r, 12.5, out string why);
            Assert.Null(why);
            Assert.Equal(r.FinalRate, item.UnitRate);
            Assert.Equal(12.5, item.Quantity);
            Assert.Equal("m3", item.Unit);
            Assert.Equal("StarRate", item.RateSource);
            Assert.Equal(r.Id, item.StarRateId);
            Assert.Equal("", item.DayworkId);
            Assert.Equal(System.Math.Round(12.5 * r.FinalRate, 2), item.TotalValue);
        }

        [Theory]
        [InlineData(VariationStatus.Approved)]
        [InlineData(VariationStatus.Rejected)]
        [InlineData(VariationStatus.Incorporated)]
        public void AValuedOrClosedVariation_IsNotRepriced(VariationStatus s)
        {
            Assert.Null(StarRatePricing.NewItem(Vo(s), Rate(), 1, out string why));
            Assert.Contains(s.ToString(), why);
        }

        [Fact]
        public void CurrencyMismatch_IsRefused_NotConverted()
        {
            Assert.Null(StarRatePricing.NewItem(Vo(currency: "USD"), Rate("UGX"), 1, out string why));
            Assert.Contains("USD", why);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        [InlineData(double.NaN)]
        public void NoMeasuredQuantity_IsRefused(double qty)
        {
            Assert.Null(StarRatePricing.NewItem(Vo(), Rate(), qty, out string why));
            Assert.NotNull(why);
        }

        [Fact]
        public void AnEmptyBuildUp_IsRefused()
        {
            Assert.Null(StarRatePricing.NewItem(Vo(), new StarRate { Description = "x" }, 1, out string why));
            Assert.NotNull(why);
        }
    }
}
