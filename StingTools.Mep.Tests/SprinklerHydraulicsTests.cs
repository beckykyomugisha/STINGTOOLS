using System.Linq;
using StingTools.Core.Fire;
using StingTools.Core.Mep.Networks;
using Xunit;

namespace StingTools.Mep.Tests
{
    public class SprinklerHydraulicsTests
    {
        private static SprinklerDesignCriteria Oh1(double areaPerHead = 12) => new SprinklerDesignCriteria
        {
            HazardId = "OH1", DensityMmMin = 5.0, DesignAreaM2 = 0, AreaPerHeadM2 = areaPerHead,
            MinHeadPressureBar = 0.35, DefaultC = 120, MaxVelocityMs = 10, MaxValveVelocityMs = 6
        };

        private static FlowNode Pipe(string id, double lengthM, double boreMm, double z = 0)
            => new FlowNode { Id = id, Label = id, Kind = FlowNodeKind.Pipe, LengthM = lengthM, DiameterMm = boreMm, ElevationM = z, HazenWilliamsC = 120 };

        private static FlowNode Head(string id, double k = 80, double z = 0)
            => new FlowNode { Id = id, Label = id, Kind = FlowNodeKind.Terminal, KFactor = k, ElevationM = z };

        [Fact]
        public void HazenWilliamsMatchesTheEn12845Formula()
        {
            // 6.05e5 · 100^1.85 / (120^1.85 · 27.3^4.87), hand-calculated.
            Assert.Equal(0.04377, SprinklerHydraulics.FrictionLossBar(100, 1, 27.3, 120), 4);
            Assert.Equal(0, SprinklerHydraulics.FrictionLossBar(0, 1, 27.3, 120));
        }

        [Fact]
        public void SingleHeadNeedsItsMinimumFlowPlusFriction()
        {
            var root = Pipe("riser", 10, 27.3);
            root.Add(Head("H1"));
            var r = SprinklerHydraulics.Solve(root, Oh1());

            Assert.True(r.Ok);
            Assert.Equal(60.0, r.MinHeadFlowLpm, 6);                // 5 mm/min × 12 m²
            // p_head = (60/80)² = 0.5625 bar > 0.35 minimum; + friction through 10 m.
            Assert.Equal(0.5625 + SprinklerHydraulics.FrictionLossBar(60, 10, 27.3, 120), r.SourcePressureBar, 6);
            Assert.Equal(60.0, r.SourceFlowLpm, 6);
        }

        [Fact]
        public void MinimumHeadPressureGovernsWhenFlowNeedsLess()
        {
            var root = Pipe("riser", 1, 27.3);
            root.Add(Head("H1", k: 115));                            // (60/115)² = 0.27 < 0.35
            var r = SprinklerHydraulics.Solve(root, Oh1());
            var head = r.Heads.Single();
            Assert.Equal(0.35, head.InletPressureBar, 6);
            Assert.True(head.FlowLpm > 60.0);
        }

        [Fact]
        public void RiseAddsStaticHead()
        {
            var flat = Pipe("riser", 5, 27.3);
            flat.Add(Head("H1", z: 0));
            var raised = Pipe("riser", 5, 27.3);
            raised.Add(Head("H1", z: 3));
            double dp = SprinklerHydraulics.Solve(raised, Oh1()).SourcePressureBar
                      - SprinklerHydraulics.Solve(flat, Oh1()).SourcePressureBar;
            Assert.Equal(3 * SprinklerHydraulics.StaticBarPerM, dp, 6);
        }

        [Fact]
        public void UnequalBranchesAreBalancedAtTheJunction()
        {
            // Main feeds a tee; the long branch governs, the short branch is
            // fed at the same pressure and so over-delivers.
            var main = Pipe("main", 5, 36.0);
            var tee = main.Add(new FlowNode { Id = "tee", Label = "tee", Kind = FlowNodeKind.Fitting });
            var near = tee.Add(Pipe("near", 1, 27.3));
            near.Add(Head("Hnear"));
            var far = tee.Add(Pipe("far", 20, 27.3));
            far.Add(Head("Hfar"));

            var r = SprinklerHydraulics.Solve(main, Oh1());
            Assert.True(r.Ok);
            var hNear = r.Heads.Single(h => h.Node.Id == "Hnear");
            var hFar = r.Heads.Single(h => h.Node.Id == "Hfar");

            Assert.Equal(60.0, hFar.FlowLpm, 1);                      // remote head at its minimum
            Assert.True(hNear.FlowLpm > hFar.FlowLpm);                // near head over-delivers
            Assert.Equal("Hfar", r.MostRemoteHead.Id);
            Assert.Equal(hNear.FlowLpm + hFar.FlowLpm, r.SourceFlowLpm, 1);
            // Every head at or above the minimum.
            Assert.All(r.Heads, h => Assert.True(h.FlowLpm >= 60.0 - 1e-6));
        }

        [Fact]
        public void EqualBranchesShareFlowEqually()
        {
            var main = Pipe("main", 5, 36.0);
            var tee = main.Add(new FlowNode { Id = "tee", Kind = FlowNodeKind.Fitting });
            tee.Add(Pipe("a", 4, 27.3)).Add(Head("Ha"));
            tee.Add(Pipe("b", 4, 27.3)).Add(Head("Hb"));
            var r = SprinklerHydraulics.Solve(main, Oh1());
            var heads = r.Heads.ToList();
            Assert.Equal(heads[0].FlowLpm, heads[1].FlowLpm, 6);
            Assert.Equal(120.0, r.SourceFlowLpm, 6);
        }

        [Fact]
        public void FittingInheritsTheUpstreamBoreForItsEquivalentLength()
        {
            var main = Pipe("main", 5, 36.0);
            var elbow = main.Add(new FlowNode { Id = "elbow", Kind = FlowNodeKind.Fitting, EquivLengthDiameters = 30 });
            elbow.Add(Pipe("drop", 1, 27.3)).Add(Head("H"));
            var r = SprinklerHydraulics.Solve(main, Oh1());
            Assert.Equal(36.0, elbow.DiameterMm, 6);
            Assert.Equal(30 * 36.0 / 1000.0, elbow.EquivLengthM, 9);
            Assert.True(r.Nodes[elbow].FrictionBar > 0);
        }

        [Fact]
        public void HeadWithoutKFactorStopsTheCalculation()
        {
            var root = Pipe("riser", 5, 27.3);
            root.Add(Head("H1", k: 0));
            var r = SprinklerHydraulics.Solve(root, Oh1());
            Assert.False(r.Ok);
            Assert.Contains(r.Warnings, w => w.Contains("K-factor"));
        }

        [Fact]
        public void TooFewHeadsForTheAreaOfOperationIsReported()
        {
            var root = Pipe("riser", 5, 27.3);
            root.Add(Head("H1"));
            var c = Oh1(areaPerHead: 0);
            c.DesignAreaM2 = 72;
            var r = SprinklerHydraulics.Solve(root, c);
            Assert.Equal(72.0, r.AreaPerHeadM2, 6);                   // design area ÷ 1 head
            Assert.True(r.Ok);
        }

        [Fact]
        public void OverVelocityIsFlagged()
        {
            var root = Pipe("tiny", 2, 10.0);
            root.Add(Head("H1"));
            var r = SprinklerHydraulics.Solve(root, Oh1());
            Assert.True(r.Nodes[root].OverVelocity);
            Assert.Contains(r.Warnings, w => w.Contains("m/s"));
        }
    }
}
