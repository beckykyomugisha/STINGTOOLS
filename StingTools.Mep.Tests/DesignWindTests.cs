using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Climate;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>DSCH-25 — design wind for the infiltration model: per design day,
    /// 4.0 m/s assumed and flagged when the site records none, legacy key accepted.</summary>
    public class DesignWindTests
    {
        [Fact]
        public void NoRecordedWindUsesFourMetresPerSecondAndFlagsBothDays()
        {
            var w = DesignWind.FromSiteRecord(JObject.Parse("{\"id\":\"x\"}"));
            Assert.Equal(4.0, w.HeatingMs);
            Assert.Equal(4.0, w.CoolingMs);
            Assert.True(w.HeatingAssumed);
            Assert.True(w.CoolingAssumed);
            Assert.Empty(w.RejectedKeys);
        }

        [Fact]
        public void PerDayValuesAreUsedForTheirOwnDesignDay()
        {
            var w = DesignWind.FromSiteRecord(JObject.Parse("{\"heatingWindMs\":5.2,\"coolingWindMs\":2.7}"));
            Assert.Equal(5.2, w.MsFor(cooling: false));
            Assert.Equal(2.7, w.MsFor(cooling: true));
            Assert.False(w.AssumedFor(false));
            Assert.False(w.AssumedFor(true));
        }

        [Fact]
        public void LegacySingleKeyAppliesToEachDayWithoutASpecificValue()
        {
            var w = DesignWind.FromSiteRecord(JObject.Parse("{\"designWindMs\":3.5,\"coolingWindMs\":2.0}"));
            Assert.Equal(3.5, w.HeatingMs);
            Assert.Equal(2.0, w.CoolingMs);
            Assert.False(w.HeatingAssumed);
        }

        [Fact]
        public void NonPositiveOrTextValueIsRejectedNotUsed()
        {
            var w = DesignWind.FromSiteRecord(JObject.Parse("{\"heatingWindMs\":0,\"coolingWindMs\":\"4\"}"));
            Assert.True(w.HeatingAssumed);
            Assert.True(w.CoolingAssumed);
            Assert.Equal(new[] { "heatingWindMs", "coolingWindMs" }, w.RejectedKeys.ToArray());
        }

        [Fact]
        public void ShippedClimateSitesCarryNoUnusableWindValue()
        {
            var sites = (JArray)JObject.Parse(RepoData.Read("STING_CLIMATE_DATA.json"))["sites"];
            Assert.NotEmpty(sites);
            foreach (JObject s in sites)
                Assert.Empty(DesignWind.FromSiteRecord(s).RejectedKeys);
        }
    }
}
