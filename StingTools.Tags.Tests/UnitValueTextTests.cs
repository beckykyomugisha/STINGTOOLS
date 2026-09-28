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

        // GetValueText reads a LENGTH parameter in the unit its name states.
        [Theory]
        [InlineData("BLE_STAIR_HEADROOM_MM", false)]
        [InlineData("PLM_PPE_LENGTH_M", true)]
        [InlineData("PLM_SPT_SPACING_M_NR", true)]
        [InlineData("BLE_WALL_THICKNESS_MM_NR", false)]
        [InlineData("FLS_SFTY_COVERAGE_AREA_SQ_M", true)]
        [InlineData("STR_FDN_DEPTH", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Length_name_suffix_picks_metres_or_millimetres(string name, bool metres)
            => Assert.Equal(metres, UnitValueText.LengthNameIsMetres(name));

        [Theory]
        [InlineData(12.5, "12.5")]
        [InlineData(900.0, "900")]
        [InlineData(0.12345, "0.123")]
        [InlineData(1234567.0, "1234567")]
        [InlineData(-2.0, "-2")]
        public void Numbers_are_plain_invariant_text(double v, string expected)
        {
            var prev = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal(expected, UnitValueText.Invariant(v));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
        }

        [Fact]
        public void Not_a_number_is_blank_never_NaN_text()
        {
            Assert.Equal("", UnitValueText.Invariant(double.NaN));
            Assert.Equal("", UnitValueText.Invariant(double.PositiveInfinity));
        }
    }
}
