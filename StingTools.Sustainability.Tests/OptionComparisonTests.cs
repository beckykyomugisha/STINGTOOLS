using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I12 — rank Design Options by carbon intensity and pick the greenest.
    public class OptionComparisonTests
    {
        private static OptionMetric O(string name, double carbon, double area, bool primary = false)
            => new OptionMetric { Set = "Structure", Option = name, TotalCarbonKg = carbon, AreaM2 = area, IsPrimary = primary };

        [Fact]
        public void Greenest_IsLowestCarbonIntensity()
        {
            var cmp = SustainOptionComparison.ByCarbon(new[]
            {
                O("Concrete frame", 500_000, 1000, primary: true),   // 500 kgCO2e/m²
                O("Steel frame",    400_000, 1000),                   // 400
                O("Timber frame",   180_000, 1000),                   // 180 — greenest
            });
            Assert.Equal("Timber frame", cmp.Greenest.Option);
            Assert.Equal("Timber frame", cmp.Ranked.First().Option);   // ranked ascending
            Assert.Equal("Concrete frame", cmp.Primary.Option);
        }

        [Fact]
        public void IntensityNormalisesByArea()
        {
            var cmp = SustainOptionComparison.ByCarbon(new[]
            {
                O("Big",   600_000, 3000),   // 200 kgCO2e/m²
                O("Small", 300_000, 1000),   // 300 kgCO2e/m²
            });
            Assert.Equal("Big", cmp.Greenest.Option);   // lower per-m², though higher absolute
        }

        [Fact]
        public void NoCarbon_NoGreenest()
        {
            var cmp = SustainOptionComparison.ByCarbon(new[] { O("A", 0, 0), O("B", 0, 0) });
            Assert.Null(cmp.Greenest);
            Assert.Equal(2, cmp.Ranked.Count);
        }

        [Fact]
        public void Empty_IsSafe()
        {
            var cmp = SustainOptionComparison.ByCarbon(null);
            Assert.Empty(cmp.Ranked);
            Assert.Null(cmp.Greenest);
        }
    }
}
