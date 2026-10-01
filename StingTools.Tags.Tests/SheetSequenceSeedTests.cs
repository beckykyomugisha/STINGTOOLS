using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-162 — a counter is seeded only from sheets in its own
    /// discipline / vol, not from every sheet of the drawing type.</summary>
    public class SheetSequenceSeedTests
    {
        [Theory]
        // the fabrication default number: SP-{disc}-{sys}-{lvl}-{seq}
        [InlineData("SP-M-HVAC-L01-0007", "", "M", "L01", true)]
        [InlineData("SP-M-HVAC-L02-0012", "", "M", "L01", false)]   // another level: was counted
        [InlineData("SP-E-LV-L01-0004",   "", "M", "L01", false)]   // another discipline: was counted
        [InlineData("M-L01-SP-0003",      "", "m", "l01", true)]    // case-insensitive
        [InlineData("SP-HVAC-L01-0003",   "M", "M", "L01", true)]   // discipline from the stamp
        [InlineData("SP-HVAC-L01-0003",   "",  "M", "L01", false)]  // no evidence of the discipline
        [InlineData("ANY-0009",           "",  "",  "",    true)]   // blank bucket tokens match anything
        [InlineData("SP-M-HVAC-XX-0002",  "",  "M", "XX",  true)]   // the composer's no-level code
        public void Sheet_belongs_to_bucket(string number, string stamped, string disc, string vol, bool expected)
            => Assert.Equal(expected, SheetSequenceSeed.MatchesBucket(number, stamped, disc, vol));

        [Fact]
        public void Level_code_inside_another_segment_is_not_a_match()
        {
            // "L1" must not match "L10" or "SL1".
            Assert.False(SheetSequenceSeed.MatchesBucket("SP-M-SL1-L10-0004", "", "M", "L1"));
        }
    }
}
