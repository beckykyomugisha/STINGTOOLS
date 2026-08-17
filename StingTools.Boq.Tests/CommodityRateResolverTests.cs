using System.Collections.Generic;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — commodity rates come from a dedicated price list, not the
    /// element-scoped BOQ rate providers. An unpriced commodity resolves to zero
    /// with a visible source, never to a borrowed rate.
    /// </summary>
    public class CommodityRateResolverTests
    {
        private static List<CommodityRate> Baseline() => new List<CommodityRate>
        {
            new CommodityRate { CommodityKey = "cement", SupplierUnit = "Bags", RateUGX = 28000 },
            new CommodityRate { CommodityKey = "sand",   SupplierUnit = "Trips (Sino Truck)", RateUGX = 1400000 }
        };

        [Fact]
        public void Resolves_From_The_Corporate_Baseline()
        {
            var r = new CommodityRateResolver(Baseline(), null);
            var hit = r.Resolve("cement");

            Assert.Equal(28000, hit.RateUGX);
            Assert.Equal("baseline", hit.Source);
        }

        [Fact]
        public void Project_Override_Beats_The_Baseline()
        {
            var overrides = new List<CommodityRate>
            {
                new CommodityRate { CommodityKey = "cement", SupplierUnit = "Bags", RateUGX = 31500 }
            };
            var r = new CommodityRateResolver(Baseline(), overrides);
            var hit = r.Resolve("cement");

            Assert.Equal(31500, hit.RateUGX);
            Assert.Equal("project", hit.Source);
        }

        [Fact]
        public void An_Unpriced_Commodity_Returns_Zero_And_Says_So()
        {
            var r = new CommodityRateResolver(Baseline(), null);
            var hit = r.Resolve("roofing-sheet");

            Assert.Equal(0, hit.RateUGX);
            Assert.Equal("unpriced", hit.Source);
            Assert.Contains("roofing-sheet", r.UnpricedKeys);
        }

        [Fact]
        public void Lookup_Is_Case_Insensitive()
        {
            var r = new CommodityRateResolver(Baseline(), null);
            Assert.Equal(28000, r.Resolve("CEMENT").RateUGX);
        }

        [Fact]
        public void Shipped_Rate_Csv_Parses_With_No_Skipped_Rows()
        {
            string path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_COMMODITY_RATES.csv");
            Assert.True(System.IO.File.Exists(path), $"missing shipped file: {path}");

            var rates = CommodityRateResolver.ParseCsv(
                System.IO.File.ReadAllLines(path), out var skipped);

            Assert.Empty(skipped);
            Assert.NotEmpty(rates);
            Assert.All(rates, r => Assert.True(r.RateUGX > 0, $"{r.CommodityKey} priced at zero"));
        }

        [Fact]
        public void Every_Supplier_Unit_Rule_Has_A_Matching_Rate()
        {
            // Guards the seam: a commodity that can be measured but not priced
            // would export a confident-looking zero.
            string unitsPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json");
            string ratesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_COMMODITY_RATES.csv");

            var table = Newtonsoft.Json.JsonConvert
                .DeserializeObject<SupplierUnitTable>(System.IO.File.ReadAllText(unitsPath));
            var rates = CommodityRateResolver.ParseCsv(System.IO.File.ReadAllLines(ratesPath), out _);
            var resolver = new CommodityRateResolver(rates, null);

            foreach (var rule in table!.Rules)
                Assert.True(resolver.Resolve(rule.CommodityKey).RateUGX > 0,
                    $"commodity '{rule.CommodityKey}' is measurable but has no baseline rate");
        }
    }
}
