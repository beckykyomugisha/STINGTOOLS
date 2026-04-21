using System;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ClashHistoryTests
    {
        [Fact]
        public void NewClash_Stamped_FirstSeen_And_State()
        {
            var run = new ClashRunRecord();
            run.Clashes.Add(new ClashRecord { Identity = "abc" });

            ClashHistory.MergeWithPrior(run, null);

            Assert.Equal("New", run.Clashes[0].State);
            Assert.Equal(1, run.Stats.New);
            Assert.Equal(0, run.Stats.Active);
        }

        [Fact]
        public void MatchingClash_Inherits_FirstSeen_And_Id()
        {
            var oldRun = new ClashRunRecord();
            oldRun.Clashes.Add(new ClashRecord
            {
                Identity = "abc",
                Id = "CLH-20260101-00001",
                FirstSeenUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                State = "Active",
            });

            var newRun = new ClashRunRecord();
            newRun.Clashes.Add(new ClashRecord { Identity = "abc" });

            ClashHistory.MergeWithPrior(newRun, oldRun);

            Assert.Equal("CLH-20260101-00001", newRun.Clashes[0].Id);
            Assert.Equal(oldRun.Clashes[0].FirstSeenUtc, newRun.Clashes[0].FirstSeenUtc);
            Assert.Equal("Active", newRun.Clashes[0].State);
            Assert.Equal(1, newRun.Stats.Active);
            Assert.Equal(0, newRun.Stats.New);
        }

        [Fact]
        public void ResolvedClash_Reappearing_Marked_Reintroduced()
        {
            var oldRun = new ClashRunRecord();
            oldRun.Clashes.Add(new ClashRecord
            {
                Identity = "abc", Id = "CLH-1", State = "Resolved",
                FirstSeenUtc = DateTime.UtcNow.AddDays(-10),
                LastSeenUtc = DateTime.UtcNow.AddDays(-5),
            });

            var newRun = new ClashRunRecord();
            newRun.Clashes.Add(new ClashRecord { Identity = "abc" });

            ClashHistory.MergeWithPrior(newRun, oldRun);

            Assert.Equal("Reintroduced", newRun.Clashes[0].State);
            Assert.Equal(1, newRun.Stats.Reintroduced);
        }

        [Fact]
        public void DisappearedClash_Counted_Resolved()
        {
            var oldRun = new ClashRunRecord();
            oldRun.Clashes.Add(new ClashRecord { Identity = "xyz", Id = "CLH-1", State = "Active" });
            var newRun = new ClashRunRecord();  // does NOT include "xyz"

            ClashHistory.MergeWithPrior(newRun, oldRun);

            Assert.Equal(1, newRun.Stats.Resolved);
        }
    }
}
