using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the SEQ counter key, SEQ string formatting, the pad-capacity
    /// overflow cap, and the collision auto-increment loop extracted from
    /// TagConfig.BuildAndWriteTag into the Revit-free <see cref="SeqAssigner"/>.
    /// </summary>
    public class SeqAssignerTests
    {
        private const string Body = "M-BLD1-Z01-L01-HVAC-SUP-AHU-";
        private const string Suffix = "";

        // ── MaxSeqForPad ────────────────────────────────────────────────
        [Theory]
        [InlineData(1, 9)]
        [InlineData(2, 99)]
        [InlineData(3, 999)]
        [InlineData(4, 9999)]
        [InlineData(5, 99999)]
        [InlineData(6, 999999)]
        public void MaxSeqForPad_matches_digit_capacity(int pad, int expected)
            => Assert.Equal(expected, SeqAssigner.MaxSeqForPad(pad));

        // ── BuildSeqKey ─────────────────────────────────────────────────
        [Fact]
        public void BuildSeqKey_without_zone()
            => Assert.Equal("M_HVAC_L01", SeqAssigner.BuildSeqKey("M", "HVAC", "L01", "Z01", includeZone: false));

        [Fact]
        public void BuildSeqKey_with_zone()
            => Assert.Equal("M_Z01_HVAC_L01", SeqAssigner.BuildSeqKey("M", "HVAC", "L01", "Z01", includeZone: true));

        [Theory]
        [InlineData("", "", "", "A_GEN_L00")]          // all empty → defaults
        [InlineData(null, null, "XX", "A_GEN_L00")]    // null + XX level → L00
        public void BuildSeqKey_normalises_empty_tokens(string disc, string sys, string lvl, string expected)
            => Assert.Equal(expected, SeqAssigner.BuildSeqKey(disc, sys, lvl, null, includeZone: false));

        [Theory]
        [InlineData("XX")]
        [InlineData("ZZ")]
        [InlineData("")]
        [InlineData(null)]
        public void BuildSeqKey_normalises_placeholder_zone_to_Z01(string zone)
            => Assert.Equal("M_Z01_HVAC_L01", SeqAssigner.BuildSeqKey("M", "HVAC", "L01", zone, includeZone: true));

        // ── BuildSeqString ──────────────────────────────────────────────
        [Theory]
        [InlineData(42, 4, "0042")]
        [InlineData(1, 4, "0001")]
        [InlineData(9999, 4, "9999")]
        [InlineData(7, 2, "07")]
        public void BuildSeqString_numeric_pads(int n, int pad, string expected)
            => Assert.Equal(expected, SeqAssigner.BuildSeqString(n, SeqScheme.Numeric, pad));

        [Fact]
        public void BuildSeqString_defaults_pad_when_non_positive()
            => Assert.Equal("0042", SeqAssigner.BuildSeqString(42, SeqScheme.Numeric, 0));

        [Theory]
        [InlineData(1, "A")]
        [InlineData(26, "Z")]
        [InlineData(27, "AA")]
        [InlineData(52, "AZ")]
        [InlineData(0, "A")]    // n<=0 floor
        [InlineData(-5, "A")]
        public void ToAlpha_and_AlphaScheme(int n, string expected)
        {
            Assert.Equal(expected, SeqAssigner.ToAlpha(n));
            Assert.Equal(expected, SeqAssigner.BuildSeqString(n, SeqScheme.Alpha, 4));
        }

        [Fact]
        public void BuildSeqString_zone_prefix_uses_first_two_chars()
            => Assert.Equal("Z0-0042", SeqAssigner.BuildSeqString(42, SeqScheme.ZonePrefix, 4, "Z01"));

        [Fact]
        public void BuildSeqString_zone_prefix_fallback_when_context_short()
            => Assert.Equal("Z1-0042", SeqAssigner.BuildSeqString(42, SeqScheme.ZonePrefix, 4, "X"));

        [Fact]
        public void BuildSeqString_disc_prefix()
            => Assert.Equal("M-0042", SeqAssigner.BuildSeqString(42, SeqScheme.DiscPrefix, 4, "M"));

        // ── AssignNext: basic allocation ────────────────────────────────
        [Fact]
        public void AssignNext_first_allocation_starts_at_one()
        {
            var counters = new Dictionary<string, int>();
            var r = SeqAssigner.AssignNext("M_HVAC_L01", counters, Body, Suffix,
                SeqScheme.Numeric, 4, "", 10000, existingTags: null);

            Assert.True(r.Success);
            Assert.Equal("0001", r.Seq);
            Assert.Equal(Body + "0001", r.Tag);
            Assert.Equal(0, r.CollisionCount);
            Assert.Equal(1, counters["M_HVAC_L01"]);
        }

        [Fact]
        public void AssignNext_is_contiguous_across_calls()
        {
            var counters = new Dictionary<string, int>();
            var tags = new HashSet<string>();
            for (int i = 1; i <= 5; i++)
            {
                var r = SeqAssigner.AssignNext("M_HVAC_L01", counters, Body, Suffix,
                    SeqScheme.Numeric, 4, "", 10000, tags);
                Assert.True(r.Success);
                Assert.Equal(i.ToString("D4"), r.Seq);
                tags.Add(r.Tag); // model would store it
            }
            Assert.Equal(5, counters["M_HVAC_L01"]);
        }

        // ── AssignNext: collision auto-increment ────────────────────────
        [Fact]
        public void AssignNext_skips_existing_tag()
        {
            var counters = new Dictionary<string, int>();
            var tags = new HashSet<string> { Body + "0001", Body + "0002" };

            var r = SeqAssigner.AssignNext("M_HVAC_L01", counters, Body, Suffix,
                SeqScheme.Numeric, 4, "", 10000, tags);

            Assert.True(r.Success);
            Assert.Equal("0003", r.Seq);
            Assert.Equal(2, r.CollisionCount);
            Assert.Equal(3, counters["M_HVAC_L01"]);
        }

        // ── AssignNext: overflow on first increment ─────────────────────
        [Fact]
        public void AssignNext_initial_overflow_rolls_back()
        {
            var counters = new Dictionary<string, int> { ["G"] = 9 }; // pad 1 → max 9
            var r = SeqAssigner.AssignNext("G", counters, Body, Suffix,
                SeqScheme.Numeric, 1, "", 10000, existingTags: null);

            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.InitialOverflow, r.Failure);
            Assert.Equal(9, counters["G"]); // rolled back to pre-increment value
        }

        // ── AssignNext: overflow inside the collision loop ──────────────
        [Fact]
        public void AssignNext_collision_overflow_rolls_back()
        {
            var counters = new Dictionary<string, int>(); // starts at 0
            // pad 1 → candidates 1..9 all taken, so the loop overflows past 9
            var tags = new HashSet<string>();
            for (int i = 1; i <= 9; i++) tags.Add(Body + i.ToString());

            var r = SeqAssigner.AssignNext("G", counters, Body, Suffix,
                SeqScheme.Numeric, 1, "", 10000, tags);

            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.CollisionOverflow, r.Failure);
            Assert.Equal(0, counters["G"]); // rolled back to pre-allocation value
        }

        // ── AssignNext: safety limit exhausted ──────────────────────────
        [Fact]
        public void AssignNext_safety_exhausted_when_limit_too_small()
        {
            var counters = new Dictionary<string, int>();
            // Block 0001..0003; with depth 2 the loop can't escape and the
            // final candidate is still a duplicate → SafetyExhausted.
            var tags = new HashSet<string> { Body + "0001", Body + "0002", Body + "0003" };

            var r = SeqAssigner.AssignNext("M_HVAC_L01", counters, Body, Suffix,
                SeqScheme.Numeric, 4, "", maxCollisionDepth: 2, tags);

            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.SafetyExhausted, r.Failure);
            Assert.Equal(0, counters["M_HVAC_L01"]); // rolled back
        }

        [Fact]
        public void AssignNext_resolves_on_final_iteration()
        {
            var counters = new Dictionary<string, int>();
            // Block only 0001..0002; depth 2 reaches 0003 on the last allowed
            // iteration and 0003 is free → success (SEQ-CRIT-01 regression).
            var tags = new HashSet<string> { Body + "0001", Body + "0002" };

            var r = SeqAssigner.AssignNext("M_HVAC_L01", counters, Body, Suffix,
                SeqScheme.Numeric, 4, "", maxCollisionDepth: 2, tags);

            Assert.True(r.Success);
            Assert.Equal("0003", r.Seq);
            Assert.Equal(2, r.CollisionCount);
            Assert.Equal(3, counters["M_HVAC_L01"]);
        }

        // ── AssignNext: prefix/suffix composition ───────────────────────
        [Fact]
        public void AssignNext_composes_body_and_suffix()
        {
            var counters = new Dictionary<string, int>();
            var r = SeqAssigner.AssignNext("M_HVAC_L01", counters, "PFX-M-...-", "-S1",
                SeqScheme.Numeric, 4, "", 10000, existingTags: null);

            Assert.True(r.Success);
            Assert.Equal("PFX-M-...-0001-S1", r.Tag);
        }

        // ── AssignNext: null existingTags skips collision handling ──────
        [Fact]
        public void AssignNext_null_index_never_collides()
        {
            var counters = new Dictionary<string, int> { ["M_HVAC_L01"] = 41 };
            var r = SeqAssigner.AssignNext("M_HVAC_L01", counters, Body, Suffix,
                SeqScheme.Numeric, 4, "", 10000, existingTags: null);

            Assert.True(r.Success);
            Assert.Equal("0042", r.Seq);
            Assert.Equal(0, r.CollisionCount);
        }
    }
}
