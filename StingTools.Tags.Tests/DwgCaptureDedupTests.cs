using StingTools.Core.Placement;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-112 — a re-run of the DWG fixture bridge must not place every
    /// fixture twice. These pin the Revit-free match the bridge makes.</summary>
    public class DwgCaptureDedupTests
    {
        private const double Mm = 1.0 / 304.8;

        [Fact]
        public void PointToken_RoundTrips_ThroughAProvenanceRuleId()
        {
            string ruleId = "DWG:WC|P-SAN|seed:STING_SEED_PlumbingFixture|var:WC|mode:block|mh:0|"
                            + DwgCaptureDedup.PointToken(12.3456, -7.25);
            Assert.True(DwgCaptureDedup.TryParsePoint(ruleId, out var x, out var y));
            Assert.Equal(12.3456, x, 4);
            Assert.Equal(-7.25, y, 4);
        }

        [Fact]
        public void RuleIdWithoutPoint_DoesNotParse()
        {
            Assert.False(DwgCaptureDedup.TryParsePoint("DWG:WC|P-SAN|seed:S|var:|mode:block|mh:0", out _, out _));
            Assert.False(DwgCaptureDedup.TryParsePoint("", out _, out _));
        }

        [Fact]
        public void SameCapturePoint_IsADuplicate_AnotherSymbolIsNot()
        {
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add("DWG:x|" + DwgCaptureDedup.PointToken(10, 10), 10.4, 10, hasLocation: true);
            Assert.True(idx.IsDuplicate(10, 10));
            Assert.True(idx.IsDuplicate(10 + 20 * Mm, 10));
            // A neighbouring basin 600 mm along the wall is a different fixture.
            Assert.False(idx.IsDuplicate(10 + 600 * Mm, 10));
        }

        [Fact]
        public void RecordedPoint_WinsOverTheHostedLocation()
        {
            // The hosted instance sits 120 mm off the DWG point (snapped to the wall face);
            // with a recorded point the 300 mm location band does not apply, so a symbol
            // 200 mm away is still placed.
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add("DWG:x|" + DwgCaptureDedup.PointToken(0, 0), 120 * Mm, 0, hasLocation: true);
            Assert.False(idx.IsDuplicate(200 * Mm, 0));
        }

        [Fact]
        public void OlderInstanceWithoutPoint_FallsBackToItsLocation()
        {
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add("DWG:x|P-SAN|seed:S|var:|mode:block|mh:0", 120 * Mm, 0, hasLocation: true);
            Assert.True(idx.IsDuplicate(0, 0));
            Assert.False(idx.IsDuplicate(500 * Mm, 0));
        }

        [Fact]
        public void EmptyIndex_NothingIsADuplicate()
        {
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add("no point", 0, 0, hasLocation: false);
            Assert.Equal(0, idx.Count);
            Assert.False(idx.IsDuplicate(0, 0));
        }
    }
}
