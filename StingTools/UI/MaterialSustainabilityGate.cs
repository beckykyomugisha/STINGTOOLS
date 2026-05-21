using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// A10 — Sustainability-gate validator. Rule-driven check that
    /// flags materials breaching LEED v5 / BREEAM v6 / EN 15978
    /// thresholds.
    ///
    /// Rules live in <c>Data/STING_SUSTAINABILITY_GATES.json</c> with
    /// the usual project override at
    /// <c>&lt;project&gt;/_BIM_COORD/sustainability_gates.json</c>.
    ///
    /// Each rule is a simple predicate:
    /// <code>
    /// {
    ///   "id": "LEED_v5_Concrete_GWP",
    ///   "standard": "LEED v5",
    ///   "severity": "Warning",
    ///   "where": { "classRegex": "(?i)concrete" },
    ///   "fail":  { "carbonKgCo2eGreaterThan": 410 },
    ///   "message": "Concrete embodied carbon &gt; 410 kgCO2e/m3 — LEED v5 MR credit threshold"
    /// }
    /// </code>
    /// </summary>
    public class SustainabilityRule
    {
        [JsonProperty("id")]        public string Id { get; set; }
        [JsonProperty("standard")]  public string Standard { get; set; }
        [JsonProperty("severity")]  public string Severity { get; set; } = "Warning"; // Info / Warning / Block
        [JsonProperty("message")]   public string Message { get; set; }

        [JsonProperty("where", NullValueHandling = NullValueHandling.Ignore)]
        public RulePredicate Where { get; set; }
        [JsonProperty("fail",  NullValueHandling = NullValueHandling.Ignore)]
        public RulePredicate Fail  { get; set; }
    }

    public class RulePredicate
    {
        [JsonProperty("nameRegex",  NullValueHandling = NullValueHandling.Ignore)] public string NameRegex { get; set; }
        [JsonProperty("classRegex", NullValueHandling = NullValueHandling.Ignore)] public string ClassRegex { get; set; }
        [JsonProperty("originIn",   NullValueHandling = NullValueHandling.Ignore)] public List<string> OriginIn { get; set; }

        [JsonProperty("carbonKgCo2eGreaterThan", NullValueHandling = NullValueHandling.Ignore)] public double? CarbonGreaterThan { get; set; }
        [JsonProperty("carbonKgCo2eLessThan",    NullValueHandling = NullValueHandling.Ignore)] public double? CarbonLessThan { get; set; }
        [JsonProperty("costGreaterThan",         NullValueHandling = NullValueHandling.Ignore)] public double? CostGreaterThan { get; set; }
        [JsonProperty("missingEpd",              NullValueHandling = NullValueHandling.Ignore)] public bool?   MissingEpd { get; set; }
        [JsonProperty("epdExpired",              NullValueHandling = NullValueHandling.Ignore)] public bool?   EpdExpired { get; set; }
    }

    public class SustainabilityGateFile
    {
        [JsonProperty("rules")] public List<SustainabilityRule> Rules { get; set; } = new List<SustainabilityRule>();
    }

    public class SustainabilityFinding
    {
        public string RuleId { get; set; }
        public string Standard { get; set; }
        public string Severity { get; set; }
        public string MaterialName { get; set; }
        public string Message { get; set; }
    }

    public static class MaterialSustainabilityGate
    {
        public static SustainabilityGateFile Load(Document doc)
        {
            var merged = new SustainabilityGateFile();
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_SUSTAINABILITY_GATES.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                {
                    var f = JsonConvert.DeserializeObject<SustainabilityGateFile>(File.ReadAllText(corp));
                    if (f?.Rules != null) merged.Rules.AddRange(f.Rules);
                }
                string proj = Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "",
                    "sustainability_gates.json");
                if (File.Exists(proj))
                {
                    var f = JsonConvert.DeserializeObject<SustainabilityGateFile>(File.ReadAllText(proj));
                    if (f?.Rules != null) merged.Rules.AddRange(f.Rules);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SustainabilityGate.Load: {ex.Message}"); }
            return merged;
        }

        public static List<SustainabilityFinding> RunAll(Document doc, IReadOnlyList<MaterialRow> rows)
        {
            var findings = new List<SustainabilityFinding>();
            if (doc == null || rows == null || rows.Count == 0) return findings;
            var gate = Load(doc);
            if (gate?.Rules == null || gate.Rules.Count == 0) return findings;

            foreach (var rule in gate.Rules)
            {
                foreach (var row in rows)
                {
                    if (!WhereMatches(rule.Where, row)) continue;
                    if (!FailMatches(rule.Fail, row)) continue;
                    findings.Add(new SustainabilityFinding
                    {
                        RuleId = rule.Id,
                        Standard = rule.Standard,
                        Severity = rule.Severity,
                        MaterialName = row.Name,
                        Message = rule.Message,
                    });
                }
            }
            return findings;
        }

        private static bool WhereMatches(RulePredicate w, MaterialRow row)
        {
            if (w == null) return true; // no filter → applies to all
            if (!RegexOk(w.NameRegex,  row.Name)) return false;
            if (!RegexOk(w.ClassRegex, row.Class)) return false;
            if (w.OriginIn != null && w.OriginIn.Count > 0 &&
                !w.OriginIn.Any(o => string.Equals(o, row.Origin, StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }

        private static bool FailMatches(RulePredicate f, MaterialRow row)
        {
            if (f == null) return false; // a rule with no fail condition is a no-op
            if (f.CarbonGreaterThan.HasValue && row.CarbonKgCo2e <= f.CarbonGreaterThan.Value) return false;
            if (f.CarbonLessThan.HasValue && row.CarbonKgCo2e >= f.CarbonLessThan.Value) return false;
            if (f.CostGreaterThan.HasValue && row.Cost <= f.CostGreaterThan.Value) return false;
            if (f.MissingEpd.HasValue && f.MissingEpd.Value && row.EpdFreshness != EpdFreshness.Missing) return false;
            if (f.EpdExpired.HasValue && f.EpdExpired.Value && row.EpdFreshness != EpdFreshness.Expired) return false;
            return true;
        }

        private static bool RegexOk(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            try { return System.Text.RegularExpressions.Regex.IsMatch(value ?? "", pattern); }
            catch { return false; }
        }
    }
}
