using System.Linq;
using StingTools.Core.Schedule;
using Xunit;

namespace StingTools.Scheduling.Tests
{
    /// <summary>PM-4 — converged MSP/XER parser: predecessors read, % normalised once,
    /// XER dates seconds-tolerant with warn-on-skip.</summary>
    public class ScheduleImporterTests
    {
        private const string MspXml =
@"<?xml version='1.0' encoding='UTF-8'?>
<Project xmlns='http://schemas.microsoft.com/project'>
  <Tasks>
    <Task><UID>0</UID><Name></Name><Summary>1</Summary></Task>
    <Task><UID>1</UID><Name>Mobilise</Name><Start>2026-01-05T08:00:00</Start><Finish>2026-01-09T17:00:00</Finish><PercentComplete>100</PercentComplete></Task>
    <Task><UID>2</UID><Name>Substructure</Name><Start>2026-01-12T08:00:00</Start><Finish>2026-01-30T17:00:00</Finish><PercentComplete>0.5</PercentComplete>
      <PredecessorLink><PredecessorUID>1</PredecessorUID><Type>1</Type></PredecessorLink></Task>
  </Tasks>
</Project>";

        [Fact]
        public void Msp_ReadsTasks_SkipsSummaryWithNoName()
        {
            var r = ScheduleImporter.ParseMsProjectXml(MspXml);
            Assert.Equal("msproject", r.Source);
            Assert.Equal(2, r.Tasks.Count);   // the no-name summary row is skipped
            Assert.Contains(r.Tasks, t => t.Name == "Mobilise");
        }

        [Fact]
        public void Msp_PredecessorLink_IsRead()
        {
            var r = ScheduleImporter.ParseMsProjectXml(MspXml);
            var sub = r.Tasks.First(t => t.Name == "Substructure");
            Assert.Single(sub.Predecessors);
            Assert.Equal("1", sub.Predecessors[0].TaskId);
            Assert.Equal("FS", sub.Predecessors[0].Type);   // MSP Type 1 = FS
        }

        [Fact]
        public void Percent_NormalisedOnce_FractionScaled()
        {
            var r = ScheduleImporter.ParseMsProjectXml(MspXml);
            // PercentComplete 0.5 is a 0..1 fraction → 50, NOT 0.5.
            Assert.Equal(50, r.Tasks.First(t => t.Name == "Substructure").PercentComplete, 4);
            Assert.Equal(100, r.Tasks.First(t => t.Name == "Mobilise").PercentComplete, 4);
        }

        private const string Xer =
"ERMHDR\t19.12\n" +
"%T\tTASK\n" +
"%F\ttask_id\ttask_code\ttask_name\ttarget_start_date\ttarget_end_date\tphys_complete_pct\n" +
"%R\t1001\tA1000\tExcavation\t2026-02-02 08:00:00\t2026-02-13 17:00:00\t0.25\n" +
"%R\t1002\tA1010\tFoundations\t2026-02-16\t2026-02-27\t0\n" +
"%R\t1003\tA1020\tBadRow\tnot-a-date\t2026-03-01\t0\n" +
"%T\tTASKPRED\n" +
"%F\ttask_pred_id\ttask_id\tpred_task_id\tpred_type\n" +
"%R\t5001\t1002\t1001\tPR_FS\n";

        [Fact]
        public void Xer_SecondsTolerantDates_AndWarnOnSkip()
        {
            var r = ScheduleImporter.ParseXer(Xer);
            Assert.Equal("xer", r.Source);
            Assert.Equal(2, r.Tasks.Count);                  // BadRow dropped
            Assert.Contains(r.Warnings, w => w.Contains("BadRow") || w.Contains("skipped"));
            var exc = r.Tasks.First(t => t.Name == "Excavation");
            Assert.Equal(2026, exc.Start.Year);
            Assert.Equal(25, exc.PercentComplete, 4);        // 0.25 fraction → 25
        }

        [Fact]
        public void Xer_Taskpred_WiresPredecessor()
        {
            var r = ScheduleImporter.ParseXer(Xer);
            var fnd = r.Tasks.First(t => t.Name == "Foundations");
            Assert.Single(fnd.Predecessors);
            Assert.Equal("1001", fnd.Predecessors[0].TaskId);
            Assert.Equal("FS", fnd.Predecessors[0].Type);
        }

        [Theory]
        [InlineData("PR_FS", "FS")]
        [InlineData("PR_SS", "SS")]
        [InlineData("PR_FF", "FF")]
        [InlineData("Finish_Start", "FS")]
        [InlineData("garbage", "FS")]
        public void RelType_Normalises(string raw, string expected)
            => Assert.Equal(expected, ScheduleImporter.NormaliseRelType(raw));

        [Theory]
        [InlineData("0.5", 50)]
        [InlineData("50", 50)]
        [InlineData("1", 100)]
        [InlineData("150", 100)]   // clamp
        [InlineData("", 0)]
        public void NormalisePercent_Worked(string raw, double expected)
            => Assert.Equal(expected, ScheduleImporter.NormalisePercent(raw), 4);
    }
}
