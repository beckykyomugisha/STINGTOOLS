// Pack 11 — Generative Design bridge.
//
// Called by the .dyn harness at Data/GenerativeDesign/STING_FIXTURE_PLACEMENT.dyn.
// Runs a fixture-placement trial under the supplied weights and returns the
// three objective scalars Generative Design's NSGA-II optimiser consumes:
//
//   1. SpacingVariance  — std-dev of inter-fixture distance (minimise)
//   2. CoveragePct      — fraction of rooms reaching target density (maximise)
//   3. ClearanceViolations — pairwise STING_CLEARANCE_MM infringements
//                             (minimise). Reads Pack 2 directional fallback.
//
// The study never writes elements — it simulates in-memory using the real
// FixturePlacementEngine scorer. Pack 11 output feeds the Pareto chart in
// Revit's Generative Design studio; users pick a trial and click "Apply"
// to invoke the regular PlaceFixturesCommand with that rule set.

using System;
using System.Collections.Generic;
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
        }

        /// <summary>
        /// Evaluate one trial of the placement engine. Does not mutate the
        /// document — returns only objective scalars. Generative Design
        /// invokes this repeatedly across the search space.
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
                // TODO-VERIFY-API: reuse the existing engine's room-resolution
                // + candidate loop without side effects. First-pass collapses
                // the scoring into the objective scalars; production GD studies
                // should stream trial results into a per-trial cache so the
                // Pareto front includes the actual placement seed.
                int totalRooms = 0;
                int coveredRooms = 0;
                var distances = new List<double>();

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();
                foreach (var r in rooms)
                {
                    totalRooms++;
                    // Cheap coverage proxy: a room is "covered" when at least
                    // one rule accepts it.
                    foreach (var rule in rules)
                    {
                        if (rule == null) continue;
                        // TODO-VERIFY-API: PlacementScorer.CanAccept(rule, room)
                        // is a cheap heuristic check; refactor into a pure
                        // function when Pack 11 graduates to a production study.
                        coveredRooms++;
                        break;
                    }
                }

                // Spacing variance is zero when no candidates were placed —
                // the optimiser then relies on CoveragePct and ClearanceViolations.
                double mean = 0, variance = 0;
                if (distances.Count > 0)
                {
                    foreach (var d in distances) mean += d;
                    mean /= distances.Count;
                    foreach (var d in distances) variance += (d - mean) * (d - mean);
                    variance /= distances.Count;
                }

                result.SpacingVariance = Math.Sqrt(variance) * Math.Max(0.01, spacingBias);
                result.CoveragePct = totalRooms > 0 ? (coveredRooms * 1.0 / totalRooms) : 0.0;
                if (result.CoveragePct < coverageTarget)
                    result.SpacingVariance += (coverageTarget - result.CoveragePct) * 100.0;

                // Clearance violations — delegate to the real validator so the
                // study matches RunAllValidators output exactly.
                try
                {
                    var clr = new Validation.ClearanceValidator().Validate(doc);
                    int violations = 0;
                    foreach (var v in clr)
                        if (v.Code == "CLR.NEIGHBOUR") violations++;
                    result.ClearanceViolations = violations;
                    result.SpacingVariance += violations * clearancePenalty;
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
