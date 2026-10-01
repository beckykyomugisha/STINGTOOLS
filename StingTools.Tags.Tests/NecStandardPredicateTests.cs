using StingTools.Commands.Electrical.Busbar;
using StingTools.Standards;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-30 follow-up — "is this the NEC?" is decided once, by ElectricalStandardId.IsNec.
    /// The electrical panel emits "NEC2023", the breaker and wizard combos emit "NEC"; five
    /// engine sites compared against one literal ("NEC" or "NEC2023"), so the other spelling
    /// was silently treated as BS 7671.
    /// </summary>
    public class NecStandardPredicateTests
    {
        [Theory]
        [InlineData("NEC", true)]
        [InlineData("NEC2023", true)]
        [InlineData("nec 2023", true)]
        [InlineData("NEC_2023", true)]
        [InlineData("BS7671", false)]
        [InlineData("BS_MCB", false)]
        [InlineData("BS_MCCB", false)]
        [InlineData("IEC60364", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsNec_accepts_every_NEC_spelling_and_nothing_else(string raw, bool expected)
            => Assert.Equal(expected, ElectricalStandardId.IsNec(raw));

        [Theory]
        [InlineData("NEC")]
        [InlineData("NEC2023")]
        public void Busbar_applies_the_NEC_125pct_factor_for_either_spelling(string std)
        {
            // 1000 A demand x1.25 = 1250 A -> 120x10 (1400 A); BS would take 100x8 (1050 A).
            Assert.Equal(1400, BusbarSizerEngine.Size(1000, std).RatingA);
            Assert.Equal(1050, BusbarSizerEngine.Size(1000, "BS7671").RatingA);
        }
    }
}
