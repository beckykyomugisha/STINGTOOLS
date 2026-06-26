using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS A1 — the monthly climate layer is synthesised from the single design-day
    // registry: temperature from cooling/heating design days, GHI from latitude or
    // a measured annual value, hemisphere-aware, with seasonality scaling by |lat|.
    public class MonthlyClimateSynthesizerTests
    {
        private static readonly int[] Days = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        private static ClimateMonthlySite Fill(double coolDb, double heatDb, double lat, double annualGhi = 0)
        {
            var s = new ClimateMonthlySite { Id = "syn" };
            MonthlyClimateSynthesizer.Fill(s, coolDb, heatDb, lat, annualGhi);
            return s;
        }

        [Fact]
        public void AnnualMeanTemperature_EqualsDesignMidpoint()
        {
            var s = Fill(coolDb: 34, heatDb: 6, lat: 51);
            Assert.Equal(20.0, s.MeanDbC.Average(), 1);   // (34+6)/2
        }

        [Fact]
        public void MeasuredAnnualGhi_IsPreserved_AcrossMonths()
        {
            var s = Fill(34, 6, 51, annualGhi: 1100);
            double total = 0;
            for (int m = 0; m < 12; m++) total += s.GhiKwhM2Day[m] * Days[m];
            Assert.Equal(1100, total, 0);                 // Σ GHI×days == annual
            Assert.Equal(1100, s.AnnualGhiKwhM2Yr, 6);
        }

        [Fact]
        public void NorthernHemisphere_PeaksInJuly()
        {
            var s = Fill(34, 6, lat: 51);                 // London-ish
            Assert.True(s.MeanDbC[6] > s.MeanDbC[0]);     // Jul warmer than Jan
            Assert.True(s.GhiKwhM2Day[6] > s.GhiKwhM2Day[0]);
        }

        [Fact]
        public void SouthernHemisphere_PeaksInJanuary()
        {
            var s = Fill(34, 6, lat: -33);                // Sydney-ish
            Assert.True(s.MeanDbC[0] > s.MeanDbC[6]);     // Jan warmer than Jul
            Assert.True(s.GhiKwhM2Day[0] > s.GhiKwhM2Day[6]);
        }

        [Fact]
        public void Tropics_NearlyFlatGhi_HighLatitude_StronglySeasonal()
        {
            var tropic = Fill(33, 22, lat: 0);            // equator
            var temperate = Fill(28, -5, lat: 60);       // high latitude

            double tropRatio = tropic.GhiKwhM2Day.Max() / tropic.GhiKwhM2Day.Min();
            double tempRatio = temperate.GhiKwhM2Day.Max() / temperate.GhiKwhM2Day.Min();

            Assert.True(tropRatio < 1.15);                // ~flat at the equator
            Assert.True(tempRatio > 2.0);                 // strong winter/summer swing
        }

        [Fact]
        public void LatitudeGhiEstimate_DecreasesWithLatitude_AndIsClamped()
        {
            double eq = MonthlyClimateSynthesizer.EstimateAnnualGhiFromLatitude(0);
            double mid = MonthlyClimateSynthesizer.EstimateAnnualGhiFromLatitude(45);
            double pole = MonthlyClimateSynthesizer.EstimateAnnualGhiFromLatitude(85);

            Assert.True(eq > mid && mid > pole);
            Assert.InRange(eq, 1900, 2100);
            Assert.True(pole >= 700);                     // floor
        }

        [Fact]
        public void NoMeasuredGhi_UsesLatitudeEstimate()
        {
            var s = Fill(34, 6, lat: 0);                  // equator, no measured GHI
            Assert.Equal(MonthlyClimateSynthesizer.EstimateAnnualGhiFromLatitude(0), s.AnnualGhiKwhM2Yr, 1);
            Assert.True(s.FellBackToDesignDay);
        }
    }
}
