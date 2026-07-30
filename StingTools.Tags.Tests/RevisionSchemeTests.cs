// RevisionSchemeTests.cs — IM-15.
//
// The point of these tests is the DEFAULT path: an existing project with no
// revision_scheme configured must behave exactly as the old hard-coded P01→C01
// code did. The override cases prove an appointment can change the convention
// without a code change.

using Planscape.Docs.Templates;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class RevisionSchemeTests
    {
        // ── Default path — must be indistinguishable from the old hard-coded behaviour ──

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("P01,P02,C01,C02")]   // the shipped manifest default
        public void UnsetOrDefaultScheme_IsPreliminaryPToContractualC(string raw)
        {
            var s = RevisionScheme.Parse(raw);

            Assert.Equal("P", s.PreliminaryPrefix);
            Assert.Equal("C", s.ContractualPrefix);
            Assert.Equal("P01", s.FirstPreliminary);
            Assert.Equal("C01", s.FirstContractual);
            Assert.False(s.SingleStage);
        }

        [Fact]
        public void DefaultScheme_PromotesPToC()
        {
            var s = RevisionScheme.Parse(null);

            Assert.Equal("C01", s.PromoteToContractual("P01"));
            Assert.Equal("C07", s.PromoteToContractual("P07"));
            // A bare "P" carries no number — must not become a malformed single-letter "C".
            Assert.Equal("C01", s.PromoteToContractual("P"));
            // Blank ⇒ first contractual.
            Assert.Equal("C01", s.PromoteToContractual(null));
        }

        [Fact]
        public void DefaultScheme_PromotionIsIdempotentOutsidePreliminarySeries()
        {
            var s = RevisionScheme.Parse(null);

            Assert.Equal("C01", s.PromoteToContractual("C01"));
            Assert.Equal("A03", s.PromoteToContractual("A03"));
        }

        [Fact]
        public void DefaultScheme_BumpsWithinSeriesAndPadsWidth()
        {
            var s = RevisionScheme.Parse(null);

            Assert.Equal("P02", s.Bump("P01"));
            Assert.Equal("C02", s.Bump("C01"));
            Assert.Equal("P10", s.Bump("P09"));
            Assert.Equal("P100", s.Bump("P99"));   // width grows rather than wrapping
            // Blank ⇒ this IS the first revision; must not skip P01.
            Assert.Equal("P01", s.Bump(null));
            Assert.Equal("P01", s.Bump("  "));
            // Letter-only sequences advance the letter.
            Assert.Equal("B", s.Bump("A"));
        }

        [Fact]
        public void Bump_LeavesUnparseableRevisionUnchanged()
        {
            var s = RevisionScheme.Parse(null);

            // Must NOT append a "+1" sentinel — that used to be persisted and corrupted
            // the sequence permanently.
            Assert.Equal("Z", s.Bump("Z"));
            Assert.Equal("12-34", s.Bump("12-34"));
        }

        // ── Override path — an appointment mandating a different convention ──

        [Fact]
        public void CustomScheme_DrivesPrefixesFromConfig()
        {
            var s = RevisionScheme.Parse("D01,D02,F01,F02");   // Draft → Final

            Assert.Equal("D", s.PreliminaryPrefix);
            Assert.Equal("F", s.ContractualPrefix);
            Assert.Equal("D01", s.FirstPreliminary);
            Assert.Equal("F01", s.FirstContractual);

            Assert.Equal("F01", s.PromoteToContractual("D01"));
            Assert.Equal("D02", s.Bump("D01"));
            // A P-series revision is foreign to this scheme — left alone, not promoted.
            Assert.Equal("P01", s.PromoteToContractual("P01"));
        }

        [Fact]
        public void MultiLetterPrefixes_AreSupported()
        {
            var s = RevisionScheme.Parse("PRE01,CON01");

            Assert.Equal("PRE", s.PreliminaryPrefix);
            Assert.Equal("CON", s.ContractualPrefix);
            Assert.Equal("CON01", s.PromoteToContractual("PRE01"));
        }

        [Fact]
        public void SingleStageScheme_MakesPromotionANoOp()
        {
            var s = RevisionScheme.Parse("P01,P02,P03");

            Assert.True(s.SingleStage);
            // No contractual series was declared, so promotion must not invent a "C" series.
            Assert.Equal("P02", s.PromoteToContractual("P02"));
            Assert.Equal("P03", s.Bump("P02"));
        }

        [Theory]
        [InlineData("01,02,03")]     // purely numeric — no stage letters at all
        [InlineData(",,,")]          // separators only
        public void SchemeWithNoUsableStages_FallsBackToDefault(string raw)
        {
            var s = RevisionScheme.Parse(raw);

            Assert.Equal("P", s.PreliminaryPrefix);
            Assert.Equal("C", s.ContractualPrefix);
        }

        [Fact]
        public void SchemeParsing_IgnoresWhitespaceAndRepeatedPrefixes()
        {
            var s = RevisionScheme.Parse("  P01 , P02 ; P03 , C01 , C02  ");

            Assert.Equal("P", s.PreliminaryPrefix);
            Assert.Equal("P01", s.FirstPreliminary);
            Assert.Equal("C", s.ContractualPrefix);
            Assert.Equal("C01", s.FirstContractual);
        }
    }
}
