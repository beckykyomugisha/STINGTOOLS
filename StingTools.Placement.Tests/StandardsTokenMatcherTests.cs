using System;
using StingTools.Core.Placement;
using Xunit;

namespace StingTools.Placement.Tests
{
    /// <summary>
    /// Regression cover for the standards gate. Each "misses" case below is a
    /// pairing that occurs in the shipped rule packs and that the previous
    /// inline comparison in PlacementRuleLoader.FilterByProfile failed to match,
    /// silently dropping the rule from the run.
    /// </summary>
    public class StandardsTokenMatcherTests
    {
        // ── Split ────────────────────────────────────────────────────

        [Fact]
        public void Split_TreatsSlashAsASeparator()
        {
            // "Approved Doc M / BS 8300-2" is one StandardRef string covering two
            // standards. Splitting only on , and ; kept it as a single opaque token.
            var parts = StandardsTokenMatcher.Split("Approved Doc M / BS 8300-2");
            Assert.Equal(new[] { "Approved Doc M", "BS 8300-2" }, parts);
        }

        [Fact]
        public void Split_HandlesCommaSemicolonAndPipe()
        {
            var parts = StandardsTokenMatcher.Split("BS 7671, BS 5839-1; BICSI | BS 6701");
            Assert.Equal(new[] { "BS 7671", "BS 5839-1", "BICSI", "BS 6701" }, parts);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(",;|/")]
        public void Split_ReturnsEmptyForNothingUseful(string raw)
        {
            Assert.Empty(StandardsTokenMatcher.Split(raw));
        }

        // ── Normalise ────────────────────────────────────────────────

        [Theory]
        [InlineData("BS 7671", "BS7671")]
        [InlineData("BS7671", "BS7671")]
        [InlineData("bs 7671", "BS7671")]
        [InlineData("Approved Doc M", "APPROVEDDOCM")]
        public void Normalise_IgnoresSpacingAndCase(string input, string expected)
        {
            Assert.Equal(expected, StandardsTokenMatcher.Normalise(input));
        }

        [Fact]
        public void Normalise_StripsEditionYearButKeepsPartNumber()
        {
            // The part number ("-1") is meaningful; the edition year is not.
            Assert.Equal("BSEN124641", StandardsTokenMatcher.Normalise("BS EN 12464-1:2011"));
            Assert.Equal("BS64651",    StandardsTokenMatcher.Normalise("BS 6465-1:2006"));
        }

        [Fact]
        public void Normalise_DoesNotMistakeAStandardNumberForAYear()
        {
            // 7671 is not in the 1800-2199 window, so it must survive.
            Assert.Equal("BS7671", StandardsTokenMatcher.Normalise("BS 7671"));
        }

        [Fact]
        public void Normalise_StripsTrailingSpaceSeparatedYear()
        {
            Assert.Equal("BS8233", StandardsTokenMatcher.Normalise("BS 8233 2014"));
        }

        // ── Matches: cases the old comparison got right ──────────────

        [Fact]
        public void Matches_BroadProfileTokenMatchesSpecificRuleCitation()
        {
            Assert.True(StandardsTokenMatcher.Matches("BS 6465-1:2006", new[] { "BS 6465" }));
        }

        [Fact]
        public void Matches_SpecificProfileTokenMatchesBroadRuleCitation()
        {
            Assert.True(StandardsTokenMatcher.Matches("BS 6465", new[] { "BS 6465-1:2006" }));
        }

        // ── Matches: cases the old comparison MISSED ─────────────────

        [Fact]
        public void Matches_AcrossSpacingDifference()
        {
            // Rule packs author the compact form; profiles are typed with spaces.
            Assert.True(StandardsTokenMatcher.Matches("BS7671", new[] { "BS 7671" }));
            Assert.True(StandardsTokenMatcher.Matches("BS 7671", new[] { "BS7671" }));
        }

        [Fact]
        public void Matches_AcrossEditionYear()
        {
            Assert.True(StandardsTokenMatcher.Matches("BS EN 12464-1:2011", new[] { "BS EN 12464" }));
        }

        [Fact]
        public void Matches_WhenRuleCitesSeveralStandardsSeparatedBySlash()
        {
            Assert.True(StandardsTokenMatcher.Matches("Approved Doc M / BS 7671", new[] { "BS7671" }));
            Assert.True(StandardsTokenMatcher.Matches("Approved Doc M / BS 8300-2", new[] { "Approved Doc M" }));
        }

        [Fact]
        public void Matches_SemicolonJoinedArrayForm()
        {
            // StringOrCsvArrayConverter turns ["BS7671","BS6701"] into "BS7671;BS6701".
            Assert.True(StandardsTokenMatcher.Matches("BS7671;BS6701", new[] { "BS 6701" }));
        }

        // ── Matches: non-matches must stay non-matches ───────────────

        [Fact]
        public void Matches_UnrelatedStandardsDoNotMatch()
        {
            Assert.False(StandardsTokenMatcher.Matches("BS 5839-1", new[] { "BS 7671" }));
        }

        [Fact]
        public void Matches_EmptyRuleSideReturnsFalseSoCallerAppliesItsOwnDefault()
        {
            // FilterByProfile treats an untagged rule as "always include"; that
            // decision belongs to the caller, not the matcher.
            Assert.False(StandardsTokenMatcher.Matches("", new[] { "BS 7671" }));
            Assert.False(StandardsTokenMatcher.Matches(null, new[] { "BS 7671" }));
        }

        [Fact]
        public void Matches_EmptyActiveStandardsReturnsFalse()
        {
            Assert.False(StandardsTokenMatcher.Matches("BS 7671", new string[0]));
            Assert.False(StandardsTokenMatcher.Matches("BS 7671", null));
        }
    }
}
