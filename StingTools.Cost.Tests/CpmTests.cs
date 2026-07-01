using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Scheduling;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>PM-4 — CPM forward/backward pass → critical path + total/free float.</summary>
    public class CpmTests
    {
        // Classic diamond:  A(3) → B(4) → D(5);  A → C(2) → D.
        // Critical path A-B-D = 12 days; C carries 2 days of float.
        private static CpmResult SolveDiamond()
        {
            var tasks = new List<CpmTask>
            {
                new CpmTask { Id = "A", DurationDays = 3 },
                new CpmTask { Id = "B", DurationDays = 4, PredecessorIds = { "A" } },
                new CpmTask { Id = "C", DurationDays = 2, PredecessorIds = { "A" } },
                new CpmTask { Id = "D", DurationDays = 5, PredecessorIds = { "B", "C" } },
            };
            return CpmEngine.Solve(tasks);
        }

        [Fact]
        public void ProjectDuration_IsLongestPath()
        {
            Assert.Equal(12, SolveDiamond().ProjectDurationDays, 4);
        }

        [Fact]
        public void EarlyDates_FromForwardPass()
        {
            var r = SolveDiamond();
            var t = r.Tasks.ToDictionary(x => x.Id);
            Assert.Equal(0, t["A"].EarlyStart, 4);
            Assert.Equal(3, t["A"].EarlyFinish, 4);
            Assert.Equal(3, t["B"].EarlyStart, 4);
            Assert.Equal(7, t["B"].EarlyFinish, 4);
            Assert.Equal(7, t["D"].EarlyStart, 4);   // max(EF B=7, EF C=5)
            Assert.Equal(12, t["D"].EarlyFinish, 4);
        }

        [Fact]
        public void CriticalPath_IsA_B_D()
        {
            var r = SolveDiamond();
            Assert.Equal(new[] { "A", "B", "D" }, r.CriticalPath.ToArray());
            var t = r.Tasks.ToDictionary(x => x.Id);
            Assert.True(t["A"].IsCritical);
            Assert.True(t["B"].IsCritical);
            Assert.True(t["D"].IsCritical);
            Assert.False(t["C"].IsCritical);
        }

        [Fact]
        public void Float_OnNonCriticalTask()
        {
            var t = SolveDiamond().Tasks.First(x => x.Id == "C");
            Assert.Equal(2, t.TotalFloat, 4);   // LS 5 − ES 3
            Assert.Equal(2, t.FreeFloat, 4);    // ES(D) 7 − EF(C) 5
        }

        [Fact]
        public void CriticalTasks_HaveZeroFloat()
        {
            foreach (var t in SolveDiamond().Tasks.Where(x => x.IsCritical))
                Assert.Equal(0, t.TotalFloat, 4);
        }

        [Fact]
        public void Cycle_IsDetected_NotInfiniteLoop()
        {
            var tasks = new List<CpmTask>
            {
                new CpmTask { Id = "X", DurationDays = 1, PredecessorIds = { "Y" } },
                new CpmTask { Id = "Y", DurationDays = 1, PredecessorIds = { "X" } },
            };
            var r = CpmEngine.Solve(tasks);
            Assert.True(r.HasCycle);
            Assert.NotEmpty(r.Warnings);
        }

        [Fact]
        public void Empty_ReturnsEmptyResult()
        {
            var r = CpmEngine.Solve(new List<CpmTask>());
            Assert.Equal(0, r.ProjectDurationDays);
            Assert.Empty(r.CriticalPath);
        }
    }
}
