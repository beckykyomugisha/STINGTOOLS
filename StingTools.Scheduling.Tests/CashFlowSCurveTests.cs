using System;
using System.Linq;
using StingTools.Core.Schedule;
using Xunit;

namespace StingTools.Scheduling.Tests
{
    /// <summary>PM-3 — schedule-driven S-curve: per-task value time-phased over its
    /// own window, so front-loaded ≠ back-loaded (the old sigmoid bug).</summary>
    public class CashFlowSCurveTests
    {
        // Task 1 in Jan (value 100), Task 2 in Mar (value 100). Total 200.
        private static ScheduleModel TwoMonthGap()
        {
            var m = new ScheduleModel();
            m.Tasks.Add(new ScheduleTask
            {
                Id = 1, Name = "Jan work", CostLoadUGX = 100,
                Start = new DateTime(2026, 1, 1), End = new DateTime(2026, 1, 31),
                PercentComplete = 100,
            });
            m.Tasks.Add(new ScheduleTask
            {
                Id = 2, Name = "Mar work", CostLoadUGX = 100,
                Start = new DateTime(2026, 3, 1), End = new DateTime(2026, 3, 31),
                PercentComplete = 0,
            });
            return m;
        }

        [Fact]
        public void Curve_HasThreeMonths_TotalIsSumOfCostLoads()
        {
            var r = CashFlowSCurve.Build(TwoMonthGap(), bac: 0);
            Assert.Equal(200, r.TotalValue, 4);
            Assert.Equal(3, r.Points.Count);   // Jan, Feb, Mar
        }

        [Fact]
        public void FrontLoaded_DiffersFromBackLoaded()
        {
            // The whole point of PM-3: the curve follows the programme.
            var r = CashFlowSCurve.Build(TwoMonthGap(), bac: 0);
            Assert.Equal(100, r.Points[0].PlannedCumulative, 4);  // Jan: 100 placed
            Assert.Equal(100, r.Points[1].PlannedCumulative, 4);  // Feb: gap, still 100
            Assert.Equal(200, r.Points[2].PlannedCumulative, 4);  // Mar: +100 = 200
        }

        [Fact]
        public void PlannedValueAt_InterpolatesBetweenMonths()
        {
            var r = CashFlowSCurve.Build(TwoMonthGap(), bac: 0);
            // End of January → 100 placed; mid-February (gap month) → still 100.
            Assert.Equal(100, r.PlannedValueAt(new DateTime(2026, 1, 31)), 0);
            Assert.Equal(100, r.PlannedValueAt(new DateTime(2026, 2, 15)), 0);
            Assert.Equal(200, r.PlannedValueAt(new DateTime(2026, 3, 31)), 0);
            Assert.Equal(0, r.PlannedValueAt(new DateTime(2025, 12, 1)), 0);  // before start
        }

        [Fact]
        public void EarnedCurve_FollowsPercentComplete()
        {
            var r = CashFlowSCurve.Build(TwoMonthGap(), bac: 0);
            // Jan task 100% done = 100 earned; Mar task 0% = 0. Earned cum = 100.
            Assert.Equal(100, r.Points[^1].EarnedCumulative, 4);
        }

        [Fact]
        public void NoCostLoad_DerivesFromBacByDurationShare()
        {
            var m = new ScheduleModel();
            m.Tasks.Add(new ScheduleTask
            {
                Id = 1, Name = "Half", Start = new DateTime(2026, 1, 1), End = new DateTime(2026, 1, 31),
            });
            m.Tasks.Add(new ScheduleTask
            {
                Id = 2, Name = "Half2", Start = new DateTime(2026, 2, 1), End = new DateTime(2026, 2, 28),
            });
            var r = CashFlowSCurve.Build(m, bac: 1000);
            Assert.Equal(1000, r.TotalValue, 0);   // whole BAC spread across the two tasks
        }
    }
}
