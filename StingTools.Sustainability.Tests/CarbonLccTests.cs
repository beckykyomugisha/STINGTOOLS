using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    /// <summary>
    /// CA-4 — carbon-as-cost. Carbon enters the LCC via a configurable carbon
    /// price: carbon cost = price × (embodied + discounted operational). The
    /// whole-life cost is the capital+maintenance LCC plus that carbon cost.
    /// </summary>
    public class CarbonLccTests
    {
        [Fact]
        public void Carbon_Cost_Is_Price_Times_Embodied_Plus_Discounted_Operational()
        {
            // Worked example: 100,000 kg embodied; 2,000 kg/yr operational; price
            // 1,000 UGX/kgCO₂e; 25-yr study; 3.5% discount.
            double embodiedKg = 100_000, opKgYr = 2_000, price = 1_000;
            int years = 25; double rate = 3.5;

            double expectedEmbodied = embodiedKg * price;                       // 100,000,000
            double opAnnualCost = opKgYr * price;                               // 2,000,000/yr
            double expectedOpNpv = SustainNpv.PresentValueAnnuity(opAnnualCost, years, rate);
            double expected = expectedEmbodied + expectedOpNpv;

            Assert.Equal(expected, CarbonLcc.CarbonCostUgx(embodiedKg, opKgYr, price, years, rate), 2);
        }

        [Fact]
        public void Zero_Price_Means_No_Carbon_Cost()
        {
            Assert.Equal(0, CarbonLcc.CarbonCostUgx(100_000, 2_000, 0, 25, 3.5), 6);
        }

        [Fact]
        public void Lifecycle_Incl_Carbon_Equals_Lcc_When_Price_Is_Zero()
        {
            double lcc = 500_000_000;
            Assert.Equal(lcc, CarbonLcc.LifecycleCostInclCarbonUgx(lcc, 100_000, 2_000, 0, 25, 3.5), 6);
        }

        [Fact]
        public void Lifecycle_Incl_Carbon_Adds_The_Carbon_Cost()
        {
            double lcc = 500_000_000;
            double embodiedKg = 80_000, price = 1_200;
            // Per-element basis: operational carbon is building-level (0 here).
            double expected = lcc + embodiedKg * price;
            Assert.Equal(expected, CarbonLcc.LifecycleCostInclCarbonUgx(lcc, embodiedKg, 0, price, 25, 3.5), 2);
        }

        [Fact]
        public void Operational_Carbon_Is_Discounted_Below_Undiscounted_Sum()
        {
            // With a positive discount rate the operational carbon NPV is below the
            // simple price × kg/yr × years.
            double opKgYr = 5_000, price = 900; int years = 60;
            double discounted = CarbonLcc.CarbonCostUgx(0, opKgYr, price, years, 3.5);
            double undiscounted = opKgYr * price * years;
            Assert.True(discounted < undiscounted, "discounted operational carbon must be below the undiscounted sum");
            Assert.True(discounted > 0);
        }
    }
}
