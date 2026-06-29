using StingTools.Core.Hvac.Loads;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L4 — DHW resolves ONLY from the load profile (the DhwForUse switch is gone).
    // The shipped hotel DHW is 120 L/p·d, superseding the old switch's 100.
    public class DhwResolutionTests
    {
        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        [Theory]
        [InlineData("office", 5)]
        [InlineData("residential", 45)]
        [InlineData("hotel", 120)]        // supersedes the old switch's 100
        [InlineData("healthcare", 60)]
        public void Dhw_ResolvesFromProfile(string use, double expected)
            => Assert.Equal(expected, Lib().ResolveForUse(use).Profile.DhwLPerPersonDay, 1);

        [Fact]
        public void Hotel_Is120_Not100()
        {
            // Explicit reconciliation: the profile (120) is authoritative, not the old 100.
            Assert.Equal(120, Lib().ResolveForUse("hotel").Profile.DhwLPerPersonDay, 1);
            Assert.NotEqual(100, Lib().ResolveForUse("hotel").Profile.DhwLPerPersonDay);
        }

        [Fact]
        public void ApplyTo_PropagatesProfileDhwOntoZone()
        {
            var z = new LoadZone { FloorAreaM2 = 100 };
            Lib().ResolveForUse("hotel").Profile.ApplyTo(z);
            Assert.Equal(120, z.DhwLPerPersonDay, 1);   // not the LoadZone default 5
        }
    }
}
