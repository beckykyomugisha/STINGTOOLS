// StingTools — Measure target-seeker (WS I14).
//
// Auto-selects the least-cost set of measures to reach the chosen EDGE level's
// per-gate targets (e.g. energy 40% / water 20% / materials 20%), using the
// per-measure cost-per-%-gain already in the LCC. Greedy by cost-efficiency
// (cheapest £/% first) per gate — a documented planning heuristic (gains assumed
// additive). Outputs the recommended measure set + the residual gap per gate so
// the user sees what's still short.
//
// Pure POCO — no Revit dependency. Unit-tested. The Revit command builds the
// candidate measures from the measure registry + the current %-achieved from the
// dashboard, then calls this.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Sustainability
{
    public class OptimizerMeasure
    {
        public string Id      { get; set; } = "";
        public string Name    { get; set; } = "";
        public string Gate    { get; set; } = "";   // energy / water / materials
        public double GainPct { get; set; }          // %-gain this measure adds to its gate
        public double Capex   { get; set; }
        /// <summary>Cost per percentage-point of gain (∞ when no gain).</summary>
        public double CostPerPct => GainPct > 0 ? Capex / GainPct : double.PositiveInfinity;
    }

    public class GateTarget
    {
        public string Gate       { get; set; } = "";
        public double TargetPct  { get; set; }
        public double CurrentPct { get; set; }   // already achieved by the design
    }

    public class OptimizerResult
    {
        public List<OptimizerMeasure> Selected { get; } = new List<OptimizerMeasure>();
        public double TotalCapex { get; set; }
        public Dictionary<string, double> AchievedByGate { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> ResidualGapByGate { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool>   GateMet { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when every gate target was reached.</summary>
        public bool AllGatesMet => GateMet.Values.All(v => v);
    }

    public static class SustainMeasureOptimizer
    {
        /// <summary>Pick the least-cost measure set to reach each gate target. Greedy:
        /// per gate, add the cheapest-per-% measures until the target is met (or none
        /// remain). Reports the selected set, total capex, achieved % + residual gap.</summary>
        public static OptimizerResult Reach(IEnumerable<OptimizerMeasure> measures, IEnumerable<GateTarget> targets)
        {
            var res = new OptimizerResult();
            var all = (measures ?? Enumerable.Empty<OptimizerMeasure>()).Where(m => m != null).ToList();
            var goals = (targets ?? Enumerable.Empty<GateTarget>()).Where(t => t != null).ToList();

            foreach (var goal in goals)
            {
                double achieved = goal.CurrentPct;
                // Candidates for this gate, cheapest cost-per-% first; zero-gain skipped.
                var candidates = all
                    .Where(m => string.Equals(m.Gate, goal.Gate, StringComparison.OrdinalIgnoreCase) && m.GainPct > 0)
                    .OrderBy(m => m.CostPerPct)
                    .ToList();

                foreach (var m in candidates)
                {
                    if (achieved >= goal.TargetPct) break;
                    res.Selected.Add(m);
                    res.TotalCapex += m.Capex;
                    achieved += m.GainPct;
                }

                res.AchievedByGate[goal.Gate]    = achieved;
                res.ResidualGapByGate[goal.Gate] = Math.Max(0, goal.TargetPct - achieved);
                res.GateMet[goal.Gate]           = achieved >= goal.TargetPct;
            }

            return res;
        }
    }
}
