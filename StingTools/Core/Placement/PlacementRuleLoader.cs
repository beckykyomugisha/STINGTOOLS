using StingTools.Core;
// StingTools v4 MVP — placement rule loader.
//
// Loads STING_PLACEMENT_RULES.json (plugin default) and optionally
// STING_PLACEMENT_RULES.project.json (per-project override) and
// merges them with project-level winning on MergeKey.
//
// Default path resolution reuses StingToolsApp.FindDataFile which
// searches the DataPath (copied from StingTools/Data/ at build time).
// Project-override path is resolved against the .rvt's directory.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Loads + merges placement rules. Stateless; all methods static.
    /// Never throws: any IO / parse failure returns an empty list and
    /// writes a StingLog.Warn line.
    /// </summary>
    public static class PlacementRuleLoader
    {
        private const string DefaultFileName = "STING_PLACEMENT_RULES.json";
        private const string ProjectOverrideFileName = "STING_PLACEMENT_RULES.project.json";
        private const string LearnedOverrideFileName = "STING_PLACEMENT_RULES.learned.json";

        // PC-20 — per-discipline packs that ship alongside the baseline.
        // The Centre's first-run flow can offer them as a sector picker;
        // for now they're auto-merged so the engine sees ~110 rules instead
        // of ~43.
        private static readonly string[] DisciplinePacks = new[]
        {
            "STING_PLACEMENT_RULES.architecture.json",
            "STING_PLACEMENT_RULES.mechanical.json",
            "STING_PLACEMENT_RULES.electrical.json",
            "STING_PLACEMENT_RULES.healthcare-education.json",
            "STING_PLACEMENT_RULES.toilet-fixtures.json",  // Phase 177 — full toilet-room fixture coverage
        };

        /// <summary>
        /// Load the default rule set + every discipline pack (PC-20).
        /// Project overrides apply on top via <see cref="Load"/>.
        /// </summary>
        public static List<PlacementRule> LoadDefaults()
        {
            string baseline = StingToolsApp.FindDataFile(DefaultFileName);
            var merged = LoadFromFileSafe(baseline);

            foreach (var packName in DisciplinePacks)
            {
                string p = StingToolsApp.FindDataFile(packName);
                if (string.IsNullOrEmpty(p)) continue;
                var packRules = LoadFromFileSafe(p);
                if (packRules == null || packRules.Count == 0) continue;
                merged = MergeRules(merged, packRules);
            }
            return merged;
        }

        /// <summary>
        /// Load + merge. projectPath is the absolute path of the
        /// current .rvt; override file is expected next to it.
        /// If projectPath is null or empty, returns defaults only.
        /// </summary>
        public static List<PlacementRule> Load(string projectPath)
        {
            List<PlacementRule> merged = LoadDefaults();

            if (string.IsNullOrWhiteSpace(projectPath))
                return merged;

            string projectDir = null;
            try { projectDir = Path.GetDirectoryName(projectPath); }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementRuleLoader: cannot derive directory from project path '{projectPath}': {ex.Message}");
                return merged;
            }

            if (string.IsNullOrEmpty(projectDir))
                return merged;

            // PC-14 — apply the learned-overrides file first (Priority 90 wins
            // unless the project-override file overwrites the same MergeKey).
            if (StingTools.Commands.Placement.PlaceFixturesOptions.HonourLearned)
            {
                string learnedPath = Path.Combine(projectDir, LearnedOverrideFileName);
                if (File.Exists(learnedPath))
                {
                    var learned = LoadFromFileSafe(learnedPath);
                    if (learned != null && learned.Count > 0)
                        merged = MergeRules(merged, learned);
                }
            }

            string overridePath = Path.Combine(projectDir, ProjectOverrideFileName);
            if (!File.Exists(overridePath))
                return merged;

            List<PlacementRule> overrides = LoadFromFileSafe(overridePath);
            if (overrides == null || overrides.Count == 0)
                return merged;

            return MergeRules(merged, overrides);
        }

        /// <summary>
        /// Phase 139.27 (M-05) — last validation result, surfaced to the
        /// engine and result panel so misconfigured rules don't only show
        /// up in the .log file (where users never look). Cleared and
        /// rebuilt by every <see cref="MergeRules"/> call.
        /// </summary>
        public static List<string> LastValidationWarnings { get; private set; } = new List<string>();

        /// <summary>
        /// Project overrides win on MergeKey match. Rules with a unique
        /// MergeKey are appended. Priority is NOT used for merge — it is
        /// a scoring tie-breaker at placement time.
        /// </summary>
        public static List<PlacementRule> MergeRules(
            List<PlacementRule> defaults,
            List<PlacementRule> overrides)
        {
            var result = new Dictionary<string, PlacementRule>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();

            if (defaults != null)
            {
                foreach (var rule in defaults)
                {
                    if (rule == null) continue;
                    // Phase 139.27 (M-04) — track BOTH sources of the
                    // colliding MergeKey so the warning is actionable.
                    // Pre-139.27 the warning only named the loser's
                    // SourcePack; users couldn't tell which pack file to
                    // edit because the winner is anonymous.
                    if (result.TryGetValue(rule.MergeKey, out var existing))
                    {
                        string msg = $"PlacementRuleLoader: duplicate MergeKey '{rule.MergeKey}' — pack '{rule.SourcePack ?? "?"}' overrides earlier '{existing.SourcePack ?? "?"}'. Truncated RuleIds may collide silently — verify the long form differs.";
                        warnings.Add(msg);
                        StingLog.Warn(msg);
                    }
                    result[rule.MergeKey] = rule.Clone();
                }
            }

            if (overrides != null)
            {
                foreach (var rule in overrides)
                {
                    if (rule == null) continue;
                    // Project override wins on same merge key — that's by design;
                    // surface as Info-level so the user can confirm the override
                    // landed without flooding the warnings list.
                    if (result.ContainsKey(rule.MergeKey))
                        StingLog.Info($"PlacementRuleLoader: project override '{rule.MergeKey}' replaces baseline rule.");
                    result[rule.MergeKey] = rule.Clone();
                }
            }

            ValidateRuleSet(result.Values, warnings);
            LastValidationWarnings = warnings;
            return new List<PlacementRule>(result.Values);
        }

        /// <summary>
        /// Phase 139.4 — log warnings for misconfigured rules so they
        /// surface at session start instead of failing silently inside
        /// the engine. Validation never throws — degraded-mode load
        /// still returns the rules. Phase 139.27 (M-05) — also append
        /// every validation warning into <paramref name="sink"/> so the
        /// engine can surface them in <see cref="PlacementResult.Warnings"/>.
        /// </summary>
        private static void ValidateRuleSet(IEnumerable<PlacementRule> rules, List<string> sink)
        {
            if (rules == null) return;
            foreach (var r in rules)
            {
                if (r == null) continue;

                // Density rule must declare at least one rate.
                if (r.RuleKind == PlacementRuleKind.Density
                    && r.PerAreaM2 <= 0 && r.PerOccupant <= 0
                    && r.PerBed <= 0 && r.PerWorkstation <= 0
                    && r.PerPupil <= 0 && r.PerToiletCubicle <= 0)
                {
                    string msg = $"PlacementRuleLoader: rule '{r.MergeKey}' is RuleKind=Density but declares no PerAreaM2/PerOccupant/PerBed/PerWorkstation/PerPupil/PerToiletCubicle rate. Will place at most one fixture per room.";
                    sink?.Add(msg);
                    StingLog.Warn(msg);
                }

                // Routing rule must declare a segment category.
                if (!string.IsNullOrEmpty(r.RoutingMode)
                    && !string.Equals(r.RoutingMode, "NONE", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(r.RouteSegmentCategory))
                {
                    string msg = $"PlacementRuleLoader: rule '{r.MergeKey}' has RoutingMode={r.RoutingMode} but no RouteSegmentCategory — defaulting to PIPE.";
                    sink?.Add(msg);
                    StingLog.Warn(msg);
                }

                // Two-phase + cluster contradiction (#43): cluster placement
                // assumes a single XYZ for the whole frame, two-phase assumes
                // per-rule first-fix boxes — they don't compose.
                if (r.TwoPhaseEnabled && r.IsClusterMember)
                {
                    string msg = $"PlacementRuleLoader: rule '{r.MergeKey}' has both TwoPhaseEnabled and IsClusterMember — undefined behaviour. Disabling cluster membership for this rule.";
                    sink?.Add(msg);
                    StingLog.Warn(msg);
                    r.IsClusterMember = false;
                }

                // Density rule with MaxPerRoom = 0 may runaway in big rooms.
                if (r.RuleKind == PlacementRuleKind.Density && r.MaxPerRoom == 0)
                {
                    StingLog.Info($"PlacementRuleLoader: rule '{r.MergeKey}' is Density with MaxPerRoom=0 — placement count is bounded only by PerAreaM2/PerOccupant.");
                }

                // Phase 139.27 (M-05) — flag truncated RuleIds so users
                // know their rule may collide with another rule whose
                // long form starts the same. The MergeKey is whatever
                // PlacementRule.MergeKey returns; if the underlying
                // RuleId looks truncated (≥ 50 chars and not ending in
                // a delimiter), warn. This is a heuristic, not a hard
                // gate.
                if (!string.IsNullOrEmpty(r.RuleId) && r.RuleId.Length >= 50
                    && !r.RuleId.EndsWith("-") && !r.RuleId.EndsWith("_"))
                {
                    string msg = $"PlacementRuleLoader: rule '{r.MergeKey}' RuleId is {r.RuleId.Length} chars — may have been truncated. Two rules whose long form starts the same will silently collide on MergeKey.";
                    sink?.Add(msg);
                    StingLog.Warn(msg);
                }
            }
        }

        /// <summary>
        /// Parse a single rules JSON file. Returns empty list on any
        /// failure (missing file, malformed JSON, wrong schema).
        /// </summary>
        private static List<PlacementRule> LoadFromFileSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new List<PlacementRule>();

            try
            {
                string json = File.ReadAllText(path);
                var set = JsonConvert.DeserializeObject<PlacementRuleSet>(json);
                if (set == null || set.Rules == null)
                    return new List<PlacementRule>();
                return set.Rules;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementRuleLoader: failed to parse '{path}': {ex.Message}");
                return new List<PlacementRule>();
            }
        }
    }
}
