using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the glob/alternation PROD FAMILY_PATTERN matcher. This is the fix
    /// for the shipped STING_PROD_CODES.csv rows (e.g. *Air Handling*,
    /// *Split*|*Packaged*) that the legacy String.Contains matcher silently
    /// never matched. Inputs are upper-cased (the caller upper-cases both sides).
    /// </summary>
    public class ProdPatternMatcherTests
    {
        [Theory]
        // bare substring — back-compatible with historic patterns
        [InlineData("DAIKIN FCU CEILING FCU-01", "FCU", true)]
        [InlineData("WALL HUNG WC PAN", "WC", true)]
        [InlineData("DAIKIN FCU CEILING", "AHU", false)]
        // *contains* glob
        [InlineData("TROX AIR HANDLING UNIT AHU-1", "*AIR HANDLING*", true)]
        [InlineData("FAN COIL UNIT", "*AIR HANDLING*", false)]
        // prefix / suffix globs
        [InlineData("VRV OUTDOOR UNIT", "VRV*", true)]
        [InlineData("OUTDOOR VRV", "VRV*", false)]
        [InlineData("CEILING CASSETTE FCU", "*FCU", true)]
        // alternation
        [InlineData("MITSUBISHI SPLIT UNIT", "*SPLIT*|*PACKAGED*", true)]
        [InlineData("ROOFTOP PACKAGED UNIT", "*SPLIT*|*PACKAGED*", true)]
        [InlineData("FAN COIL UNIT", "*SPLIT*|*PACKAGED*", false)]
        // embedded wildcard
        [InlineData("VRV CONDENSER UNIT", "VRV*UNIT", true)]
        [InlineData("VRV CONDENSER MODULE", "VRV*UNIT", false)]
        public void Matches_handles_substrings_globs_and_alternation(string name, string pattern, bool expected)
        {
            Assert.Equal(expected, ProdPatternMatcher.Matches(name, pattern));
        }

        [Theory]
        [InlineData(null, "FCU")]
        [InlineData("", "FCU")]
        [InlineData("DAIKIN FCU", null)]
        [InlineData("DAIKIN FCU", "")]
        public void Matches_returns_false_on_empty_inputs(string name, string pattern)
        {
            Assert.False(ProdPatternMatcher.Matches(name, pattern));
        }

        [Fact]
        public void Matches_ignores_blank_alternation_branches()
        {
            // trailing/empty '|' branch must not match everything
            Assert.False(ProdPatternMatcher.Matches("FAN COIL", "*AHU*|"));
            Assert.True(ProdPatternMatcher.Matches("AHU-1", "*AHU*|"));
        }

        [Theory]
        // '?' = exactly one char
        [InlineData("DN20", "DN2?", true)]
        [InlineData("DN2", "DN2?", false)]      // nothing for the '?' to match
        [InlineData("DN200", "DN2?", false)]    // anchored — too long
        // character class
        [InlineData("DN20-PN16", "DN20-PN1[06]", true)]
        [InlineData("DN20-PN10", "DN20-PN1[06]", true)]
        [InlineData("DN20-PN13", "DN20-PN1[06]", false)]
        // class combined with glob
        [InlineData("VALVE PN16 BRONZE", "*PN1[06]*", true)]
        // literal punctuation outside wildcards stays literal (regex chars escaped)
        [InlineData("TYPE (A) UNIT", "*(A)*", true)]
        [InlineData("TYPE A UNIT", "*(A)*", false)]
        // unterminated '[' treated as a literal, not a crash
        [InlineData("ROW [1", "ROW [1", true)]
        public void Matches_handles_question_mark_and_char_classes(string name, string pattern, bool expected)
        {
            Assert.Equal(expected, ProdPatternMatcher.Matches(name, pattern));
        }
    }
}
