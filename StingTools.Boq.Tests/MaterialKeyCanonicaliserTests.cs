using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// RC-1 — param-value canonicalisation so a value that DENOTES a catalogued
    /// type actually resolves instead of silently falling to DEFAULT. The shipped
    /// MATERIAL_LOOKUP.csv registers BLOCK 440x215 (= 10.0/m²); the variants below
    /// must all reach that key, not the DEFAULT (12.5/m², a ~25% error).
    /// </summary>
    public class MaterialKeyCanonicaliserTests
    {
        [Theory]
        [InlineData("440X215", "440x215")]
        [InlineData("440×215", "440x215")]   // Unicode ×
        [InlineData("440 x 215", "440x215")]
        [InlineData("215x440", "440x215")]   // H×L order flipped → largest-first
        [InlineData("440*215", "440x215")]
        [InlineData(" 390 X 190 ", "390x190")]
        public void BlockSize_Canonicalises_To_Table_Key(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.BlockSize(raw));
        }

        [Theory]
        [InlineData("1:6", "1:6")]
        [InlineData("1 : 6", "1:6")]
        [InlineData(" 1:3 ", "1:3")]
        [InlineData("1 :4", "1:4")]
        public void MortarMix_Collapses_Whitespace(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.MortarMix(raw));
        }

        [Theory]
        [InlineData("(iii)", "M_III")]
        [InlineData("M(ii)", "M_II")]
        public void MortarMix_Designations_Alias(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.MortarMix(raw));
        }

        [Theory]
        [InlineData("english garden wall", "ENGLISH_GARDEN_WALL")]
        [InlineData("Flemish", "FLEMISH")]
        [InlineData("running", "STRETCHER")]     // alias
        [InlineData("half brick", "STRETCHER")]  // alias
        public void BrickBond_Canonicalises(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.BrickBond(raw));
        }

        [Theory]
        [InlineData("thin coat", "THIN_COAT")]
        [InlineData("render", "THIN_COAT")]      // alias
        [InlineData("two coat", "STANDARD")]     // alias
        [InlineData("rough cast", "THICK")]      // alias
        public void PlasterType_Canonicalises(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.PlasterType(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_Returns_Empty(string raw)
        {
            Assert.Equal("", MaterialKeyCanonicaliser.BlockSize(raw));
            Assert.Equal("", MaterialKeyCanonicaliser.MortarMix(raw));
            Assert.Equal("", MaterialKeyCanonicaliser.BrickBond(raw));
        }
    }

    /// <summary>
    /// RC-1 — the canonical keys resolve against the SHIPPED table to the correct
    /// ratio, closing the ~25% block-count and half-cement mortar errors.
    /// </summary>
    public class CanonicalKeyResolutionTests
    {
        private static System.Collections.Generic.Dictionary<string, StingTools.UI.MaterialLookupRow> Load()
        {
            var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data", "MATERIAL_LOOKUP.csv");
            return StingTools.UI.MaterialLookupParser.Parse(System.IO.File.ReadAllLines(path));
        }

        [Theory]
        [InlineData("440X215")]
        [InlineData("440×215")]
        [InlineData("440 x 215")]
        public void Block_Variants_All_Resolve_To_10_Per_M2(string raw)
        {
            var d = Load();
            string key = "BLOCK " + MaterialKeyCanonicaliser.BlockSize(raw);
            Assert.True(d.ContainsKey(key), $"canonical key {key} not found");
            Assert.Equal(10.0, d[key].Properties["BLOCKS_PER_M2"], 3);
        }

        [Fact]
        public void Mortar_1to3_With_Spaces_Resolves_To_12_Bags()
        {
            var d = Load();
            string key = "MORTAR " + MaterialKeyCanonicaliser.MortarMix("1 : 3");
            Assert.True(d.ContainsKey(key));
            Assert.Equal(12, d[key].Properties["CEMENT_BAGS_PER_M3"], 3);
        }
    }
}
