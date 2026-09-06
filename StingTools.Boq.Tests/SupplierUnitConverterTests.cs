using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — SI measured quantities become supplier units. Wastage is a
    /// separate visible step, and countable units round UP: you cannot buy 2.08
    /// trips of sand.
    /// </summary>
    public class SupplierUnitConverterTests
    {
        private static SupplierUnitRule SandTrips() => new SupplierUnitRule
        {
            CommodityKey = "sand",
            Description = "Sand",
            SupplierUnit = "Trips (Sino Truck)",
            SourceUnit = "m3",
            SourceUnitsPerSupplierUnit = 12.0,
            RoundUpToWhole = true,
            DefaultWastagePct = 0
        };

        private static SupplierUnitRule CementBags() => new SupplierUnitRule
        {
            CommodityKey = "cement",
            Description = "Cement (OPC 42.5N)",
            SupplierUnit = "Bags",
            SourceUnit = "bag",
            SourceUnitsPerSupplierUnit = 1.0,
            RoundUpToWhole = true,
            DefaultWastagePct = 2.5
        };

        [Fact]
        public void Converts_Cubic_Metres_To_Whole_Trips_Rounding_Up()
        {
            var r = SupplierUnitConverter.Convert(SandTrips(), sourceQuantity: 25.0);

            Assert.Equal("Trips (Sino Truck)", r.SupplierUnit);
            Assert.Equal(25.0 / 12.0, r.NetQuantity, 6);   // 2.0833…
            Assert.Equal(3, r.OrderQuantity);              // ceil, not 2
        }

        [Fact]
        public void Applies_Wastage_Before_Rounding_And_Reports_It_Separately()
        {
            // 100 bags + 2.5% = 102.5 → 103
            var r = SupplierUnitConverter.Convert(CementBags(), sourceQuantity: 100.0);

            Assert.Equal(100.0, r.NetQuantity, 6);   // net stays PRE-wastage
            Assert.Equal(2.5, r.WastagePct);
            Assert.Equal(103, r.OrderQuantity);
        }

        [Fact]
        public void Non_Countable_Units_Keep_Their_Fraction()
        {
            var rule = SandTrips();
            rule.SupplierUnit = "m³";
            rule.SourceUnitsPerSupplierUnit = 1.0;
            rule.RoundUpToWhole = false;

            var r = SupplierUnitConverter.Convert(rule, sourceQuantity: 2.5);

            Assert.Equal(2.5, r.OrderQuantity, 6);
        }

        [Fact]
        public void A_Zero_Or_Negative_Conversion_Factor_Falls_Back_To_One_Not_Infinity()
        {
            var rule = SandTrips();
            rule.SourceUnitsPerSupplierUnit = 0;   // bad data in the JSON

            var r = SupplierUnitConverter.Convert(rule, sourceQuantity: 25.0);

            Assert.Equal(25.0, r.NetQuantity, 6);
            Assert.False(double.IsInfinity(r.OrderQuantity));
        }

        [Fact]
        public void Rules_Resolve_By_Constituent_Kind()
        {
            var table = new SupplierUnitTable();
            table.Rules.Add(CementBags());
            table.Rules[0].MatchKinds.Add("mortar_cement");
            table.Rules[0].MatchKinds.Add("plaster_cement");

            Assert.Equal("cement", table.ResolveByKind("plaster_cement")?.CommodityKey);
            Assert.Null(table.ResolveByKind("formwork"));
        }

        [Fact]
        public void Shipped_Baseline_Json_Parses_And_Every_Rule_Is_Usable()
        {
            string path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json");
            Assert.True(System.IO.File.Exists(path), $"missing shipped file: {path}");

            var table = Newtonsoft.Json.JsonConvert
                .DeserializeObject<SupplierUnitTable>(System.IO.File.ReadAllText(path));

            Assert.NotNull(table);
            Assert.NotEmpty(table!.Rules);
            foreach (var r in table.Rules)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.CommodityKey), "rule with no commodityKey");
                Assert.False(string.IsNullOrWhiteSpace(r.SupplierUnit), $"{r.CommodityKey}: no supplierUnit");
                Assert.True(r.SourceUnitsPerSupplierUnit > 0, $"{r.CommodityKey}: non-positive conversion factor");
            }
        }
    }
}
