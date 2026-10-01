using StingTools.Core.Placement;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-112 / DTW-131 — a re-run of the DWG fixture bridge must not place every
    /// fixture twice, and must not drop the fixtures of a stacked floor as "already placed"
    /// by the floor below. These pin the Revit-free match the bridge makes.</summary>
    public class DwgCaptureDedupTests
    {
        private const double Mm = 1.0 / 304.8;
        private const double Storey = 3500 * Mm;
        private static readonly double[] Levels = { 0, Storey, 2 * Storey };

        private static DwgCaptureDedup.CaptureLevel L(int i, string key = null)
            => new DwgCaptureDedup.CaptureLevel(key ?? ("L" + i), Levels[i], DwgCaptureDedup.HalfStoreyFt(Levels, Levels[i]));

        private static string Rule(double x, double y, string levelKey)
            => "DWG:x|" + DwgCaptureDedup.PointToken(x, y) + (levelKey == null ? "" : "|" + DwgCaptureDedup.LevelToken(levelKey));

        [Fact]
        public void PointToken_RoundTrips_ThroughAProvenanceRuleId()
        {
            string ruleId = "DWG:WC|P-SAN|seed:STING_SEED_PlumbingFixture|var:WC|mode:block|mh:0|"
                            + DwgCaptureDedup.PointToken(12.3456, -7.25) + "|" + DwgCaptureDedup.LevelToken("311");
            Assert.True(DwgCaptureDedup.TryParsePoint(ruleId, out var x, out var y));
            Assert.Equal(12.3456, x, 4);
            Assert.Equal(-7.25, y, 4);
            Assert.True(DwgCaptureDedup.TryParseLevel(ruleId, out var lvl));
            Assert.Equal("311", lvl);
        }

        [Fact]
        public void RuleIdWithoutPoint_DoesNotParse()
        {
            Assert.False(DwgCaptureDedup.TryParsePoint("DWG:WC|P-SAN|seed:S|var:|mode:block|mh:0", out _, out _));
            Assert.False(DwgCaptureDedup.TryParsePoint("", out _, out _));
            Assert.False(DwgCaptureDedup.TryParseLevel("DWG:WC|pt:1,2", out _));
        }

        [Fact]
        public void SameCapturePoint_IsADuplicate_AnotherSymbolIsNot()
        {
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add(Rule(10, 10, "L0"), 10.4, 10, hasLocation: true, priorLevelElevFt: 0);
            Assert.True(idx.IsDuplicate(10, 10, L(0)));
            Assert.True(idx.IsDuplicate(10 + 20 * Mm, 10, L(0)));
            // A neighbouring basin 600 mm along the wall is a different fixture.
            Assert.False(idx.IsDuplicate(10 + 600 * Mm, 10, L(0)));
        }

        [Fact]
        public void StackedFloors_SamePlanPoint_OnAnotherLevel_IsNotADuplicate()
        {
            // DTW-131 — identical floors: the WC on L1 sits at the same X/Y as the WC on L0.
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add(Rule(10, 10, "L0"), 10, 10, hasLocation: true, priorLevelElevFt: 0);
            Assert.True(idx.IsDuplicate(10, 10, L(0)));
            Assert.False(idx.IsDuplicate(10, 10, L(1)));
            Assert.False(idx.IsDuplicate(10, 10, L(2)));
        }

        [Fact]
        public void OldStampWithoutLevel_MatchesOnlyWithinHalfAStorey()
        {
            // Stamped before DTW-131: a capture point but no level — fall back to the
            // prior instance's level elevation.
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add(Rule(10, 10, null), 10, 10, hasLocation: true, priorLevelElevFt: Storey);
            Assert.False(idx.IsDuplicate(10, 10, L(0)));
            Assert.True(idx.IsDuplicate(10, 10, L(1)));
            Assert.False(idx.IsDuplicate(10, 10, L(2)));
        }

        [Fact]
        public void PriorWithNoLevelEvidence_IsNeverADuplicate()
        {
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add(Rule(10, 10, null), 10, 10, hasLocation: true, priorLevelElevFt: null);
            Assert.False(idx.IsDuplicate(10, 10, L(0)));
        }

        [Fact]
        public void HalfStorey_IsHalfTheGapToTheNextLevel()
        {
            Assert.Equal(1750 * Mm, DwgCaptureDedup.HalfStoreyFt(Levels, 0), 6);
            // Top level: half the gap to the level below.
            Assert.Equal(1750 * Mm, DwgCaptureDedup.HalfStoreyFt(Levels, 2 * Storey), 6);
            Assert.Equal(DwgCaptureDedup.DefaultHalfStoreyFt, DwgCaptureDedup.HalfStoreyFt(new[] { 0.0 }, 0), 6);
        }

        [Fact]
        public void RecordedPoint_WinsOverTheHostedLocation()
        {
            // The hosted instance sits 120 mm off the DWG point (snapped to the wall face);
            // with a recorded point the 300 mm location band does not apply, so a symbol
            // 200 mm away is still placed.
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add(Rule(0, 0, "L0"), 120 * Mm, 0, hasLocation: true, priorLevelElevFt: 0);
            Assert.False(idx.IsDuplicate(200 * Mm, 0, L(0)));
        }

        [Fact]
        public void OlderInstanceWithoutPoint_FallsBackToItsLocation()
        {
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add("DWG:x|P-SAN|seed:S|var:|mode:block|mh:0", 120 * Mm, 0, hasLocation: true, priorLevelElevFt: 0);
            Assert.True(idx.IsDuplicate(0, 0, L(0)));
            Assert.False(idx.IsDuplicate(500 * Mm, 0, L(0)));
            Assert.False(idx.IsDuplicate(0, 0, L(1)));
        }

        [Fact]
        public void EmptyIndex_NothingIsADuplicate()
        {
            var idx = new DwgCaptureDedup.PriorIndex();
            idx.Add("no point", 0, 0, hasLocation: false, priorLevelElevFt: 0);
            Assert.Equal(0, idx.Count);
            Assert.False(idx.IsDuplicate(0, 0, L(0)));
        }
    }
}
