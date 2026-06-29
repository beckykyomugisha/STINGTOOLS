using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Sustainability.Tests
{
    // WS M — the live occupancy-derivation chain: EstimateOccupancy fallback (=
    // profile.OccupantCountFor) → CreateDefault (occupancy blank, non-explicit) →
    // engine zone derivation → SustainOccupancy.Resolve. A 170 m² residential
    // building must resolve ~5 (not the office-density 17), source "model", with
    // both estimators on that same 5. (The K test fed a LoadZone directly and never
    // went through this path — this closes that gap.)
    public class OccupancyDerivationTests
    {
        private readonly ITestOutputHelper _out;
        public OccupancyDerivationTests(ITestOutputHelper o) { _out = o; }

        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        // Mirrors SustainCmdHelper.EstimateOccupancy's density fallback (no modelled
        // people): the resolved profile's OccupantCountFor — the SAME pure method the
        // engine uses on synthetic zones.
        private static int EstimateOccupancyFallback(LoadProfileLibrary lib, double areaM2, string use)
            => lib.ResolveForUse(use).Profile.OccupantCountFor(areaM2);

        [Theory]
        [InlineData("residential", 5)]
        [InlineData("office", 17)]
        [InlineData("hotel", 7)]    // 170 / 25
        public void EstimateOccupancy_UsesPerUseDensity_NotFlatOffice10(string use, int expected)
            => Assert.Equal(expected, EstimateOccupancyFallback(Lib(), 170, use));

        [Fact]
        public void Residential170m2_BlankOccupancy_ResolvesAbout5_SourceModel_EndToEnd()
        {
            var lib = Lib();
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            var water = WaterUsageProfileRegistry.LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json"));

            // 1) Auto-seed: floor area set, occupancy BLANK + non-explicit (WS M2).
            var setup = SustainProjectSetup.CreateDefault(170, 0);
            setup.Zones[0].BuildingUse = "residential";
            setup.OccupancyExplicit = false;
            Assert.Equal(0, setup.TotalOccupancy);   // no office-density 17 seeded

            // 2) Engine synthetic-zone derivation: occupancy NOT explicit → 0 → the
            //    profile density fills it (the ApplyProfile path), i.e. OccupantCountFor.
            var resi = lib.ResolveForUse("residential").Profile;
            var z = new LoadZone { Id = "z", FloorAreaM2 = 170, HeightM = 3 };
            resi.ApplyTo(z);
            z.OccupantCount = setup.OccupancyExplicit ? setup.Zones[0].Occupancy : 0;
            if (z.OccupantCount <= 0 && z.FloorAreaM2 > 0)
                z.OccupantCount = resi.OccupantCountFor(z.FloorAreaM2);
            Assert.Equal(5, z.OccupantCount);   // not 17

            // 3) SustainOccupancy.Resolve → 5, source "model" (not "setup").
            int zoneDerived = z.OccupantCount;
            var occ = SustainOccupancy.Resolve(setup.TotalOccupancy, zoneDerived, setup.OccupancyExplicit);
            _out.WriteLine($"Resolved occupancy: {occ.Occupancy} (source {occ.Source})");
            Assert.Equal(5, occ.Occupancy);
            Assert.Equal("model", occ.Source);

            // 4) BOTH estimators use that same 5.
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 120, UvalueWm2K = 0.4 });
            var energy = AnnualEnergyEstimator.Estimate(new[] { z },
                NewClimate(), baselines.Resolve("*", "1A", "residential").Baseline, 3.2);
            Assert.Equal(5, energy.Occupancy);

            var w = AnnualWaterEstimator.Estimate(new FixtureFlows(), new FixtureFlows(),
                water.Get("residential"), occ.Occupancy);
            Assert.Equal(5, w.Occupancy);
        }

        [Fact]
        public void UserTypedTotal_StillWins_AsSetup()
        {
            // A genuinely user-entered total (explicit) overrides the model.
            var occ = SustainOccupancy.Resolve(setupTotalOccupancy: 40, zoneDerivedOccupants: 5, occupancyIsExplicit: true);
            Assert.Equal(40, occ.Occupancy);
            Assert.Equal("setup", occ.Source);
        }

        [Fact]
        public void NonExplicitSetupEstimate_IsLabelledModel_NotSetup()
        {
            // A stale/non-explicit setup figure is an estimate → "model", never "setup".
            var occ = SustainOccupancy.Resolve(setupTotalOccupancy: 17, zoneDerivedOccupants: 5, occupancyIsExplicit: false);
            Assert.Equal(5, occ.Occupancy);          // model-derived wins over the estimate
            Assert.Equal("model", occ.Source);
        }

        [Fact]
        public void UseChange_ReKeysTheRun_SoStaleOccupancyDoesNotCarryOver()
        {
            // WS M3 — changing the building use changes the content hash (cache re-keys),
            // so a persisted office-density occupancy can't carry into a residential run.
            var office = SustainProjectSetup.CreateDefault(170, 0);
            office.Zones[0].BuildingUse = "office";
            var resi = SustainProjectSetup.CreateDefault(170, 0);
            resi.Zones[0].BuildingUse = "residential";
            Assert.NotEqual(office.ContentHash(), resi.ContentHash());
        }

        private static ClimateMonthlySite NewClimate()
        {
            var s = new ClimateMonthlySite { Id = "c", AnnualGhiKwhM2Yr = 1850 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 26.5; s.GhiKwhM2Day[m] = 5.0; s.MeanRhPct[m] = 75; }
            return s;
        }
    }
}
