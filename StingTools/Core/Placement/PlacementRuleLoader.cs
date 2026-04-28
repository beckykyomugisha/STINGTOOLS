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

        // PC-20 + Phase 139 — per-discipline packs that ship alongside the
        // baseline.  The Centre's first-run flow can offer them as a sector
        // picker; for now they're auto-merged.  Each pack tags its rules with
        // SourcePack so the rules viewmodel can filter by pack chip.
        private static readonly (string FileName, string PackTag)[] DisciplinePacks = new[]
        {
            ("STING_PLACEMENT_RULES.architecture.json",          "Architecture"),
            ("STING_PLACEMENT_RULES.mechanical.json",            "Mechanical"),
            ("STING_PLACEMENT_RULES.electrical.json",            "Electrical"),
            ("STING_PLACEMENT_RULES.healthcare-education.json",  "HealthcareEdu"),
            ("STING_PLACEMENT_RULES.baseline-extensions.json",   "Baseline"),
            ("STING_PLACEMENT_RULES.baseline-extensions2.json",  "Baseline"),
            ("STING_PLACEMENT_RULES.windows-glazing.json",       "Windows"),
            ("STING_PLACEMENT_RULES.routing.json",               "Routing"),
            ("STING_PLACEMENT_RULES.medical-gases.json",         "MedicalGases"),
            ("STING_PLACEMENT_RULES.accessibility.json",         "Accessibility"),
            ("STING_PLACEMENT_RULES.commissioning.json",         "Commissioning"),
            ("STING_PLACEMENT_RULES.mk-electrical.json",         "MK_Electrical"),
            ("STING_PLACEMENT_RULES.ceiling-pendants.json",      "Ceiling_Pendants"),
            ("STING_PLACEMENT_RULES.conduiting-phase.json",      "Conduiting_Phase"),
            ("STING_PLACEMENT_RULES.in-wall-chase.json",         "InWall_Chase"),
        };

        /// <summary>
        /// Load the default rule set + every discipline pack (PC-20).
        /// Project overrides apply on top via <see cref="Load"/>.
        /// </summary>
        public static List<PlacementRule> LoadDefaults()
        {
            string baseline = StingToolsApp.FindDataFile(DefaultFileName);
            var merged = LoadFromFileSafe(baseline);
            // Phase 139 — stamp baseline rules so the UI can filter them.
            foreach (var r in merged)
                if (r != null && string.IsNullOrEmpty(r.SourcePack)) r.SourcePack = "Baseline";

            foreach (var (packName, packTag) in DisciplinePacks)
            {
                string p = StingToolsApp.FindDataFile(packName);
                if (string.IsNullOrEmpty(p)) continue;
                var packRules = LoadFromFileSafe(p);
                if (packRules == null || packRules.Count == 0) continue;
                foreach (var r in packRules)
                    if (r != null && string.IsNullOrEmpty(r.SourcePack)) r.SourcePack = packTag;
                merged = MergeRules(merged, packRules);
            }
            return merged;
        }

        /// <summary>
        /// Phase 139 F2 — filter rules by active project profile.  A rule
        /// is included when (a) its BuildingType is empty OR matches
        /// profile.BuildingType, AND (b) its ApplicableStandards is empty
        /// OR overlaps profile.ActiveStandards.  Returns ordered by
        /// Priority descending.
        /// </summary>
        public static List<PlacementRule> FilterByProfile(
            List<PlacementRule> rules,
            ProjectBuildingProfile profile)
        {
            if (rules == null) return new List<PlacementRule>();
            if (profile == null) return rules;

            var bt = profile.BuildingType ?? "";
            var act = profile.ActiveStandards ?? new string[0];
            var actSet = new HashSet<string>(act, StringComparer.OrdinalIgnoreCase);

            var filtered = new List<PlacementRule>();
            foreach (var r in rules)
            {
                if (r == null) continue;

                // BuildingType gate (empty rule.BuildingType matches any)
                if (!string.IsNullOrEmpty(r.BuildingType) && !string.IsNullOrEmpty(bt))
                {
                    if (!string.Equals(r.BuildingType, bt, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(r.BuildingType, "Mixed",  StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // ApplicableStandards gate
                if (r.ApplicableStandards != null && r.ApplicableStandards.Length > 0 && actSet.Count > 0)
                {
                    bool any = false;
                    foreach (var s in r.ApplicableStandards)
                        if (!string.IsNullOrEmpty(s) && actSet.Contains(s)) { any = true; break; }
                    if (!any) continue;
                }

                filtered.Add(r);
            }

            filtered.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return filtered;
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
                    if (result.ContainsKey(rule.MergeKey))
                        StingLog.Warn($"PlacementRuleLoader: duplicate RuleId/MergeKey '{rule.MergeKey}' in baseline pack '{rule.SourcePack}' — later definition wins.");
                    result[rule.MergeKey] = rule.Clone();
                }
            }

            if (overrides != null)
            {
                foreach (var rule in overrides)
                {
                    if (rule == null) continue;
                    // Project override wins on same merge key — that's by design;
                    // only log when the override is a strict duplicate of another override.
                    result[rule.MergeKey] = rule.Clone();
                }
            }

            ValidateRuleSet(result.Values);
            return new List<PlacementRule>(result.Values);
        }

        /// <summary>
        /// Phase 139.4 — log warnings for misconfigured rules so they
        /// surface at session start instead of failing silently inside
        /// the engine. Validation never throws — degraded-mode load
        /// still returns the rules.
        /// </summary>
        private static void ValidateRuleSet(IEnumerable<PlacementRule> rules)
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
                    StingLog.Warn($"PlacementRuleLoader: rule '{r.MergeKey}' is RuleKind=Density but declares no PerAreaM2/PerOccupant/PerBed/PerWorkstation/PerPupil/PerToiletCubicle rate. Will place at most one fixture per room.");
                }

                // Routing rule must declare a segment category.
                if (!string.IsNullOrEmpty(r.RoutingMode)
                    && !string.Equals(r.RoutingMode, "NONE", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(r.RouteSegmentCategory))
                {
                    StingLog.Warn($"PlacementRuleLoader: rule '{r.MergeKey}' has RoutingMode={r.RoutingMode} but no RouteSegmentCategory — defaulting to PIPE.");
                }

                // Two-phase + cluster contradiction (#43): cluster placement
                // assumes a single XYZ for the whole frame, two-phase assumes
                // per-rule first-fix boxes — they don't compose.
                if (r.TwoPhaseEnabled && r.IsClusterMember)
                {
                    StingLog.Warn($"PlacementRuleLoader: rule '{r.MergeKey}' has both TwoPhaseEnabled and IsClusterMember — undefined behaviour. Disabling cluster membership for this rule.");
                    r.IsClusterMember = false;
                }

                // Density rule with MaxPerRoom = 0 may runaway in big rooms.
                if (r.RuleKind == PlacementRuleKind.Density && r.MaxPerRoom == 0)
                {
                    StingLog.Info($"PlacementRuleLoader: rule '{r.MergeKey}' is Density with MaxPerRoom=0 — placement count is bounded only by PerAreaM2/PerOccupant.");
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
