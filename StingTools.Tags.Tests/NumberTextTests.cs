using System.Globalization;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ROADMAP ELEC-30: ParameterHelpers.GetDouble read TEXT with NumberStyles.Any + invariant,
    /// so a "2,5" written on a comma-decimal machine read as 25.
    /// </summary>
    public class NumberTextTests
    {
        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        [Theory]
        [InlineData("2.5", 2.5)]
        [InlineData(" -3 ", -3)]
        [InlineData("1e3", 1000)]
        [InlineData("2,5", 2.5)]       // decimal comma — was 25
        [InlineData("12,50", 12.5)]    // was 1250
        [InlineData("1,250", 1250)]    // a thousands group, as before
        [InlineData("1,234.5", 1234.5)]
        [InlineData("(5)", -5)]        // NumberStyles.Any forms still read when there is no comma
        public void Reads_on_an_english_machine(string text, double expected)
        {
            Assert.True(NumberText.TryParse(text, En, out double v));
            Assert.Equal(expected, v, 9);
        }

        [Theory]
        [InlineData("2,5", 2.5)]
        [InlineData("1,250", 1.25)]    // on a comma-decimal machine its own culture wins
        [InlineData("2.5", 2.5)]
        public void Reads_on_a_comma_decimal_machine(string text, double expected)
        {
            Assert.True(NumberText.TryParse(text, De, out double v));
            Assert.Equal(expected, v, 9);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("1,2,3")]
        [InlineData("2.5 mm")]
        [InlineData("abc")]
        public void Junk_is_not_a_number(string text) => Assert.False(NumberText.TryParse(text, En, out _));
    }
}
