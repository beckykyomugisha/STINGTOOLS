using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// What a command reports after its transaction (Circuit Check, Fault Level stamp,
    /// AIC stamp). A rolled-back Commit() used to be reported as "Stamped N values".
    /// </summary>
    public class ElecWriteReportTests
    {
        [Fact]
        public void Committed_run_reports_its_count()
        {
            Assert.Equal("12 values stamped.", ElecWriteReport.Landed("values stamped", 12, committed: true, "Committed"));
            Assert.Equal(12, ElecWriteReport.Kept(12, committed: true));
        }

        [Fact]
        public void Rolled_back_run_reports_zero_and_says_rolled_back()
        {
            string s = ElecWriteReport.Landed("values stamped", 12, committed: false, "RolledBack");
            Assert.StartsWith("ROLLED BACK (RolledBack): 0 values stamped", s);
            Assert.Contains("12 write(s) were made and then undone", s);
            Assert.DoesNotContain("12 values stamped", s);
            Assert.Equal(0, ElecWriteReport.Kept(12, committed: false));
        }

        [Fact]
        public void Rolled_back_with_no_writes_still_says_rolled_back()
            => Assert.Equal("ROLLED BACK (not committed): 0 verdicts.", ElecWriteReport.Landed("verdicts", 0, committed: false));
    }
}
