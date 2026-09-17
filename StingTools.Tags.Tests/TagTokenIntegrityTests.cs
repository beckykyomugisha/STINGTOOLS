using System;
using System.Collections.Generic;
using System.IO;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Locks the three defects traced on 2026-09-17 behind the "27 elements missing
    /// FUNC/PROD" report, the ROOMTAG-1 tag `A-BLD1-Z01-L01-ARC---`, and the blank
    /// SEQ on every tag in that run. All three are one chain: a token is derived
    /// non-empty, the write silently fails, the read-back is empty, and the empty
    /// value then becomes the tag.
    /// </summary>
    public class TagTokenIntegrityTests
    {
        private const string Sep = "-";

        // ── 1. GEN must not block completeness ───────────────────────────────
        // STING_TAG_TOKEN_POLICY.json declares SYS/FUNC/PROD level=DERIVED with
        // fallback "GEN" and argues GEN is "a real answer for them, not a guess".
        // TagIsComplete rejected it, so every architectural element was permanently
        // incomplete, re-derived on every run and never reachable by Skip mode.

        [Theory]
        [InlineData("A-BLD1-Z01-L01-GEN-GEN-GEN-0001")]
        [InlineData("A-BLD1-Z01-L01-ARC-FIT-DR-0001")]
        public void Gen_does_not_block_completeness(string tag)
            => Assert.False(TagTokenIntegrity.HasStructuralPlaceholder(tag, Sep));

        [Theory]
        [InlineData("A-BLD1-XX-L01-ARC-FIT-DR-0001")]
        [InlineData("A-BLD1-ZZ-L01-ARC-FIT-DR-0001")]
        [InlineData("A-BLD1-Z01-L01-ARC-FIT-DR-0000")]
        public void Genuine_unknowns_still_block_completeness(string tag)
            => Assert.True(TagTokenIntegrity.HasStructuralPlaceholder(tag, Sep));

        // Strict/compliance reporting must keep counting GEN as unresolved, so
        // ComplianceScan.StrictPercent does not silently inflate.
        [Fact]
        public void Strict_reading_still_counts_gen_as_unresolved()
            => Assert.True(TagTokenIntegrity.HasPlaceholderOrAssumed(
                "A-BLD1-Z01-L01-GEN-GEN-GEN-0001", Sep));

        [Fact]
        public void Strict_reading_accepts_a_fully_measured_tag()
            => Assert.False(TagTokenIntegrity.HasPlaceholderOrAssumed(
                "A-BLD1-Z01-L01-ARC-FIT-DR-0001", Sep));

        // A real code that merely CONTAINS a placeholder must not trip the match.
        [Fact]
        public void Placeholder_match_is_whole_segment_only()
            => Assert.False(TagTokenIntegrity.HasStructuralPlaceholder(
                "A-BLD1-Z01-L01-ARC-FIT-XXL-0001", Sep));

        // ── 2. The malformed-tag guard must reject blank segments ────────────
        // It counted separators, so ROOMTAG-1's `A-BLD1-Z01-L01-ARC---` has exactly
        // seven separators and sailed through as an eight-segment tag.

        [Fact]
        public void Roomtag1_shape_is_rejected()
            => Assert.False(TagTokenIntegrity.AllSegmentsPresent(
                "A-BLD1-Z01-L01-ARC---", Sep, 8));

        [Fact]
        public void Trailing_blank_seq_is_rejected()
            => Assert.False(TagTokenIntegrity.AllSegmentsPresent(
                "A-BLD1-Z01-L01-ARC-FIT-DR-", Sep, 8));

        [Fact]
        public void Fully_populated_tag_is_accepted()
            => Assert.True(TagTokenIntegrity.AllSegmentsPresent(
                "A-BLD1-Z01-L01-ARC-FIT-DR-0001", Sep, 8));

        [Fact]
        public void Too_many_segments_is_rejected()
            => Assert.False(TagTokenIntegrity.AllSegmentsPresent(
                "A-BLD1-Z01-L01-ARC-FIT-DR-0001-EXTRA", Sep, 8));

        // A material-suffixed PROD joins with "_" (TAGPROD-1), so it is ONE segment.
        [Fact]
        public void Material_suffixed_prod_stays_one_segment()
            => Assert.True(TagTokenIntegrity.AllSegmentsPresent(
                "A-BLD1-Z01-L01-ARC-FIT-DR_GLZ-0001", Sep, 8));

        // ── 3. An empty read-back must not destroy a derived value ───────────
        // SetString returns false in silence when the parameter is not reachable on
        // the instance. BuildAndWriteTag discarded that bool, re-read the element,
        // and rebuilt TAG1 from the empties.

        [Fact]
        public void Failed_write_does_not_blank_a_derived_token()
        {
            var derived  = new[] { "A", "BLD1", "Z01", "L01", "ARC", "FIT", "DR", "0001" };
            var readBack = new[] { "A", "BLD1", "Z01", "L01", "ARC", "",    "",   "" };

            var effective = TagTokenIntegrity.Reconcile(derived, readBack, out int recovered);

            Assert.Equal(derived, effective);
            Assert.Equal(3, recovered);
        }

        [Fact]
        public void A_users_edit_still_wins_over_the_derived_value()
        {
            // SetIfEmpty only declines when the stored value is NON-empty, so a
            // non-empty read-back is a real value and must be preserved.
            var derived  = new[] { "A", "BLD1", "Z01", "L01", "ARC", "FIT", "DR", "0001" };
            var readBack = new[] { "A", "BLD2", "Z01", "L01", "ARC", "FIT", "DR", "0001" };

            var effective = TagTokenIntegrity.Reconcile(derived, readBack, out int recovered);

            Assert.Equal("BLD2", effective[1]);
            Assert.Equal(0, recovered);
        }

        [Fact]
        public void Both_empty_stays_empty_and_is_not_counted_as_recovered()
        {
            var derived  = new[] { "A", "BLD1", "Z01", "L01", "ARC", "FIT", "DR", "" };
            var readBack = new[] { "A", "BLD1", "Z01", "L01", "ARC", "FIT", "DR", "" };

            var effective = TagTokenIntegrity.Reconcile(derived, readBack, out int recovered);

            Assert.Equal("", effective[7]);
            Assert.Equal(0, recovered);
        }

        // ── The durable invariant ────────────────────────────────────────────
        // Asserted against the SHIPPED policy, not a list written here, so a new
        // token added to STING_TAG_TOKEN_POLICY.json is covered without anyone
        // remembering to extend this file.

        private static string PolicyPath()
        {
            string p = Path.Combine(AppContext.BaseDirectory, "Data", TagTokenPolicy.BaselineFileName);
            if (!File.Exists(p))
                throw new FileNotFoundException(
                    "The shipped token policy was not copied to the test output; without it this "
                    + "assertion would pass over an empty library and prove nothing.", p);
            return p;
        }

        /// <summary>
        /// A token's terminal fallback must never be a value that TagIsComplete rejects.
        /// When it is, the guarantee "no element ends up missing this token" is
        /// unreachable by construction: the code supplies a value and the completeness
        /// check refuses it, so the element is permanently incomplete, re-derived on
        /// every run and never reachable by Skip mode. That was the state of SYS, FUNC
        /// and PROD — all three fall back to "GEN", and "GEN" was a placeholder.
        /// </summary>
        [Fact]
        public void No_policy_fallback_is_a_value_completeness_rejects()
        {
            var lib = TagTokenPolicy.Parse(File.ReadAllText(PolicyPath()));
            Assert.NotEmpty(lib.Tokens);

            var offenders = new List<string>();
            foreach (var t in lib.Tokens)
            {
                if (string.IsNullOrEmpty(t.Fallback)) continue;   // refusal, not a fallback
                if (TagTokenIntegrity.HasStructuralPlaceholder(t.Fallback, Sep))
                    offenders.Add($"{t.Token} falls back to '{t.Fallback}', which TagIsComplete rejects");
            }

            Assert.True(offenders.Count == 0, string.Join("; ", offenders));
        }

    }
}
