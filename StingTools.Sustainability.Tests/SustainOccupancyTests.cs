using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS H2 — one project occupancy feeds both the energy and water estimators.
    // On a zero-setup model (no typed total) both must see the SAME population —
    // the sum of per-zone occupants the energy estimator derives.
    public class SustainOccupancyTests
    {
        [Fact]
        public void ZeroSetup_BothSeeZoneDerivedPopulation()
        {
            // No user total → the model-derived per-zone sum is used (the same
            // population the energy estimator runs on). This is the core H2 fix:
            // water no longer runs on 0 while energy runs on density-derived people.
            var r = SustainOccupancy.Resolve(setupTotalOccupancy: 0, zoneDerivedOccupants: 137);
            Assert.Equal(137, r.Occupancy);
            Assert.Equal("model", r.Source);
        }

        [Fact]
        public void UserSetTotal_Overrides()
        {
            var r = SustainOccupancy.Resolve(setupTotalOccupancy: 200, zoneDerivedOccupants: 137);
            Assert.Equal(200, r.Occupancy);
            Assert.Equal("setup", r.Source);
        }

        [Fact]
        public void NoData_ResolvesZero()
        {
            var r = SustainOccupancy.Resolve(0, 0);
            Assert.Equal(0, r.Occupancy);
            Assert.Equal("none", r.Source);
        }

        [Fact]
        public void WaterEstimate_SeesUnifiedOccupancy_LikeEnergy()
        {
            // Prove that feeding the unified occupancy to the water estimator scales
            // its annual demand exactly as occupancy does — i.e. water now runs on the
            // same population the resolver hands energy, not 0.
            var profile = WaterUsageProfileRegistry
                .LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json")).Get("office");
            var flows = new FixtureFlows { WcLpf = 6, UrinalLpf = 4, BasinTapLpm = 8, ShowerLpm = 10, KitchenTapLpm = 8 };

            var occ = SustainOccupancy.Resolve(0, 137);          // zero-setup model
            var water = AnnualWaterEstimator.Estimate(flows, flows, profile, occ.Occupancy);

            Assert.True(water.AnnualDemandL > 0);                 // not the 0-occupancy degenerate case
            double perPerson = water.DesignLPersonDay * profile.OperatingDaysPerYear;
            Assert.Equal(perPerson * 137, water.AnnualDemandL, 1);
        }
    }
}
