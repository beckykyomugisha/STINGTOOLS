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
        /// Project overrides win on MergeKey match. Rules with a unique
        /// MergeKey are appended. Priority is NOT used for merge — it is
        /// a scoring tie-breaker at placement time.
        /// </summary>
        public static List<PlacementRule> MergeRules(
            List<PlacementRule> defaults,
            List<PlacementRule> overrides)
        {
            var result = new Dictionary<string, PlacementRule>(StringComparer.OrdinalIgnoreCase);

            if (defaults != null)
            {
                foreach (var rule in defaults)
                {
                    if (rule == null) continue;
                    result[rule.MergeKey] = rule.Clone();
                }
            }

            if (overrides != null)
            {
                foreach (var rule in overrides)
                {
                    if (rule == null) continue;
                    // Project override wins on same merge key.
                    result[rule.MergeKey] = rule.Clone();
                }
            }

            return new List<PlacementRule>(result.Values);
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
