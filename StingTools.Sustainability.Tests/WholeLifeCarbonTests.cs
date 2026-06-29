using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS H4 — one whole-life carbon figure: embodied A1–A3 (materials net carbon) +
    // operational (energy × grid factor) over a data-driven study period. Carbon
    // only; the embodied-energy (MJ) track is never folded in.
    public class WholeLifeCarbonTests
    {
        [Fact]
        public void WholeLife_IsEmbodiedPlusOperationalOverPeriod()
        {
            var r = WholeLifeCarbon.Compute(
                embodiedA1A3Kg: 500_000, operationalKgPerYr: 20_000,
                studyPeriodYears: 60, floorAreaM2: 2_000);

            Assert.Equal(60, r.StudyPeriodYears);
            Assert.Equal(1_200_000, r.OperationalTotalKg, 1);       // 20k × 60
            Assert.Equal(1_700_000, r.WholeLifeKg, 1);              // 500k + 1.2M
            Assert.Equal(850, r.WholeLifeKgM2, 1);                  // /2000 m²
            Assert.Equal(250, r.EmbodiedKgM2, 1);
            Assert.Equal(10, r.OperationalKgM2Yr, 1);
            Assert.True(r.Computed);
        }

        [Fact]
        public void StudyPeriod_DataDriven_ChangesOperationalShare()
        {
            var p30 = WholeLifeCarbon.Compute(500_000, 20_000, 30, 2_000);
            var p60 = WholeLifeCarbon.Compute(500_000, 20_000, 60, 2_000);
            // Longer study period ⇒ more operational carbon accrues; embodied is fixed.
            Assert.True(p60.WholeLifeKg > p30.WholeLifeKg);
            Assert.Equal(p30.EmbodiedA1A3Kg, p60.EmbodiedA1A3Kg, 1);
        }

        [Fact]
        public void ZeroPeriod_FallsBackToDefault60()
        {
            var r = WholeLifeCarbon.Compute(0, 1_000, 0, 1_000);
            Assert.Equal(WholeLifeCarbon.DefaultStudyPeriodYears, r.StudyPeriodYears);
            Assert.Equal(60_000, r.OperationalTotalKg, 1);
        }

        [Fact]
        public void NotComputed_WhenNoFloorAreaOrNoCarbon()
        {
            Assert.False(WholeLifeCarbon.Compute(500_000, 20_000, 60, 0).Computed);   // no GFA
            Assert.False(WholeLifeCarbon.Compute(0, 0, 60, 2_000).Computed);          // no carbon
        }

        [Fact]
        public void NegativeOperational_IsClampedToZero()
        {
            var r = WholeLifeCarbon.Compute(100_000, -5_000, 60, 1_000);
            Assert.Equal(0, r.OperationalTotalKg, 1);
            Assert.Equal(100_000, r.WholeLifeKg, 1);
        }

        [Fact]
        public void Setup_StudyPeriod_ChangesContentHash()
        {
            var a = SustainProjectSetup.CreateDefault(2000, 100);
            var b = SustainProjectSetup.CreateDefault(2000, 100);
            b.StudyPeriodYears = 30;
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }
    }
}
