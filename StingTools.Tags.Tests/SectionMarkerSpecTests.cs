using System.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-65 — sectionMarker. No marker family ships, so DT-030 is info, not a
    /// warning on every project; farClipMm has a real consumer. Both sides are
    /// Revit-bound, so they are checked as source text.
    /// </summary>
    public class SectionMarkerSpecTests
    {
        [Fact]
        public void Missing_marker_family_is_reported_as_info()
        {
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypeValidator.cs");
            Assert.Contains("r.Add(ValidationSeverity.Info, \"DT-030\"", src);
            Assert.DoesNotContain("ValidationSeverity.Warning, \"DT-030\"", src);
        }

        [Fact]
        public void FarClipMm_is_applied_in_feet_to_the_far_clip_offset()
        {
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypePresentation.cs");
            Assert.Contains("BuiltInParameter.VIEWER_BOUND_OFFSET_FAR", src);
            Assert.Contains("spec.FarClipMm / 304.8", src);
            Assert.Contains("ApplySectionMarkerFarClip(view, dt, r)", src);
        }

        [Fact]
        public void Every_shipped_marker_declares_a_positive_far_clip()
        {
            var withMarker = DrawingCatalogueFixture.Shipped().DrawingTypes
                .Where(t => !string.IsNullOrWhiteSpace(t.SectionMarker?.Family)).ToList();
            Assert.True(withMarker.Count >= 10, $"Only {withMarker.Count} types declare a marker");
            Assert.All(withMarker, t => Assert.True(t.SectionMarker.FarClipMm > 0, t.Id));
        }
    }
}
