// ConduitRouteEngineTests — pure-logic regression tests for the
// rectilinear routing engine + bend-counting helpers.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Commands.Electrical.Routing;
using Xunit;

namespace StingTools.Routing.Tests
{
    public class ConduitRouteEngineTests
    {
        // ── ComputeRoute ────────────────────────────────────────────────

        [Fact]
        public void ComputeRoute_StraightHorizontal_ProducesOneSegment()
        {
            var start = new XYZ(0, 0, 0);
            var end   = new XYZ(10, 0, 0);
            var segs  = ConduitRouteEngine.ComputeRoute(start, end, 25, "C-1");
            Assert.Single(segs);
            Assert.Equal(start, segs[0].Start);
            Assert.Equal(end,   segs[0].End);
        }

        [Fact]
        public void ComputeRoute_LRoute_ProducesThreeSegmentsAtMost()
        {
            var start = new XYZ(0, 0, 0);
            var end   = new XYZ(10, 5, 8);
            var segs  = ConduitRouteEngine.ComputeRoute(start, end, 25, "C-1");
            Assert.InRange(segs.Count, 1, 3);
            // Last segment must end exactly at the goal.
            Assert.Equal(end, segs[segs.Count - 1].End);
        }

        [Fact]
        public void ComputeRoute_NullEndpoints_ReturnsEmpty()
        {
            Assert.Empty(ConduitRouteEngine.ComputeRoute(null, new XYZ(1, 1, 1), 25, "C"));
            Assert.Empty(ConduitRouteEngine.ComputeRoute(new XYZ(1, 1, 1), null, 25, "C"));
        }

        // ── CountBends ──────────────────────────────────────────────────

        [Fact]
        public void CountBends_StraightLine_Zero()
        {
            var segs = new List<RouteSegment>
            {
                new RouteSegment(new XYZ(0, 0, 0), new XYZ(5, 0, 0), 25, ""),
                new RouteSegment(new XYZ(5, 0, 0), new XYZ(10, 0, 0), 25, ""),
            };
            Assert.Equal(0, ConduitRouteEngine.CountBends(segs));
        }

        [Fact]
        public void CountBends_OneRightAngle_Returns1()
        {
            var segs = new List<RouteSegment>
            {
                new RouteSegment(new XYZ(0, 0, 0), new XYZ(5, 0, 0), 25, ""),
                new RouteSegment(new XYZ(5, 0, 0), new XYZ(5, 5, 0), 25, ""),
            };
            Assert.Equal(1, ConduitRouteEngine.CountBends(segs));
        }

        [Fact]
        public void CountBends_LZRoute_Returns2()
        {
            // X-run, Y-run, Z-run — typical L/Z drop produces 2 bends.
            var segs = ConduitRouteEngine.ComputeRoute(
                new XYZ(0, 0, 0), new XYZ(5, 5, 5), 25, "");
            Assert.Equal(2, ConduitRouteEngine.CountBends(segs));
        }

        [Fact]
        public void CountBends_5DegreeColinearTolerance_NotCountedAsBend()
        {
            // 4° off straight — within 5° tolerance, should not count as a bend.
            var segs = new List<RouteSegment>
            {
                new RouteSegment(new XYZ(0, 0, 0), new XYZ(10, 0, 0), 25, ""),
                new RouteSegment(new XYZ(10, 0, 0), new XYZ(20, 0.7, 0), 25, ""),
            };
            // arctan(0.7/10) ≈ 4° — under threshold.
            Assert.Equal(0, ConduitRouteEngine.CountBends(segs));
        }

        [Fact]
        public void CountBends_NullOrEmpty_ReturnsZero()
        {
            Assert.Equal(0, ConduitRouteEngine.CountBends(null));
            Assert.Equal(0, ConduitRouteEngine.CountBends(new List<RouteSegment>()));
        }

        [Fact]
        public void CountBends_ZeroLengthSegment_Skipped()
        {
            // A zero-length filler between bends doesn't make CountBends miscount.
            var segs = new List<RouteSegment>
            {
                new RouteSegment(new XYZ(0, 0, 0), new XYZ(5, 0, 0), 25, ""),
                new RouteSegment(new XYZ(5, 0, 0), new XYZ(5, 0, 0), 25, ""),  // zero
                new RouteSegment(new XYZ(5, 0, 0), new XYZ(5, 5, 0), 25, ""),
            };
            Assert.Equal(1, ConduitRouteEngine.CountBends(segs));
        }

        // ── SplitAtBendCap ──────────────────────────────────────────────

