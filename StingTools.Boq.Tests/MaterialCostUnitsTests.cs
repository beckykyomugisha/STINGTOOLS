using System;
using System.IO;
using System.Linq;
using StingTools.BOQ.Rates;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-16. The material library carried no unit, so the D9 guard ("a rate
    /// with no unit is not a rate") checked the bill line's unit instead of the
    /// material's, and a per-litre paint rate could price square metres.
    /// </summary>
    public class MaterialCostUnitsTests
    {
        private static string Shipped(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", name));

        [Theory]
        [InlineData("BLE_MATERIALS.csv")]
        [InlineData("MEP_MATERIALS.csv")]
        public void Both_libraries_declare_units_by_name(string file)
        {
            var map = MaterialCostUnits.Parse(Shipped(file), CommodityRateResolver.SplitCsvLine);
            Assert.True(map.Count > 300, $"{file}: only {map.Count} materials carry a unit");
            Assert.All(map.Values, u => Assert.Contains(u, new[] { "m", "m2", "m3", "kg", "tonne", "L", "each" }));
        }

        [Fact]
        public void A_library_without_the_column_declares_nothing()
            => Assert.Empty(MaterialCostUnits.Parse("MAT_NAME,MAT_CODE\nBRICK,B1\n", CommodityRateResolver.SplitCsvLine));

        [Fact]
        public void A_per_litre_rate_cannot_price_square_metres()
        {
            Assert.False(MaterialCostUnits.TryAgree("m2", "L", out _, out string why));
            Assert.Contains("per 'L'", why);
        }

        [Theory]
        [InlineData("m²", "m2")]
        [InlineData("tonne", "kg")]
        [InlineData("nr", "each")]
        public void Equivalent_units_agree(string bill, string material)
            => Assert.True(MaterialCostUnits.TryAgree(bill, material, out _, out _));

        [Fact]
        public void The_material_unit_fills_a_bill_line_without_one()
        {
            Assert.True(MaterialCostUnits.TryAgree("", "m3", out string unit, out _));
            Assert.Equal("m3", unit);
        }

        [Fact]
        public void Neither_unit_known_is_refused_not_defaulted_to_each()
            => Assert.False(MaterialCostUnits.TryAgree(null, null, out _, out _));
    }
}
