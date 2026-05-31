using System;
using System.IO;
using System.Linq;
using StingTools.UI;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Z-24b STEP 1 — locks the metal/glass/timber rows added to MATERIAL_LOOKUP.csv.
    // Pre-population (Z-24) these all returned 0 (LOOKUP carried concrete grades only).
    // Resolves through the same MaterialLookupParser the runtime uses.
    public class MaterialLookupPopulateTests
    {
        private static System.Collections.Generic.Dictionary<string, MaterialLookupRow> Lookup()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "MATERIAL_LOOKUP.csv");
            return MaterialLookupParser.Parse(File.ReadAllLines(path));
        }

        [Theory]
        // material key (bare TypeKey / category) -> expected carbon, density
        [InlineData("GALVANISED", 22372, 7850)] // ICE v3.0 galv sheet 2.85 × 7850 (= Z-20 galv duct)
        [InlineData("STRUCTURAL", 12090, 7850)] // ICE v3.0 structural steel 1.54 × 7850
        [InlineData("COPPER", 31360, 8960)]     // category->DEFAULT row; ICE copper 3.50 × 8960
        [InlineData("FLOAT", 3600, 2500)]       // ICE general glass 1.44 × 2500
        public void NewMaterials_CarbonAndDensity_Populated(string key, double carbon, double density)
        {
            var c = Lookup();
            Assert.True(c.ContainsKey(key), $"LOOKUP missing key '{key}'");
            Assert.Equal(carbon, c[key].CarbonKgCo2e);
            Assert.Equal(density, c[key].DensityKgM3);
        }

        [Fact]
        public void Timber_Softwood_CarriesFossilBiogenicSplit()
        {
            var r = Lookup()["SOFTWOOD"];
            Assert.Equal(126, r.FossilCarbonKgCo2e);     // 0.263 × 480
            Assert.Equal(-787, r.BiogenicCarbonKgCo2e);  // -1.64 × 480
            Assert.Equal(-661, r.CarbonKgCo2e);          // net
            Assert.Equal(r.CarbonKgCo2e, r.FossilCarbonKgCo2e + r.BiogenicCarbonKgCo2e, 3);
            Assert.Equal(480, r.DensityKgM3);
        }

        [Fact]
        public void ConcreteGradeCarbon_StillResolves_NoRegression()
        {
            // Z-24's concrete rows must still work after the append. NOTE: in the
            // real file "C30" is NOT a bare key (it collides — CONCRETE/C30 AND
            // REBAR_LAP/C30 — so the parser only registers the composite). Use it.
            Assert.Equal(345, Lookup()["CONCRETE C30"].CarbonKgCo2e);
            Assert.False(Lookup().ContainsKey("C30")); // ambiguous → not bare-registered
        }

        [Fact]
        public void UnknownMaterial_StillZero()
        {
            Assert.False(Lookup().ContainsKey("UNOBTANIUM"));
        }
    }
}
