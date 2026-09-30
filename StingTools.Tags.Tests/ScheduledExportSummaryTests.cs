using StingTools.Docs;
using Xunit;

namespace StingTools.Tags.Tests
{
    // R9: the Export Centre schedule step reported success whether or not anything was exported.
    public class ScheduledExportSummaryTests
    {
        private static ScheduledExportSummary With(params ScheduledJobOutcome[] jobs)
        {
            var s = new ScheduledExportSummary();
            s.Jobs.AddRange(jobs);
            return s;
        }

        [Fact]
        public void NothingDue_FailsAPreset_ButIsOnlyNothingDueByHand()
        {
            var s = new ScheduledExportSummary();
            Assert.Equal(ScheduledExportVerdict.Failed, s.Verdict(inPreset: true));
            Assert.Equal(ScheduledExportVerdict.NothingDue, s.Verdict(inPreset: false));
            Assert.Contains("nothing was exported", s.Describe());
        }

        [Theory]
        [InlineData(ScheduledJobState.Blocked)]
        [InlineData(ScheduledJobState.NoSheets)]
        [InlineData(ScheduledJobState.ProfileMissing)]
        [InlineData(ScheduledJobState.Error)]
        public void ADueJobThatDidNotRun_FailsTheStep_EvenBesideOneThatDid(ScheduledJobState state)
        {
            var s = With(new ScheduledJobOutcome { Name = "PDF", State = ScheduledJobState.Ran, FilesOk = 12 },
                         new ScheduledJobOutcome { Name = "DWG", State = state, Detail = "why" });
            Assert.Equal(ScheduledExportVerdict.Failed, s.Verdict(false));
            Assert.Equal(ScheduledExportVerdict.Failed, s.Verdict(true));
            Assert.Contains("NOT RUN", s.Describe());
        }

        [Fact]
        public void AFailedFile_FailsTheStep_ASkippedOneDoesNot()
        {
            Assert.Equal(ScheduledExportVerdict.Failed,
                With(new ScheduledJobOutcome { State = ScheduledJobState.Ran, FilesOk = 3, FilesFailed = 1 }).Verdict(true));
            Assert.Equal(ScheduledExportVerdict.Succeeded,
                With(new ScheduledJobOutcome { State = ScheduledJobState.Ran, FilesOk = 3, FilesSkipped = 2 }).Verdict(true));
        }

        [Fact]
        public void AJobThatRanButProducedNothing_IsNotASuccess()
        {
            var s = With(new ScheduledJobOutcome { State = ScheduledJobState.Ran, FilesSkipped = 4 });
            Assert.Equal(ScheduledExportVerdict.Failed, s.Verdict(true));
            Assert.Equal(ScheduledExportVerdict.NothingDue, s.Verdict(false));
        }

        [Fact]
        public void UnreadableState_Fails()
        {
            var s = new ScheduledExportSummary { LoadError = "bad json" };
            Assert.Equal(ScheduledExportVerdict.Failed, s.Verdict(false));
            Assert.Contains("bad json", s.Describe());
        }
    }
}
