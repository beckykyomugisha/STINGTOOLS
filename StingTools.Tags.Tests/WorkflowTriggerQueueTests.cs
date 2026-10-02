using System;
using StingTools.Core;
using Xunit;
using D = StingTools.Core.WorkflowTriggerDrainPolicy.Decision;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-40 — the workflow trigger queue and the rule the Idling drain follows.
    /// The queue used to drain only when a document opened; a compliance-fall or
    /// warning-threshold trigger mid-session waited for the next open.
    /// </summary>
    public class WorkflowTriggerQueueTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Gap = WorkflowTriggerDrainPolicy.MinInterval;

        [Fact]
        public void Queue_IsFifo_AndCollapsesAPresetAlreadyWaiting()
        {
            var q = new WorkflowTriggerQueue();
            Assert.True(q.Enqueue("DailyQA"));
            Assert.True(q.Enqueue("MorningHealthCheck"));
            Assert.False(q.Enqueue("dailyqa"));          // same preset, still waiting
            Assert.False(q.Enqueue("  "));
            Assert.Equal(2, q.Count);
            Assert.Equal("DailyQA", q.TryDequeue());
            Assert.True(q.Enqueue("DailyQA"));            // no longer waiting: may queue again
            Assert.Equal("MorningHealthCheck", q.TryDequeue());
            Assert.Equal("DailyQA", q.TryDequeue());
            Assert.Null(q.TryDequeue());
            Assert.True(q.IsEmpty);
        }

        [Fact]
        public void Queue_Clear_EmptiesItAndForgetsPendingNames()
        {
            var q = new WorkflowTriggerQueue();
            q.Enqueue("A");
            q.Clear();
            Assert.True(q.IsEmpty);
            Assert.True(q.Enqueue("A"));
        }

        [Fact]
        public void NothingWaiting_IsIdle()
            => Assert.Equal(D.Idle, WorkflowTriggerDrainPolicy.Decide(false, false, false, null, T0, Gap));

        [Fact]
        public void FirstPreset_IsDispatched()
            => Assert.Equal(D.Dispatch, WorkflowTriggerDrainPolicy.Decide(true, false, false, null, T0, Gap));

        [Fact]
        public void APresetStillRunning_Waits()
            => Assert.Equal(D.Wait, WorkflowTriggerDrainPolicy.Decide(true, true, false, null, T0, Gap));

        [Fact]
        public void ReEntry_Waits()
            => Assert.Equal(D.Wait, WorkflowTriggerDrainPolicy.Decide(true, false, true, null, T0, Gap));

        [Fact]
        public void WithinTheInterval_Waits_AndAfterIt_Dispatches()
        {
            Assert.Equal(D.Wait, WorkflowTriggerDrainPolicy.Decide(true, false, false, T0, T0 + Gap - TimeSpan.FromSeconds(1), Gap));
            Assert.Equal(D.Dispatch, WorkflowTriggerDrainPolicy.Decide(true, false, false, T0, T0 + Gap, Gap));
        }

        [Fact]
        public void TheRateLimitIsNotZero()
            => Assert.True(WorkflowTriggerDrainPolicy.MinInterval >= TimeSpan.FromSeconds(10));
    }
}
