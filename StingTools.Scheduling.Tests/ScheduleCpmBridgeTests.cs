using System;
using System.Linq;
using StingTools.Core.Schedule;
using Xunit;

namespace StingTools.Scheduling.Tests
{
    /// <summary>PM-4 — the bridge from the unified ScheduleModel into CpmEngine,
    /// in working days, projecting float/critical back.</summary>
    public class ScheduleCpmBridgeTests
    {
        // Two-task chain on working days: A (Mon-Fri = 5wd) FS→ B (next Mon-Fri = 5wd).
        private static ScheduleModel Chain()
        {
            var m = new ScheduleModel();
            m.Tasks.Add(new ScheduleTask
            {
                Id = 1, MsUid = "1", Name = "A",
                Start = new DateTime(2026, 2, 2), End = new DateTime(2026, 2, 6),   // Mon-Fri
            });
            m.Tasks.Add(new ScheduleTask
            {
                Id = 2, MsUid = "2", Name = "B",
                Start = new DateTime(2026, 2, 9), End = new DateTime(2026, 2, 13),  // next Mon-Fri
                Predecessors = { new SchedulePredecessor { TaskId = "1", Type = "FS" } },
            });
            return m;
        }

        [Fact]
        public void Chain_BothCritical_ProjectIs10WorkingDays()
        {
            var r = ScheduleCpmBridge.Solve(Chain());
            Assert.Equal(10, r.ProjectDurationWorkingDays, 4);
            Assert.True(r.ByTask[1].IsCritical);
            Assert.True(r.ByTask[2].IsCritical);
            Assert.Equal(new[] { 1, 2 }, r.CriticalPath.ToArray());
        }

        [Fact]
        public void NonCriticalParallelTask_HasFloat()
        {
            var m = Chain();
            // C runs in parallel with A but is shorter (2wd), then also feeds B.
            m.Tasks.Add(new ScheduleTask
            {
                Id = 3, MsUid = "3", Name = "C",
                Start = new DateTime(2026, 2, 2), End = new DateTime(2026, 2, 3),  // Mon-Tue = 2wd
            });
            m.Tasks[1].Predecessors.Add(new SchedulePredecessor { TaskId = "3", Type = "FS" });
            var r = ScheduleCpmBridge.Solve(m);
            // A is 5wd, C is 2wd, both feed B → C carries 3 working days of float.
            Assert.Equal(3, r.ByTask[3].TotalFloatDays, 4);
            Assert.False(r.ByTask[3].IsCritical);
        }

        [Fact]
        public void NonFsLink_IsWarnedNotSilent()
        {
            var m = Chain();
            m.Tasks[1].Predecessors[0].Type = "SS";
            var r = ScheduleCpmBridge.Solve(m);
            Assert.Contains(r.Warnings, w => w.Contains("SS"));
        }

        [Fact]
        public void Empty_ReturnsEmpty()
        {
            var r = ScheduleCpmBridge.Solve(new ScheduleModel());
            Assert.Empty(r.ByTask);
        }
    }
}
