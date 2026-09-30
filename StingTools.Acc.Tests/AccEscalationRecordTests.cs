// A6 — the escalation record: an unreadable pushed_clashes.json is NOT an empty one, and a
//      failed save is counted, not swallowed.
// A15 — the append-only origin record ACC_ImportIssues reads, which ACC_SyncIssueStatus
//       never prunes.
// A2 — the create-and-record loop both escalations share: record after EACH create, stop on
//      an auth failure, count what exists in ACC but could not be recorded.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccEscalationRecordTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _pushed;
        private readonly string _origins;
        private static readonly DateTime T0 = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        public AccEscalationRecordTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accesc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _pushed = Path.Combine(_dir, AccPushedMap.ClashFileName);
            _origins = Path.Combine(_dir, AccIssueOrigins.FileName);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        // ── A6: tri-state load ──────────────────────────────────────────────

        [Fact]
        public void Absent_file_is_an_empty_map_not_an_error()
        {
            var map = AccPushedMap.Load(_pushed, out string err);
            Assert.NotNull(map);
            Assert.Empty(map);
            Assert.Null(err);
        }

        [Fact]
        public void Readable_file_round_trips()
        {
            Assert.True(AccPushedMap.TrySave(_pushed, new Dictionary<string, string> { ["a|b"] = "issue-1", ["c|d"] = "issue-2" }, out _));
            var map = AccPushedMap.Load(_pushed, out string err);
            Assert.Null(err);
            Assert.Equal(2, map.Count);
            Assert.Equal("issue-1", map["a|b"]);
        }

        [Theory]
        [InlineData("{ this is not json")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("[\"a\",\"b\"]")]
        [InlineData("{\"a|b\": {\"id\": \"x\"}}")]
        public void Unreadable_file_is_null_with_a_reason_never_empty(string content)
        {
            File.WriteAllText(_pushed, content);
            var map = AccPushedMap.Load(_pushed, out string err);
            Assert.Null(map);   // NOT an empty map: that re-escalated every clash
            Assert.False(string.IsNullOrWhiteSpace(err));
            Assert.Equal(content, File.ReadAllText(_pushed));   // left untouched
        }

        [Fact]
        public void Failed_save_reports_the_reason()
        {
            // A directory where the file should be: the write cannot happen.
            Directory.CreateDirectory(_pushed);
            bool ok = AccPushedMap.TrySave(_pushed, new Dictionary<string, string> { ["x"] = "1" }, out string err);
            Assert.False(ok);
            Assert.False(string.IsNullOrWhiteSpace(err));
        }

        // ── A15: the origin record ───────────────────────────────────────────

        [Fact]
        public void Origin_record_survives_the_tracking_set_being_pruned()
        {
            var tracked = new Dictionary<string, string> { ["sig-1"] = "acc-1", ["sig-2"] = "acc-2" };
            var origins = AccIssueOrigins.Load(_origins, out _);
            Assert.Equal(2, origins.Absorb(AccIssueImport.ClashEscalationOrigin, tracked, T0));
            Assert.True(origins.TrySave(_origins, out _));

            // Sync closes sig-1: it leaves the tracking set...
            tracked.Remove("sig-1");
            Assert.True(AccPushedMap.TrySave(_pushed, tracked, out _));

            // ...and Import still recognises acc-1 as a STING clash escalation.
            var links = new Dictionary<string, AccOriginLink>();
            AccIssueOrigins.Load(_origins, out _).AddTo(links);
            AccOriginLink.AddSidecar(links, AccIssueImport.ClashEscalationOrigin, AccPushedMap.Load(_pushed, out _));
            Assert.Equal(2, links.Count);
            Assert.Equal("sig-1", links["acc-1"].Key);
            Assert.Equal(AccIssueImport.ClashEscalationOrigin, links["acc-1"].Origin);
        }

        [Fact]
        public void Origin_record_is_idempotent_and_keeps_two_origins_apart()
        {
            var o = new AccIssueOrigins();
            Assert.True(o.Record(AccIssueImport.ClashEscalationOrigin, "sig", "acc-1", T0));
            Assert.False(o.Record(AccIssueImport.ClashEscalationOrigin, "sig", "acc-1", T0.AddDays(1)));
            Assert.True(o.Record(AccIssueImport.LifecycleGapOrigin, "PRICED_UNSPECIFIED:42", "acc-2", T0));
            Assert.False(o.Record(AccIssueImport.LifecycleGapOrigin, "", "acc-3", T0));
            Assert.Equal(2, o.Entries.Count);

            var links = new Dictionary<string, AccOriginLink>();
            o.AddTo(links);
            Assert.Equal(AccIssueImport.LifecycleGapOrigin, links["acc-2"].Origin);
        }

        [Fact]
        public void Unreadable_origin_record_is_null_not_empty()
        {
            File.WriteAllText(_origins, "{ broken");
            Assert.Null(AccIssueOrigins.Load(_origins, out string err));
            Assert.False(string.IsNullOrWhiteSpace(err));
        }

        // ── the shared create-and-record loop ────────────────────────────────

        private sealed class Gap { public string Sig; public AccPushResult Answer; }

        private static AccPushResult Created(string id) => new AccPushResult { Id = id, Status = AccFetchStatus.Ok };
        private static AccPushResult Refused(AccFetchStatus st) => new AccPushResult { Status = st, Detail = st.ToString() };

        [Fact]
        public void Each_created_issue_is_saved_before_the_next_is_created()
        {
            var items = new[]
            {
                new Gap { Sig = "g1", Answer = Created("acc-1") },
                new Gap { Sig = "g2", Answer = Created("acc-2") },
            };
            var tracked = new Dictionary<string, string>(StringComparer.Ordinal);
            var savedCounts = new List<int>();
            var created = new List<string>();

            var outcome = AccIssueCreateLoop.Run(items, g => g.Sig, g => g.Sig,
                g => { created.Add(g.Sig); Assert.Equal(created.Count - 1, savedCounts.Count); return g.Answer; },
                tracked, m => { savedCounts.Add(m.Count); return null; },
                new AccIssueOrigins(), AccIssueImport.LifecycleGapOrigin, o => null, T0);

            Assert.Equal(2, outcome.Created);
            Assert.Equal(new[] { 1, 2 }, savedCounts);   // one save per created issue, in order
            Assert.False(outcome.HasProblems);
        }

        [Fact]
        public void Auth_failure_stops_the_run_and_counts_what_was_not_attempted()
        {
            var items = new[]
            {
                new Gap { Sig = "g1", Answer = Created("acc-1") },
                new Gap { Sig = "g2", Answer = Refused(AccFetchStatus.AuthFailed) },
                new Gap { Sig = "g3", Answer = Created("acc-3") },
                new Gap { Sig = "g4", Answer = Created("acc-4") },
            };
            int calls = 0;
            var outcome = AccIssueCreateLoop.Run(items, g => g.Sig, g => g.Sig,
                g => { calls++; return g.Answer; },
                new Dictionary<string, string>(), m => null, null, AccIssueImport.LifecycleGapOrigin, o => null, T0);

            Assert.Equal(2, calls);
            Assert.Equal(1, outcome.Created);
            Assert.Equal(1, outcome.Failed);
            Assert.True(outcome.StoppedOnAuth);
            Assert.Equal(2, outcome.NotAttempted);
            Assert.True(outcome.HasProblems);
        }

        [Fact]
        public void A_non_auth_failure_is_counted_and_the_run_continues()
        {
            var items = new[]
            {
                new Gap { Sig = "g1", Answer = Refused(AccFetchStatus.TransportFailed) },
                new Gap { Sig = "g2", Answer = Created("acc-2") },
            };
            var outcome = AccIssueCreateLoop.Run(items, g => g.Sig, g => g.Sig, g => g.Answer,
                new Dictionary<string, string>(), m => null, null, "x", o => null, T0);
            Assert.Equal(1, outcome.Created);
            Assert.Equal(1, outcome.Failed);
            Assert.False(outcome.StoppedOnAuth);
            Assert.True(outcome.HasProblems);
        }

        [Fact]
        public void An_issue_created_but_not_recorded_is_counted_and_said_loudly()
        {
            var items = new[] { new Gap { Sig = "g1", Answer = Created("acc-1") } };
            var outcome = AccIssueCreateLoop.Run(items, g => g.Sig, g => g.Sig, g => g.Answer,
                new Dictionary<string, string>(), m => "disk full", new AccIssueOrigins(), "x", o => null, T0);
            Assert.Equal(1, outcome.Created);
            Assert.Equal(1, outcome.NotRecorded);
            Assert.True(outcome.HasProblems);
            Assert.Contains("NOT RECORDED", outcome.Describe());
            Assert.Contains("acc-1", outcome.Describe());
        }

        [Fact]
        public void Already_tracked_items_are_skipped_without_a_create()
        {
            var items = new[] { new Gap { Sig = "g1", Answer = Created("new") } };
            var tracked = new Dictionary<string, string> { ["g1"] = "acc-old" };
            int calls = 0;
            var outcome = AccIssueCreateLoop.Run(items, g => g.Sig, g => g.Sig, g => { calls++; return g.Answer; },
                tracked, m => null, null, "x", o => null, T0);
            Assert.Equal(0, calls);
            Assert.Equal(1, outcome.Skipped);
            Assert.Equal("acc-old", tracked["g1"]);
        }
    }
}
