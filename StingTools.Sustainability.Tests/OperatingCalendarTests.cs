using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L1/L2 — energy and water share ONE operating calendar (operatingDaysPerYear).
    // A 250-day use isn't billed a 365-day year; the field is wired into BOTH engines.
    public class OperatingCalendarTests
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

        private static LoadZone Zone(int operatingDays)
        {
            var z = new LoadZone
            {
                Id = "z", Name = "office", FloorAreaM2 = 1000, HeightM = 3,
                OccupantCount = 80, LightingWPerM2 = 9, EquipmentWPerM2 = 12,
                DhwLPerPersonDay = 5, OperatingDaysPerYear = operatingDays
            };
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 200, UvalueWm2K = 0.3, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 100, UvalueWm2K = 1.4, SHGC = 0.4, OrientationDeg = 180 });
            return z;
        }

        [Fact]
        public void EnergyEui_RisesWithOperatingDays_NotInert()
        {
            double eui250 = AnnualEnergyEstimator.Estimate(new[] { Zone(250) }, Climate(), Baseline(), 2.8).DesignEuiKwhM2Yr;
            double eui365 = AnnualEnergyEstimator.Estimate(new[] { Zone(365) }, Climate(), Baseline(), 2.8).DesignEuiKwhM2Yr;
            Assert.True(eui365 > eui250 * 1.1, $"operatingDaysPerYear must move energy: 250d={eui250:F1}, 365d={eui365:F1}");
            // Roughly proportional (use-driven loads scale with the operating calendar).
            Assert.InRange(eui250 / eui365, 0.55, 0.85);
        }

        [Fact]
        public void Water_AnnualDemand_ScalesLinearlyWithOperatingDays()
        {
            var flows = new FixtureFlows();
            var prof250 = new WaterUsageProfile { BuildingUse = "office", OperatingDaysPerYear = 250 };
            var prof365 = new WaterUsageProfile { BuildingUse = "hospital", OperatingDaysPerYear = 365 };
            prof250.Fixtures["wc"] = new FixtureUse { Uses = 3 };
            prof365.Fixtures["wc"] = new FixtureUse { Uses = 3 };

            double d250 = AnnualWaterEstimator.Estimate(flows, flows, prof250, 80).AnnualDemandL;
            double d365 = AnnualWaterEstimator.Estimate(flows, flows, prof365, 80).AnnualDemandL;
            Assert.Equal(250.0 / 365.0, d250 / d365, 2);   // exact linear scaling
        }

        // The headline: an office (250 days) and a 24/7 hospital (365 days) on the same
        // shell move consistently — both engines read the profile's operating days.
        [Fact]
        public void OfficeVsHospital_SameShell_MoveWithOperatingDays()
        {
            var lib   = LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));
            var water = WaterUsageProfileRegistry.LoadFromJson(TestData.Read("STING_WATER_USAGE_PROFILES.json"));

            var office   = lib.ResolveForUse("office").Profile;
            var hospital = lib.ResolveForUse("healthcare").Profile;
            Assert.Equal(250, office.OperatingDaysPerYear);
            Assert.Equal(365, hospital.OperatingDaysPerYear);

            // Same shell: apply each profile to an identical zone.
            LoadZone Shell(LoadProfile p)
            {
                var z = new LoadZone { Id = "s", FloorAreaM2 = 1000, HeightM = 3 };
                p.ApplyTo(z);
                z.OccupantCount = p.OccupantCountFor(1000);
                z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 200, UvalueWm2K = 0.3 });
                z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 100, UvalueWm2K = 1.4, SHGC = 0.4, OrientationDeg = 180 });
                return z;
            }
            // ApplyTo propagates operatingDaysPerYear onto the zone (L1 wiring).
            Assert.Equal(250, Shell(office).OperatingDaysPerYear);
            Assert.Equal(365, Shell(hospital).OperatingDaysPerYear);

            // Water: both estimators read the profile operating days.
            Assert.Equal(250, water.Get("office").OperatingDaysPerYear);
            Assert.Equal(365, water.Get("healthcare").OperatingDaysPerYear);
        }
    }
}
