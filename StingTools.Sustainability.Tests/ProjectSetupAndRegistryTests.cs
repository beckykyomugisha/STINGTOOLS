using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    public class ProjectSetupAndRegistryTests
    {
        [Fact]
        public void ProjectSetup_RoundTrips()
        {
            var s = SustainProjectSetup.CreateDefault(2550, 200);
            s.Country = "CF"; s.ClimateZone = "0A";
            s.Supply.PvKwp = 100; s.Units = SustainUnits.SI;
            string json = s.ToJson();
            var back = SustainProjectSetup.Parse(json);

            Assert.Equal("CF", back.Country);
            Assert.Equal("0A", back.ClimateZone);
            Assert.Equal(100, back.Supply.PvKwp, 1);
            Assert.Single(back.Zones);
            Assert.Equal("Advanced", back.LevelFor("EDGE", "Certified"));
        }

        [Fact]
        public void ProjectSetup_ParseEmpty_GivesUsableDefault()
        {
            var s = SustainProjectSetup.Parse(null);
            Assert.NotEmpty(s.Zones);
            Assert.NotNull(s.Supply);
            Assert.Contains("EDGE", s.Schemes);
        }

        [Fact]
        public void GreenSchemeRegistry_ProjectOverride_WinsById()
        {
            string corp = TestData.Read("STING_GREEN_SCHEMES.json");
            string proj = @"{ ""schema"": ""sting.green.schemes/v1"", ""schemes"": [
                { ""id"": ""EDGE"", ""name"": ""EDGE (project override)"", ""aggregation"": ""all_required"",
                  ""levels"": { ""Advanced"": { ""energy"": 35, ""water"": 20, ""materials"": 20 } },
                  ""gates"": [ { ""id"": ""energy"", ""metric"": ""energy_savings_pct"", ""provider"": ""AnnualEnergyEstimator"", ""operator"": "">="", ""required"": true } ] } ] }";
            var reg = GreenSchemeRegistry.LoadFromJson(corp, proj);
            var edge = reg.Get("EDGE");
            Assert.Equal("EDGE (project override)", edge.Name);
            Assert.Equal(35, edge.Levels["Advanced"]["energy"], 1);
        }

        [Fact]
        public void GreenMeasureRegistry_LoadsMeasuresWithCostHandles()
        {
            var reg = GreenMeasureRegistry.LoadFromJson(TestData.Read("STING_GREEN_MEASURES.json"));
            Assert.NotEmpty(reg.All);
            var pv = reg.Get("solar_pv");
            Assert.NotNull(pv);
            Assert.Equal("energy", pv.Gate);
            Assert.Equal("boqRateKey", pv.Cost.Type);
            Assert.False(string.IsNullOrEmpty(pv.Cost.Key));
            // Each gate has at least one measure.
            Assert.NotEmpty(reg.ForGate("water").ToList());
            Assert.NotEmpty(reg.ForGate("materials").ToList());
        }

        [Fact]
        public void ClimateMonthly_ShippedSites_HaveTwelveMonths()
        {
            var reg = ClimateMonthlyRegistry.LoadFromJson(TestData.Read("STING_CLIMATE_MONTHLY.json"));
            var bangui = reg.Get("bangui");
            Assert.NotNull(bangui);
            Assert.Equal(12, bangui.MeanDbC.Length);
            Assert.True(bangui.AnnualGhiKwhM2Yr > 0);
            Assert.True(bangui.AnnualRainfallMm > 0);
        }

        [Fact]
        public void ClimateMonthly_MissingSite_SynthesisesAndWarns()
        {
            var reg = ClimateMonthlyRegistry.LoadFromJson(TestData.Read("STING_CLIMATE_MONTHLY.json"));
            // 'tokyo' has design-day data but no monthly record -> synthesise + warn.
            var site = reg.ResolveOrSynthesise("tokyo", "Tokyo", coolingDesignDbC: 32.8, heatingDesignDbC: -1.6);
            Assert.True(site.FellBackToDesignDay);
            Assert.NotEmpty(reg.Warnings);
            // Synthesised monthly temps lie between the two design points.
            Assert.All(site.MeanDbC, t => Assert.InRange(t, -1.6, 32.8));
        }
    }
}
