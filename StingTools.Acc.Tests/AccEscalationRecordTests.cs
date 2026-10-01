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

        // ── E1: closed in ACC while the clash persists is HELD, not re-raised ──────

        private static readonly Func<string, bool> Closed = AccIssueSync.IsClosedStatus;

        [Theory]
        [InlineData("closed")]
        [InlineData("void")]
        [InlineData("not_an_issue")]
        public void Closed_in_acc_while_still_clashing_is_held_not_plainly_untracked(string status)
        {
            var pushed = new Dictionary<string, string> { ["a|b"] = "i1" };
            var st = new Dictionary<string, string> { ["i1"] = status };
            var present = new HashSet<string> { "a|b" };
            var d = Assert.Single(AccEscalationReconcile.Decide(pushed, st, Closed, present));
            Assert.Equal(AccEscalationAction.HoldClosedStillClashing, d.Action);
            Assert.True(d.RemovesFromTracking);
        }

        [Fact]
        public void Closed_in_acc_and_absent_from_a_complete_pull_is_resolved()
        {
            var d = Assert.Single(AccEscalationReconcile.Decide(
                new Dictionary<string, string> { ["a|b"] = "i1" },
                new Dictionary<string, string> { ["i1"] = "closed" }, Closed, new HashSet<string> { "x|y" }));
            Assert.Equal(AccEscalationAction.Untrack, d.Action);
        }

        [Fact]
        public void With_no_complete_pull_known_a_closed_escalation_is_held_never_assumed_gone()
        {
            var d = Assert.Single(AccEscalationReconcile.Decide(
                new Dictionary<string, string> { ["a|b"] = "i1" },
                new Dictionary<string, string> { ["i1"] = "closed" }, Closed, null));
            Assert.Equal(AccEscalationAction.HoldClosedStillClashing, d.Action);
        }

        [Fact]
        public void An_issue_deleted_in_acc_is_untracked_and_named_not_kept_forever()
        {
            var d = Assert.Single(AccEscalationReconcile.Decide(
                new Dictionary<string, string> { ["a|b"] = "gone" },
                new Dictionary<string, string> { ["i1"] = "open" }, Closed, new HashSet<string> { "a|b" }));
            Assert.Equal(AccEscalationAction.UntrackNotFound, d.Action);
            Assert.Equal(AccEscalationReconcile.NotFoundStatus, d.Status);
            Assert.True(d.RemovesFromTracking);
        }

        [Fact]
        public void An_open_escalation_stays_tracked()
        {
            var d = Assert.Single(AccEscalationReconcile.Decide(
                new Dictionary<string, string> { ["a|b"] = "i1" },
                new Dictionary<string, string> { ["i1"] = "open" }, Closed, new HashSet<string>()));
            Assert.Equal(AccEscalationAction.Keep, d.Action);
            Assert.False(d.RemovesFromTracking);
        }

        [Fact]
        public void A_hold_survives_save_and_load_and_is_released_only_when_the_clash_is_gone()
        {
            var o = new AccIssueOrigins();
            Assert.True(o.Hold(AccIssueImport.ClashEscalationOrigin, "a|b", "i1", "void", T0));
            Assert.False(o.Hold(AccIssueImport.ClashEscalationOrigin, "a|b", "i1", "void", T0));
            Assert.True(o.TrySave(_origins, out _));
            var back = AccIssueOrigins.Load(_origins, out string err);
            Assert.Null(err);
            Assert.Contains("a|b", back.HeldKeys(AccIssueImport.ClashEscalationOrigin));

            // still present -> nothing released; null (no complete pull) -> nothing released
            Assert.Empty(back.ReleaseAbsent(AccIssueImport.ClashEscalationOrigin, new HashSet<string> { "a|b" }));
            Assert.Empty(back.ReleaseAbsent(AccIssueImport.ClashEscalationOrigin, null));
            Assert.Contains("a|b", back.HeldKeys(AccIssueImport.ClashEscalationOrigin));

            // gone from a complete pull -> released, so a later recurrence escalates again
            Assert.Equal(new[] { "a|b" }, back.ReleaseAbsent(AccIssueImport.ClashEscalationOrigin, new HashSet<string>()));
            Assert.Empty(back.HeldKeys(AccIssueImport.ClashEscalationOrigin));
        }

        [Fact]
        public void An_origin_file_written_before_E1_loads_with_no_holds()
        {
            File.WriteAllText(_origins, "{\"entries\":[{\"origin\":\"clash\",\"key\":\"a|b\",\"issueId\":\"i1\",\"recordedUtc\":\"2026-01-01T00:00:00Z\"}]}");
            var o = AccIssueOrigins.Load(_origins, out string err);
            Assert.Null(err);
            Assert.Single(o.Entries);
            Assert.Empty(o.HeldKeys(AccIssueImport.ClashEscalationOrigin));
        }

        // F1: a pull whose clashes have no document names has no signatures; recording it as a
        // complete, empty pull released every "closed in ACC" hold and re-escalated them all.
        [Theory]
        [InlineData(false, 0, true)]
        [InlineData(true, 0, false)]     // truncated
        [InlineData(false, 3, false)]    // live clashes with no key: presence unknown
        [InlineData(true, 3, false)]
        public void Only_an_untruncated_fully_keyed_pull_is_complete_evidence(bool truncated, int unkeyed, bool expected)
        {
            bool ok = AccClashPresence.IsCompleteEvidence(truncated, unkeyed, out string why);
            Assert.Equal(expected, ok);
            Assert.Equal(expected, why == null);
            if (unkeyed > 0 && !truncated) Assert.Contains("no document names", why);
        }

        [Fact]
        public void An_unkeyed_pull_is_not_recorded_so_a_held_clash_stays_held()
        {
            // The scenario end to end, on the Revit-free parts: a hold exists, a pull arrives
            // with only unkeyed clashes. It is not complete evidence, so nothing is recorded
            // and Present() still contains the held clash from the last complete pull.
            var p = new AccClashPresence();
            p.Record("set-1", "Arch+MEP", new[] { "held|sig" }, T0);
            bool complete = AccClashPresence.IsCompleteEvidence(truncated: false, liveWithoutSignature: 2, out _);
            if (complete) p.Record("set-1", "Arch+MEP", new string[0], T0.AddDays(1));
            Assert.Contains("held|sig", p.Present());
        }

        [Fact]
        public void Clash_presence_is_tri_state_and_unions_model_sets()
        {
            string path = Path.Combine(_dir, AccClashPresence.FileName);
            var p = AccClashPresence.Load(path, out string err);
            Assert.Null(err);
            Assert.Null(p.Present());   // nothing recorded is "unknown", not "nothing present"

            p.Record("set-1", "Arch+MEP", new[] { "a|b" }, T0);
            p.Record("set-2", "Str+MEP", new[] { "c|d" }, T0);
            Assert.True(p.TrySave(path, out _));
            var back = AccClashPresence.Load(path, out err);
            Assert.Null(err);
            Assert.Equal(new HashSet<string> { "a|b", "c|d" }, back.Present());

            back.Record("set-1", "Arch+MEP", new string[0], T0);   // a complete empty pull of set-1
            Assert.Equal(new HashSet<string> { "c|d" }, back.Present());

            File.WriteAllText(path, "{ not json");
            Assert.Null(AccClashPresence.Load(path, out err));
            Assert.NotNull(err);
        }
    }
}
