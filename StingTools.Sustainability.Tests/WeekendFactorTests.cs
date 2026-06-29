using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L2 — the weekday/closure factor is folded into the annualisation via
    // operatingDaysPerYear, so weekday-only uses (office/school/worship) aren't
    // over-counted against 24/7 uses (residential/healthcare/data centre).
    public class WeekendFactorTests
    {
        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        [Theory]
        [InlineData("office")]
        [InlineData("education")]
        [InlineData("worship")]
        public void WeekdayUses_OperateBelow365(string use)
            => Assert.True(Lib().ResolveForUse(use).Profile.OperatingDaysPerYear < 365);

        [Theory]
        [InlineData("residential")]
        [InlineData("healthcare")]
        [InlineData("datacentre")]
        [InlineData("parking")]
        public void ContinuousUses_Operate365(string use)
            => Assert.Equal(365, Lib().ResolveForUse(use).Profile.OperatingDaysPerYear);

        private static ClimateMonthlySite Climate()
        {
            var s = new ClimateMonthlySite { Id = "c", AnnualGhiKwhM2Yr = 1850 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 30; s.GhiKwhM2Day[m] = 5.0; s.MeanRhPct[m] = 70; }
            return s;
        }

        private static LoadZone Office(int days)
        {
            var z = new LoadZone { Id = "z", FloorAreaM2 = 1000, HeightM = 3, OccupantCount = 80,
                                   LightingWPerM2 = 9, EquipmentWPerM2 = 12, DhwLPerPersonDay = 5,
                                   OperatingDaysPerYear = days };
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 200, UvalueWm2K = 0.3 });
            return z;
        }

        [Fact]
        public void WeekdayUse_NotOverCounted_VsContinuous()
        {
            var b = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"))
                    .Resolve("*", "0A", "office").Baseline;
            double weekday   = AnnualEnergyEstimator.Estimate(new[] { Office(250) }, Climate(), b, 2.8).DesignEuiKwhM2Yr;
            double continuous = AnnualEnergyEstimator.Estimate(new[] { Office(365) }, Climate(), b, 2.8).DesignEuiKwhM2Yr;
            // The weekday office is annualised below the 24/7 run — roughly the day ratio.
            Assert.True(weekday < continuous);
            Assert.InRange(weekday / continuous, 0.55, 0.85);
        }
    }
}
