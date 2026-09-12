using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Which commodities feed a consumable driver is DATA, not code.
    ///
    /// It used to be two hardcoded keys:
    ///
    ///     commodityKey == "roof-sheet" || commodityKey == "roof-tile"
    ///
    /// Five roofing commodities were then added to the data file, none of them
    /// fed the fastener driver, and a real export reported the driver as zero
    /// for a shingle roof that was measured, converted and priced three rows
    /// above it. Two lists of roofing commodities — one in code, one in data —
    /// drift the moment either is edited, and only the data one ever is.
    /// </summary>
    public class FeedsDriverTests
    {
        private static SupplierUnitTable Table(params SupplierUnitRule[] rules) =>
            new SupplierUnitTable { Rules = rules.ToList() };

        private static SupplierUnitRule Covering(string key, string driver = "roof_covering_m2") =>
            new SupplierUnitRule
            {
                CommodityKey = key, SupplierUnit = "No.", SourceUnit = "m2",
                SourceUnitsPerSupplierUnit = 1, MatchCategories = { "Roofs" },
                MatchTypePatterns = { key }, FeedsDriver = driver
            };

        private static ConstituentInput Row(string type, double m2 = 100, string unit = "m2") =>
            new ConstituentInput
            {
                ConstituentKind = "", Category = "Roofs", TypeName = type,
                Description = type, Unit = unit, Quantity = m2
            };

        [Fact]
        public void A_Commodity_That_Declares_The_Driver_Feeds_It()
        {
            var d = ConsumableDrivers.From(new[] { Row("roof-shingle") },
                                           Table(Covering("roof-shingle")));

            Assert.Equal(100, d.RoofCoveringM2);
        }

        [Theory]
        [InlineData("roof-shingle")]
        [InlineData("roof-tile-stonecoated")]
        [InlineData("roof-tile-clay")]
        [InlineData("roof-tile-concrete")]
        [InlineData("roof-sheet-boxprofile")]
        public void Every_Roofing_Commodity_Added_In_840_Feeds_It_Too(string key)
        {
            // The five that silently did not. Named individually so a future
            // addition that forgets the declaration is a visible omission
            // rather than a silent one.
            var d = ConsumableDrivers.From(new[] { Row(key) }, Table(Covering(key)));

            Assert.Equal(100, d.RoofCoveringM2);
        }

        [Fact]
        public void A_Commodity_Declaring_Nothing_Feeds_Nothing()
        {
            var d = ConsumableDrivers.From(new[] { Row("roof-underlay") },
                                           Table(Covering("roof-underlay", driver: "")));

            Assert.Equal(0, d.RoofCoveringM2);
        }

        [Fact]
        public void The_Unit_Guard_Survives_The_Change()
        {
            // A count is never added to an area total, whatever the rule says.
            var d = ConsumableDrivers.From(new[] { Row("roof-shingle", 29, "each") },
                                           Table(Covering("roof-shingle")));

            Assert.Equal(0, d.RoofCoveringM2);
            Assert.NotEmpty(d.UnitMismatches);
        }

        [Fact]
        public void An_Unattributed_Covering_Is_Still_Tracked_Apart()
        {
            // Category matches a covering, type matches no pattern. The product
            // is unknown, so the ratio for it is unknown — measured, not usable.
            var d = ConsumableDrivers.From(new[] { Row("Generic - 225mm") },
                                           Table(Covering("roof-shingle")));

            Assert.Equal(0, d.RoofCoveringM2);
            Assert.Equal(100, d.RoofCoveringUnattributedM2);
        }

        [Fact]
        public void A_Rule_Naming_A_Driver_That_Does_Not_Exist_Adds_Nothing()
        {
            var d = ConsumableDrivers.From(new[] { Row("roof-shingle") },
                                           Table(Covering("roof-shingle", driver: "invented_driver")));

            Assert.Equal(0, d.RoofCoveringM2);
            Assert.Equal(0, d.WalledAreaM2);
            Assert.Equal(0, d.FormworkM2);
        }

        [Fact]
        public void The_Material_Is_Used_When_Resolving_A_Driver_Row()
        {
            // The shingle roof in the real model: type "Generic - 225mm 2",
            // material "Asphalt Shingle". Before this the driver pass resolved
            // WITHOUT the material and would have missed it even though the
            // schedule converted it.
            var rule = Covering("roof-shingle");
            rule.MatchTypePatterns.Clear();
            rule.MatchMaterialPatterns.Add("shingle");

            var row = Row("Generic - 225mm 2");
            row.MaterialName = "Asphalt Shingle";

            var d = ConsumableDrivers.From(new[] { row }, Table(rule));

            Assert.Equal(100, d.RoofCoveringM2);
        }

        // ── the shipped file ────────────────────────────────────────────────

        private static SupplierUnitTable Shipped() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(
                File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory,
                                              "Data", "STING_SUPPLIER_UNITS.json")));

        [Fact]
        public void Every_Shipped_Roof_COVERING_Declares_The_Driver()
        {
            // A covering that does not declare it produces no fasteners, and
            // the export says the driver is zero while the covering sits priced
            // three rows above — which is exactly what happened.
            var coverings = Shipped().Rules.Where(r =>
                r.CommodityKey.StartsWith("roof-")
                && r.CommodityKey != "roof-underlay"      // goes UNDER a covering
                && r.CommodityKey != "roof-fastener");    // is the OUTPUT

            Assert.NotEmpty(coverings);
            foreach (var r in coverings)
                Assert.Equal("roof_covering_m2", r.FeedsDriver);
        }

        [Fact]
        public void The_Fastener_Does_Not_Feed_The_Driver_It_Produces()
        {
            // Circular: fasteners are derived FROM covering area.
            Assert.True(string.IsNullOrEmpty(
                Shipped().ResolveByCommodityKey("roof-fastener")?.FeedsDriver));
        }

        [Fact]
        public void Underlay_Does_Not_Feed_The_Covering_Driver()
        {
            // It goes under a covering; counting it would double the area.
            Assert.True(string.IsNullOrEmpty(
                Shipped().ResolveByCommodityKey("roof-underlay")?.FeedsDriver));
        }

        [Fact]
        public void Every_Declared_Driver_In_The_Shipped_File_Is_A_Real_One()
        {
            var known = ConsumablesCalculator.AllDrivers;

            foreach (var r in Shipped().Rules.Where(r => !string.IsNullOrWhiteSpace(r.FeedsDriver)))
                Assert.Contains(r.FeedsDriver, known);
        }
    }
}
