// DTW-114: a re-run of production refreshed every view it found already made with
// every annotation pass hard-coded off, so nothing modelled since the first run was
// ever tagged or dimensioned. A refresh now runs the passes a new view gets, by the
// same switches, and holds back only the matchline frame, which is not idempotent.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ProductionAnnotationPolicyTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_refresh_runs_the_same_passes_as_a_new_view(bool refresh)
        {
            var c = ProductionAnnotationPolicy.Choose(true, true, true, true, true, refresh, packDrawsMatchlineFrame: false);
            Assert.False(c.SkipTags);
            Assert.False(c.SkipDims);
            Assert.False(c.SkipDecorative);
            Assert.False(c.SkipSpots);
            Assert.Null(c.HeldBack);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_dialog_boxes_decide_on_a_refresh_too(bool refresh)
        {
            var c = ProductionAnnotationPolicy.Choose(true, runTags: false, runDims: true, runDecorative: true,
                runSpots: false, refresh: refresh, packDrawsMatchlineFrame: false);
            Assert.True(c.SkipTags);
            Assert.False(c.SkipDims);
            Assert.True(c.SkipSpots);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_master_switch_off_runs_nothing(bool refresh)
        {
            var c = ProductionAnnotationPolicy.Choose(false, true, true, true, true, refresh, true);
            Assert.False(c.RunsAnything);
            Assert.Null(c.HeldBack);
        }

        [Fact]
        public void A_new_view_draws_the_matchline_frame()
        {
            var c = ProductionAnnotationPolicy.Choose(true, true, true, true, true, refresh: false, packDrawsMatchlineFrame: true);
            Assert.False(c.SkipDecorative);
            Assert.Null(c.HeldBack);
        }

        [Fact]
        public void A_refresh_holds_back_the_matchline_frame_and_says_why()
        {
            var c = ProductionAnnotationPolicy.Choose(true, true, true, true, true, refresh: true, packDrawsMatchlineFrame: true);
            Assert.True(c.SkipDecorative);
            Assert.False(c.SkipTags);
            Assert.False(c.SkipDims);
            Assert.False(c.SkipSpots);
            Assert.Contains("matchline", c.HeldBack);
        }

        [Fact]
        public void Nothing_is_held_back_when_decorative_was_not_asked_for()
        {
            var c = ProductionAnnotationPolicy.Choose(true, true, true, runDecorative: false, runSpots: true,
                refresh: true, packDrawsMatchlineFrame: true);
            Assert.True(c.SkipDecorative);
            Assert.Null(c.HeldBack);
        }

        [Fact]
        public void A_refresh_that_placed_nothing_adds_no_report_line()
            => Assert.Null(ProductionAnnotationPolicy.RefreshLine("L01 Plan", 0, 0, 0));

        [Fact]
        public void A_refresh_that_placed_something_reports_its_counts()
        {
            var line = ProductionAnnotationPolicy.RefreshLine("L01 Plan", 7, 2, 1);
            Assert.Contains("L01 Plan", line);
            Assert.Contains("7 tag(s)", line);
            Assert.Contains("2 dimension(s)", line);
            Assert.Contains("1 spot / symbol(s)", line);
        }
    }
}
