using System.Linq;
using StingTools.Core.Hvac.Loads;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L5 — the LPD/EPD vintage is deliberate (kept ~90.1-2013, conservative for an
    // efficiency tool) and auditable per profile via the source string.
    public class LpdVintageTests
    {
        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        [Fact]
        public void EverySource_PinsTheLpdEpdVintage()
        {
            foreach (var p in Lib().ById.Values)
            {
                Assert.Contains("LPD/EPD:", p.Source);
                Assert.Contains("90.1", p.Source);     // the standard is stated
                Assert.Contains("vintage", p.Source);  // the vintage decision is auditable
            }
        }

        [Fact]
        public void LpdValues_AreKeptAsSeed_NotSystematicallyLowered()
        {
            // The seed densities are kept deliberately (a documented choice), not
            // rewritten to 2019 LED minima — office 7.6, classroom 12 W/m².
            Assert.Equal(7.6, Lib().ResolveForUse("office").Profile.LightingWPerM2, 1);
            Assert.Equal(12, Lib().ResolveForUse("education").Profile.LightingWPerM2, 1);
        }

        [Fact]
        public void Sources_AreCleanAscii()
        {
            // Guards the earlier mid-dot encoding regression.
            foreach (var p in Lib().ById.Values)
                Assert.DoesNotContain(p.Source, c => c > 127);
        }
    }
}
