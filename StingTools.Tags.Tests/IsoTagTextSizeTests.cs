// Tag text height on a drawing, against ISO 3098. Revit prints annotation at its
// family size whatever the view scale, so the old scale ladder (2 mm at 1:100, 1 mm
// at 1:200 and 1:500) put illegible text on every small-scale plan.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class IsoTagTextSizeTests
    {
        private static DrawingType Dt(int scale, string paper = "A1", double explicitMm = 0)
            => new DrawingType { Id = "t", Scale = scale, PaperSize = paper, TagTextSizeMm = explicitMm };

        [Theory]
        [InlineData(20)] [InlineData(50)] [InlineData(100)] [InlineData(200)] [InlineData(500)] [InlineData(0)]
        public void The_default_does_not_shrink_with_the_scale(int scale)
            => Assert.Equal(2.5, Dt(scale).EffectiveTagTextSizeMm());

        [Fact]
        public void An_A0_sheet_defaults_to_3_5_mm()
            => Assert.Equal(3.5, Dt(100, "A0").EffectiveTagTextSizeMm());

        [Fact]
        public void An_explicit_size_wins()
            => Assert.Equal(5.0, Dt(100, "A1", 5.0).EffectiveTagTextSizeMm());

        [Fact]
        public void The_default_is_not_snapped_down_to_an_illegible_variant()
        {
            // 2 mm is nearer to 2.5 than 3.5 is, but it is below the A1 minimum.
            Assert.Equal(3.5, Dt(100).NearestAvailableTagSizeMm(new[] { 2.0, 3.5 }));
        }

        [Fact]
        public void A_small_variant_is_used_when_nothing_legible_is_loaded()
            => Assert.Equal(2.0, Dt(100).NearestAvailableTagSizeMm(new[] { 1.5, 2.0 }));

        [Fact]
        public void An_explicit_small_size_is_the_projects_decision()
            => Assert.Equal(2.0, Dt(100, "A1", 2.0).NearestAvailableTagSizeMm(new[] { 2.0, 3.5 }));

        [Theory]
        [InlineData("A1", 0, null)]      // default: nothing to report
        [InlineData("A1", 2.5, null)]
        [InlineData("A1", 3.5, null)]
        [InlineData("A4", 1.8, null)]
        [InlineData("A1", 2.0, "below")]
        [InlineData("A1", 1.0, "below")]
        [InlineData("A1", 3.0, "not an ISO 3098")]
        public void Explicit_sizes_are_checked_against_iso_3098(string paper, double mm, string expect)
        {
            var issue = Dt(100, paper, mm).IsoTagSizeIssue();
            if (expect == null) Assert.Null(issue);
            else Assert.Contains(expect, issue);
        }
    }
}
