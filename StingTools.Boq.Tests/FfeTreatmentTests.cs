using System;
using System.Collections.Generic;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase C.2 (KUT lifecycle) — FF&E BOQ-treatment resolution. KUT default is the
    // transparent Owner-procured FF&E category ("ffe"); categories may be flipped to
    // measured, excluded, or the explicit contractual pcSum.
    public class FfeTreatmentTests
    {
        [Theory]
        [InlineData("ffe", "ffe")]
        [InlineData("owner-procured", "ffe")]
        [InlineData("", "ffe")]
        [InlineData(null, "ffe")]
        [InlineData("nonsense", "ffe")]
        [InlineData("pcSum", "pcSum")]
        [InlineData("PC", "pcSum")]
        [InlineData("provisional", "pcSum")]
        [InlineData("measured", "measured")]
        [InlineData("Measure", "measured")]
        [InlineData("ownerSupplied-excluded", "ownerSupplied-excluded")]
        [InlineData("excluded", "ownerSupplied-excluded")]
        public void Normalize_TolerantOfAliases(string raw, string expected)
            => Assert.Equal(expected, FfeTreatment.Normalize(raw));

        [Fact]
        public void Resolve_DefaultsToFfe_WhenNoOverride()
        {
            Assert.Equal(FfeTreatment.Ffe, FfeTreatment.Resolve("Furniture", "ffe", null));
            Assert.Equal(FfeTreatment.Ffe, FfeTreatment.Resolve("Furniture", null, new Dictionary<string, string>()));
        }

        [Fact]
        public void Resolve_PerCategoryOverrideWins_CaseInsensitive()
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Casework"] = "measured",
                ["Specialty Equipment"] = "ownerSupplied-excluded",
                ["Lighting Fixtures"] = "pcSum",
            };
            Assert.Equal(FfeTreatment.Measured, FfeTreatment.Resolve("casework", "ffe", overrides));
            Assert.Equal(FfeTreatment.Excluded, FfeTreatment.Resolve("Specialty Equipment", "ffe", overrides));
            Assert.Equal(FfeTreatment.PcSum, FfeTreatment.Resolve("lighting fixtures", "ffe", overrides));
            // Unlisted category falls back to the map default.
            Assert.Equal(FfeTreatment.Ffe, FfeTreatment.Resolve("Furniture", "ffe", overrides));
        }

        [Fact]
        public void Resolve_ExactlyOneTreatment_NeverBlank()
        {
            foreach (var cat in new[] { "Furniture", "Casework", "Lighting Fixtures", "Unknown" })
            {
                string t = FfeTreatment.Resolve(cat, "ffe", null);
                Assert.Contains(t, new[] { FfeTreatment.Ffe, FfeTreatment.PcSum, FfeTreatment.Measured, FfeTreatment.Excluded });
            }
        }
    }
}
