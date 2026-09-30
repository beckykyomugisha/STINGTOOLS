using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    // R6 / R7: IssueSheetsForRevision issued to zero sheets whenever a person ran it,
    // and in a workflow issued a P/C revision with no suitability at all.
    public class RevisionIssueGateTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZeroTargetSheets_IsNeverIssued(bool inPreset)
        {
            Assert.False(RevisionIssueGate.MayIssue("P04", "S2", 0, inPreset, out string why));
            Assert.Contains("no sheet carries a cloud", why);
        }

        [Fact]
        public void AnInconsistentPair_IsRefused()
        {
            Assert.False(RevisionIssueGate.MayIssue("P03", "A1", 5, false, out string why));
            Assert.Contains("NOT issued", why);
        }

        [Fact]
        public void BlankSuitability_OnAPairedSeries_IsRefusedOnlyInAPreset()
        {
            Assert.False(RevisionIssueGate.MayIssue("P04", "", 3, inPreset: true, out string why));
            Assert.Contains("no suitability", why);
            Assert.True(RevisionIssueGate.MayIssue("P04", "", 3, inPreset: false, out _));
            // A series the P/C rule does not govern is not held up.
            Assert.True(RevisionIssueGate.MayIssue("7", "", 3, inPreset: true, out _));
        }

        [Fact]
        public void AConsistentPair_Issues()
        {
            Assert.True(RevisionIssueGate.MayIssue("P04", "S2", 3, true, out string why));
            Assert.Equal("", why);
            Assert.True(RevisionIssueGate.MayIssue("C01", "A1", 1, true, out _));
        }

        [Fact]
        public void AgreedSuitability_IsTheOneCodeAllSheetsCarry_ElseBlank()
        {
            Assert.Equal("S2", RevisionIssueGate.AgreedSuitability(new[] { "S2", "", "s2", null }));
            Assert.Equal("", RevisionIssueGate.AgreedSuitability(new[] { "S2", "S3" }));
            Assert.Equal("", RevisionIssueGate.AgreedSuitability(new[] { "", null }));
            Assert.Equal("", RevisionIssueGate.AgreedSuitability(null));
        }

        [Theory]
        [InlineData("P01", true)]
        [InlineData("C02", true)]
        [InlineData("7", false)]
        [InlineData("", false)]
        public void IsPairedSeries(string rev, bool expected)
            => Assert.Equal(expected, Iso19650RevisionRules.IsPairedSeries(rev));
    }
}
