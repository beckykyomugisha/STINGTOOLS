// StingTools — PlacementAdvisoryValidator.
//
// Post-placement pass that answers a question the Centre could not answer
// before: "I set this field on the rule — did it do anything?"
//
// Several PlacementRule specification fields are consumed only on specific
// code paths:
//
//   MinSlopePercent        InWallChaseRouter / WallFollowerRouter -> SlopeValidator
//   EmitSupports           RoutingSupportPlacer / WallFollowerRouter
//   Material               RoutingSupportPlacer (segment stamping)
//   InsulationThicknessMm  RoutingSupportPlacer / InWallChaseRouter
//   NominalDiameterMm      InWallChaseRouter chase depth / RoutingSupportPlacer
//   ExposureClass          InWallChaseRouter -> ConcreteCoverTable
//   MinUniformityRatio     LightingGridCalculator
//   MaintenanceClearance   MaintenanceAccessValidator, via STING_MAINT_CLEAR_TXT
//
// Every one of those paths is conditional. A rule that sets MinSlopePercent
// but leaves RoutingMode = NONE runs no router, so the field is inert — and
// until now nothing said so; the run reported success and the user assumed
// the slope had been applied. This validator emits one advisory per inert
// field on rules that actually fired, into PlacementResult.Warnings, where
// the Centre's result panel already renders them.
//
// It is advisory only: it never blocks a run and never edits the model.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Placement
{
    public static class PlacementAdvisoryValidator
    {
        // Anchor types routed through LightingGridCalculator, the only consumer
        // of MinUniformityRatio. Mirrors the aliases in PlacementRulesViewModel.
        private static readonly HashSet<string> LightingGridAnchors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "LIGHTING_GRID", "LUX_GRID", "EN12464" };

        // Clearance class codes MaintenanceAccessValidator.ResolveClearance
        // understands. Anything else resolves to null and is silently skipped
        // by that validator, so flag it here instead.
        private static readonly HashSet<string> MaintenanceClearanceCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "FRONT_600", "FRONT_1000", "SIDES_300", "TOP_900" };

        // Glazing specs that satisfy a ToughenedGlazingRequired flag.
        private static readonly HashSet<string> ToughenedGlazingSpecs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "TOUGHENED", "LAMINATED" };

        /// <summary>
        /// Emit advisories for specification fields that are set on a rule but
        /// cannot take effect given that rule's configuration.
        ///
        /// Scoping: when the run produced placement counts
        /// (<paramref name="result"/>.CountsByRule is non-empty) only rules that
        /// actually fired are considered — a rule that placed nothing already
        /// reports that through the per-rule diagnostics, and repeating it here
        /// would be noise. When there are no counts — a null/empty result, as in
        /// the unit tests, or a run that placed nothing at all — every rule is
        /// considered, so a fully mis-configured pack still gets feedback.
        /// </summary>
        public static List<string> Validate(IEnumerable<PlacementRule> rules, PlacementResult result)
        {
            var advisories = new List<string>();
            if (rules == null) return advisories;

            foreach (var rule in rules)
            {
                if (rule == null) continue;

                // Scope to rules that fired when we have counts to scope by.
                // CountsByRule is keyed by MergeKey.
                if (result?.CountsByRule != null && result.CountsByRule.Count > 0)
                {
                    var key = rule.MergeKey ?? "";
                    if (!result.CountsByRule.TryGetValue(key, out int placed) || placed <= 0) continue;
                }

                string id = string.IsNullOrWhiteSpace(rule.RuleId) ? (rule.MergeKey ?? "(unnamed rule)") : rule.RuleId;
                string mode = (rule.RoutingMode ?? "").Trim();
                bool routes = !string.IsNullOrEmpty(mode)
                              && !mode.Equals("NONE", StringComparison.OrdinalIgnoreCase);

                // ── Router-only fields on a non-routing rule ──────────
                if (!routes)
                {
                    if (rule.MinSlopePercent > 0)
                        advisories.Add($"[{id}] MinSlopePercent = {rule.MinSlopePercent:0.##}% has no effect: " +
                                       "slope is applied by the routing engines, and this rule's RoutingMode is NONE. " +
                                       "Set RoutingMode to AUTO_PIPE / AUTO_DUCT / WALL_FOLLOWER, or clear the field.");

                    if (rule.EmitSupports)
                        advisories.Add($"[{id}] EmitSupports is on but has no effect: supports are placed by the " +
                                       "routing path, and this rule's RoutingMode is NONE.");

                    if (!string.IsNullOrWhiteSpace(rule.ExposureClass))
                        advisories.Add($"[{id}] ExposureClass '{rule.ExposureClass}' has no effect: it feeds the " +
                                       "concrete-cover calculation in the in-wall chase router, which only runs for " +
                                       "routing rules.");

                    // These three are still pushed onto family types by
                    // "Push to Families" — say so rather than calling them dead.
                    if (!string.IsNullOrWhiteSpace(rule.Material))
                        advisories.Add($"[{id}] Material '{rule.Material}' is not applied during this run: it stamps " +
                                       "routed segments, and RoutingMode is NONE. It is still written to the family " +
                                       "types by 'Push to Families'.");

                    if (rule.InsulationThicknessMm > 0)
                        advisories.Add($"[{id}] InsulationThicknessMm = {rule.InsulationThicknessMm:0.##} is not " +
                                       "applied during this run: it stamps routed segments, and RoutingMode is NONE. " +
                                       "It is still written to the family types by 'Push to Families'.");

                    if (rule.NominalDiameterMm > 0)
                        advisories.Add($"[{id}] NominalDiameterMm = {rule.NominalDiameterMm:0.##} is not applied " +
                                       "during this run: it sizes chases and supports on the routing path, and " +
                                       "RoutingMode is NONE. It is still written to the family types by " +
                                       "'Push to Families'.");
                }

                // ── Lighting-grid-only field ─────────────────────────
                if (rule.MinUniformityRatio > 0
                    && !LightingGridAnchors.Contains((rule.AnchorType ?? "").Trim()))
                {
                    advisories.Add($"[{id}] MinUniformityRatio = {rule.MinUniformityRatio:0.##} has no effect: " +
                                   "uniformity is only evaluated by the lighting-grid solver. Set AnchorType to " +
                                   "LIGHTING_GRID (or LUX_GRID / EN12464) to enable it.");
                }

                // ── Maintenance clearance class code ─────────────────
                var clear = (rule.MaintenanceClearance ?? "").Trim();
                if (!string.IsNullOrEmpty(clear) && !MaintenanceClearanceCodes.Contains(clear))
                {
                    advisories.Add($"[{id}] MaintenanceClearance '{clear}' is not a class code the maintenance-access " +
                                   $"validator recognises ({string.Join(" / ", MaintenanceClearanceCodes.OrderBy(c => c))}). " +
                                   "The clearance check will be skipped for this rule.");
                }

                // ── Glazing spec vs toughened flag ───────────────────
                if (rule.ToughenedGlazingRequired)
                {
                    var spec = (rule.GlazingSpec ?? "").Trim();
                    if (string.IsNullOrEmpty(spec))
                        advisories.Add($"[{id}] ToughenedGlazingRequired is set but GlazingSpec is empty — the " +
                                       "requirement is recorded on the rule but nothing states which spec satisfies it.");
                    else if (!ToughenedGlazingSpecs.Contains(spec))
                        advisories.Add($"[{id}] ToughenedGlazingRequired is set but GlazingSpec is '{spec}', which is " +
                                       $"not a toughened spec ({string.Join(" / ", ToughenedGlazingSpecs.OrderBy(c => c))}). " +
                                       "Placed instances will not satisfy the requirement.");
                }
            }

            return advisories;
        }
    }
}
