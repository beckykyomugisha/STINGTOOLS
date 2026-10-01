using System.Collections.Generic;
using System.IO;
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

        [Fact]
        public void WindSourceIsCarriedAsSourceAndVerify()
        {
            var w = DesignWind.FromSiteRecord(JObject.Parse(
                "{\"heatingWindMs\":2.1,\"coolingWindMs\":3.9,\"windSource\":{\"station\":\"London-Heathrow.Intl.AP ENG GBR\"," +
                "\"wmo\":\"037720\",\"ashraeEdition\":\"2025 ASHRAE Handbook\",\"verify\":\"check the printed table\"}}"));
            Assert.Equal("London-Heathrow.Intl.AP ENG GBR, WMO 037720, 2025 ASHRAE Handbook", w.Source);
            Assert.Equal("check the printed table", w.Verify);
            Assert.False(w.RecordedWithoutSource);
        }

        [Fact]
        public void RecordedSpeedWithNoWindSourceIsFlagged()
        {
            Assert.True(DesignWind.FromSiteRecord(JObject.Parse("{\"heatingWindMs\":2.1}")).RecordedWithoutSource);
            Assert.False(DesignWind.FromSiteRecord(JObject.Parse("{\"id\":\"x\"}")).RecordedWithoutSource);
        }

        // DSCH-37: every shipped climate site that records a design wind must carry a
        // plausible mean coincident wind speed (0.5-20 m/s; a 10 m met-station MCWS, not a
        // structural gust) and say where it came from, with what is still unconfirmed.
        public static IEnumerable<object[]> ShippedClimateFiles() => new[]
        {
            new object[] { "StingTools/Data/STING_CLIMATE_DATA.json" },
            new object[] { "project-templates/KUT/_BIM_COORD/climate_data.json" },
        };

        [Theory]
        [MemberData(nameof(ShippedClimateFiles))]
        public void ShippedDesignWindIsInRangeAndSourced(string relPath)
        {
            string root = Path.GetFullPath(Path.Combine(RepoData.Dir, "..", ".."));
            var sites = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(root, relPath)))["sites"];
            var problems = new List<string>();
            int populated = 0;
            foreach (JObject s in sites)
            {
                string id = (string)s["id"];
                bool any = false;
                foreach (var key in new[] { "heatingWindMs", "coolingWindMs", "designWindMs" })
                {
                    var t = s[key];
                    if (t == null || t.Type == JTokenType.Null) continue;
                    any = true;
                    if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)
                        { problems.Add($"{id}.{key} is not a number"); continue; }
                    double v = t.Value<double>();
                    if (v < 0.5 || v > 20.0) problems.Add($"{id}.{key} = {v} m/s is outside 0.5-20 m/s");
                }
                if (!any) continue;
                populated++;
                if (!(s["windSource"] is JObject src)) { problems.Add($"{id}: wind recorded with no windSource"); continue; }
                foreach (var f in new[] { "station", "wmo", "ashraeEdition", "url", "verify" })
                    if (string.IsNullOrWhiteSpace((string)src[f])) problems.Add($"{id}.windSource.{f} is empty");
            }
            Assert.True(populated > 0, $"{relPath}: no site records a design wind");
            Assert.True(problems.Count == 0, string.Join("; ", problems));
        }
    }
}
