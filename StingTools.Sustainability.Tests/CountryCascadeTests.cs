using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS J1/J2 — the Country drives the run: it resolves a climate zone + grid factor,
    // and the cascade fills site/zone/grid/diesel without clobbering user-typed values.
    public class CountryCascadeTests
    {
        private static CountryRegistry Shipped()
            => CountryRegistry.LoadFromJson(TestData.Read("STING_COUNTRIES.json"));

        // ── Registry ──────────────────────────────────────────────────────────
        [Fact]
        public void Resolve_KnownCountries_CarryZoneAndGrid()
        {
            var reg = Shipped();
            var usa = reg.Resolve("USA");
            var uga = reg.Resolve("UGA");
            var caf = reg.Resolve("CAF");

            Assert.Equal("4A", usa.ClimateZone);
            Assert.Equal(0.37, usa.GridKgCo2ePerKwh, 2);
            Assert.Equal("2A", uga.ClimateZone);
            Assert.Equal(0.05, uga.GridKgCo2ePerKwh, 2);
            Assert.Equal("1A", caf.ClimateZone);
            Assert.Equal(0.07, caf.GridKgCo2ePerKwh, 2);
        }

        [Fact]
        public void Resolve_ByLabelOrFriendlyLabel()
        {
            var reg = Shipped();
            Assert.Equal("CAF", reg.Resolve("Central African Republic").Iso3);
            Assert.Equal("CAF", reg.Resolve("CAF — Central African Republic").Iso3);
        }

        [Fact]
        public void Resolve_UnknownOrBlank_IsFlaggedDefault()
        {
            var reg = Shipped();
            Assert.True(reg.Resolve("").IsDefault);
            Assert.True(reg.Resolve("*").IsDefault);
            Assert.True(reg.Resolve("ZZZ").IsDefault);
            Assert.Equal(0.45, reg.Resolve("").GridKgCo2ePerKwh, 2);
        }

        [Fact]
        public void FriendlyLabel_AndDropdown()
        {
            var reg = Shipped();
            Assert.Equal("CAF — Central African Republic", reg.Resolve("CAF").FriendlyLabel);
            var labels = reg.DropdownLabels();
            Assert.Equal("*", labels[0]);
            Assert.Contains("USA — United States", labels);
            Assert.True(labels.Count >= 21);   // * + 20 countries
        }

        [Fact]
        public void ProjectOverride_ReplacesByIso3()
        {
            const string corp = @"{ ""countries"": [ { ""iso3"": ""UGA"", ""label"": ""Uganda"", ""climateZone"": ""2A"", ""gridKgCo2ePerKwh"": 0.05 } ] }";
            const string proj = @"{ ""countries"": [ { ""iso3"": ""UGA"", ""label"": ""Uganda"", ""climateZone"": ""2A"", ""gridKgCo2ePerKwh"": 0.02, ""source"": ""contracted PPA"" } ] }";
            var reg = CountryRegistry.LoadFromJson(corp, proj);
            Assert.Equal(0.02, reg.Resolve("UGA").GridKgCo2ePerKwh, 2);
        }

        // ── Cascade ───────────────────────────────────────────────────────────
        private static SustainProjectSetup Blank() => SustainProjectSetup.CreateDefault(0, 0);

        [Fact]
        public void Cascade_FillsBlankFields_FromCountry()
        {
            var reg = Shipped();
            var s = Blank();
            CountryCascade.Apply(s, reg.Resolve("CAF"));
            Assert.Equal("1A", s.ClimateZone);
            Assert.Equal("Bangui", s.ClimateSiteId);
            Assert.Equal(0.07, s.Supply.GridCarbonKgco2eKwh, 2);
            Assert.Equal(0.80, s.Supply.DieselCarbonKgco2eKwh, 2);
        }

        [Fact]
        public void Cascade_DoesNotClobberUserTypedZoneOrExplicitGrid()
        {
            var reg = Shipped();
            var s = Blank();
            s.ClimateZone = "5A";                       // user-typed
            s.Supply.GridCarbonExplicit = true;          // user override
            s.Supply.GridCarbonKgco2eKwh = 0.20;
            CountryCascade.Apply(s, reg.Resolve("CAF"));
            Assert.Equal("5A", s.ClimateZone);           // preserved
            Assert.Equal(0.20, s.Supply.GridCarbonKgco2eKwh, 2);  // preserved
        }

        // ── The headline acceptance: CAF vs USA from Country alone ─────────────
        [Fact]
        public void CafVsUsa_FromCountryAlone_DifferentZoneAndGrid()
        {
            var reg = Shipped();
            var caf = Blank(); CountryCascade.Apply(caf, reg.Resolve("CAF"));
            var usa = Blank(); CountryCascade.Apply(usa, reg.Resolve("USA"));

            // CAF — hot tropical zone + ~0.07 grid.
            Assert.StartsWith("1", caf.ClimateZone);
            Assert.True(caf.Supply.GridCarbonKgco2eKwh < 0.10);
            Assert.InRange(caf.Supply.GridCarbonKgco2eKwh, 0.06, 0.08);
            // USA — temperate 4A + ~0.37 grid.
            Assert.Equal("4A", usa.ClimateZone);
            Assert.InRange(usa.Supply.GridCarbonKgco2eKwh, 0.35, 0.39);

            Assert.NotEqual(caf.ClimateZone, usa.ClimateZone);
            Assert.NotEqual(caf.Supply.GridCarbonKgco2eKwh, usa.Supply.GridCarbonKgco2eKwh);
        }

        [Fact]
        public void UsaVsUganda_DifferFromCountryAlone()
        {
            var reg = Shipped();
            var usa = Blank(); CountryCascade.Apply(usa, reg.Resolve("USA"));
            var uga = Blank(); CountryCascade.Apply(uga, reg.Resolve("UGA"));
            Assert.NotEqual(usa.ClimateZone, uga.ClimateZone);
            Assert.True(uga.Supply.GridCarbonKgco2eKwh < usa.Supply.GridCarbonKgco2eKwh);
        }
    }
}
