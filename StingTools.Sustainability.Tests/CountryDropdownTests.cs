using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS J2 — the Country dropdown is data-driven from the seed with friendly labels,
    // and the friendly label round-trips back to the row.
    public class CountryDropdownTests
    {
        private static CountryRegistry Shipped()
            => CountryRegistry.LoadFromJson(TestData.Read("STING_COUNTRIES.json"));

        [Fact]
        public void Seed_HasTheResearchedCountries()
        {
            var reg = Shipped();
            Assert.True(reg.All.Count >= 20, $"expected ≥20 countries, got {reg.All.Count}");
            foreach (var iso in new[] { "CAF", "UGA", "USA", "GBR", "ZAF", "BRA", "ARE" })
                Assert.Contains(reg.All, r => r.Iso3 == iso);
        }

        [Fact]
        public void EveryRow_CarriesRequiredFields()
        {
            foreach (var r in Shipped().All)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Label), $"{r.Iso3} label");
                Assert.False(string.IsNullOrWhiteSpace(r.DefaultCity), $"{r.Iso3} city");
                Assert.False(string.IsNullOrWhiteSpace(r.ClimateZone), $"{r.Iso3} zone");
                Assert.True(r.GridKgCo2ePerKwh > 0, $"{r.Iso3} grid");
            }
        }

        [Fact]
        public void DropdownLabels_AreFriendly_DefaultFirst_Sorted()
        {
            var labels = Shipped().DropdownLabels();
            Assert.Equal("*", labels[0]);
            Assert.All(labels.Skip(1), l => Assert.Contains(" — ", l));
            // Alphabetical by country name (Australia before Brazil before ...).
            var names = labels.Skip(1).ToList();
            Assert.Equal(names.OrderBy(n => n.Substring(n.IndexOf('—') + 1).Trim()).ToList(), names);
        }

        [Fact]
        public void FriendlyLabel_RoundTrips_BackToRow()
        {
            var reg = Shipped();
            foreach (var r in reg.All)
                Assert.Equal(r.Iso3, reg.Resolve(r.FriendlyLabel).Iso3);
        }
    }
}
