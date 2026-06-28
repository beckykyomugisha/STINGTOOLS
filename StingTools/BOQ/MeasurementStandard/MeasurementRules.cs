// ══════════════════════════════════════════════════════════════════════════
//  MeasurementRules.cs — Phase 2A. Data-driven NRM2/CESMM4 measurement rules.
//
//  Turns modelled geometry into a *measured* quantity: net of openings/voids
//  per the standard's rules, with a visible wastage step. Mirrors the
//  TakeoffRuleRegistry pattern (corporate baseline in Data/ + project override
//  at <project>/_BIM_COORD/{id}_measurement_rules.json, merged by id, project
//  wins and is evaluated first).
//
//  The corporate file is named per standard:
//    nrm2   -> STING_NRM2_MEASUREMENT_RULES.json
//    cesmm4 -> STING_CESMM4_MEASUREMENT_RULES.json (optional; falls back to
//              the nrm2 ruleset when absent so CESMM4 reuses the geometry logic
//              with its own de-minimis threshold).
//
//  No Revit transaction is taken here — all reads. The actual geometry
//  traversal (Wall.FindInserts, void areas, girth) lives in
//  MeasurementDeductionEngine; this file only loads + matches the rules.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.BOQ.MeasurementStandard
{
    /// <summary>Project-wide measurement defaults (de-minimis thresholds + waste).</summary>
    public class MeasurementDefaults
    {
        public double OpeningDeMinimisM2 { get; set; } = 0.5;
        public double VoidDeMinimisM2 { get; set; } = 1.0;
        /// <summary>-1 = inherit project default / takeoff-rule waste.</summary>
        public double WastePercent { get; set; } = -1;
    }

    public class MeasurementRule
    {
        public string Id { get; set; } = "";
        public string MatchCategory { get; set; } = "";
        public string MatchDiscipline { get; set; } = "*";
        public string MatchProdCode { get; set; } = "*";
        public string Unit { get; set; } = "m2";
        /// <summary>"area" | "volume" | "length" | "girth".</summary>
        public string Measure { get; set; } = "area";
        public bool DeductOpenings { get; set; } = false;
        public bool DeductVoids { get; set; } = false;
        /// <summary>Per-rule de-minimis; -1 (or unset) inherits the library default.</summary>
        public double OpeningDeMinimisM2 { get; set; } = -1;
        public double VoidDeMinimisM2 { get; set; } = -1;
        /// <summary>-1 = inherit project default / takeoff-rule waste.</summary>
        public double WastePercent { get; set; } = -1;
        public string Note { get; set; } = "";
        public string Origin { get; set; } = "corporate";

        internal bool Matches(string categoryName, string discipline, string prodCode)
        {
            if (!FieldMatches(MatchCategory, categoryName)) return false;
            if (!FieldMatches(MatchDiscipline, discipline)) return false;
            if (!FieldMatches(MatchProdCode, prodCode)) return false;
            return true;
        }

        private static bool FieldMatches(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Effective opening de-minimis (rule value, else library default).</summary>
        public double ResolveOpeningDeMinimis(MeasurementDefaults defaults)
            => OpeningDeMinimisM2 >= 0 ? OpeningDeMinimisM2 : (defaults?.OpeningDeMinimisM2 ?? 0.5);

        /// <summary>Effective void de-minimis (rule value, else library default).</summary>
        public double ResolveVoidDeMinimis(MeasurementDefaults defaults)
            => VoidDeMinimisM2 >= 0 ? VoidDeMinimisM2 : (defaults?.VoidDeMinimisM2 ?? 1.0);
    }

    public class MeasurementRuleLibrary
    {
        public string Version { get; set; } = "1.0";
        public MeasurementDefaults Defaults { get; set; } = new MeasurementDefaults();
        public List<MeasurementRule> Rules { get; set; } = new List<MeasurementRule>();
    }

    /// <summary>
    /// Per-(standard,document) registry. Corporate baseline + project override
    /// composed at load time; project rules win by id and are prepended so
    /// they're evaluated first (first-match-wins).
    /// </summary>
    public sealed class MeasurementRuleRegistry
    {
        private static readonly ConcurrentDictionary<string, MeasurementRuleRegistry> _cache
            = new ConcurrentDictionary<string, MeasurementRuleRegistry>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<MeasurementRule> Rules { get; }
        public MeasurementDefaults Defaults { get; }
        public DateTime LoadedAtUtc { get; }

        private MeasurementRuleRegistry(IReadOnlyList<MeasurementRule> rules, MeasurementDefaults defaults)
        {
            Rules = rules;
            Defaults = defaults ?? new MeasurementDefaults();
            LoadedAtUtc = DateTime.UtcNow;
        }

        public static MeasurementRuleRegistry Get(Autodesk.Revit.DB.Document doc, string standardId)
        {
            string id = string.IsNullOrEmpty(standardId) ? "nrm2" : standardId.ToLowerInvariant();
            string key = (doc?.PathName ?? "default") + "::" + id;
            return _cache.GetOrAdd(key, _ => Load(doc, id));
        }

        public static void Invalidate() => _cache.Clear();

        /// <summary>First-match-wins resolution. Null when no rule matches.</summary>
        public MeasurementRule Match(string categoryName, string discipline, string prodCode)
        {
            if (Rules == null) return null;
            foreach (var r in Rules)
                if (r.Matches(categoryName, discipline, prodCode)) return r;
            return null;
        }

        private static MeasurementRuleRegistry Load(Autodesk.Revit.DB.Document doc, string id)
        {
            string corpName = $"STING_{id.ToUpperInvariant()}_MEASUREMENT_RULES.json";
            string corpPath = StingToolsApp.FindDataFile(corpName);

            // CESMM4 (and any future standard) without its own corporate file
            // reuses the NRM2 geometry ruleset — the category-level measurement
            // rules are universal; the standard supplies its own threshold.
            if ((string.IsNullOrEmpty(corpPath) || !File.Exists(corpPath))
                && !string.Equals(id, "nrm2", StringComparison.OrdinalIgnoreCase))
            {
                corpPath = StingToolsApp.FindDataFile("STING_NRM2_MEASUREMENT_RULES.json");
            }

            string projectPath = ResolveProjectOverridePath(doc, id);

            var corpLib = LoadFile(corpPath, "corporate");
            var projLib = LoadFile(projectPath, "project");

            var projectIds = new HashSet<string>(projLib.Rules.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
            var merged = new List<MeasurementRule>(projLib.Rules.Count + corpLib.Rules.Count);
            merged.AddRange(projLib.Rules);
            merged.AddRange(corpLib.Rules.Where(r => !projectIds.Contains(r.Id)));

            // Project defaults win when the override file declares them.
            var defaults = projLib.Defaults != null && projLib.RawHasDefaults
                ? projLib.Defaults : corpLib.Defaults;

            StingLog.Info($"MeasurementRuleRegistry[{id}]: loaded {merged.Count} rule(s) " +
                          $"({projLib.Rules.Count} project + {corpLib.Rules.Count} corporate).");
            return new MeasurementRuleRegistry(merged, defaults);
        }

        private sealed class LoadedLib
        {
            public List<MeasurementRule> Rules = new List<MeasurementRule>();
            public MeasurementDefaults Defaults = new MeasurementDefaults();
            public bool RawHasDefaults;
        }

        private static LoadedLib LoadFile(string path, string origin)
        {
            var result = new LoadedLib();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
            try
            {
                string json = File.ReadAllText(path);
                var lib = JsonConvert.DeserializeObject<MeasurementRuleLibrary>(json);
                if (lib == null) return result;
                if (lib.Rules != null)
                {
                    foreach (var r in lib.Rules) r.Origin = origin;
                    result.Rules = lib.Rules;
                }
                if (lib.Defaults != null)
                {
                    result.Defaults = lib.Defaults;
                    result.RawHasDefaults = json.IndexOf("\"defaults\"", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MeasurementRuleRegistry.LoadFile({Path.GetFileName(path)}): {ex.Message}");
            }
            return result;
        }

        private static string ResolveProjectOverridePath(Autodesk.Revit.DB.Document doc, string id)
        {
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return null;
                string parent = Path.GetDirectoryName(bimDir);
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", $"{id}_measurement_rules.json");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MeasurementRuleRegistry.ResolveProjectOverridePath: {ex.Message}");
                return null;
            }
        }
    }
}
