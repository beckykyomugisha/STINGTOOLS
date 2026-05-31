using StingTools.UI;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Z-24 — locks in that MATERIAL_LOOKUP.csv's LONG format (Category,TypeKey,
    // Property,Value) is parsed/pivoted correctly. Before Z-24 the loader looked
    // for a wide "Material"/"Name" column, found none (iName < 0), loaded an
    // empty cache, and GetCarbon/GetCost/GetDensity always returned 0. These
    // tests run against the pure MaterialLookupParser so the regression can't
    // recur silently.
    public class MaterialLookupParserTests
    {
        // Mirrors the shipped file shape: '#' comment banner, real header, then
        // long rows. Two concrete grades + a per-category DEFAULT + a unique
        // bond-type key with several properties.
        private const string Sample = @"# MATERIAL_LOOKUP.csv v1.0
# Format: Category, TypeKey, Property, Value, Unit, Description
Category,TypeKey,Property,Value,Unit,Description
CONCRETE,C30,CEMENT_BAGS_PER_M3,6.5,bags/m³,Cement bags
CONCRETE,C30,STEEL_KG_PER_M3,110,kg/m³,Reinforcement
CONCRETE,C30,CARBON_KG_PER_M3,345,kgCO₂/m³,Embodied carbon
CONCRETE,C45,CARBON_KG_PER_M3,400,kgCO₂/m³,Embodied carbon
CONCRETE,DEFAULT,CARBON_KG_PER_M3,350,kgCO₂/m³,Default embodied carbon
BRICK_BOND,FLEMISH,BRICKS_PER_M2,79,nr/m²,Bricks per m²
BRICK_BOND,FLEMISH,WASTE_PCT,8,%,Cutting waste";

        private static System.Collections.Generic.Dictionary<string, MaterialLookupRow> Parse()
            => MaterialLookupParser.Parse(Sample.Replace("\r\n", "\n").Split('\n'));

        [Fact]
        public void Carbon_ResolvesByBareUniqueTypeKey()
        {
            var c = Parse();
            // Was 0 before Z-24 (empty cache); now the real concrete-grade carbon.
            Assert.Equal(345, c["C30"].CarbonKgCo2e);
            Assert.Equal(400, c["C45"].CarbonKgCo2e);
        }

        [Fact]
        public void Carbon_ResolvesByCompositeKeys()
        {
            var c = Parse();
            Assert.Equal(345, c["CONCRETE C30"].CarbonKgCo2e);
            Assert.Equal(345, c["CONCRETE:C30"].CarbonKgCo2e);
        }

        [Fact]
        public void Category_ResolvesToDefaultRow()
        {
            var c = Parse();
            // Bare category name → that category's DEFAULT row.
            Assert.Equal(350, c["CONCRETE"].CarbonKgCo2e);
        }

        [Fact]
        public void AmbiguousDefaultKey_IsNotBareRegistered()
        {
            var c = Parse();
            // "DEFAULT" is shared across categories, so it must never be bare-keyed.
            Assert.False(c.ContainsKey("DEFAULT"));
        }

        [Fact]
        public void MultipleProperties_PivotOntoOneRow()
        {
            var c = Parse();
            var row = c["C30"];
            // All three C30 properties land on the same pivoted row.
            Assert.Equal(6.5, row.Properties["CEMENT_BAGS_PER_M3"]);
            Assert.Equal(110, row.Properties["STEEL_KG_PER_M3"]);
            Assert.Equal(345, row.Properties["CARBON_KG_PER_M3"]);
            Assert.Equal("CONCRETE", row.Category);
            Assert.Equal("C30", row.TypeKey);
        }

        [Fact]
        public void UniqueBondTypeKey_AndItsProperties()
        {
            var c = Parse();
            Assert.True(c.ContainsKey("FLEMISH"));
            Assert.Equal(79, c["FLEMISH"].Properties["BRICKS_PER_M2"]);
            Assert.Equal(8, c["FLEMISH"].Properties["WASTE_PCT"]);
        }

        [Fact]
        public void NoDensityOrCostData_ReturnsZeroGracefully()
        {
            var c = Parse();
            // The file has no DENSITY/COST property — these stay 0 (data gap).
            Assert.Equal(0, c["C30"].DensityKgM3);
            Assert.Equal(0, c["C30"].Cost);
        }

        [Fact]
        public void UnknownMaterial_IsAbsent()
        {
            var c = Parse();
            Assert.False(c.ContainsKey("STEEL"));
            Assert.False(c.ContainsKey("NONSENSE-KEY"));
        }

        [Fact]
        public void UnrecognisedHeader_YieldsEmptyCache()
        {
            // No Category/TypeKey/Property/Value and no Material/Name column.
            var c = MaterialLookupParser.Parse(new[] { "Foo,Bar,Baz", "1,2,3" });
            Assert.Empty(c);
        }

        [Fact]
        public void LegacyWideFormat_StillParses()
        {
            var c = MaterialLookupParser.Parse(new[]
            {
                "Material,EmbodiedCarbon,Density",
                "Float Glass,1.44,2500"
            });
            Assert.Equal(1.44, c["Float Glass"].CarbonKgCo2e);
            Assert.Equal(2500, c["Float Glass"].DensityKgM3);
        }
    }
}
