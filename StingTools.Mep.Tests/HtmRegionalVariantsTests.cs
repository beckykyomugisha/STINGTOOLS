using System;
using System.Linq;
using StingTools.Standards.HTM;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>DSCH-36 — HtmRegionalVariants must not carry a single "hot delivery"
    /// temperature: TMV outlet limits are per outlet, and their one owner is
    /// Data/Plumbing/STING_TMV_STANDARDS.json.</summary>
    public class HtmRegionalVariantsTests
    {
        [Fact]
        public void NoRegionCarriesATmvDeliveryTemperature()
        {
            foreach (HtmRegion region in Enum.GetValues(typeof(HtmRegion)))
                Assert.DoesNotContain(HtmRegionalVariants.GetForRegion(region).Keys,
                    k => k.IndexOf("HOT_DELIVERY", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // HTM 04-01 Part A §15.16 is about witnessing commissioning, not a temperature.
        [Fact]
        public void NoValueCitesTheCommissioningClause()
        {
            foreach (HtmRegion region in Enum.GetValues(typeof(HtmRegion)))
                Assert.DoesNotContain(HtmRegionalVariants.GetForRegion(region).Values,
                    v => v.SourceClause.Contains("15.16"));
        }
    }
}
