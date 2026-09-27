using System;
using System.Linq;
using StingTools.Core.Fire;
using StingTools.Core.Gas;
using StingTools.Core.Mep.Networks;
using Xunit;

namespace StingTools.Mep.Tests
{
    public class PipeNetworkTests
    {
        private static SprinklerDesignCriteria Oh1() => new SprinklerDesignCriteria
        {
            DensityMmMin = 5.0, AreaPerHeadM2 = 12, MinHeadPressureBar = 0.35, DefaultC = 120,
            MaxVelocityMs = 10, MaxValveVelocityMs = 6
        };

        private static PipeNetwork Net(params (string a, string b, double l, double d)[] links)
        {
            var n = new PipeNetwork();
            foreach (var (a, b, l, d) in links)
            {
                foreach (var id in new[] { a, b })
                    if (!n.Nodes.ContainsKey(id))
                        n.AddNode(new NetNode
                        {
                            Id = id, Label = id,
                            Kind = id == "S" ? NetNodeKind.Source : id.StartsWith("H") ? NetNodeKind.Terminal : NetNodeKind.Junction,
                            KFactor = id.StartsWith("H") ? 80 : 0,
                            LoadKw = id.StartsWith("A") ? 0 : 0
                        });
                n.Connect(new NetLink { Id = $"{a}-{b}", Label = $"{a}-{b}", A = a, B = b, LengthM = l, BoreMm = d, HazenWilliamsC = 120, Kind = NetLinkKind.Pipe });
            }
            return n;
        }

        [Fact]
        public void SingleHeadMatchesTheTreeSolverExactly()
        {
            var net = Net(("S", "H1", 10, 27.3));
            var r = SprinklerNetworkHydraulics.Solve(net, Oh1());
            Assert.True(r.Ok);
            double expected = 0.5625 + SprinklerHydraulics.FrictionLossBar(60, 10, 27.3, 120);
            Assert.Equal(expected, r.SourcePressureBar, 4);
            Assert.Equal(60.0, r.SourceFlowLpm, 2);
        }

        [Fact]
        public void TreeWithUnequalBranchesAgreesWithTheHandMethod()
        {
            // Network: S —5 m DN36— J —1 m— H1 ; J —20 m— H2
            var net = Net(("S", "J", 5, 36.0), ("J", "H1", 1, 27.3), ("J", "H2", 20, 27.3));
            var nr = SprinklerNetworkHydraulics.Solve(net, Oh1());

            var main = new FlowNode { Id = "S", Kind = FlowNodeKind.Pipe, LengthM = 5, DiameterMm = 36, HazenWilliamsC = 120 };
            var tee = main.Add(new FlowNode { Id = "J", Kind = FlowNodeKind.Fitting });
            tee.Add(new FlowNode { Id = "p1", Kind = FlowNodeKind.Pipe, LengthM = 1, DiameterMm = 27.3, HazenWilliamsC = 120 })
               .Add(new FlowNode { Id = "H1", Kind = FlowNodeKind.Terminal, KFactor = 80 });
            tee.Add(new FlowNode { Id = "p2", Kind = FlowNodeKind.Pipe, LengthM = 20, DiameterMm = 27.3, HazenWilliamsC = 120 })
               .Add(new FlowNode { Id = "H2", Kind = FlowNodeKind.Terminal, KFactor = 80 });
            var tr = SprinklerHydraulics.Solve(main, Oh1());

            Assert.Equal(tr.SourcePressureBar, nr.SourcePressureBar, 2);
            Assert.InRange(nr.SourceFlowLpm, tr.SourceFlowLpm * 0.99, tr.SourceFlowLpm * 1.01);
            Assert.Equal("H2", nr.MostRemoteHead.Node.Id);
            Assert.True(Math.Abs(nr.MostRemoteHead.RequiredBar - nr.MostRemoteHead.PressureBar) < 1e-4);
        }

        [Fact]
        public void ParallelPipesSplitByHazenWilliamsResistance()
        {
            // Two pipes in parallel between S and J, then one head.
            var net = Net(("S", "J", 10, 27.3), ("S", "J", 10, 36.0), ("J", "H1", 1, 27.3));
            net.Links[1].Id = "S-J big";
            Assert.Equal(1, net.LoopCount);
            var r = SprinklerNetworkHydraulics.Solve(net, Oh1());
            Assert.True(r.Ok);
            double q1 = r.Links.Single(l => l.Link.BoreMm == 27.3 && l.Link.A == "S").FlowLpm;
            double q2 = r.Links.Single(l => l.Link.BoreMm == 36.0).FlowLpm;
            // Equal head loss: Q ∝ d^(4.87/1.85)
            Assert.Equal(Math.Pow(36.0 / 27.3, 4.87 / 1.85), q2 / q1, 3);
            Assert.Equal(r.SourceFlowLpm, q1 + q2, 3);
        }

