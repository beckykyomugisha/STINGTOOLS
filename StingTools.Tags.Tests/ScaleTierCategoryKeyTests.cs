using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-27 — the Tag Studio Scale-tab multipliers are keyed DUCTS / PIPES /
    /// EQUIPMENT / FIXTURES. Smart Tag Placement maps each element's category to
    /// one of those keys; a category outside the four gets none (multiplier 1.0).
    /// </summary>
    public class ScaleTierCategoryKeyTests
    {
        [Theory]
        [InlineData("Ducts", "DUCTS")]
        [InlineData("duct fittings", "DUCTS")]
        [InlineData("Air Terminals", "DUCTS")]
        [InlineData("Pipes", "PIPES")]
        [InlineData("Pipe Accessories", "PIPES")]
        [InlineData("Mechanical Equipment", "EQUIPMENT")]
        [InlineData("Electrical Equipment", "EQUIPMENT")]
        [InlineData("Lighting Fixtures", "FIXTURES")]
        [InlineData("Plumbing Fixtures", "FIXTURES")]
        [InlineData("Fire Alarm Devices", "FIXTURES")]
        public void MapsTheScaleTabCategories(string category, string key)
            => Assert.Equal(key, ScaleTierCategoryKey.For(category));

        [Theory]
        [InlineData("Walls")]
        [InlineData("Rooms")]
        [InlineData("")]
        [InlineData(null)]
        public void OtherCategoriesHaveNoKey(string category)
            => Assert.Null(ScaleTierCategoryKey.For(category));
    }
}
