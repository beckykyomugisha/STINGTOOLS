using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I9 — global location resolution: ANY site resolves a real climate zone from
    // latitude (not a temperate default) and a real grid factor from country.
    public class GlobalLocationTests
    {
        [Theory]
        [InlineData(4.4, 0)]    // Bangui — equatorial, extremely hot
        [InlineData(0.3, 0)]    // Kampala
        [InlineData(1.3, 0)]    // Singapore
        [InlineData(25.3, 2)]   // Dubai-ish
        [InlineData(51.5, 6)]   // London
        [InlineData(59.9, 7)]   // Oslo
        [InlineData(78.0, 8)]   // high arctic
        public void Latitude_DerivesThermalZone(double lat, int expectedNumber)
            => Assert.Equal(expectedNumber, AshraeClimateZone.NumberFromLatitude(lat));

        [Fact]
        public void TropicalLatitude_NotTemperate()
        {
            // The key fix: a tropical site must NOT resolve to a temperate 4A.
            string z = AshraeClimateZone.ClassifyByLatitude(4.4);   // Bangui
            Assert.StartsWith("0", z);
            Assert.NotEqual("4A", z);
        }

        [Fact]
        public void SouthernHemisphere_UsesAbsoluteLatitude()
        {
            Assert.Equal(AshraeClimateZone.NumberFromLatitude(33.9),
                         AshraeClimateZone.NumberFromLatitude(-33.9));   // Cape Town ≈ Casablanca band
        }

        [Fact]
        public void ColdZones_CarryNoMoistureLetter()
        {
            Assert.Equal("8", AshraeClimateZone.ClassifyByLatitude(80));
            Assert.Equal("7", AshraeClimateZone.ClassifyByLatitude(60));
        }
    }
}