        [Fact]
        public void SplitAtBendCap_BelowCap_ReturnsSingleGroup()
        {
            var segs = ConduitRouteEngine.ComputeRoute(
                new XYZ(0, 0, 0), new XYZ(5, 0, 0), 25, "");
            var groups = ConduitRouteEngine.SplitAtBendCap(segs, 3);
            Assert.Single(groups);
            Assert.Equal(segs.Count, groups[0].Count);
        }

        [Fact]
        public void SplitAtBendCap_FiveBends_SplitsAtThree()
        {
            // Build a 5-bend path manually: x → y → x → y → x → y.
            var segs = new List<RouteSegment>
            {
                new RouteSegment(new XYZ(0, 0, 0), new XYZ(1, 0, 0), 25, ""),
                new RouteSegment(new XYZ(1, 0, 0), new XYZ(1, 1, 0), 25, ""),  // bend 1
                new RouteSegment(new XYZ(1, 1, 0), new XYZ(2, 1, 0), 25, ""),  // bend 2
                new RouteSegment(new XYZ(2, 1, 0), new XYZ(2, 2, 0), 25, ""),  // bend 3
                new RouteSegment(new XYZ(2, 2, 0), new XYZ(3, 2, 0), 25, ""),  // bend 4 — split
                new RouteSegment(new XYZ(3, 2, 0), new XYZ(3, 3, 0), 25, ""),  // bend 5
            };
            Assert.Equal(5, ConduitRouteEngine.CountBends(segs));
            var groups = ConduitRouteEngine.SplitAtBendCap(segs, 3);
            Assert.True(groups.Count >= 2,
                $"5 bends with cap=3 should split into at least 2 groups, got {groups.Count}");
        }

        [Fact]
        public void SplitAtBendCap_ZeroCap_ReturnsAsSingleGroup()
        {
            // cap=0 disables splitting per the engine's documented behaviour.
            var segs = ConduitRouteEngine.ComputeRoute(
                new XYZ(0, 0, 0), new XYZ(5, 5, 5), 25, "");
            var groups = ConduitRouteEngine.SplitAtBendCap(segs, 0);
            Assert.Single(groups);
        }

        // ── SelectConduitDiameterMm ─────────────────────────────────────

        [Fact]
        public void SelectConduitDiameterMm_NullOrEmpty_Returns20mmDefault()
        {
            Assert.Equal(20, ConduitRouteEngine.SelectConduitDiameterMm(null));
            Assert.Equal(20, ConduitRouteEngine.SelectConduitDiameterMm(new List<StingTools.Core.Electrical.StingCable>()));
        }

        [Fact]
        public void SelectConduitDiameterMm_SingleSmallCable_RoundsUpToStandard()
        {
            var cables = new List<StingTools.Core.Electrical.StingCable>
            {
                new StingTools.Core.Electrical.StingCable
                {
                    CsaMm2 = 2.5, CoreCount = 3,
                    OuterDiameterMm = ConduitRouteEngine.EstimateCableOdMm(2.5)
                }
            };
            double d = ConduitRouteEngine.SelectConduitDiameterMm(cables);
            // Must be one of the standard list values (16/20/25/32/40/50/63/75/100).
            Assert.Contains((int)d, new[] { 16, 20, 25, 32, 40, 50, 63, 75, 100 });
        }

        [Fact]
        public void SelectConduitDiameterMm_MultipleCables_PicksBigger()
        {
            var single = new List<StingTools.Core.Electrical.StingCable>
            {
                new StingTools.Core.Electrical.StingCable { CsaMm2 = 4, CoreCount = 3, OuterDiameterMm = 8.5 }
            };
            var quintuple = new List<StingTools.Core.Electrical.StingCable>();
            for (int i = 0; i < 5; i++)
                quintuple.Add(new StingTools.Core.Electrical.StingCable
                { CsaMm2 = 4, CoreCount = 3, OuterDiameterMm = 8.5 });

            double dSingle = ConduitRouteEngine.SelectConduitDiameterMm(single);
            double dQuint  = ConduitRouteEngine.SelectConduitDiameterMm(quintuple);
            Assert.True(dQuint >= dSingle, $"5 cables ({dQuint}) should size ≥ 1 cable ({dSingle}).");
        }

        [Theory]
        [InlineData(1.5, 6.5)]
        [InlineData(2.5, 7.5)]
        [InlineData(4.0, 8.5)]
        [InlineData(10.0, 11.5)]
        [InlineData(50.0, 22.0)]
        public void EstimateCableOdMm_StandardSizes(double csa, double expectedOd)
        {
            Assert.Equal(expectedOd, ConduitRouteEngine.EstimateCableOdMm(csa));
        }
    }
}
