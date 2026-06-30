using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // PM-5 — per-material/category waste table shared by the cost + carbon paths.
    public class WasteTableTests
    {
        [Theory]
        [InlineData("Rebar 16mm", 2.5)]
        [InlineData("Reinforcement Bar", 2.5)]
        [InlineData("In-situ Concrete C30", 5.0)]
        [InlineData("Cement Mortar", 10.0)]
        [InlineData("Clay Brick", 5.0)]
        [InlineData("Softwood Timber", 10.0)]
        [InlineData("Ceramic Floor Tile", 10.0)]
        [InlineData("Copper Pipe", 3.0)]
        public void Lookup_matches_material_keyword(string material, double expected)
        {
            Assert.Equal(expected, WasteTable.Lookup(material));
        }

        [Fact]
        public void Lookup_returns_null_for_unmatched()
        {
            Assert.Null(WasteTable.Lookup("Sanitary Fitting"));
            Assert.Null(WasteTable.Lookup(""));
            Assert.Null(WasteTable.Lookup(null));
        }

        [Fact]
        public void Override_wins_over_table_and_default()
        {
            // Explicit per-element override (>0) always wins.
            Assert.Equal(12.0, WasteTable.ResolveWastePercent("Timber", "Walls", 12.0, 5.0));
        }

        [Fact]
        public void Table_used_when_no_override()
        {
            // Timber → 10% from the table, not the 5% project default.
            Assert.Equal(10.0, WasteTable.ResolveWastePercent("Timber Stud", "Walls", 0.0, 5.0));
        }

        [Fact]
        public void Category_consulted_when_material_unmatched()
        {
            // Material has no keyword; category "Structural Columns" → no hit either,
            // but category "Concrete" would. Verify the category fallback fires.
            Assert.Equal(5.0, WasteTable.ResolveWastePercent("Generic", "Concrete Slab", 0.0, 2.0));
        }

        [Fact]
        public void Project_default_when_nothing_matches()
        {
            // No override, no table hit on material or category → project default.
            Assert.Equal(7.0, WasteTable.ResolveWastePercent("Pump", "Mechanical Equipment", 0.0, 7.0));
            // NaN / negative override falls through to the table/default path.
            Assert.Equal(7.0, WasteTable.ResolveWastePercent("Pump", "Equipment", double.NaN, 7.0));
            Assert.Equal(7.0, WasteTable.ResolveWastePercent("Pump", "Equipment", -3.0, 7.0));
        }

        [Fact]
        public void Resolved_waste_feeds_WasteFactor_Apply_consistently()
        {
            // The same resolved % grosses up a quantity identically whether the
            // caller is the cost path or the carbon path.
            double pct = WasteTable.ResolveWastePercent("Reinforcement", "Structural Framing", 0.0, 5.0);
            Assert.Equal(2.5, pct);
            Assert.Equal(102.5, WasteFactor.Apply(100.0, "kg", pct), 6);
        }
    }
}