        [Fact]
        public void GridConservesFlowAndMeetsEveryHead()
        {
            // 2 × 2 grid fed at one corner, a head at every other corner.
            var net = Net(("S", "A", 3, 36), ("A", "B", 4, 27.3), ("A", "C", 4, 27.3),
                          ("B", "D", 4, 27.3), ("C", "D", 4, 27.3),
                          ("B", "H1", 0.5, 27.3), ("C", "H2", 0.5, 27.3), ("D", "H3", 0.5, 27.3));
            Assert.Equal(1, net.LoopCount);
            var r = SprinklerNetworkHydraulics.Solve(net, Oh1());
            Assert.True(r.Ok);
            Assert.All(r.Heads, h => Assert.True(h.PressureBar >= h.RequiredBar - 1e-4));
            double intoGrid = r.Links.Single(l => l.Link.A == "S").FlowLpm;
            Assert.Equal(r.SourceFlowLpm, intoGrid, 3);
            Assert.Equal("H3", r.MostRemoteHead.Node.Id);                 // furthest corner
            Assert.Equal(r.Heads.Single(h => h.Node.Id == "H1").FlowLpm,
                         r.Heads.Single(h => h.Node.Id == "H2").FlowLpm, 3);   // symmetry
        }

        [Fact]
        public void StaticHeadIsIncluded()
        {
            var flat = Net(("S", "H1", 5, 27.3));
            var raised = Net(("S", "H1", 5, 27.3));
            raised.Nodes["H1"].ElevationM = 4;
            double dp = SprinklerNetworkHydraulics.Solve(raised, Oh1()).SourcePressureBar
                      - SprinklerNetworkHydraulics.Solve(flat, Oh1()).SourcePressureBar;
            Assert.Equal(4 * SprinklerHydraulics.StaticBarPerM, dp, 3);
        }

        [Fact]
        public void DeadEndsArePrunedButTerminalsKept()
        {
            var net = Net(("S", "J", 5, 27.3), ("J", "H1", 1, 27.3), ("J", "X", 3, 27.3), ("X", "Y", 3, 27.3));
            int removed = net.PruneDeadEnds();
            Assert.Equal(2, removed);
            Assert.False(net.Nodes.ContainsKey("X"));
            Assert.True(net.Nodes.ContainsKey("H1"));
            Assert.Equal(2, net.Links.Count);
        }

        private static readonly GasProperties Ng = new GasProperties { Id = "NG", RelativeDensity = 0.6, CalorificValueMJm3 = 38.7, MaxDropMbar = 1.0 };

        [Fact]
        public void GasSinglePipeMatchesPole()
        {
            var net = Net(("S", "A1", 15, 20.2));
            net.Nodes["A1"].Kind = NetNodeKind.Terminal;
            net.Nodes["A1"].LoadKw = 30;
            var r = GasNetworkCheck.Check(net, Ng);
            Assert.True(r.Converged);
            double q = GasPipeSizer.FlowM3h(30, Ng);
            Assert.Equal(GasPipeSizer.DropMbar(q, 15, 20.2, 0.6), r.WorstDropMbar, 4);
            Assert.True(r.Ok);
        }

        [Fact]
        public void GasRingMainSplitsByBore()
        {
            // Ring: two routes from S to the appliance node, 22 mm and 15 mm.
            var net = Net(("S", "J", 10, 20.2), ("S", "J", 10, 13.6), ("J", "A1", 1, 20.2));
            net.Nodes["A1"].Kind = NetNodeKind.Terminal;
            net.Nodes["A1"].LoadKw = 40;
            var r = GasNetworkCheck.Check(net, Ng);
            Assert.True(r.Converged);
            Assert.Equal(1, r.Loops);
            double big = r.Links.Single(l => l.Link.BoreMm == 20.2 && l.Link.B == "J").FlowM3h;
            double small = r.Links.Single(l => l.Link.BoreMm == 13.6).FlowM3h;
            Assert.Equal(Math.Pow(20.2 / 13.6, 2.5), big / small, 3);      // equal drop: Q ∝ d^2.5
            Assert.Equal(GasPipeSizer.FlowM3h(40, Ng), big + small, 4);
        }
    }
}
