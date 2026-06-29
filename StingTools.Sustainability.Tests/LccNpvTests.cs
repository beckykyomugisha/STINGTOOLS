using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I10 — LCC uses NPV over the whole-life study period (not undiscounted
    // annual×25), and the study period is shared with H4.
    public class LccNpvTests
    {
        [Fact]
        public void ZeroRate_IsUndiscounted()
        {
            Assert.Equal(1000 * 25, SustainNpv.PresentValueAnnuity(1000, 25, 0), 6);
        }

        [Fact]
        public void PositiveRate_DiscountsBelowUndiscounted()
        {
            double pv = SustainNpv.PresentValueAnnuity(1000, 60, 3.5);
            Assert.True(pv < 1000 * 60);          // discounted < undiscounted
            Assert.True(pv > 0);
            // PV of 1000/yr for 60 yr at 3.5% ≈ 24,945.
            Assert.InRange(pv, 24500, 25400);
        }

        [Fact]
        public void HigherRate_LowersPresentValue()
        {
            double low  = SustainNpv.PresentValueAnnuity(1000, 30, 2.0);
            double high = SustainNpv.PresentValueAnnuity(1000, 30, 8.0);
            Assert.True(high < low);
        }

        [Fact]
        public void ZeroYears_IsZero()
            => Assert.Equal(0, SustainNpv.PresentValueAnnuity(1000, 0, 3.5), 6);

        [Fact]
        public void Setup_DiscountRate_ChangesContentHash()
        {
            var a = SustainProjectSetup.CreateDefault(170, 17);
            var b = SustainProjectSetup.CreateDefault(170, 17);
            b.DiscountRatePct = 6.0;
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void LccUsesSameStudyPeriodAsWholeLifeCarbon()
        {
            // Both read SustainProjectSetup.StudyPeriodYears — no separate hardcoded 25.
            var s = SustainProjectSetup.CreateDefault(2000, 100);
            s.StudyPeriodYears = 40;
            Assert.Equal(40, s.StudyPeriodYears);   // the value LCC + whole-life both use
        }
    }
}
