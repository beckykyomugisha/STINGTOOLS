// StingTools — Scheme evaluator (Phase 195, spec §4).
//
// Walks a scheme's gates, pulls the named metric from the matching provider,
// applies operator/threshold or the points step function, then aggregates:
//   all_required -> AND of required gates (EDGE); achieved level = highest level
//                   whose per-gate thresholds are ALL met.
//   pointSum     -> SUM of points -> certification band (LEED).
//
// The evaluator never names a scheme. Adding BREEAM / Green Star later = a new
// scheme JSON + reuse of providers, ZERO engine changes (acceptance §13.8).
//
// Pure POCO — no Revit dependency. Has dedicated unit tests.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Sustainability
{
    public static class SchemeEvaluator
    {
        /// <summary>
        /// Evaluate one scheme at a chosen target level (e.g. EDGE "Advanced").
        /// </summary>
        public static SchemeResult Evaluate(
            GreenScheme scheme, string targetLevel,
            MetricProviderRegistry providers, SchemeContext ctx)
        {
            var res = new SchemeResult
            {
                SchemeId    = scheme.Id,
                SchemeName  = scheme.Name,
                Aggregation = scheme.Aggregation,
                TargetLevel = string.IsNullOrWhiteSpace(targetLevel) ? scheme.DefaultLevel : targetLevel
            };

            bool isPointSum = string.Equals(scheme.Aggregation, "pointSum", StringComparison.OrdinalIgnoreCase);

            // Resolve the per-gate threshold map for the target level (all_required).
            Dictionary<string, double> levelThresholds = null;
            if (!isPointSum && scheme.Levels != null)
                scheme.Levels.TryGetValue(res.TargetLevel, out levelThresholds);

            foreach (var gate in scheme.Gates)
            {
                var provider = providers.Get(gate.Provider);
                var metric = provider?.Evaluate(ctx);

                var gr = new GateResult
                {
                    GateId    = gate.Id,
                    Label     = gate.Label,
                    Metric    = gate.Metric,
                    Provider  = gate.Provider,
                    Required   = gate.Required,
                    // WS B5 — a delegated gate (EDGE materials) stops being delegated
                    // once the user records the EDGE-app's official figure: it then
                    // contributes to the determined level instead of being caveated.
                    Delegated  = !string.IsNullOrEmpty(gate.Delegated) && !(ctx?.HasOfficial(gate.Metric) ?? false),
                    Unit       = gate.Unit
                };

                if (metric == null)
                {
                    gr.NotEvaluated = true;
                    gr.Note = $"provider '{gate.Provider}' not registered";
                    res.Gates.Add(gr);
                    continue;
                }

                // "Was this metric computed from real model data?" A gate that is a
                // zero-design artefact, a hardcoded indicative default, or a delegated
                // EDGE-app number can NEVER show as an earned pass.
                gr.Computed = metric.IsComputed(gate.Metric);
                string note = metric.GetNote(gate.Metric);
                if (!string.IsNullOrEmpty(note)) gr.Note = note;

                if (gate.Points.Count > 0)
                {
                    // pointSum step function.
                    double val = metric.GetNumber(gate.Metric, 0);
                    gr.IndicativeValue = val;
                    gr.Points = gr.Computed ? StepPoints(gate.Points, val) : 0;
                    gr.Passed = gr.Computed && gr.Points > 0;
                    res.TotalPoints += gr.Points;
                }
                else if (gate.HasThresholdBool)
                {
                    // Boolean prerequisite (e.g. wblca_completed == true).
                    bool b = metric.GetBool(gate.Metric, false);
                    gr.IndicativeValue = b ? 1 : 0;
                    gr.Passed = gr.Computed && b == gate.ThresholdBool;
                    gr.Threshold = gate.ThresholdBool ? 1 : 0;
                }
                else
                {
                    // Numeric gate (operator against the level threshold).
                    double val = metric.GetNumber(gate.Metric, 0);
                    gr.IndicativeValue = val;
                    double threshold = 0;
                    if (levelThresholds != null) levelThresholds.TryGetValue(gate.Id, out threshold);
                    gr.Threshold = threshold;
                    // Not-computed ⇒ never a pass (e.g. energy 100% from zero design).
                    gr.Passed = gr.Computed && Compare(val, gate.Operator, threshold);
                }

                res.Gates.Add(gr);
            }

            if (isPointSum)
            {
                // Prerequisites must pass for any band to be achievable.
                bool prereqsOk = res.Gates.Where(g => g.Required).All(g => g.Passed);
                res.Band = prereqsOk ? BandFor(scheme, res.TotalPoints) : "None";
                res.AchievedLevel = res.Band;
                res.Passed = prereqsOk && !string.Equals(res.Band, "None", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // all_required: AND of required gates against the target level.
                // Delegated gates (EDGE materials = EDGE-app-owned) are caveated, not
                // blocking — STING can't certify them, so they don't gate the
                // STING-determinable result. A not-computed NON-delegated gate (zero-
                // design energy, indicative-default water) DOES block (its Passed=false).
                var determinable = res.Gates.Where(g => g.Required && !g.Delegated).ToList();
                res.Passed = determinable.Count > 0 && determinable.All(g => g.Passed);
                res.AchievedLevel = HighestAchievableLevel(scheme, providers, ctx);
            }

            return res;
        }

        /// <summary>Step function: largest pts whose pct threshold is met.</summary>
        public static int StepPoints(List<PointStep> steps, double value)
        {
            int pts = 0;
            foreach (var s in steps.OrderBy(s => s.Pct))
                if (value >= s.Pct) pts = s.Pts;
            return pts;
        }

        public static bool Compare(double value, string op, double threshold)
        {
            switch (op)
            {
                case ">=": return value >= threshold;
                case ">":  return value > threshold;
                case "<=": return value <= threshold;
                case "<":  return value < threshold;
                case "==": return Math.Abs(value - threshold) < 1e-6;
                default:    return value >= threshold;
            }
        }

        private static string BandFor(GreenScheme scheme, int points)
        {
            string band = "None";
            int best = -1;
            foreach (var kv in scheme.CertificationBands)
                if (points >= kv.Value && kv.Value > best) { best = kv.Value; band = kv.Key; }
            return band;
        }

        /// <summary>For all_required schemes: the highest level whose per-gate
        /// thresholds are ALL met by the current metrics.</summary>
        private static string HighestAchievableLevel(
            GreenScheme scheme, MetricProviderRegistry providers, SchemeContext ctx)
        {
            if (scheme.Levels == null || scheme.Levels.Count == 0) return "None";

            // Cache metric values + computed flags per gate once.
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var computed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var gate in scheme.Gates)
            {
                var metric = providers.Get(gate.Provider)?.Evaluate(ctx);
                values[gate.Id]   = metric?.GetNumber(gate.Metric, 0) ?? 0;
                computed[gate.Id] = metric?.IsComputed(gate.Metric) ?? true;
            }

            // Levels are typically ordered Certified < Advanced < ZeroCarbon by the
            // energy threshold; rank by the max energy threshold so we report the
            // highest level that fully passes.
            string achieved = "None";
            int bestRank = -1;
            foreach (var lvl in scheme.Levels)
            {
                bool allMet = true;
                // Determinable, non-delegated, numeric required gates only. A delegated
                // gate (EDGE materials) is the EDGE app's to certify; a not-computed
                // gate blocks (can't be silently passed off a zero/default value).
                foreach (var gate in scheme.Gates.Where(g =>
                             g.Required && g.Points.Count == 0 && !g.HasThresholdBool
                             && (string.IsNullOrEmpty(g.Delegated) || ctx.HasOfficial(g.Metric))))
                {
                    if (!computed.TryGetValue(gate.Id, out var c) || !c) { allMet = false; break; }
                    lvl.Value.TryGetValue(gate.Id, out double thr);
                    if (!Compare(values.TryGetValue(gate.Id, out var v) ? v : 0, gate.Operator, thr))
                    { allMet = false; break; }
                }
                if (allMet)
                {
                    int rank = lvl.Value.Values.Count > 0 ? (int)lvl.Value.Values.Max() : 0;
                    if (rank > bestRank) { bestRank = rank; achieved = lvl.Key; }
                }
            }
            return achieved;
        }
    }
}
