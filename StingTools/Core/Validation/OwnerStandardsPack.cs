using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B2) — Owner Standards Pack.
    //
    // The Owner's BIM modeling standards arrive in week 1 and supersede STING's
    // interim conventions. Encoding them must be a CONFIGURATION exercise, not a
    // code change — this is the rule-pack loader + one evaluator that switches
    // over the rule types. The tagSchemeConsistent rule reuses TagSchemeRenderer
    // (Phase 191), it does not duplicate that logic.
    // ─────────────────────────────────────────────────────────────────────────

    public class OwnerStandardRule
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "WARN";   // BLOCK / WARN / INFO
        public bool Enabled { get; set; } = true;
        public string Description { get; set; } = "";
        public string Source { get; set; } = "";
        // rule-specific
        public string Param { get; set; }
        public List<string> Categories { get; set; }
        public string Pattern { get; set; }
        public List<string> Values { get; set; }
        public bool Forbid { get; set; }
        public string SchemeId { get; set; }
    }

    public class OwnerStandardsPackDef
    {
        public string Version { get; set; }
        public string Description { get; set; }
        public List<OwnerStandardRule> Rules { get; set; } = new List<OwnerStandardRule>();
    }

    public class OwnerStandardFinding
    {
        public string RuleId { get; set; } = "";
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "WARN";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "";
        public int Checked { get; set; }
        public int Violations { get; set; }
        public List<string> Samples { get; set; } = new List<string>();
        public bool Skipped { get; set; }
        public string SkipReason { get; set; }
    }

    public static class OwnerStandardsRegistry
    {
        private const string CorporateFileName = "STING_OWNER_STANDARDS_PACK.json";
        private const string ProjectFileName = "owner_standards.json";

        private static readonly ConcurrentDictionary<string, OwnerStandardsPackDef> _cache
            = new ConcurrentDictionary<string, OwnerStandardsPackDef>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; }
            catch { return ""; }
        }

        public static OwnerStandardsPackDef Get(Document doc) => _cache.GetOrAdd(DocKey(doc), _ => Load(doc));

        public static void Reload(Document doc = null)
        {
            if (doc == null) _cache.Clear();
            else _cache.TryRemove(DocKey(doc), out _);
        }

        public static void InvalidateCache(Document doc) => Reload(doc);

        private static OwnerStandardsPackDef Load(Document doc)
        {
            OwnerStandardsPackDef def = null;
            try
            {
                string corp = StingToolsApp.FindDataFile(CorporateFileName);
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    def = JsonConvert.DeserializeObject<OwnerStandardsPackDef>(File.ReadAllText(corp));
            }
            catch (Exception ex) { StingLog.Warn($"OwnerStandardsRegistry corporate load: {ex.Message}"); }
            def = def ?? new OwnerStandardsPackDef();

            try
            {
                string dir = DocKey(doc);
                if (!string.IsNullOrEmpty(dir))
                {
                    string proj = Path.Combine(dir, "_BIM_COORD", ProjectFileName);
                    if (File.Exists(proj))
                    {
                        var overlay = JsonConvert.DeserializeObject<OwnerStandardsPackDef>(File.ReadAllText(proj));
                        foreach (var r in overlay?.Rules ?? new List<OwnerStandardRule>())
                        {
                            if (string.IsNullOrWhiteSpace(r?.Id)) continue;
                            def.Rules.RemoveAll(x => string.Equals(x.Id, r.Id, StringComparison.OrdinalIgnoreCase));
                            def.Rules.Add(r);
                        }
                        StingLog.Info($"OwnerStandardsRegistry: project overlay loaded from {proj}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"OwnerStandardsRegistry overlay load: {ex.Message}"); }

            return def;
        }
    }

    public static class OwnerStandardsEvaluator
    {
        public static List<OwnerStandardFinding> Evaluate(Document doc)
        {
            var def = OwnerStandardsRegistry.Get(doc);
            var findings = new List<OwnerStandardFinding>();
            foreach (var rule in def.Rules ?? new List<OwnerStandardRule>())
            {
                if (rule == null || !rule.Enabled) continue;
                var f = new OwnerStandardFinding
                {
                    RuleId = rule.Id, Type = rule.Type, Severity = (rule.Severity ?? "WARN").ToUpperInvariant(),
                    Description = rule.Description, Source = rule.Source
                };
                try { EvaluateRule(doc, rule, f); }
                catch (Exception ex)
                {
                    f.Skipped = true; f.SkipReason = ex.Message;
                    StingLog.Warn($"OwnerStandards rule '{rule.Id}': {ex.Message}");
                }
                findings.Add(f);
            }
            return findings;
        }

        private static void EvaluateRule(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            switch ((rule.Type ?? "").Trim())
            {
                case "paramRequired": ParamRequired(doc, rule, f); break;
                case "paramPattern": ParamPattern(doc, rule, f); break;
                case "paramInList": ParamInList(doc, rule, f); break;
                case "familyNamePattern": NamePattern(doc, rule, f, useType: false); break;
                case "typeNamePattern": NamePattern(doc, rule, f, useType: true); break;
                case "worksetPattern": WorksetPattern(doc, rule, f); break;
                case "viewNamePattern": ViewNamePattern(doc, rule, f); break;
                case "sheetNumberPattern": SheetNumberPattern(doc, rule, f); break;
                case "tagSchemeConsistent": TagSchemeConsistent(doc, rule, f); break;
                default: f.Skipped = true; f.SkipReason = $"unknown rule type '{rule.Type}'"; break;
            }
        }

        // ── element collection helpers ──────────────────────────────────
        private static List<Element> Collect(Document doc, List<string> categories)
        {
            if (categories != null &&
                categories.Any(c => string.Equals(c, "Project Information", StringComparison.OrdinalIgnoreCase)))
            {
                var pi = doc.ProjectInformation;
                return pi != null ? new List<Element> { pi } : new List<Element>();
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            bool all = categories == null || categories.Count == 0 ||
                       categories.Any(c => c == "*");
            var wanted = all ? null : new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);

            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .Where(e =>
                {
                    string cat = ParameterHelpers.GetCategoryName(e);
                    return all ? known.Contains(cat) : wanted.Contains(cat);
                })
                .ToList();
        }

        private static void ParamRequired(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            var els = Collect(doc, rule.Categories);
            foreach (var el in els)
            {
                f.Checked++;
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, rule.Param)))
                {
                    f.Violations++;
                    if (f.Samples.Count < 10) f.Samples.Add($"{el.Id} [{ParameterHelpers.GetCategoryName(el)}] missing {rule.Param}");
                }
            }
        }

        private static void ParamPattern(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            var rx = new Regex(rule.Pattern);
            foreach (var el in Collect(doc, rule.Categories))
            {
                string v = ParameterHelpers.GetString(el, rule.Param);
                if (string.IsNullOrEmpty(v)) continue; // emptiness is paramRequired's job
                f.Checked++;
                bool match = rx.IsMatch(v);
                bool bad = rule.Forbid ? match : !match;
                if (bad)
                {
                    f.Violations++;
                    if (f.Samples.Count < 10) f.Samples.Add($"{el.Id} {rule.Param}='{v}'");
                }
            }
        }

        private static void ParamInList(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            var set = new HashSet<string>(rule.Values ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var el in Collect(doc, rule.Categories))
            {
                string v = ParameterHelpers.GetString(el, rule.Param);
                if (string.IsNullOrEmpty(v)) continue;
                f.Checked++;
                if (!set.Contains(v))
                {
                    f.Violations++;
                    if (f.Samples.Count < 10) f.Samples.Add($"{el.Id} {rule.Param}='{v}' not in list");
                }
            }
        }

        private static void NamePattern(Document doc, OwnerStandardRule rule, OwnerStandardFinding f, bool useType)
        {
            var rx = new Regex(rule.Pattern);
            foreach (var el in Collect(doc, rule.Categories))
            {
                string name = useType ? ParameterHelpers.GetFamilySymbolName(el) : ParameterHelpers.GetFamilyName(el);
                if (string.IsNullOrEmpty(name)) continue;
                f.Checked++;
                bool match = rx.IsMatch(name);
                bool bad = rule.Forbid ? match : !match;
                if (bad)
                {
                    f.Violations++;
                    if (f.Samples.Count < 10) f.Samples.Add($"{el.Id} {(useType ? "type" : "family")}='{name}'");
                }
            }
        }

        private static void WorksetPattern(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            if (!doc.IsWorkshared) { f.Skipped = true; f.SkipReason = "document not workshared"; return; }
            var rx = new Regex(rule.Pattern);
            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset).ToWorksets();
            foreach (var ws in worksets)
            {
                f.Checked++;
                if (!rx.IsMatch(ws.Name))
                {
                    f.Violations++;
                    if (f.Samples.Count < 10) f.Samples.Add($"workset '{ws.Name}'");
                }
            }
        }

        private static void ViewNamePattern(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            var rx = new Regex(rule.Pattern);
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted);
            foreach (var v in views)
            {
                f.Checked++;
                if (!rx.IsMatch(v.Name))
                {
                    f.Violations++;
                    if (f.Samples.Count < 10) f.Samples.Add($"view '{v.Name}'");
                }
            }
        }

        private static void SheetNumberPattern(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            var rx = new Regex(rule.Pattern);
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder);
            foreach (var s in sheets)
            {
                f.Checked++;
                if (!rx.IsMatch(s.SheetNumber ?? ""))
                {
                    f.Violations++;
                    if (f.Samples.Count < 10) f.Samples.Add($"sheet '{s.SheetNumber}' ({s.Name})");
                }
            }
        }

        // Reuses TagSchemeRenderer.Render — does NOT duplicate the scheme logic.
        private static void TagSchemeConsistent(Document doc, OwnerStandardRule rule, OwnerStandardFinding f)
        {
            var schemes = TagSchemeRegistry.EnabledSchemes(doc);
            if (!string.IsNullOrEmpty(rule.SchemeId))
                schemes = schemes.Where(s => string.Equals(s.Id, rule.SchemeId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (schemes.Count == 0) { f.Skipped = true; f.SkipReason = "no enabled tag scheme"; return; }

            var scope = StingTools.Tags.TagSchemeCommandHelper.CollectScope(null, doc, out _);
            foreach (var el in scope)
            {
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.TAG1))) continue;
                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                foreach (var scheme in schemes)
                {
                    string expected = TagSchemeRenderer.Render(doc, el, scheme, tokenVals);
                    string stored = ParameterHelpers.GetString(el, scheme.TargetParam);
                    f.Checked++;
                    if (!string.Equals(stored, expected, StringComparison.Ordinal))
                    {
                        f.Violations++;
                        if (f.Samples.Count < 10)
                            f.Samples.Add($"{el.Id} [{scheme.Id}] stored '{stored}' ≠ expected '{expected}'");
                    }
                }
            }
        }
    }
}
