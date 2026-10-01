using System.Linq;
using StingTools.Core.Plumbing;
using StingTools.Standards.HTM;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>DSCH-36 — Scottish TMV limits (SHTM 04-01 Part A v2 Table 4) selected by
    /// PRJ_ORG_HEALTH_HTM_REGION_TXT; an unrecorded region keeps the HTM 04-01 (England)
    /// rows and says so; the paediatric-bath limit is NOT CHECKED, never assumed.</summary>
    public class WaterSafetyJurisdictionTests
    {
        private static WaterSafetyLimitsFile Shipped()
        {
            var f = WaterSafetyLimits.Parse(RepoData.Read("Plumbing/STING_TMV_STANDARDS.json"), out var errors);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            return f;
        }

        private static TmvCheck Tmv(HtmRegion? region, string outlet, double setC, bool? assisted = false, string scheme = "TMV3")
            => WaterSafetyLimits.CheckTmv(Shipped(), outlet, scheme, assisted, true, setC, 0, region);

        [Fact]
        public void ScottishRowsAreShippedWithTheirSource()
        {
            var sco = Shipped().OutletLimits.Where(r => r.Jurisdiction == "SCOTLAND").ToList();
            double Max(string outlet, bool assisted = false, bool paed = false) =>
                sco.Single(r => r.Outlet == outlet && r.Scheme == "TMV3" && r.Assisted == assisted && r.Paediatric == paed).MaxSetC;
            Assert.Equal(43, Max("BATH"));
            Assert.Equal(46, Max("BATH", assisted: true));
            Assert.Equal(40, Max("BATH", paed: true));
            Assert.Equal(41, Max("SHOWER"));
            Assert.Equal(38, Max("BIDET"));
            Assert.Equal(41, Max("BASIN"));
            Assert.All(sco, r => Assert.Contains("SHTM 04-01", r.Source));
        }

        [Fact]
        public void ScottishGeneralBathIs43NotEngland44()
        {
            Assert.Equal(WaterCheckStatus.Fail, Tmv(HtmRegion.Scotland, "BATH", 44).Status);
            Assert.Equal(WaterCheckStatus.Pass, Tmv(HtmRegion.England, "BATH", 44).Status);
            Assert.Equal(WaterCheckStatus.Pass, Tmv(HtmRegion.Scotland, "BATH", 43).Status);
            Assert.Contains("SHTM", Tmv(HtmRegion.Scotland, "BATH", 43).StandardRef);
        }

        [Fact]
        public void ScottishAssistedShowerBidetBasin()
        {
            Assert.Equal(WaterCheckStatus.Pass, Tmv(HtmRegion.Scotland, "BATH", 46, assisted: true).Status);
            Assert.Equal(WaterCheckStatus.Fail, Tmv(HtmRegion.Scotland, "BATH", 47, assisted: true).Status);
            Assert.Equal(WaterCheckStatus.Fail, Tmv(HtmRegion.Scotland, "SHOWER", 42).Status);
            Assert.Equal(WaterCheckStatus.Fail, Tmv(HtmRegion.Scotland, "BIDET", 39).Status);
            Assert.Equal(WaterCheckStatus.Pass, Tmv(HtmRegion.Scotland, "BASIN", 41).Status);
        }

        [Fact]
        public void PaediatricBathLimitIsReportedNotChecked()
        {
            var c = Tmv(HtmRegion.Scotland, "BATH", 42);
            Assert.Equal(WaterCheckStatus.Pass, c.Status);   // checked as a general bath (43)
            Assert.Contains(c.Notes, n => n.Contains("paediatric") && n.Contains("NOT CHECKED"));
            // at or under 40 °C the paediatric limit is met either way: nothing to report
            Assert.DoesNotContain(Tmv(HtmRegion.Scotland, "BATH", 40).Notes, n => n.Contains("paediatric"));
            // England has no paediatric row
            Assert.DoesNotContain(Tmv(HtmRegion.England, "BATH", 42).Notes, n => n.Contains("paediatric"));
        }

        [Fact]
        public void UnrecordedJurisdictionKeepsEnglandRowsAndSaysSo()
        {
            var c = Tmv(null, "BATH", 44);
            Assert.Equal(WaterCheckStatus.Pass, c.Status);
            Assert.Contains(c.Notes, n => n.Contains("jurisdiction not recorded"));
            Assert.Empty(Tmv(HtmRegion.England, "BATH", 44).Notes);
            Assert.Contains(Tmv(HtmRegion.Wales, "BATH", 44).Notes, n => n.Contains("WHTM"));
        }

        [Fact]
        public void ScotlandWithoutARowForTheSchemeUsesEnglandAndSaysSo()
        {
            var f = WaterSafetyLimits.CheckTmv(Shipped(), "BATH", "TMV2", false, false, 44, 0, HtmRegion.Scotland);
            Assert.Equal(WaterCheckStatus.Pass, f.Status);
            Assert.Contains(f.Notes, n => n.Contains("no SCOTLAND row"));
        }

        [Fact]
        public void RegionTextParsesWithoutGuessing()
        {
            Assert.False(HtmRegionalVariants.TryParseRegion("", out _));
            Assert.False(HtmRegionalVariants.TryParseRegion("Kampala", out _));
            Assert.True(HtmRegionalVariants.TryParseRegion("SHTM", out var r) && r == HtmRegion.Scotland);
            Assert.True(HtmRegionalVariants.TryParseRegion("NHS-England", out var e) && e == HtmRegion.England);
            Assert.Equal(HtmRegion.England, HtmRegionalVariants.ParseRegion(""));
        }

        [Fact]
        public void UnknownJurisdictionInDataIsRefused()
        {
            const string json = "{\"healthcareRequiredScheme\":\"TMV3\",\"outletLimits\":[{\"outlet\":\"BATH\",\"scheme\":\"TMV3\",\"jurisdiction\":\"MARS\",\"maxSetC\":44,\"neverExceedC\":46,\"source\":\"s\"}]}";
            Assert.Null(WaterSafetyLimits.Parse(json, out var e));
            Assert.Contains(e, x => x.Contains("unknown jurisdiction"));
        }
    }
}
