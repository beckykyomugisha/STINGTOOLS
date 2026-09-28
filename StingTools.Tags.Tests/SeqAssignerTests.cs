using System;
using System.Collections.Generic;
using System.Linq;
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

        // A sequence is exactly the pad width and at least 1 (was: up to pad + 1 digits,
        // any shorter width, and zero all accepted).
        [Theory]
        [InlineData("0001", 4, true)]
        [InlineData("0042", 4, true)]
        [InlineData("9999", 4, true)]
        [InlineData("042", 3, true)]
        [InlineData("12", 4, false)]      // not padded
        [InlineData("00012", 4, false)]   // one digit too many
        [InlineData("0000", 4, false)]    // sequences start at 1
        [InlineData("+001", 4, false)]
        [InlineData("-001", 4, false)]
        [InlineData("00A1", 4, false)]
        [InlineData("", 4, false)]
        public void ValidateNumericSeq_requires_the_pad_width_and_a_positive_value(string seq, int pad, bool valid)
            => Assert.Equal(valid, SeqAssigner.ValidateNumericSeq(seq, pad) == null);

        [Fact]
        public void Every_sequence_BuildSeqString_writes_is_valid()
        {
            foreach (int pad in new[] { 3, 4, 5 })
                foreach (int n in new[] { 1, 7, 42, SeqAssigner.MaxSeqForPad(pad) })
                    Assert.Null(SeqAssigner.ValidateNumericSeq(SeqAssigner.BuildSeqString(n, SeqScheme.Numeric, pad, ""), pad));
        }

        [Theory]
        [InlineData(3, "000")]
        [InlineData(4, "0000")]
        [InlineData(5, "00000")]
        public void The_unassigned_placeholder_follows_the_pad(int pad, string placeholder)
        {
            Assert.Equal(placeholder, SeqAssigner.UnassignedSeq(pad));
            Assert.True(SeqAssigner.IsUnassignedSeq(placeholder));
            Assert.True(SeqAssigner.IsUnresolvedToken(placeholder));
            Assert.NotNull(SeqAssigner.ValidateNumericSeq(placeholder, pad));
        }

        [Theory]
        [InlineData("0001", false)]
        [InlineData("XX", true)]
        [InlineData("ZZ", true)]
        [InlineData("GEN", false)]   // assumed, not unresolved
        [InlineData("", false)]
        public void Unresolved_tokens(string token, bool unresolved)
            => Assert.Equal(unresolved, SeqAssigner.IsUnresolvedToken(token));

        // The placeholder was the literal "0000" in six places, so at pad 3 or 5 it was
        // never recognised. Any new literal comparison fails here.
        [Fact]
        public void No_plugin_source_compares_against_a_literal_0000_placeholder()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "StingTools", "Core"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var hits = System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(dir.FullName, "StingTools"), "*.cs", System.IO.SearchOption.AllDirectories)
                .Where(f => !f.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar))
                .Where(f => !f.EndsWith("Iso19650DocumentCode.cs"))   // document numbers, not tag SEQ
                .SelectMany(f => System.IO.File.ReadAllLines(f).Select((l, n) => (f, n, l)))
                .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.l, @"(==|!=)\s*""0000""|""0000""\s*(==|!=)|\{[^}]*""0000""[^}]*\}"))
                .Select(x => $"{System.IO.Path.GetFileName(x.f)}:{x.n + 1}")
                .ToList();
            Assert.True(hits.Count == 0, "Literal \"0000\" placeholder: " + string.Join(", ", hits));
        }

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

        // ZonePrefix / DiscPrefix are deprecated — they injected the tag
        // separator into the SEQ segment and duplicated the ZONE/DISC tokens,
        // breaking the fixed 8-segment grammar. They now behave as Numeric so a
        // persisted scheme self-heals and the canonical tag can't be corrupted.
        [Fact]
        public void BuildSeqString_zone_prefix_deprecated_falls_back_to_numeric()
            => Assert.Equal("0042", SeqAssigner.BuildSeqString(42, SeqScheme.ZonePrefix, 4, "Z01"));

        [Fact]
        public void BuildSeqString_disc_prefix_deprecated_falls_back_to_numeric()
            => Assert.Equal("0042", SeqAssigner.BuildSeqString(42, SeqScheme.DiscPrefix, 4, "M"));

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
