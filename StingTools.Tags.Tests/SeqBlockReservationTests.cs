using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers server-side block reservation (R3) — the mechanism that closes the
    /// cross-host duplicate window between Revit's <see cref="SeqAssigner"/> and
    /// StingBridge.
    ///
    /// The property under test is NOT "the numbers look right"; it is that a
    /// reserved block is consumed exactly once and never invented. The
    /// adversarial cases are therefore the boundaries: a spent block, an
    /// unreserved key, and a partially-reserved run — all three must fall back
    /// to local allocation rather than reuse a number, because reuse is exactly
    /// the duplicate this exists to prevent.
    /// </summary>
    public class SeqBlockReservationTests
    {
        private const string Body = "M-BLD1-Z01-L01-HVAC-SUP-AHU-";
        private const string Key = "M_HVAC_L01";

        private static SeqResult Assign(
            Dictionary<string, int> counters, SeqBlockReservation res)
            => SeqAssigner.AssignNext(
                Key, counters, Body, "", SeqScheme.Numeric, pad: 4,
                seqSchemeContext: "", maxCollisionDepth: 50,
                existingTags: new HashSet<string>(), reservation: res);

        // ── the reservation container itself ────────────────────────────
        [Fact]
        public void TryTake_issues_each_number_once_then_reports_exhausted()
        {
            var res = new SeqBlockReservation();
            res.Add(Key, 41, 43);
            Assert.Equal(3, res.Remaining(Key));

            Assert.True(res.TryTake(Key, out var a));
            Assert.True(res.TryTake(Key, out var b));
            Assert.True(res.TryTake(Key, out var c));
            Assert.Equal(new[] { 41, 42, 43 }, new[] { a, b, c });

            Assert.False(res.TryTake(Key, out _));   // spent, not wrapped
            Assert.Equal(0, res.Remaining(Key));
        }

        [Fact]
        public void TryTake_on_unreserved_key_reports_false_rather_than_inventing()
        {
            var res = new SeqBlockReservation();
            res.Add(Key, 1, 5);
            Assert.False(res.TryTake("SOME_OTHER_KEY", out var n));
            Assert.Equal(0, n);
        }

        [Fact]
        public void Add_ignores_an_empty_grant()
        {
            var res = new SeqBlockReservation();
            res.Add(Key, 10, 9);                     // end < start
            Assert.Equal(0, res.Remaining(Key));
            Assert.False(res.TryTake(Key, out _));
        }

        // ── integration with AssignNext ─────────────────────────────────
        [Fact]
        public void AssignNext_uses_the_reserved_block_not_the_local_counter()
        {
            // Local counter is at 7; the server granted 41..42. The server wins —
            // otherwise two hosts that both reached 7 locally would both mint 8.
            var counters = new Dictionary<string, int> { [Key] = 7 };
            var res = new SeqBlockReservation();
            res.Add(Key, 41, 42);

            var r1 = Assign(counters, res);
            var r2 = Assign(counters, res);

            Assert.True(r1.Success);
            Assert.True(r2.Success);
            Assert.Equal("0041", r1.Seq);
            Assert.Equal("0042", r2.Seq);
            // Local counter tracks what was actually used, so the later
            // /seq/sync max-merge reports an honest high-water mark.
            Assert.Equal(42, counters[Key]);
        }

        [Fact]
        public void AssignNext_falls_back_to_local_when_the_block_is_spent()
        {
            // The adversarial case: a run longer than the block it reserved.
            // Element 2 must still get a number — degrade, don't fail — and it
            // must continue from the block, not restart at the stale local value.
            var counters = new Dictionary<string, int> { [Key] = 7 };
            var res = new SeqBlockReservation();
            res.Add(Key, 41, 41);                    // block of exactly one

            var fromBlock = Assign(counters, res);
            var fromLocal = Assign(counters, res);

            Assert.Equal("0041", fromBlock.Seq);
            Assert.Equal("0042", fromLocal.Seq);     // 41 + 1, not 7 + 1
        }

        [Fact]
        public void AssignNext_with_null_reservation_is_todays_local_behaviour()
        {
            // The offline path. Passing null must be byte-identical to the
            // pre-R3 call, so an unconfigured or unreachable server changes
            // nothing about how existing projects number.
            var withNull = new Dictionary<string, int> { [Key] = 7 };
            var legacy = new Dictionary<string, int> { [Key] = 7 };

            var a = Assign(withNull, null);
            var b = SeqAssigner.AssignNext(
                Key, legacy, Body, "", SeqScheme.Numeric, pad: 4,
                seqSchemeContext: "", maxCollisionDepth: 50,
                existingTags: new HashSet<string>());

            Assert.Equal(b.Seq, a.Seq);
            Assert.Equal("0008", a.Seq);
            Assert.Equal(legacy[Key], withNull[Key]);
        }

        [Fact]
        public void Two_hosts_drawing_from_disjoint_blocks_never_collide()
        {
            // The whole point, stated as a test: the server hands host A 1..3 and
            // host B 4..6 for the SAME key. Both allocate from a local counter
            // that starts at the same stale value — the pre-R3 setup that
            // produced duplicates — and must still produce disjoint numbers.
            var hostA = new Dictionary<string, int> { [Key] = 0 };
            var hostB = new Dictionary<string, int> { [Key] = 0 };

            var resA = new SeqBlockReservation(); resA.Add(Key, 1, 3);
            var resB = new SeqBlockReservation(); resB.Add(Key, 4, 6);

            var seqs = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                seqs.Add(Assign(hostA, resA).Seq);
                seqs.Add(Assign(hostB, resB).Seq);
            }

            Assert.Equal(6, seqs.Count);
            Assert.Equal(6, new HashSet<string>(seqs).Count);   // all distinct
        }
    }
}
