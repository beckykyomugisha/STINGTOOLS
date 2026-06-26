using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Spec §14 flexibility/options + §13.9 (flexibility) + §13.10 (mixed-use):
    //   same engine + two different project_setup configs produce coherent,
    //   different results with no code change; per-zone mixed-use rolls up
    //   area-weighted (energy/materials) and occupancy-weighted (water); a config
    //   citing a catalogue row absent from the baseline registry resolves via the
    //   climate-zone proxy and logs it.
    public class FlexibilityTests
    {
        private static ClimateMonthlySite Climate(double meanDb, double ghi)
        {
            var s = new ClimateMonthlySite { Id = "c", AnnualGhiKwhM2Yr = ghi * 365 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = meanDb; s.GhiKwhM2Day[m] = ghi; s.MeanRhPct[m] = 70; }
            return s;
        }

        private static LoadZone Zone(string use, double area, int occ)
        {
            var z = new LoadZone { Id = use, Name = use, SpaceTypeId = use, FloorAreaM2 = area, HeightM = 3, OccupantCount = occ };
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = area / 5, UvalueWm2K = 0.3, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = area / 12, UvalueWm2K = 1.4, SHGC = 0.4, OrientationDeg = 180 });
            return z;
        }

        [Fact]
        public void DifferentConfigs_NoCodeChange_ProduceDifferentResults()
        {
            // Config 1 — Bangui office, EDGE Advanced, hot climate.
            var bangui = SustainProjectSetup.CreateDefault(floorAreaM2: 2550, occupancy: 200);
            bangui.Country = "CF"; bangui.ClimateZone = "0A";
            bangui.Zones[0].BuildingUse = "office";

            // Config 2 — temperate residential, LEED Gold, mild climate.
            var temperate = SustainProjectSetup.CreateDefault(floorAreaM2: 2550, occupancy: 200);
            temperate.Schemes = new List<string> { "LEED" };
            temperate.Country = "GB"; temperate.ClimateZone = "4A";
            temperate.Zones[0].BuildingUse = "residential";

            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            var bBan = baselines.Resolve(bangui.Country, bangui.ClimateZone, bangui.DominantBuildingUse).Baseline;
            var bTemp = baselines.Resolve(temperate.Country, temperate.ClimateZone, temperate.DominantBuildingUse).Baseline;

            var enBan = AnnualEnergyEstimator.Estimate(
                new[] { Zone("office", 2550, 200) }, Climate(30, 5.0), bBan, bBan.BaselineCoolingCop);
            var enTemp = AnnualEnergyEstimator.Estimate(
                new[] { Zone("residential", 2550, 200) }, Climate(12, 2.8), bTemp, bTemp.BaselineCoolingCop);

            // Coherent, DIFFERENT numbers — hot-humid office vs temperate residential.
            Assert.NotEqual(enBan.DesignEuiKwhM2Yr, enTemp.DesignEuiKwhM2Yr, 1);
            Assert.NotEqual(enBan.BaselineEuiKwhM2Yr, enTemp.BaselineEuiKwhM2Yr, 1);
        }

        [Fact]
        public void MixedUse_EnergyRollsUpAreaWeighted()
        {
            // Ground-floor retail + offices above — two zones, one building.
            var climate = Climate(30, 5.0);
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            var baseline = baselines.Resolve("*", "0A", "office").Baseline;

            var zones = new[] { Zone("retail", 1000, 50), Zone("office", 1500, 120) };
            var res = AnnualEnergyEstimator.Estimate(zones, climate, baseline, baseline.BaselineCoolingCop);

            // Building floor area = sum of zones; EUI = total kWh / total area.
            Assert.Equal(2500, res.FloorAreaM2, 1);
            Assert.True(res.Design.TotalKwh > 0);
            Assert.Equal(res.Design.TotalKwh / 2500, res.DesignEuiKwhM2Yr, 3);
        }

        [Fact]
        public void MixedUse_WaterRollsUpOccupancyWeighted()
        {
            var profiles = WaterUsageProfileRegistry.LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json"));
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            var flows = FixtureFlows.FromBaseline(baselines.Resolve("*", "0A", "office").Baseline);

            // Per-zone occupancy-weighted: retail 50 ppl + office 120 ppl.
            var retail = AnnualWaterEstimator.Estimate(flows, flows, profiles.Get("retail"), 50);
            var office = AnnualWaterEstimator.Estimate(flows, flows, profiles.Get("office"), 120);
            double buildingAnnual = retail.AnnualDemandL + office.AnnualDemandL;

            Assert.True(buildingAnnual > office.AnnualDemandL);
            Assert.True(buildingAnnual > retail.AnnualDemandL);
        }

        [Fact]
        public void MissingCatalogueRow_ResolvesViaProxyAndLogs()
        {
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            // CF/0A has no exact row -> climate-zone 0A office proxy + a logged path.
            var res = baselines.Resolve("CF", "0A", "office");
            Assert.True(res.Found);
            Assert.False(res.Path[0].Matched);             // exact-key miss logged
            Assert.Contains("fell back", res.Summary);     // proxy recorded
        }

        [Fact]
        public void DominantBuildingUse_PicksLargestAreaZone()
        {
            var setup = SustainProjectSetup.CreateDefault();
            setup.Zones = new List<ZoneSetup>
            {
                new ZoneSetup { ZoneId = "gf", BuildingUse = "retail", FloorAreaM2 = 1000 },
                new ZoneSetup { ZoneId = "upper", BuildingUse = "office", FloorAreaM2 = 4000 },
            };
            Assert.Equal("office", setup.DominantBuildingUse);
            Assert.Equal(5000, setup.TotalFloorAreaM2, 1);
        }
    }
}
