using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-144 — hand-typed LOC / ZONE / AREA box names the strict grammar refuses
    /// must be named, not silently treated as plain boxes. The grammar itself is unchanged.</summary>
    public class ScopeBoxNameAuditTests
    {
        [Theory]
        [InlineData("STING-LOC::Block A")]         // space in the code
        [InlineData("STING-LOC::A::B")]            // two segments
        [InlineData("STING-ZONE::North Wing")]
        [InlineData("STING-AREA::A 01")]
        [InlineData("STING-LOCATION::X")]          // near-miss prefix (classifies as plain)
        [InlineData("STING-LOC:X")]                // one colon
        [InlineData("sting loc::X")]
        [InlineData("STING-ZONES::Z1")]
        public void RefusedNames_AreReported(string name)
            => Assert.False(string.IsNullOrEmpty(ScopeBoxNameAudit.Problem(name)));

        [Theory]
        [InlineData("STING-LOC::BLOCK-A")]
        [InlineData("STING-ZONE::Z01")]
        [InlineData("STING-AREA::A01::L01")]
        [InlineData("Scope Box 1")]
        [InlineData("STING-SEED::40x30")]
        [InlineData("STING::arch-plan-A1-1to100::L01")]
        [InlineData("STING Location Plan")]        // no colon: an ordinary name
        [InlineData("")]
        public void ValidOrUnrelatedNames_AreNot(string name)
            => Assert.Null(ScopeBoxNameAudit.Problem(name));

        [Fact]
        public void NearMiss_SaysWhichPrefixWasMeant()
        {
            Assert.Contains("STING-LOC::", ScopeBoxNameAudit.Problem("STING-LOCATION::X"));
            Assert.Contains("STING-ZONE::", ScopeBoxNameAudit.Problem("STING-ZONES::Z1"));
        }

        [Fact]
        public void TheTaggingReportNamesTheRefusedBoxes()
        {
            var stats = new StingTools.Core.TaggingStats();
            stats.ScopeBoxNameProblems.Add("'STING-LOC::Block A' — " + ScopeBoxNameAudit.Problem("STING-LOC::Block A"));
            var report = stats.BuildReport();
            Assert.Contains("SCOPE BOXES", report);
            Assert.Contains("STING-LOC::Block A", report);        }

        [Fact]
        public void TheGrammarIsNotRelaxed()
        {
            Assert.False(ScopeBoxNames.TryParseLoc("STING-LOC::Block A", out _, out _));
            Assert.False(ScopeBoxNames.TryParseLoc("STING-LOCATION::X", out _, out _));
        }
    }
}
