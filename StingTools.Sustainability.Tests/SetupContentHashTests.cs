using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS E1 — the run cache is keyed by SustainProjectSetup.ContentHash(). It must
    // be stable for an identical setup (so repeated runs hit the cache) and change
    // whenever any result-affecting control changes (so the cache never masks a
    // setup edit — supports acceptance criterion 5).
    public class SetupContentHashTests
    {
        private static SustainProjectSetup Base()
        {
            var s = SustainProjectSetup.CreateDefault(2000, 150);
            s.Schemes = new System.Collections.Generic.List<string> { "EDGE" };
            s.ClimateSiteId = "kampala";
            return s;
        }

        [Fact]
        public void Hash_StableForIdenticalSetup()
            => Assert.Equal(Base().ContentHash(), Base().ContentHash());

        [Fact]
        public void Hash_IgnoresUpdatedUtc()
        {
            var a = Base(); var b = Base();
            b.UpdatedUtc = "2099-01-01T00:00:00Z";   // re-save timestamp must not change the key
            Assert.Equal(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void Hash_ChangesWith_ClimateSite()
        {
            var a = Base(); var b = Base();
            b.ClimateSiteId = "london";
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void Hash_ChangesWith_Units()
        {
            var a = Base(); var b = Base();
            b.Units = SustainUnits.IP;
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void Hash_ChangesWith_ZoneOccupancy()
        {
            var a = Base(); var b = Base();
            b.Zones[0].Occupancy += 1;
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void Hash_ChangesWith_FactorSourcesOrder()
        {
            var a = Base(); var b = Base();
            b.FactorSources.EmbodiedCarbon = new System.Collections.Generic.List<string> { "ICE_v3" };
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void Hash_ChangesWith_EdgeOfficial()
        {
            var a = Base(); var b = Base();
            b.EdgeOfficial.EnergySavingsPct = 42;
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void Hash_ChangesWith_SupplyCop()
        {
            var a = Base(); var b = Base();
            b.Supply.HeatingSeasonalEfficiency = 3.0;
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }
    }
}
