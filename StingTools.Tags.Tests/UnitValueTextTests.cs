using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// A _TXT display mirror carries a plain number; the tag row supplies the unit through
    /// its own suffix. AsValueString does not follow that convention, and four shipped rows
    /// declare a suffix — so an untrimmed value renders "MCB: 100 A A" on the drawing.
    /// </summary>
    public class UnitValueTextTests
    {
        [Theory]
        [InlineData("100 A", "100")]
        [InlineData("18 W", "18")]
        [InlineData("1200 lm", "1200")]
        [InlineData("18.5 W", "18.5")]
        [InlineData("60 min", "60")]
        [InlineData("100A", "100")]          // no space before the unit
        [InlineData("2.5 m\u00b2", "2.5")]
        [InlineData("-3.5 \u00b0C", "-3.5")]
        [InlineData("  42 kg  ", "42")]
        public void The_unit_is_dropped_and_the_number_kept(string input, string expected)
        {
            Assert.Equal(expected, UnitValueText.StripUnitSuffix(input));
        }

        [Theory]
        [InlineData("1 200 W", "1 200")]      // space as a thousands separator
        [InlineData("1,200 lm", "1,200")]
        [InlineData("1.200,5 W", "1.200,5")]  // European grouping
        public void A_separator_survives_when_a_digit_follows_it(string input, string expected)
        {
            Assert.Equal(expected, UnitValueText.StripUnitSuffix(input));
        }

        [Theory]
        [InlineData("By Category")]
        [InlineData("Undefined")]
        [InlineData("Type A")]
        public void A_value_that_is_not_a_quantity_is_returned_unchanged(string input)
        {
            // Truncating "By Category" to "" would replace a readable value with a blank,
            // which is the failure this whole area keeps producing.
            Assert.Equal(input, UnitValueText.StripUnitSuffix(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Null_and_empty_pass_through(string input)
        {
            Assert.Equal(input, UnitValueText.StripUnitSuffix(input));
        }

        [Fact]
        public void A_bare_number_is_left_exactly_as_it_is()
        {
            Assert.Equal("900", UnitValueText.StripUnitSuffix("900"));
            Assert.Equal("0", UnitValueText.StripUnitSuffix("0"));
        }
    }
}
