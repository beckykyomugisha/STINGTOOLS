// DTW-93: one segment rule for every STING scope-box prefix.
//
// STING::, STING-AREA::, STING-SEED::, STING-LOC:: and STING-ZONE:: were parsed by
// four different pieces of code: the binder tested its prefix ignoring case but
// matched its regex with case, so "sting::arch-plan" was told it was malformed; the
// LOC index accepted spaces and a trailing space the planner rejected, so one box
// meant one building to tagging and none to the planner. ScopeBoxNames now holds
// the rule — prefix ignores case, the name and each segment are trimmed, a segment
// is A-Z 0-9 . _ - — and every parser calls it.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ScopeBoxNameGrammarTests
    {
        [Theory]
        [InlineData("STING::arch-plan-A1-1to100")]
        [InlineData("sting::arch-plan-A1-1to100")]
        [InlineData("  Sting::arch-plan-A1-1to100  ")]
        public void A_drawing_type_name_parses_whatever_the_prefix_case(string name)
        {
            Assert.True(ScopeBoxNames.TryParseDrawingType(name, out var id, out var level, out var tag, out var why), why);
            Assert.Equal("arch-plan-A1-1to100", id);
            Assert.Null(level);
            Assert.Null(tag);
        }

        [Fact]
        public void A_drawing_type_name_trims_every_segment()
        {
            Assert.True(ScopeBoxNames.TryParseDrawingType("STING:: arch-plan :: L02 :: east ", out var id, out var level, out var tag, out var why), why);
            Assert.Equal("arch-plan", id);
            Assert.Equal("L02", level);
            Assert.Equal("east", tag);
        }

        [Theory]
        [InlineData("STING::arch plan")]
        [InlineData("STING::")]
        [InlineData("STING::a::b::c::d")]
        public void A_malformed_drawing_type_name_says_why(string name)
        {
            Assert.False(ScopeBoxNames.TryParseDrawingType(name, out _, out _, out _, out var why));
            Assert.Equal(ScopeBoxNames.DrawingTypePatternReason, why);
        }

        [Fact]
        public void A_name_without_the_prefix_is_not_an_error()
        {
            Assert.False(ScopeBoxNames.TryParseDrawingType("Scope Box 1", out _, out _, out _, out var why));
            Assert.Null(why);
        }

        [Theory]
        [InlineData("STING-LOC::BLD1", "BLD1")]
        [InlineData("sting-loc::BLD1", "BLD1")]
        [InlineData("STING-LOC::BLD1 ", "BLD1")]
        [InlineData(" STING-LOC:: BLD1", "BLD1")]
        public void A_building_code_is_trimmed_and_the_prefix_ignores_case(string name, string loc)
        {
            Assert.True(ScopeBoxNames.TryParseLoc(name, out var got, out var why), why);
            Assert.Equal(loc, got);
        }

        [Theory]
        [InlineData("STING-LOC::BLD 1")]
        [InlineData("STING-LOC::")]
        [InlineData("STING-LOC::A::B")]
        public void A_building_code_the_planner_cannot_use_is_refused_for_tagging_too(string name)
        {
            Assert.False(ScopeBoxNames.TryParseLoc(name, out _, out var why));
            Assert.Equal(ScopeBoxNames.LocPatternReason, why);
        }

        [Fact]
        public void A_name_like_the_loc_prefix_is_not_a_building()
        {
            // The LOC index used to accept anything starting "STING-LOC" with a "::"
            // anywhere, so "STING-LOCATION::X" became building X.
            Assert.False(ScopeBoxNames.TryParseLoc("STING-LOCATION::X", out _, out var why));
            Assert.Null(why);
        }

        [Theory]
        [InlineData(" STING-AREA::A1-100-01 ", "A1-100-01", null)]
        [InlineData("sting-area:: A1-100-01 :: L02", "A1-100-01", "L02")]
        public void An_area_name_is_trimmed_like_every_other(string name, string area, string level)
        {
            Assert.Equal(ScopeBoxKind.Area, ScopeBoxNames.Classify(name));
            Assert.True(ScopeBoxNames.TryParseArea(name, out var a, out var l, out var why), why);
            Assert.Equal(area, a);
            Assert.Equal(level, l);
        }

        [Theory]
        [InlineData("STING::has space")]
        [InlineData("STING-AREA::has space")]
        [InlineData("STING-LOC::has space")]
        [InlineData("STING-ZONE::has space")]
        [InlineData("STING-SEED::has space")]
        public void Every_prefix_refuses_the_same_bad_segment(string name)
            => Assert.False(ScopeBoxNames.TryParseSegments(name, out _, out _, out var why) || why == null);

        [Theory]
        [InlineData("STING::ok", ScopeBoxKind.DrawingType)]
        [InlineData("STING-AREA::ok", ScopeBoxKind.Area)]
        [InlineData("STING-LOC::ok", ScopeBoxKind.Building)]
        [InlineData("STING-ZONE::ok", ScopeBoxKind.Zone)]
        [InlineData("STING-SEED::ok", ScopeBoxKind.Seed)]
        public void Every_prefix_accepts_the_same_good_segment(string name, ScopeBoxKind kind)
        {
            Assert.True(ScopeBoxNames.TryParseSegments(name, out var k, out var segs, out var why), why);
            Assert.Equal(kind, k);
            Assert.Equal(new[] { "ok" }, segs);
        }
    }
}
