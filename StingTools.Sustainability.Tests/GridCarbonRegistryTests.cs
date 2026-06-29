using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I3/I9 — grid carbon factor resolves per country from a documented seed,
    // and is flagged a "default" until a real one resolves. A hydro-dominant grid
    // (CAR, Uganda) must come out far below the temperate 0.45 default.
    public class GridCarbonRegistryTests
    {
        private static GridCarbonRegistry Shipped()
            => GridCarbonRegistry.LoadFromJson(TestData.Read("STING_GRID_CARBON_FACTORS.json"));

        [Fact]
        public void HydroGrid_ResolvesFarBelowDefault()
        {
            var reg = Shipped();
            var car = reg.Resolve("CF");   // Central African Republic — hydro-dominant
            Assert.False(car.IsDefault);
            Assert.True(car.Factor < 0.2, $"CAR grid {car.Factor} should be well below the 0.45 default");
            Assert.True(car.Factor < reg.Default);
        }

        [Fact]
        public void CoalGrid_ResolvesWellAboveDefault()
        {
            var za = Shipped().Resolve("ZA");   // South Africa — coal
            Assert.False(za.IsDefault);
            Assert.True(za.Factor > 0.45);
        }

        [Fact]
        public void UnsetCountry_UsesLabelledDefault()
        {
            var reg = Shipped();
            var r = reg.Resolve("");
            Assert.True(r.IsDefault);
            Assert.Equal(reg.Default, r.Factor, 6);
            Assert.Contains("unset", r.Source);
        }

        [Fact]
        public void UnknownCountry_UsesLabelledDefault()
        {
            var r = Shipped().Resolve("ZZ");
            Assert.True(r.IsDefault);
            Assert.Contains("ZZ", r.Source);
        }

        [Fact]
        public void ProjectOverride_ReplacesSeed()
        {
            const string corp = @"{ ""default"": 0.45, ""factors"": [ { ""country"": ""UG"", ""kgco2ePerKwh"": 0.05 } ] }";
            const string proj = @"{ ""factors"": [ { ""country"": ""UG"", ""kgco2ePerKwh"": 0.02, ""source"": ""contracted PPA"" } ] }";
            var r = GridCarbonRegistry.LoadFromJson(corp, proj).Resolve("UG");
            Assert.Equal(0.02, r.Factor, 6);
            Assert.Equal("contracted PPA", r.Source);
            Assert.False(r.IsDefault);
        }

        [Fact]
        public void KnownCountry_NotFlaggedDefault()
        {
            var gb = Shipped().Resolve("GB");
            Assert.False(gb.IsDefault);
            Assert.True(gb.Factor > 0 && gb.Factor < 0.45);
        }
    }
}
