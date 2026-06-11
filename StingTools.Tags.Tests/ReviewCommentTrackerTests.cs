using System;
using System.Collections.Generic;
using StingTools.Docs;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the pure upsert / status-normalisation / close-out-rate / KPI logic
    /// of the Bluebeam comment tracker (CSV/XLSX IO + dashboard live in
    /// ReviewCommentCommands and are not under test here).
    /// </summary>
    public class ReviewCommentTrackerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ReviewComment C(string id, ReviewStatus status, string gate = "Deliverable B (50%)")
            => new ReviewComment { SessionId = "S1", CommentId = id, Status = status, Gate = gate };

        // ── NormalizeStatus ─────────────────────────────────────────────
        [Theory]
        [InlineData("", ReviewStatus.Open)]
        [InlineData(null, ReviewStatus.Open)]
        [InlineData("None", ReviewStatus.Open)]
        [InlineData("Completed", ReviewStatus.Closed)]
        [InlineData("Accepted", ReviewStatus.Closed)]
        [InlineData("Rejected", ReviewStatus.Closed)]
        [InlineData("Replied", ReviewStatus.Answered)]
        [InlineData("Resolved - pending owner", ReviewStatus.ResolvedPendingOwner)]
        public void NormalizeStatus_maps_bluebeam_states(string raw, ReviewStatus expected)
            => Assert.Equal(expected, ReviewCommentTracker.NormalizeStatus(raw));

        // ── Upsert ──────────────────────────────────────────────────────
        [Fact]
        public void Upsert_adds_new_and_sets_first_and_last_seen()
        {
            var existing = new List<ReviewComment>();
            var (added, updated) = ReviewCommentTracker.Upsert(existing, new[] { C("1", ReviewStatus.Open) }, T0);
            Assert.Equal(1, added);
            Assert.Equal(0, updated);
            Assert.Equal(T0, existing[0].FirstSeenUtc);
            Assert.Equal(T0, existing[0].LastSeenUtc);
        }

        [Fact]
        public void Upsert_reimport_keeps_first_seen_refreshes_last_seen_and_status()
        {
            var existing = new List<ReviewComment>();
            ReviewCommentTracker.Upsert(existing, new[] { C("1", ReviewStatus.Open) }, T0);
            var t1 = T0.AddDays(10);
            var (added, updated) = ReviewCommentTracker.Upsert(existing, new[] { C("1", ReviewStatus.Closed) }, t1);
            Assert.Equal(0, added);
            Assert.Equal(1, updated);
            Assert.Single(existing);
            Assert.Equal(T0, existing[0].FirstSeenUtc);   // preserved
            Assert.Equal(t1, existing[0].LastSeenUtc);     // refreshed
            Assert.Equal(ReviewStatus.Closed, existing[0].Status);
        }

        [Fact]
        public void Upsert_preserves_assigned_owner_when_import_has_none()
        {
            var existing = new List<ReviewComment>
            {
                new ReviewComment { SessionId = "S1", CommentId = "1", Owner = "Becky", FirstSeenUtc = T0, LastSeenUtc = T0 }
            };
            ReviewCommentTracker.Upsert(existing, new[] { C("1", ReviewStatus.Answered) }, T0.AddDays(1));
            Assert.Equal("Becky", existing[0].Owner);
        }

        // ── Close-out rate ──────────────────────────────────────────────
        [Fact]
        public void CloseOutRate_counts_closed_over_total()
        {
            var list = new List<ReviewComment>
            {
                C("1", ReviewStatus.Closed), C("2", ReviewStatus.Closed),
                C("3", ReviewStatus.Open), C("4", ReviewStatus.Answered),
            };
            Assert.Equal(50.0, ReviewCommentTracker.CloseOutRate(list));
        }

        [Fact]
        public void CloseOutRate_empty_is_100()
            => Assert.Equal(100.0, ReviewCommentTracker.CloseOutRate(new List<ReviewComment>()));

        // ── Age ─────────────────────────────────────────────────────────
        [Fact]
        public void AgeDays_open_uses_now_closed_uses_last_seen()
        {
            var open = new ReviewComment { Status = ReviewStatus.Open, FirstSeenUtc = T0, LastSeenUtc = T0 };
            Assert.Equal(20.0, ReviewCommentTracker.AgeDays(open, T0.AddDays(20)));

            var closed = new ReviewComment { Status = ReviewStatus.Closed, FirstSeenUtc = T0, LastSeenUtc = T0.AddDays(5) };
            Assert.Equal(5.0, ReviewCommentTracker.AgeDays(closed, T0.AddDays(20)));
        }

        // ── KPI + overdue ───────────────────────────────────────────────
        [Fact]
        public void BuildKpi_groups_by_gate_with_all_row_and_overdue()
        {
            var list = new List<ReviewComment>
            {
                new ReviewComment { SessionId="S", CommentId="1", Gate="A", Status=ReviewStatus.Open,   FirstSeenUtc=T0,           LastSeenUtc=T0 },
                new ReviewComment { SessionId="S", CommentId="2", Gate="A", Status=ReviewStatus.Closed, FirstSeenUtc=T0,           LastSeenUtc=T0.AddDays(3) },
                new ReviewComment { SessionId="S", CommentId="3", Gate="B", Status=ReviewStatus.Open,   FirstSeenUtc=T0,           LastSeenUtc=T0 },
            };
            var now = T0.AddDays(20);
            var kpi = ReviewCommentTracker.BuildKpi(list, now, overdueSlaDays: 14);

            var gateA = kpi.Find(k => k.Gate == "A");
            Assert.Equal(2, gateA.Total);
            Assert.Equal(1, gateA.Closed);
            Assert.Equal(50.0, gateA.CloseOutPct);
            Assert.Equal(1, gateA.Overdue);  // comment 1 open 20d > 14

            var all = kpi.Find(k => k.Gate == "ALL");
            Assert.Equal(3, all.Total);
            Assert.Equal(1, all.Closed);
            Assert.Equal(2, all.Overdue);    // comments 1 and 3 open 20d
        }
    }
}
