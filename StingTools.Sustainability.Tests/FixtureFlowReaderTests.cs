using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Gap fix #2 — read low-flow fixture ratings off the schedule/type name so the
    // water % is model-derived instead of the 25%-below-baseline indicative default.
    public class FixtureFlowReaderTests
    {
        [Theory]
        [InlineData("WC - Close Coupled 4.5L", FixtureKind.Wc)]
        [InlineData("Water Closet Dual Flush", FixtureKind.Wc)]
        [InlineData("Toilet Pan", FixtureKind.Wc)]
        [InlineData("Wall Urinal Waterless", FixtureKind.Urinal)]
        [InlineData("Wash Hand Basin + Mixer", FixtureKind.Basin)]
        [InlineData("Lavatory Tap", FixtureKind.Basin)]
        [InlineData("Shower - Thermostatic Eco", FixtureKind.Shower)]
        [InlineData("Kitchen Sink Mixer", FixtureKind.KitchenTap)]
        [InlineData("Random Furniture", FixtureKind.Unknown)]
        public void Classify_FixtureKind(string name, FixtureKind expected)
            => Assert.Equal(expected, FixtureFlowReader.ClassifyKind(name));

        [Fact]
        public void Urinal_BeatsWcKeyword()
        {
            // A urinal name should classify as urinal even if it mentions nothing
            // WC-ish; and the WC branch must not steal it.
            Assert.Equal(FixtureKind.Urinal, FixtureFlowReader.ClassifyKind("Urinal Bowl"));
        }

        [Fact]
        public void ParseWc_SingleFlush()
            => Assert.Equal(4.5, FixtureFlowReader.ParseFlow(FixtureKind.Wc, "WC 4.5L Single Flush"));

        [Fact]
        public void ParseWc_DualFlush_Averaged()
            // "6/4" → effective average 5.0.
            => Assert.Equal(5.0, FixtureFlowReader.ParseFlow(FixtureKind.Wc, "WC Dual Flush 6/4L"));

        [Fact]
        public void ParseWc_DualFlush_DecimalComma()
            // "4,5/3" → 4.5 & 3 → 3.75 (comma normalised to dot).
            => Assert.Equal(3.75, FixtureFlowReader.ParseFlow(FixtureKind.Wc, "WC 4,5/3 dual"));

        [Fact]
        public void ParseBasin_Lpm()
            => Assert.Equal(5.0, FixtureFlowReader.ParseFlow(FixtureKind.Basin, "Basin Mixer 5 L/min"));

        [Fact]
        public void ParseShower_Lpm()
            => Assert.Equal(8.0, FixtureFlowReader.ParseFlow(FixtureKind.Shower, "Shower Eco 8 lpm"));

        [Fact]
        public void ParseKitchen_Lpm()
            => Assert.Equal(6.0, FixtureFlowReader.ParseFlow(FixtureKind.KitchenTap, "Kitchen Tap 6 L/min"));

        [Fact]
        public void OutOfBandNumber_Ignored_ReturnsNull()
            // A 4-digit year must NOT be read as a flow.
            => Assert.Null(FixtureFlowReader.ParseFlow(FixtureKind.Wc, "WC Model 2024 Series"));

        [Fact]
        public void NoNumber_ReturnsNull()
            => Assert.Null(FixtureFlowReader.ParseFlow(FixtureKind.Basin, "Basin Mixer Chrome"));

        [Fact]
        public void Unknown_Kind_ReturnsNull()
            => Assert.Null(FixtureFlowReader.ParseFlow(FixtureKind.Unknown, "5 L/min"));

        [Fact]
        public void InBand_GuardsValues()
        {
            Assert.True(FixtureFlowReader.InBand(FixtureKind.Wc, 4.5));
            Assert.False(FixtureFlowReader.InBand(FixtureKind.Wc, 2024));
            Assert.True(FixtureFlowReader.InBand(FixtureKind.Shower, 8));
            Assert.False(FixtureFlowReader.InBand(FixtureKind.Shower, 1));   // below shower band
        }

        [Fact]
        public void StandaloneNumber_LastResort()
            // No unit suffix, but an in-band number → accepted as last resort.
            => Assert.Equal(6.0, FixtureFlowReader.ParseFlow(FixtureKind.Wc, "Eco WC 6"));
    }
}
