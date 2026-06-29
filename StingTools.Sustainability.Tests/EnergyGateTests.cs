using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS J3 — floor area 0 / occupancy 0 must NOT yield a computed EUI; and the E1
    // run cache must re-key when Country / site / zone change.
    public class EnergyGateTests
    {
        private static ClimateMonthlySite Climate()
        {
            var s = new ClimateMonthlySite { Id = "c", AnnualGhiKwhM2Yr = 1850 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 30; s.GhiKwhM2Day[m] = 5.0; s.MeanRhPct[m] = 70; }
            return s;
        }

        private static GreenBaseline Baseline()
            => GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"))
               .Resolve("*", "0A", "office").Baseline;

        private static LoadZone Zone(int occupants)
        {
            var z = new LoadZone
            {
                Id = "z", Name = "office", FloorAreaM2 = 1000, HeightM = 3,
                OccupantCount = occupants, LightingWPerM2 = 9, EquipmentWPerM2 = 12,
                CoolingSetpointC = 24, HeatingSetpointC = 21
            };
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 200, UvalueWm2K = 0.3, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 100, UvalueWm2K = 1.4, SHGC = 0.4, OrientationDeg = 180 });
            return z;
        }

        [Fact]
        public void ZeroOccupancy_NotComputed()
        {
            var res = AnnualEnergyEstimator.Estimate(new[] { Zone(0) }, Climate(), Baseline(), 2.8);
            Assert.Equal(0, res.Occupancy);
            Assert.False(res.Computed);   // WS J3 — degenerate input, not a result
            Assert.Contains(res.Warnings, w => w.Contains("occupancy is 0"));
        }

        [Fact]
        public void WithOccupancy_IsComputed()
        {
            var res = AnnualEnergyEstimator.Estimate(new[] { Zone(80) }, Climate(), Baseline(), 2.8);
            Assert.Equal(80, res.Occupancy);
            Assert.True(res.Computed);
        }

        [Fact]
        public void ZeroFloorArea_NotComputed()
        {
            var z = Zone(80); z.FloorAreaM2 = 0;
            var res = AnnualEnergyEstimator.Estimate(new[] { z }, Climate(), Baseline(), 2.8);
            Assert.False(res.Computed);
            Assert.Equal(0, res.ZoneCount);
        }

        // ── E1 cache re-keys on Country / site / zone change ──────────────────
        [Fact]
        public void ContentHash_ReKeys_OnCountryChange()
        {
            var usa = SustainProjectSetup.CreateDefault(2000, 100); usa.Country = "USA";
            var uga = SustainProjectSetup.CreateDefault(2000, 100); uga.Country = "UGA";
            Assert.NotEqual(usa.ContentHash(), uga.ContentHash());
        }

        [Fact]
        public void ContentHash_ReKeys_AfterCountryCascade()
        {
            var reg = CountryRegistry.LoadFromJson(TestData.Read("STING_COUNTRIES.json"));
            var usa = SustainProjectSetup.CreateDefault(2000, 100); usa.Country = "USA";
            var uga = SustainProjectSetup.CreateDefault(2000, 100); uga.Country = "UGA";
            CountryCascade.Apply(usa, reg.Resolve("USA"));
            CountryCascade.Apply(uga, reg.Resolve("UGA"));
            // Cascade filled different zone + grid → the cache key differs (no stale reuse).
            Assert.NotEqual(usa.ContentHash(), uga.ContentHash());
            Assert.NotEqual(usa.ClimateZone, uga.ClimateZone);
        }
    }
}
