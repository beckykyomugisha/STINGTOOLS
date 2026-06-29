using StingTools.Core.Plumbing;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I7 — RWH yield needs real rainfall from the location/climate registry, not 0.
    public class RwhRainfallTests
    {
        [Fact]
        public void SynthesisedSite_CarriesAnnualRainfall()
        {
            var reg = ClimateMonthlyRegistry.LoadFromJson(null);   // empty → synthesise
            var site = reg.ResolveOrSynthesise("bangui", "Bangui", 33, 20,
                latDeg: 4.4, annualRainfallMmFallback: 1500);
            Assert.True(site.AnnualRainfallMm > 1400,
                $"expected ~1500 mm/yr, got {site.AnnualRainfallMm}");
        }

        [Fact]
        public void RainySite_ProducesRealRwhYield_NotZero()
        {
            var reg = ClimateMonthlyRegistry.LoadFromJson(null);
            var site = reg.ResolveOrSynthesise("bangui", "Bangui", 33, 20,
                latDeg: 4.4, annualRainfallMmFallback: 1500);

            var rwh = RainwaterHarvestingCalc.Calculate(
                roofAreaM2: 200, annualRainfallMm: site.AnnualRainfallMm,
                runoffCoefficient: 0, filterEfficiency: 0, dailyDemandM3: 1.0);
            Assert.True(rwh.AnnualYieldM3 > 0, "a rainy site must yield > 0");
        }

        [Fact]
        public void HigherRainfall_YieldsMoreThanDrier()
        {
            var reg = ClimateMonthlyRegistry.LoadFromJson(null);
            var wet = reg.ResolveOrSynthesise("wet", "Wet", 33, 20, latDeg: 4.4, annualRainfallMmFallback: 1500);
            var dry = reg.ResolveOrSynthesise("dry", "Dry", 33, 20, latDeg: 24, annualRainfallMmFallback: 100);

            double wetYield = RainwaterHarvestingCalc.Calculate(500, wet.AnnualRainfallMm, 0, 0, 2.0).AnnualYieldM3;
            double dryYield = RainwaterHarvestingCalc.Calculate(500, dry.AnnualRainfallMm, 0, 0, 2.0).AnnualYieldM3;
            Assert.True(wetYield > dryYield);
        }
    }
}
