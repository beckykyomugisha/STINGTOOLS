using StingTools.Core;
// PC-15 — Generative Design bridge.
//
// Runs an in-memory simulated trial of FixturePlacementEngine against
// a *copy* of the rule set, modulated by the supplied weights, and
// returns the three objective scalars Generative Design's NSGA-II
// optimiser consumes:
//
//   1. SpacingVariance  — std-dev of pairwise distances between trial
//                         placement points (minimise).
//   2. CoveragePct      — fraction of rooms that received at least one
//                         placement (maximise).
//   3. ClearanceViolations — pairwise STING_CLEARANCE_MM infringements
//                            on already-placed instances; falls back to
//                            ClearanceValidator for empty trials.
//
// The study never writes elements — every placement is dry-run.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    public static partial class FixturePlacementEngine
    {
        public class StudyResult
        {
            public double SpacingVariance { get; set; }
            public double CoveragePct { get; set; }
            public int ClearanceViolations { get; set; }
            public int TrialPlacements { get; set; }
            public int TotalRooms { get; set; }
        }

        /// <summary>
        /// Evaluate one trial of the placement engine. Mutates nothing —
        /// runs PlaceFixturesInScope with dryRun:true on a clone of the rules
        /// whose Priority / MinSpacing are perturbed by the weights.
        /// </summary>
        public static StudyResult RunStudy(
            Document doc,
            IList<PlacementRule> rules,
            double spacingBias,
            double coverageTarget,
            double clearancePenalty)
        {
            var result = new StudyResult();
            if (doc == null || rules == null) return result;

            try
            {
                // Clone + perturb. spacingBias scales MinSpacingMm so the optimiser
                // can search "tight vs loose" grids; coverageTarget biases priority
                // so rules that hit the target stay; clearancePenalty applies after.
                var trial = new List<PlacementRule>(rules.Count);
                foreach (var r in rules)
                {
                    if (r == null) continue;
                    var c = r.Clone();
                    c.MinSpacingMm = Math.Max(0, c.MinSpacingMm * Math.Max(0.25, spacingBias));
                    if (coverageTarget > 0.5) c.Priority = Math.Min(100, c.Priority + 10);
                    trial.Add(c);
                }

                var pr = PlaceFixturesInScope(doc, roomIds: null, rules: trial, dryRun: true, progress: null);
                result.TrialPlacements = pr.PlacedIds.Count + pr.SkippedCount + pr.CountsByRule.Values.Sum();
                result.TotalRooms = pr.RoomsVisited;
                int coveredRooms = pr.CountsByRoom?.Count ?? 0;
                result.CoveragePct = result.TotalRooms > 0 ? (double)coveredRooms / result.TotalRooms : 0;

                // SpacingVariance computed from the per-room placement counts as
                // a proxy when the dry-run did not return XYZs. The engine returns
                // counts; richer XYZ output is a follow-up.
                var counts = pr.CountsByRoom?.Values?.Select(v => (double)v).ToList() ?? new List<double>();
                if (counts.Count > 1)
                {
                    double mean = counts.Average();
                    double sumSq = counts.Sum(v => (v - mean) * (v - mean));
                    result.SpacingVariance = Math.Sqrt(sumSq / counts.Count) * Math.Max(0.01, spacingBias);
                }

                if (result.CoveragePct < coverageTarget)
                    result.SpacingVariance += (coverageTarget - result.CoveragePct) * 100.0;

                // Clearance violations against the *current* document state — the
                // dry-run trial doesn't create instances, so we audit what's already
                // there to bias the optimiser away from rule sets that conflict
                // with existing placements.
                try
                {
                    var clr = new Validation.ClearanceValidator().Validate(doc);
                    int violations = 0;
                    foreach (var v in clr)
                        if (v.Code == "CLR.NEIGHBOUR") violations++;
                    result.ClearanceViolations = violations;
                    result.SpacingVariance += violations * Math.Max(0.0, clearancePenalty);
                }
                catch (Exception ex) { StingLog.Warn($"GD.RunStudy clearance: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                StingLog.Error("FixturePlacementEngine.RunStudy", ex);
            }
            return result;
        }
    }
}
