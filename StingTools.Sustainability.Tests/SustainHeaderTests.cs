using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I8 — the dashboard subtitle drops the empty "zone" placeholder and labels
    // an unresolved use honestly.
    public class SustainHeaderTests
    {
        [Fact]
        public void EmptyZone_IsOmitted_NoPlaceholder()
        {
            string s = SustainHeader.Subtitle("office", useResolved: true, climateZone: "", floorAreaM2: 170, occupancy: 5);
            Assert.DoesNotContain("zone", s);
            Assert.Contains("office", s);
            Assert.Contains("170 m²", s);
        }

        [Fact]
        public void ZonePresent_IsShown()
        {
            string s = SustainHeader.Subtitle("office", true, "4A", 170, 5);
            Assert.Contains("zone 4A", s);
        }

        [Fact]
        public void UnresolvedUse_LabelledNotSet()
        {
            string s = SustainHeader.Subtitle("office", useResolved: false, climateZone: "", floorAreaM2: 170, occupancy: 0);
            Assert.Contains("use not set", s);
            Assert.DoesNotContain("occ", s);   // occupancy 0 omitted
        }

        [Fact]
        public void ZeroArea_Omitted()
        {
            string s = SustainHeader.Subtitle("residential", true, "0A", 0, 17);
            Assert.DoesNotContain("m²", s);
            Assert.Contains("occ 17", s);
        }
    }
}
