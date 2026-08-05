using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Project-overlay-aware registry of template-assignment rules and
    /// compliance weight profiles. Loads STING_TEMPLATE_ASSIGNMENT_RULES.json
    /// from the corporate baseline (data dir) and layers
    /// <project>/_BIM_COORD/template_assignment_rules.json on top.
    ///
    /// Same id wins (project overrides corporate). Project entries without
    /// a matching corporate id are appended.
    /// </summary>
    public sealed class TemplateAssignmentRules
    {
        public string SchemaVersion { get; set; } = "1.0";
        public Dictionary<string, string> ViewTypeDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<NameRule> NameRules { get; set; } = new();
        public List<LevelRule> LevelRules { get; set; } = new();
        public List<PhaseRule> PhaseRules { get; set; } = new();
        public List<ScopeBoxRule> ScopeBoxRules { get; set; } = new();
        public Dictionary<string, Dictionary<string, double>> ComplianceWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class NameRule
    {
        public string Id { get; set; }
        public string Pattern { get; set; }
        public string ViewType { get; set; }
        public string TemplateName { get; set; }
        public string Discipline { get; set; }
    }

    public class LevelRule
    {
        public string Id { get; set; }
        public string LevelPattern { get; set; }
        public string ViewType { get; set; }
        public string TemplateName { get; set; }
        public string Discipline { get; set; }
    }

    public class PhaseRule
    {
        public string Id { get; set; }
        public string PhaseName { get; set; }
        public string TemplateName { get; set; }
        public string Discipline { get; set; }
    }

    public class ScopeBoxRule
    {
        public string Id { get; set; }
        public string Pattern { get; set; }
        public string TemplateName { get; set; }
        public string Discipline { get; set; }
    }

    /// <summary>
    /// Static loader + per-document cache. Mirror of AecFilterRegistry pattern.
    /// </summary>
    public static class TemplateRulesRegistry
    {
        private static readonly ConcurrentDictionary<string, TemplateAssignmentRules> _cache = new();
        private const string CorporateFileName = "STING_TEMPLATE_ASSIGNMENT_RULES.json";
        private const string ProjectOverlayFile = "template_assignment_rules.json";

        /// <summary>Get rules for the active document (cached per-doc).</summary>
        public static TemplateAssignmentRules Get(Document doc)
        {
            string key = doc?.PathName ?? doc?.Title ?? "default";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload(Document doc)
        {
            string key = doc?.PathName ?? doc?.Title ?? "default";
            _cache.TryRemove(key, out _);
        }

        public static void ReloadAll() => _cache.Clear();

        /// <summary>Load corporate baseline + apply project overlay (if present).</summary>
        private static TemplateAssignmentRules Load(Document doc)
        {
            var rules = new TemplateAssignmentRules();
            string corpPath = null;
            try
            {
                corpPath = StingTools.Core.StingToolsApp.FindDataFile(CorporateFileName);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"TemplateRulesRegistry locate corp: {ex.Message}"); }

            if (!string.IsNullOrEmpty(corpPath) && File.Exists(corpPath))
            {
                try
                {
                    var json = File.ReadAllText(corpPath);
                    rules = JsonConvert.DeserializeObject<TemplateAssignmentRules>(json) ?? rules;
                    NormaliseDefaults(rules);
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"TemplateRulesRegistry read corp: {ex.Message}"); }
            }
            else
            {
                // Fall back to bare defaults so callers still get sensible behaviour
                rules = BuildHardcodedFallback();
                StingTools.Core.StingLog.Warn("TemplateRulesRegistry: corp JSON not found — using hardcoded fallback.");
            }

            // Apply project overlay
            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(projDir))
                    {
                        string overlayPath = Path.Combine(projDir, "_BIM_COORD", ProjectOverlayFile);
                        if (File.Exists(overlayPath))
                        {
                            var overlayJson = File.ReadAllText(overlayPath);
                            var overlay = JsonConvert.DeserializeObject<TemplateAssignmentRules>(overlayJson);
                            if (overlay != null) Merge(rules, overlay);
                        }
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"TemplateRulesRegistry overlay: {ex.Message}"); }

            return rules;
        }

        private static void NormaliseDefaults(TemplateAssignmentRules r)
        {
            // ViewTypeDefaults shipped as List<obj> in JSON; convert
            if (r.ViewTypeDefaults == null || r.ViewTypeDefaults.Count == 0)
                r.ViewTypeDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void Merge(TemplateAssignmentRules baseR, TemplateAssignmentRules overlay)
        {
            // Same-id overrides; new ids appended; collections are id-keyed.
            MergeList(baseR.NameRules, overlay.NameRules, x => x.Id);
            MergeList(baseR.LevelRules, overlay.LevelRules, x => x.Id);
            MergeList(baseR.PhaseRules, overlay.PhaseRules, x => x.Id);
            MergeList(baseR.ScopeBoxRules, overlay.ScopeBoxRules, x => x.Id);

            if (overlay.ViewTypeDefaults != null)
                foreach (var kvp in overlay.ViewTypeDefaults)
                    baseR.ViewTypeDefaults[kvp.Key] = kvp.Value;

            if (overlay.ComplianceWeights != null)
                foreach (var kvp in overlay.ComplianceWeights)
                    baseR.ComplianceWeights[kvp.Key] = kvp.Value;
        }

        private static void MergeList<T>(List<T> baseList, List<T> overlayList, Func<T, string> idSelector)
        {
            if (overlayList == null) return;
            foreach (var item in overlayList)
            {
                string id = idSelector(item);
                if (string.IsNullOrEmpty(id)) { baseList.Add(item); continue; }
                int idx = baseList.FindIndex(x => string.Equals(idSelector(x), id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) baseList[idx] = item;
                else baseList.Add(item);
            }
        }

        // Fallback so the system never returns a totally empty rule set.
        private static TemplateAssignmentRules BuildHardcodedFallback()
        {
            return new TemplateAssignmentRules
            {
                ViewTypeDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FloorPlan"] = "STING - Architectural Plan",
                    ["CeilingPlan"] = "STING - Ceiling RCP",
                    ["Section"] = "STING - Working Section",
                    ["ThreeD"] = "STING - Coordination 3D",
                    ["Elevation"] = "STING - Working Elevation"
                },
                NameRules = new List<NameRule>(),
                LevelRules = new List<LevelRule>(),
                PhaseRules = new List<PhaseRule>(),
                ScopeBoxRules = new List<ScopeBoxRule>(),
                ComplianceWeights = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["default"] = new Dictionary<string, double>
                    {
                        ["HasTemplate"] = 1.5,
                        ["IsStingTemplate"] = 1.0,
                        ["HasFilters"] = 1.5,
                        ["FilterOverrides"] = 1.0,
                        ["DetailLevel"] = 0.5,
                        ["CorrectDiscipline"] = 1.5,
                        ["PhaseCorrect"] = 0.5,
                        ["VGConsistent"] = 1.0,
                        ["NoOrphans"] = 0.5,
                        ["ScaleAppropriate"] = 0.5
                    }
                }
            };
        }

        /// <summary>
        /// Resolve the "best" weight profile for the current project.
        /// Reads PRJ_ORG_HEALTH_PACK_PROFILE_TXT to choose healthcare if set;
        /// otherwise reads PRJ_TEMPLATE_PROFILE_TXT; falls back to default.
        /// </summary>
        public static Dictionary<string, double> ResolveComplianceProfile(Document doc, string explicitProfile = null)
        {
            var rules = Get(doc);
            string profile = explicitProfile;
            if (string.IsNullOrEmpty(profile) && doc != null)
            {
                try
                {
                    var pi = doc.ProjectInformation;
                    if (pi != null)
                    {
                        var hcPar = pi.LookupParameter("PRJ_ORG_HEALTH_PACK_PROFILE_TXT");
                        if (hcPar != null && hcPar.HasValue && !string.IsNullOrWhiteSpace(hcPar.AsString()))
                            profile = "healthcare";
                        if (string.IsNullOrEmpty(profile))
                        {
                            var tpPar = pi.LookupParameter("PRJ_TEMPLATE_PROFILE_TXT");
                            if (tpPar != null && tpPar.HasValue) profile = tpPar.AsString();
                        }
                    }
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveComplianceProfile: {ex.Message}"); }
            }
            if (string.IsNullOrEmpty(profile)) profile = "default";
            if (rules.ComplianceWeights.TryGetValue(profile, out var weights)) return weights;
            return rules.ComplianceWeights["default"];
        }
    }
}
