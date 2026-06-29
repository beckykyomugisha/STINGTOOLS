using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I14 — least-cost measure set to reach the EDGE per-gate targets.
    public class MeasureOptimizerTests
    {
        private static OptimizerMeasure M(string name, string gate, double gain, double capex)
            => new OptimizerMeasure { Id = name, Name = name, Gate = gate, GainPct = gain, Capex = capex };

        [Fact]
        public void PicksCheapestPerPct_UntilTargetMet()
        {
            var measures = new[]
            {
                M("LED lighting",  "energy", 10, 1000),   // 100 /%
                M("Better chiller","energy", 10, 3000),   // 300 /%
                M("PV array",      "energy", 20, 1500),   // 75  /% — most efficient
            };
            var targets = new[] { new GateTarget { Gate = "energy", TargetPct = 25, CurrentPct = 0 } };

            var r = SustainMeasureOptimizer.Reach(measures, targets);
            Assert.True(r.GateMet["energy"]);
            // PV (20%, cheapest/%) first → 20; then LED (10%, next cheapest/%) → 30 ≥ 25.
            Assert.Contains(r.Selected, s => s.Name == "PV array");
            Assert.Contains(r.Selected, s => s.Name == "LED lighting");
            Assert.DoesNotContain(r.Selected, s => s.Name == "Better chiller");
            Assert.Equal(2500, r.TotalCapex, 0);   // 1500 + 1000
        }

        [Fact]
        public void CurrentDesign_CountsTowardTarget()
        {
            var measures = new[] { M("PV", "energy", 20, 1500) };
            var targets = new[] { new GateTarget { Gate = "energy", TargetPct = 25, CurrentPct = 10 } };
            var r = SustainMeasureOptimizer.Reach(measures, targets);
            // 10% already + 20% PV = 30 ≥ 25 with just the one measure.
            Assert.True(r.GateMet["energy"]);
            Assert.Single(r.Selected);
        }

        [Fact]
        public void ResidualGap_ReportedWhenUnreachable()
        {
            var measures = new[] { M("PV", "energy", 5, 1000) };
            var targets = new[] { new GateTarget { Gate = "energy", TargetPct = 40, CurrentPct = 0 } };
            var r = SustainMeasureOptimizer.Reach(measures, targets);
            Assert.False(r.GateMet["energy"]);
            Assert.Equal(35, r.ResidualGapByGate["energy"], 1);   // 40 - 5
            Assert.False(r.AllGatesMet);
        }

        [Fact]
        public void MultiGate_HandledIndependently()
        {
            var measures = new[]
            {
                M("PV",          "energy",   20, 1500),
                M("Low-flow WC", "water",    20, 200),
                M("Timber frame","materials",20, 5000),
            };
            var targets = new[]
            {
                new GateTarget { Gate = "energy",    TargetPct = 20, CurrentPct = 0 },
                new GateTarget { Gate = "water",     TargetPct = 20, CurrentPct = 0 },
                new GateTarget { Gate = "materials", TargetPct = 20, CurrentPct = 0 },
            };
            var r = SustainMeasureOptimizer.Reach(measures, targets);
            Assert.True(r.AllGatesMet);
            Assert.Equal(3, r.Selected.Count);
            Assert.Equal(6700, r.TotalCapex, 0);
        }

        [Fact]
        public void ZeroGainMeasures_Ignored()
        {
            var measures = new[] { M("Decorative", "energy", 0, 500) };
            var targets = new[] { new GateTarget { Gate = "energy", TargetPct = 10, CurrentPct = 0 } };
            var r = SustainMeasureOptimizer.Reach(measures, targets);
            Assert.Empty(r.Selected);
            Assert.False(r.GateMet["energy"]);
        }

        [Fact]
        public void Empty_IsSafe()
        {
            var r = SustainMeasureOptimizer.Reach(null, null);
            Assert.Empty(r.Selected);
            Assert.True(r.AllGatesMet);   // vacuously (no gates)
        }
    }
}
