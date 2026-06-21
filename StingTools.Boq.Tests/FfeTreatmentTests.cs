using System;
using System.Collections.Generic;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase C (KUT lifecycle) — FF&E BOQ-treatment resolution. KUT default is a
    // PC sum referencing Fohlio; categories may be flipped to measured or excluded.
    public class FfeTreatmentTests
    {
        [Theory]
        [InlineData("pcSum", "pcSum")]
        [InlineData("PC", "pcSum")]
        [InlineData("provisional", "pcSum")]
        [InlineData("", "pcSum")]
        [InlineData(null, "pcSum")]
        [InlineData("measured", "measured")]
        [InlineData("Measure", "measured")]
        [InlineData("ownerSupplied-excluded", "ownerSupplied-excluded")]
        [InlineData("owner", "ownerSupplied-excluded")]
        [InlineData("excluded", "ownerSupplied-excluded")]
        public void Normalize_TolerantOfAliases(string raw, string expected)
            => Assert.Equal(expected, FfeTreatment.Normalize(raw));

        [Fact]
        public void Resolve_DefaultsToPcSum_WhenNoOverride()
        {
            Assert.Equal(FfeTreatment.PcSum, FfeTreatment.Resolve("Furniture", "pcSum", null));
            Assert.Equal(FfeTreatment.PcSum, FfeTreatment.Resolve("Furniture", null, new Dictionary<string, string>()));
        }

        [Fact]
        public void Resolve_PerCategoryOverrideWins_CaseInsensitive()
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Casework"] = "measured",
                ["Specialty Equipment"] = "ownerSupplied-excluded",
            };
            Assert.Equal(FfeTreatment.Measured, FfeTreatment.Resolve("casework", "pcSum", overrides));
            Assert.Equal(FfeTreatment.Excluded, FfeTreatment.Resolve("Specialty Equipment", "pcSum", overrides));
            // Unlisted category falls back to the map default.
            Assert.Equal(FfeTreatment.PcSum, FfeTreatment.Resolve("Furniture", "pcSum", overrides));
        }

        [Fact]
        public void Resolve_ExactlyOneTreatment_NeverBlank()
        {
            foreach (var cat in new[] { "Furniture", "Casework", "Lighting Fixtures", "Unknown" })
            {
                string t = FfeTreatment.Resolve(cat, "pcSum", null);
                Assert.Contains(t, new[] { FfeTreatment.PcSum, FfeTreatment.Measured, FfeTreatment.Excluded });
            }
        }
    }
}
