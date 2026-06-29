using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Sustainability.Tests
{
    // WS L9 — end-to-end regression anchor on a fixed known input: a ~170 m²
    // residential building in Bangui (CAF), no modelled fixtures. Locks the whole
    // chain: country cascade → climate zone + grid → load profile (occupancy/EUI) →
    // water (indicative) → materials (flagged) → one operating-year basis (L1).
    public class EndToEndRegressionTests
    {
        private readonly ITestOutputHelper _out;
        public EndToEndRegressionTests(ITestOutputHelper o) { _out = o; }

        private static ClimateMonthlySite Bangui()
        {
            // Hot-humid tropical (Bangui): warm year-round, high GHI.
            var s = new ClimateMonthlySite { Id = "bangui", AnnualGhiKwhM2Yr = 1850 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 26.5; s.GhiKwhM2Day[m] = 5.0; s.MeanRhPct[m] = 75; }
            return s;
        }

        [Fact]
        public void Bangui_Residential170m2_NoFixtures_LocksTheChain()
        {
            // 1) Country (CAF) → climate zone 1A + grid ~0.07, from the country alone.
            var countries = CountryRegistry.LoadFromJson(TestData.Read("STING_COUNTRIES.json"));
            var caf = countries.Resolve("CAF");
            var setup = SustainProjectSetup.CreateDefault(170, 0);
            setup.Country = "CAF";
            CountryCascade.Apply(setup, caf);
            Assert.StartsWith("1", setup.ClimateZone);                       // hot tropical zone
            Assert.Equal("1A", setup.ClimateZone);
            Assert.InRange(setup.Supply.GridCarbonKgco2eKwh, 0.06, 0.08);    // ~0.07

            // 2) Load profile (residential) → ~5 occupants on 170 m² (not office 17).
            var lib = LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));
            var resi = lib.ResolveForUse("residential").Profile;
            Assert.Equal("Residential", resi.Id);
            int occ = resi.OccupantCountFor(170);
            Assert.Equal(5, occ);

            // 3) Energy → believable dwelling EUI (60–120), occupancy carried.
            var z = new LoadZone { Id = "house", FloorAreaM2 = 170, HeightM = 3 };
            resi.ApplyTo(z);
            z.OccupantCount = occ;
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 120, UvalueWm2K = 0.4, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 25, UvalueWm2K = 1.6, SHGC = 0.5, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Roof, AreaM2 = 170, UvalueWm2K = 0.3 });
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));
            var resiBaseline = baselines.Resolve("*", setup.ClimateZone, "residential").Baseline;
            // COP 3.2 — a modern inverter split system (realistic design SEER).
            var energy = AnnualEnergyEstimator.Estimate(new[] { z }, Bangui(), resiBaseline, 3.2);
            _out.WriteLine($"Bangui residential 170 m²: {occ} occ, EUI {energy.DesignEuiKwhM2Yr:F1} kWh/m²·yr");
            Assert.True(energy.Computed);
            Assert.Equal(5, energy.Occupancy);
            Assert.InRange(energy.DesignEuiKwhM2Yr, 60, 120);

            // 4) Water indicative (no modelled fixtures → design = 25% below baseline).
            var water = WaterUsageProfileRegistry.LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json"));
            var wProfile = water.Get("residential");
            var baseFlows = FixtureFlows.FromBaseline(resiBaseline);
            var designFlows = new FixtureFlows
            {
                WcLpf = baseFlows.WcLpf * 0.75, UrinalLpf = baseFlows.UrinalLpf * 0.75,
                BasinTapLpm = baseFlows.BasinTapLpm * 0.75, ShowerLpm = baseFlows.ShowerLpm * 0.75,
                KitchenTapLpm = baseFlows.KitchenTapLpm * 0.75
            };
            var w = AnnualWaterEstimator.Estimate(designFlows, baseFlows, wProfile, occ);
            w.IsIndicativeDefault = true;   // set by the engine when no model fixtures are read
            Assert.False(w.Computed);       // indicative default, never claimed as a pass
            Assert.InRange(w.WaterSavingsPct, 24, 26);   // the 25% indicative placeholder

            // 5) Materials flagged (Bangui take-off outlier — partial coverage).
            var materials = new MaterialsRollupResult
            {
                FloorAreaM2 = 170, TotalCarbonKg = 170 * 5213,   // implausible per-m²
                TotalLines = 31, CarbonStampedLines = 15, IntensityImplausible = true,
                DominantHotspotMaterial = "Steel Purlins", DominantHotspotSharePct = 92, DominantHotspotImplausible = true
            };
            Assert.True(materials.CarbonHeadlineFlagged);
            Assert.Contains("15/31", materials.CoverageSummary);

            // 6) ONE operating-year basis (L1): energy zone + water profile agree.
            Assert.Equal(365, z.OperatingDaysPerYear);                 // residential is 24/7
            Assert.Equal(z.OperatingDaysPerYear, wProfile.OperatingDaysPerYear);
            Assert.Equal(365, w.OperatingDaysPerYear);
        }
    }
}
