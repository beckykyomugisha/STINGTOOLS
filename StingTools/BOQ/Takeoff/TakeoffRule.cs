// ══════════════════════════════════════════════════════════════════════════
//  TakeoffRule.cs — Data-driven take-off rule model + registry.
//
//  Replaces the hard-coded if/switch chains in BOQCostManager.DeriveQuantity
//  and DeriveNrm2Section with rules loaded from STING_TAKEOFF_RULES.json.
//  Mirrors the pattern used by DrawingTypeRegistry and AecFilterRegistry —
//  corporate baseline ships in Data/; project overrides at
//  <project>/_BIM_COORD/takeoff_rules.json take precedence by id.
//
//  Rule fields:
//    id              — stable identifier
//    matchCategory   — Revit category contains-match (e.g. "Walls", "Pipes")
//    matchDiscipline — STING discipline letter (M/E/P/S/A/...), "*" = any
//    matchProdCode   — STING PROD code contains-match, "*" = any
//    unit            — "m2" / "m3" / "m" / "kg" / "each" / "item"
//    quantitySource  — "HOST_AREA_COMPUTED" / "HOST_VOLUME_COMPUTED" /
//                      "CURVE_ELEM_LENGTH" / "LookupParameter:Weight" /
//                      "LocationCurve" / "literal:1.0"
//    unitConversion  — "ft2_to_m2" / "ft3_to_m3" / "ft_to_m" / "none"
//    wastePercent    — applied at line-item build time (P0 reserves field;
//                      consumed once StingCostRateOverrideSchema v2 lands)
//    nrm2Section     — section code to file the line under
//    description     — token-substituted line-item description
//    notes           — free-text comment for the QS
//
//  Matching is first-match-wins. Rules in the project override file are
//  prepended to the corporate baseline so a QS can shadow a rule without
//  copying it.
//
//  P0 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.BOQ.Takeoff
{
    public class TakeoffRule
    {
        public string Id { get; set; } = "";
        public string MatchCategory { get; set; } = "";
        public string MatchDiscipline { get; set; } = "*";
        public string MatchProdCode { get; set; } = "*";
        public string Unit { get; set; } = "each";
        public string QuantitySource { get; set; } = "literal:1.0";
        public string UnitConversion { get; set; } = "none";
        public double WastePercent { get; set; } = 0;
        public string Nrm2Section { get; set; } = "23";
        public string Description { get; set; } = "";
        public string Notes { get; set; } = "";
        public string Origin { get; set; } = "corporate"; // "corporate" | "project"

        /// <summary>
        /// Returns true when this rule matches the element's category /
        /// discipline / PROD code. Match is contains+case-insensitive on
        /// strings, "*" matches anything.
        /// </summary>
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
    }

    public class TakeoffRuleLibrary
    {
        public string Version { get; set; } = "1.0";
        public List<TakeoffRule> Rules { get; set; } = new List<TakeoffRule>();
    }

    /// <summary>
    /// Per-document registry. Corporate baseline + project override
    /// composed at load time; project rules win by id and are
    /// prepended so they're evaluated first.
    /// </summary>
    public sealed class TakeoffRuleRegistry
    {
        private static readonly ConcurrentDictionary<string, TakeoffRuleRegistry> _cache
            = new ConcurrentDictionary<string, TakeoffRuleRegistry>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<TakeoffRule> Rules { get; }
        public DateTime LoadedAtUtc { get; }
        public string CorporatePath { get; }
        public string ProjectPath { get; }

        private TakeoffRuleRegistry(IReadOnlyList<TakeoffRule> rules,
                                    string corporatePath, string projectPath)
        {
            Rules = rules;
            LoadedAtUtc = DateTime.UtcNow;
            CorporatePath = corporatePath;
            ProjectPath = projectPath;
        }

        public static TakeoffRuleRegistry Get(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Invalidate() => _cache.Clear();

        /// <summary>
        /// First-match-wins resolution. Returns null when no rule matches
        /// — callers should fall back to legacy logic.
        /// </summary>
        public TakeoffRule Match(string categoryName, string discipline, string prodCode)
        {
            if (Rules == null) return null;
            foreach (var r in Rules)
            {
                if (r.Matches(categoryName, discipline, prodCode)) return r;
            }
            return null;
        }

        /// <summary>
        /// Evaluate the rule's quantitySource against the element.
        /// Returns 1.0 on any failure so a missing parameter doesn't
        /// crash the whole take-off; the QS sees a flagged confidence.
        /// </summary>
        public static double EvaluateQuantity(Element el, TakeoffRule rule)
        {
            if (el == null || rule == null) return 1.0;
            try
            {
                string src = rule.QuantitySource ?? "";

                if (src.StartsWith("literal:", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(src.Substring("literal:".Length),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double lit))
                        return ApplyConversion(lit, rule.UnitConversion);
                    return 1.0;
                }

                if (string.Equals(src, "LocationCurve", StringComparison.OrdinalIgnoreCase))
                {
                    if (el.Location is LocationCurve lc && lc.Curve != null)
                        return ApplyConversion(lc.Curve.Length, rule.UnitConversion);
                    return 1.0;
                }

                if (src.StartsWith("LookupParameter:", StringComparison.OrdinalIgnoreCase))
                {
                    string paramName = src.Substring("LookupParameter:".Length);
                    Parameter p = el.LookupParameter(paramName);
                    if (p != null && p.HasValue)
                        return ApplyConversion(p.AsDouble(), rule.UnitConversion);
                    return 1.0;
                }

                // BuiltInParameter name lookup — e.g. "HOST_AREA_COMPUTED".
                if (Enum.TryParse<BuiltInParameter>(src, true, out var bip))
                {
                    Parameter p = el.get_Parameter(bip);
                    if (p != null && p.HasValue)
                        return ApplyConversion(p.AsDouble(), rule.UnitConversion);
                    return 1.0;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TakeoffRule.EvaluateQuantity({rule.Id}): {ex.Message}");
            }
            return 1.0;
        }

        private static double ApplyConversion(double value, string conversion)
        {
            switch ((conversion ?? "none").ToLowerInvariant())
            {
                case "ft2_to_m2": return value * 0.092903;
                case "ft3_to_m3": return value * 0.0283168;
                case "ft_to_m":   return value * 0.3048;
                case "none":
                default:          return value;
            }
        }

        private static TakeoffRuleRegistry Load(Document doc)
        {
            string corpPath = StingToolsApp.FindDataFile("STING_TAKEOFF_RULES.json");
            string projectPath = ResolveProjectOverridePath(doc);

            var corporate = LoadFile(corpPath, origin: "corporate");
            var project = LoadFile(projectPath, origin: "project");

            // Project rules prepended so they win at first-match.
            // Corporate rules with same id as a project rule are dropped.
            var projectIds = new HashSet<string>(project.Select(r => r.Id),
                StringComparer.OrdinalIgnoreCase);
            var merged = new List<TakeoffRule>(project.Count + corporate.Count);
            merged.AddRange(project);
            merged.AddRange(corporate.Where(r => !projectIds.Contains(r.Id)));

            StingLog.Info($"TakeoffRuleRegistry: loaded {merged.Count} rules " +
                          $"({project.Count} project + {corporate.Count - (corporate.Count - (merged.Count - project.Count))} corporate)");
            return new TakeoffRuleRegistry(merged, corpPath, projectPath);
        }

        private static List<TakeoffRule> LoadFile(string path, string origin)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new List<TakeoffRule>();
            try
            {
                var lib = JsonConvert.DeserializeObject<TakeoffRuleLibrary>(File.ReadAllText(path));
                if (lib?.Rules == null) return new List<TakeoffRule>();
                foreach (var r in lib.Rules) r.Origin = origin;
                return lib.Rules;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TakeoffRuleRegistry.LoadFile({Path.GetFileName(path)}): {ex.Message}");
                return new List<TakeoffRule>();
            }
        }

        private static string ResolveProjectOverridePath(Document doc)
        {
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return null;
                // BIMManager dir is typically <project>/_bim_manager. Walk up
                // one level and look for the canonical _BIM_COORD folder.
                string parent = Path.GetDirectoryName(bimDir);
                if (string.IsNullOrEmpty(parent)) return null;
                string canonical = Path.Combine(parent, "_BIM_COORD", "takeoff_rules.json");
                return canonical;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TakeoffRuleRegistry.ResolveProjectOverridePath: {ex.Message}");
                return null;
            }
        }
    }
}
