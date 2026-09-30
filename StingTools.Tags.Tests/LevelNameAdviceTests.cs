using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The Project Setup wizard's level-name findings. "L02 - Office Level" is an ISO
    /// name, but the raw name feeds {lvl} in sheet numbers and the level segment of
    /// scope-box names, neither of which may contain a space.
    /// </summary>
    public class LevelNameAdviceTests
    {
        [Theory]
        [InlineData("L02")]
        [InlineData("GF")]
        [InlineData("B01")]
        [InlineData("L02 - Office Level")]
        [InlineData("RF")]
        public void IsoNamesPassTheIsoCheck(string name) => Assert.True(LevelNameAdvice.IsIsoLevelName(name));

        [Theory]
        [InlineData("Level 2")]
        [InlineData("First Floor")]
        [InlineData("")]
        public void NonIsoNamesFailTheIsoCheck(string name) => Assert.False(LevelNameAdvice.IsIsoLevelName(name));

        [Theory]
        [InlineData("L02 - Office Level", true)]
        [InlineData("Level 2", true)]
        [InlineData("L02 ", true)]
        [InlineData("L02\tA", true)]
        [InlineData("L02", false)]
        [InlineData("L02-Office", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void WhitespaceIsDetected(string name, bool expected)
            => Assert.Equal(expected, LevelNameAdvice.HasWhitespace(name));

        [Theory]
        [InlineData("L02 - Office Level", "L02")]
        [InlineData("gf - Reception", "GF")]
        [InlineData("B01 _ Car Park", "B01")]
        [InlineData("Level 2", "Level2")]
        [InlineData("First  Floor", "FirstFloor")]
        public void SuggestsTheShortCode(string name, string expected)
            => Assert.Equal(expected, LevelNameAdvice.SuggestShortCode(name));

        [Fact]
        public void FindingsListOnlySpacedNamesOnce()
        {
            var f = LevelNameAdvice.WhitespaceFindings(new[] { "GF", "L01 - Office", "L01 - Office", "L02", null, "Roof Level" });
            Assert.Equal(new[] { "L01 - Office", "Roof Level" }, f.Select(x => x.Name).ToArray());
            Assert.Equal(new[] { "L01", "RoofLevel" }, f.Select(x => x.Suggestion).ToArray());
        }

        [Fact]
        public void EverySuggestionIsSpaceFree()
        {
            foreach (var n in new[] { "L02 - Office Level", "Level 2", " GF ", "Mezz  Floor 1" })
                Assert.False(LevelNameAdvice.HasWhitespace(LevelNameAdvice.SuggestShortCode(n)), n);
        }
    }
}
