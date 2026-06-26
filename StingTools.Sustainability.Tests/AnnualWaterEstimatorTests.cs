using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Spec §14: L/person.day for the office profile; occupancy scaling; RWH/
    // greywater subtraction; a residential profile yields a different number with
    // the SAME engine (building type only selects the profile).
    public class AnnualWaterEstimatorTests
    {
        private static WaterUsageProfileRegistry Profiles()
            => WaterUsageProfileRegistry.LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json"));

        private static FixtureFlows BaselineFlows() => new FixtureFlows
        {
            WcLpf = 6.0, UrinalLpf = 4.0, BasinTapLpm = 8.0, ShowerLpm = 10.0, KitchenTapLpm = 8.0
        };

        private static FixtureFlows LowFlow() => new FixtureFlows
        {
            WcLpf = 4.0, UrinalLpf = 2.0, BasinTapLpm = 5.0, ShowerLpm = 7.0, KitchenTapLpm = 6.0
        };

        [Fact]
        public void LitresPerPersonDay_OfficeProfile_KnownHandCalc()
        {
            var office = Profiles().Get("office");
            // Baseline office: wc 6x3=18; urinal 4x1=4; basin 8x0.25x4=8;
            // shower 10x5x0.05=2.5; kitchen 8x0.3=2.4 -> 34.9 L/person.day.
            double l = AnnualWaterEstimator.LitresPerPersonDay(BaselineFlows(), office);
            Assert.Equal(34.9, l, 2);
        }

        [Fact]
        public void Estimate_LowFlowFixtures_SavesWater()
        {
            var office = Profiles().Get("office");
            var res = AnnualWaterEstimator.Estimate(LowFlow(), BaselineFlows(), office, occupancy: 100);

            Assert.True(res.DesignLPersonDay < res.BaselineLPersonDay);
            Assert.True(res.WaterSavingsPct > 0);
        }

        [Fact]
        public void Estimate_OccupancyScalesAnnualDemand()
        {
            var office = Profiles().Get("office");
            var r100 = AnnualWaterEstimator.Estimate(BaselineFlows(), BaselineFlows(), office, 100);
            var r200 = AnnualWaterEstimator.Estimate(BaselineFlows(), BaselineFlows(), office, 200);

            Assert.Equal(2 * r100.AnnualDemandL, r200.AnnualDemandL, 1);
        }

        [Fact]
        public void Estimate_RwhAndGreywater_SubtractFromNetDemand()
        {
            var office = Profiles().Get("office");
            var res = AnnualWaterEstimator.Estimate(
                BaselineFlows(), BaselineFlows(), office, occupancy: 100,
                rwhYieldLPerYr: 50000, greywaterReuseFraction: 0.1);

            Assert.True(res.NetDemandL < res.AnnualDemandL);
            Assert.Equal(res.AnnualDemandL * 0.1, res.GreywaterReuseL, 1);
            Assert.Equal(50000, res.RwhYieldL, 1);
        }

        [Fact]
        public void Estimate_ResidentialProfile_DiffersFromOffice_SameEngine()
        {
            var profiles = Profiles();
            var office = AnnualWaterEstimator.Estimate(BaselineFlows(), BaselineFlows(), profiles.Get("office"), 100);
            var resi   = AnnualWaterEstimator.Estimate(BaselineFlows(), BaselineFlows(), profiles.Get("residential"), 100);

            // Residential uses more water per person (showers every day, more WC use).
            Assert.True(resi.DesignLPersonDay > office.DesignLPersonDay);
        }

        [Fact]
        public void Estimate_ZeroOccupancy_Warns()
        {
            var office = Profiles().Get("office");
            var res = AnnualWaterEstimator.Estimate(BaselineFlows(), BaselineFlows(), office, occupancy: 0);
            Assert.Contains(res.Warnings, w => w.Contains("Occupancy"));
        }

        [Fact]
        public void FixtureFlows_FromBaseline_ReadsBaselineFixtures()
        {
            var reg = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            var baseline = reg.Resolve("*", "0A", "office").Baseline;
            var flows = FixtureFlows.FromBaseline(baseline);
            Assert.Equal(6.0, flows.WcLpf, 2);
        }
    }
}
