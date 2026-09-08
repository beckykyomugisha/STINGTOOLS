using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the glob/alternation PROD FAMILY_PATTERN matcher. This is the fix
    /// for the shipped STING_PROD_CODES.csv rows (e.g. *Air Handling*,
    /// *Split*|*Packaged*) that the legacy String.Contains matcher silently
    /// never matched. Inputs are upper-cased (the caller upper-cases both sides).
    /// </summary>
    public class ProdPatternMatcherTests
    {
        [Theory]
        // bare substring — back-compatible with historic patterns
        [InlineData("DAIKIN FCU CEILING FCU-01", "FCU", true)]
        [InlineData("WALL HUNG WC PAN", "WC", true)]
        [InlineData("DAIKIN FCU CEILING", "AHU", false)]
        // *contains* glob
        [InlineData("TROX AIR HANDLING UNIT AHU-1", "*AIR HANDLING*", true)]
        [InlineData("FAN COIL UNIT", "*AIR HANDLING*", false)]
        // prefix / suffix globs
        [InlineData("VRV OUTDOOR UNIT", "VRV*", true)]
        [InlineData("OUTDOOR VRV", "VRV*", false)]
        [InlineData("CEILING CASSETTE FCU", "*FCU", true)]
        // alternation
        [InlineData("MITSUBISHI SPLIT UNIT", "*SPLIT*|*PACKAGED*", true)]
        [InlineData("ROOFTOP PACKAGED UNIT", "*SPLIT*|*PACKAGED*", true)]
        [InlineData("FAN COIL UNIT", "*SPLIT*|*PACKAGED*", false)]
        // embedded wildcard
        [InlineData("VRV CONDENSER UNIT", "VRV*UNIT", true)]
        [InlineData("VRV CONDENSER MODULE", "VRV*UNIT", false)]
        public void Matches_handles_substrings_globs_and_alternation(string name, string pattern, bool expected)
        {
            Assert.Equal(expected, ProdPatternMatcher.Matches(name, pattern));
        }

        [Theory]
        [InlineData(null, "FCU")]
        [InlineData("", "FCU")]
        [InlineData("DAIKIN FCU", null)]
        [InlineData("DAIKIN FCU", "")]
        public void Matches_returns_false_on_empty_inputs(string name, string pattern)
        {
            Assert.False(ProdPatternMatcher.Matches(name, pattern));
        }

        [Fact]
        public void Matches_ignores_blank_alternation_branches()
        {
            // trailing/empty '|' branch must not match everything
            Assert.False(ProdPatternMatcher.Matches("FAN COIL", "*AHU*|"));
            Assert.True(ProdPatternMatcher.Matches("AHU-1", "*AHU*|"));
        }

        [Theory]
        // '?' = exactly one char
        [InlineData("DN20", "DN2?", true)]
        [InlineData("DN2", "DN2?", false)]      // nothing for the '?' to match
        [InlineData("DN200", "DN2?", false)]    // anchored — too long
        // character class
        [InlineData("DN20-PN16", "DN20-PN1[06]", true)]
        [InlineData("DN20-PN10", "DN20-PN1[06]", true)]
        [InlineData("DN20-PN13", "DN20-PN1[06]", false)]
        // class combined with glob
        [InlineData("VALVE PN16 BRONZE", "*PN1[06]*", true)]
        // literal punctuation outside wildcards stays literal (regex chars escaped)
        [InlineData("TYPE (A) UNIT", "*(A)*", true)]
        [InlineData("TYPE A UNIT", "*(A)*", false)]
        // unterminated '[' treated as a literal, not a crash
        [InlineData("ROW [1", "ROW [1", true)]
        public void Matches_handles_question_mark_and_char_classes(string name, string pattern, bool expected)
        {
            Assert.Equal(expected, ProdPatternMatcher.Matches(name, pattern));
        }

        // ── PROD-4: the bare-substring path must be word-bounded ────────────
        //
        // An alternative with no *, ? or [ takes a fast path that used a plain
        // String.Contains, so a short pattern matched INSIDE a longer word. Same
        // defect class as #863 and Phase 255, and as the oil painting that filed
        // itself under ELEMENT 03: ROOF because "bRIDGEe" contains "ridge".
        //
        // All 376 shipped FAMILY_PATTERN alternatives are globs, so nothing live
        // went through it. A PROJECT overlay can, and Prod_GenerateRules seeds
        // overlays from live family names, so a hand-curated short pattern is a
        // plausible route in. Nothing errors: the element simply carries a
        // confident wrong PROD code into the tag.

        [Theory]
        // ── must NOT match: the pattern is inside a longer word ──
        [InlineData("PUMPHOUSE CONTROL PANEL", "PUMP", false)]
        [InlineData("PORCELAIN SINK", "RC", false)]
        [InlineData("CARTRIDGE FILTER", "RIDGE", false)]
        [InlineData("SUBSTATION", "SUB", false)]
        // a gauge is not a prefix of another gauge: digits are word characters
        [InlineData("DN200 HEADER", "DN20", false)]
        // ── must STILL match: bounded by a separator or the string end ──
        [InlineData("FIRE PUMP 01", "PUMP", true)]
        [InlineData("BOOSTER-PUMP", "PUMP", true)]
        [InlineData("PUMP", "PUMP", true)]
        [InlineData("CONDENSATE PUMP", "PUMP", true)]
        [InlineData("DN20 HEADER", "DN20", true)]
        // multi-word bare patterns are matched as a run, bounded at each end
        [InlineData("TROX AIR HANDLING UNIT", "AIR HANDLING", true)]
        [InlineData("PREAIR HANDLINGS", "AIR HANDLING", false)]
        public void Bare_substring_alternatives_match_whole_words(
            string name, string pattern, bool expected)
        {
            Assert.Equal(expected, ProdPatternMatcher.Matches(name, pattern));
        }

        /// <summary>
        /// Matches() and Strength() evaluate the alternatives through two separate
        /// code paths, so a fix applied to one and not the other would leave a rule
        /// that does not match yet still outranks the rule that does. Nothing in the
        /// resolver would report that; it would just pick the wrong PROD code.
        /// </summary>
        [Theory]
        [InlineData("PUMPHOUSE CONTROL PANEL", "PUMP")]
        [InlineData("PORCELAIN SINK", "RC")]
        [InlineData("DN200 HEADER", "DN20")]
        [InlineData("FIRE PUMP 01", "PUMP")]
        [InlineData("CARTRIDGE FILTER", "RIDGE|CARTRIDGE")]
        public void Strength_agrees_with_Matches_on_bare_substrings(
            string name, string pattern)
        {
            bool matched = ProdPatternMatcher.Matches(name, pattern);
            int strength = ProdPatternMatcher.Strength(name, pattern);
            Assert.Equal(matched, strength >= 0);
        }
    }
}
