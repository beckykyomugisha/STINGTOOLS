using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Commands.Panels
{
    public sealed class PanelTemplateRule
    {
        public List<string> NamePatterns { get; set; } = new List<string>();
        public string PanelType { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public List<string> FallbackTemplateNames { get; set; } = new List<string>();
        public int Priority { get; set; } = 999;
    }

    /// <summary>
    /// Resolves which existing PanelScheduleTemplate to apply to a panel based on
    /// rules in STING_PANEL_SCHEDULE_TEMPLATES.json. Loads project override from
    /// &lt;project&gt;/_BIM_COORD/panel_schedule_templates.json when present.
    /// </summary>
    public static class PanelScheduleTemplateRegistry
    {
        private static List<PanelTemplateRule> _rules;
        private static List<string> _skipPatterns;
        private static bool _useFirstAvailableFallback = true;
        private static string _loadedFromPath;

        public static IReadOnlyList<PanelTemplateRule> Rules => _rules ?? new List<PanelTemplateRule>();
        public static IReadOnlyList<string> SkipPatterns => _skipPatterns ?? new List<string>();
        public static bool UseFirstAvailableFallback => _useFirstAvailableFallback;
        public static string LoadedFromPath => _loadedFromPath;

        public static void EnsureLoaded(Document doc = null)
        {
            if (_rules != null) return;
            Reload(doc);
        }

        public static void Reload(Document doc = null)
        {
            _rules = new List<PanelTemplateRule>();
            _skipPatterns = new List<string>();

            string corporatePath = StingToolsApp.FindDataFile("STING_PANEL_SCHEDULE_TEMPLATES.json");
            if (!string.IsNullOrEmpty(corporatePath) && File.Exists(corporatePath))
                Merge(corporatePath);

            if (doc != null)
            {
                try
                {
                    string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                    string projOverride = Path.Combine(projDir, "_BIM_COORD", "panel_schedule_templates.json");
                    if (File.Exists(projOverride)) Merge(projOverride);
                }
                catch (Exception ex) { StingLog.Warn($"PanelScheduleTemplateRegistry project override: {ex.Message}"); }
            }

            _rules = _rules.OrderBy(r => r.Priority).ToList();
        }

        private static void Merge(string path)
        {
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                var rulesArr = root["rules"] as JArray;
                if (rulesArr != null)
                {
                    foreach (var r in rulesArr)
                    {
                        var rule = new PanelTemplateRule
                        {
                            PanelType = (string)r["panelType"] ?? "",
                            TemplateName = (string)r["templateName"] ?? "",
                            Priority = (int?)r["priority"] ?? 999
                        };
                        var np = r["namePatterns"] as JArray;
                        if (np != null) rule.NamePatterns = np.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        var fb = r["fallbackTemplateNames"] as JArray;
                        if (fb != null) rule.FallbackTemplateNames = fb.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        _rules.Add(rule);
                    }
                }

                var skip = root["skipPatterns"]?["patterns"] as JArray;
                if (skip != null)
                    _skipPatterns.AddRange(skip.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)));

                var gf = root["globalFallback"];
                if (gf != null)
                    _useFirstAvailableFallback = (bool?)gf["useFirstAvailableTemplate"] ?? true;

                _loadedFromPath = path;
                StingLog.Info($"PanelScheduleTemplateRegistry merged {path}: {_rules.Count} rules total");
            }
            catch (Exception ex) { StingLog.Error($"PanelScheduleTemplateRegistry.Merge {path}", ex); }
        }

        public static bool ShouldSkip(string panelName)
        {
            if (string.IsNullOrEmpty(panelName)) return false;
            foreach (string p in _skipPatterns ?? new List<string>())
            {
                try { if (Regex.IsMatch(panelName, p, RegexOptions.IgnoreCase)) return true; }
                catch (Exception ex) { StingLog.Warn($"Skip pattern '{p}' invalid: {ex.Message}"); }
            }
            return false;
        }

        /// <summary>
        /// Resolve a PanelScheduleTemplate ElementId for a given panel. Returns
        /// InvalidElementId if no rule and no fallback yields a usable template.
        /// </summary>
        public static ElementId Resolve(Document doc, FamilyInstance panel, out string ruleUsed, out string templateUsed)
        {
            return ResolveCandidates(doc, panel, out ruleUsed, out templateUsed).FirstOrDefault();
        }

        /// <summary>
        /// Returns an ordered list of candidate template ElementIds for the panel.
        /// Used by BatchPanelSchedulesCommand to fall through templates if the
        /// first one fails CreateInstanceView (e.g. configuration mismatch). Orders
        /// the rule's primary first, then its fallbacks, then the global fallback.
        /// </summary>
        public static List<ElementId> ResolveCandidates(Document doc, FamilyInstance panel,
            out string ruleUsed, out string templateUsed)
        {
            ruleUsed = null; templateUsed = null;
            var ordered = new List<ElementId>();
            EnsureLoaded(doc);

            var allTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleTemplate))
                .Cast<PanelScheduleTemplate>()
                .ToList();
            if (allTemplates.Count == 0) return ordered;

            string panelName = panel?.Name ?? "";

            foreach (var rule in _rules)
            {
                if (!MatchesAnyPattern(panelName, rule.NamePatterns)) continue;

                var t = FindTemplateByName(allTemplates, rule.TemplateName);
                if (t != null && !ordered.Contains(t.Id))
                {
                    if (templateUsed == null)
                    {
                        ruleUsed = $"name~/{string.Join("|", rule.NamePatterns.Take(3))}/ priority={rule.Priority}";
                        templateUsed = t.Name;
                    }
                    ordered.Add(t.Id);
                }
                foreach (string fb in rule.FallbackTemplateNames)
                {
                    var t2 = FindTemplateByName(allTemplates, fb);
                    if (t2 != null && !ordered.Contains(t2.Id)) ordered.Add(t2.Id);
                }

                // AUTO-1: also append every template whose PanelConfiguration matches
                // the rule's expected panelType, so a configuration-aware fallback
                // exists even if naming drifted. Best-effort — wrapped in try/catch
                // because the API surface for GetPanelConfiguration may differ
                // between Revit versions.
                if (!string.IsNullOrEmpty(rule.PanelType))
                {
                    foreach (var t3 in allTemplates)
                    {
                        if (ordered.Contains(t3.Id)) continue;
                        if (PanelTypeMatches(t3, rule.PanelType)) ordered.Add(t3.Id);
                    }
                }
            }

            if (_useFirstAvailableFallback)
            {
                foreach (var t in allTemplates)
                    if (!ordered.Contains(t.Id)) ordered.Add(t.Id);
                if (templateUsed == null && ordered.Count > 0)
                {
                    var first = doc.GetElement(ordered[0]) as PanelScheduleTemplate;
                    ruleUsed = "global-fallback (first available)";
                    templateUsed = first?.Name;
                }
            }

            return ordered;
        }

        private static bool PanelTypeMatches(PanelScheduleTemplate t, string ruleType)
        {
            if (t == null || string.IsNullOrEmpty(ruleType)) return false;
            try
            {
                // Reflection-friendly probe: try GetPanelConfiguration() if it exists.
                var m = t.GetType().GetMethod("GetPanelConfiguration");
                if (m != null)
                {
                    object cfg = m.Invoke(t, null);
                    if (cfg != null)
                    {
                        string s = cfg.ToString();
                        // Switchboard, BranchPanelThreePhase, BranchPanelSinglePhase, DataPanel
                        if (ruleType.IndexOf("Switchboard", StringComparison.OrdinalIgnoreCase) >= 0
                            && s.IndexOf("Switchboard", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        if (ruleType.IndexOf("Branch", StringComparison.OrdinalIgnoreCase) >= 0
                            && s.IndexOf("Branch", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        if (ruleType.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0
                            && s.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PanelTypeMatches '{t?.Name}' vs '{ruleType}': {ex.Message}"); }
            return false;
        }

        private static bool MatchesAnyPattern(string name, List<string> patterns)
        {
            if (patterns == null) return false;
            foreach (string p in patterns)
            {
                try { if (Regex.IsMatch(name, p, RegexOptions.IgnoreCase)) return true; }
                catch (Exception ex) { StingLog.Warn($"Pattern '{p}' invalid: {ex.Message}"); }
            }
            return false;
        }

        private static PanelScheduleTemplate FindTemplateByName(List<PanelScheduleTemplate> templates, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return templates.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
