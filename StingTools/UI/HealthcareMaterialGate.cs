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
    /// N+6 — Healthcare material gate.
    ///
    /// Validates element ↔ material pairings against healthcare design
    /// codes (HTM 04-01 water, MRI Zone IV ferrous block, HTM 03-01
    /// isolation finishes, NCRP 147 lead shielding). Different from the
    /// generic sustainability gate: predicates here filter the ELEMENT
    /// (its category + parameters), then validate the material it uses.
    ///
    /// Rule JSON shape (corporate at <c>Data/STING_HEALTHCARE_MATERIAL_GATES.json</c>;
    /// project override at <c>&lt;project&gt;/_BIM_COORD/healthcare_material_gates.json</c>):
    /// <code>
    /// {
    ///   "rules": [
    ///     {
    ///       "id": "HTM_04_NoLeadSolder",
    ///       "standard": "HTM 04-01",
    ///       "severity": "Block",
    ///       "where": {
    ///         "categoryIn": ["Pipes","Plumbing Fixtures"],
    ///         "paramRegex": { "MGS_GAS_TYPE_TXT": "^(O2|MA4|MA7|N2O|N2|CO2|HE|VAC)$" }
    ///       },
    ///       "materialMustNotRegex": "(?i)lead.solder|leaded.brass",
    ///       "message": "Water / medical-gas pipework must not use lead-soldered fittings (HTM 04-01)."
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    public class HealthcareGateRule
    {
        [JsonProperty("id")]                   public string Id { get; set; }
        [JsonProperty("standard")]             public string Standard { get; set; }
        [JsonProperty("severity")]             public string Severity { get; set; } = "Warning";
        [JsonProperty("message")]              public string Message { get; set; }

        [JsonProperty("where", NullValueHandling = NullValueHandling.Ignore)]
        public HealthcareWherePredicate Where { get; set; }

        [JsonProperty("materialMustRegex",    NullValueHandling = NullValueHandling.Ignore)] public string MaterialMustRegex { get; set; }
        [JsonProperty("materialMustNotRegex", NullValueHandling = NullValueHandling.Ignore)] public string MaterialMustNotRegex { get; set; }
        [JsonProperty("materialClassRegex",   NullValueHandling = NullValueHandling.Ignore)] public string MaterialClassRegex { get; set; }
    }

    public class HealthcareWherePredicate
    {
        [JsonProperty("categoryIn",     NullValueHandling = NullValueHandling.Ignore)] public List<string> CategoryIn { get; set; }
        [JsonProperty("paramRegex",     NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, string> ParamRegex { get; set; }
        [JsonProperty("paramGreaterThan", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, double> ParamGreaterThan { get; set; }
        [JsonProperty("paramEquals",    NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, string> ParamEquals { get; set; }
    }

    public class HealthcareGateFile
    {
        [JsonProperty("rules")] public List<HealthcareGateRule> Rules { get; set; } = new List<HealthcareGateRule>();
    }

    public class HealthcareGateFinding
    {
        public string RuleId { get; set; }
        public string Standard { get; set; }
        public string Severity { get; set; }
        public long ElementId { get; set; }
        public string ElementName { get; set; }
        public string MaterialName { get; set; }
        public string Message { get; set; }
    }

    public static class HealthcareMaterialGate
    {
        public static HealthcareGateFile Load(Document doc)
        {
            var merged = new HealthcareGateFile();
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_HEALTHCARE_MATERIAL_GATES.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                {
                    var f = JsonConvert.DeserializeObject<HealthcareGateFile>(File.ReadAllText(corp));
                    if (f?.Rules != null) merged.Rules.AddRange(f.Rules);
                }
                string proj = Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "",
                    "healthcare_material_gates.json");
                if (File.Exists(proj))
                {
                    var f = JsonConvert.DeserializeObject<HealthcareGateFile>(File.ReadAllText(proj));
                    if (f?.Rules != null) merged.Rules.AddRange(f.Rules);
                }
            }
            catch (Exception ex) { StingLog.Warn($"HealthcareMaterialGate.Load: {ex.Message}"); }
            return merged;
        }

        public static List<HealthcareGateFinding> RunAll(Document doc)
        {
            var findings = new List<HealthcareGateFinding>();
            if (doc == null) return findings;
            var gate = Load(doc);
            if (gate?.Rules == null || gate.Rules.Count == 0) return findings;

            // Collect all categories referenced by rules so the
            // FilteredElementCollector pass is bounded.
            var catSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in gate.Rules)
                if (r.Where?.CategoryIn != null)
                    foreach (var c in r.Where.CategoryIn) catSet.Add(c);

            try
            {
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string catName = el.Category?.Name ?? "";
                    if (catSet.Count > 0 && !catSet.Contains(catName)) continue;

                    foreach (var r in gate.Rules)
                    {
                        if (!WhereMatches(r.Where, el, catName)) continue;
                        var (matName, matClass) = ResolveMaterial(doc, el);
                        if (!MaterialMatches(r, matName, matClass)) continue;

                        findings.Add(new HealthcareGateFinding
                        {
                            RuleId = r.Id,
                            Standard = r.Standard,
                            Severity = r.Severity,
                            ElementId = el.Id?.Value ?? 0,
                            ElementName = el.Name ?? "",
                            MaterialName = matName ?? "(none)",
                            Message = r.Message,
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Error("HealthcareMaterialGate.RunAll", ex); }
            return findings;
        }

        // ── Predicate evaluators ───────────────────────────────────────────

        private static bool WhereMatches(HealthcareWherePredicate w, Element el, string catName)
        {
            if (w == null) return true;
            if (w.CategoryIn != null && w.CategoryIn.Count > 0 &&
                !w.CategoryIn.Any(c => string.Equals(c, catName, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (w.ParamEquals != null)
                foreach (var kv in w.ParamEquals)
                {
                    string v = ReadString(el, kv.Key);
                    if (!string.Equals(v ?? "", kv.Value ?? "", StringComparison.OrdinalIgnoreCase)) return false;
                }
            if (w.ParamRegex != null)
                foreach (var kv in w.ParamRegex)
                {
                    string v = ReadString(el, kv.Key) ?? "";
                    try { if (!System.Text.RegularExpressions.Regex.IsMatch(v, kv.Value)) return false; }
                    catch { return false; }
                }
            if (w.ParamGreaterThan != null)
                foreach (var kv in w.ParamGreaterThan)
                {
                    double v = ReadDouble(el, kv.Key);
                    if (!(v > kv.Value)) return false;
                }
            return true;
        }

        private static bool MaterialMatches(HealthcareGateRule r, string matName, string matClass)
        {
            matName ??= "";
            matClass ??= "";
            if (!string.IsNullOrEmpty(r.MaterialMustRegex))
            {
                try { if (!System.Text.RegularExpressions.Regex.IsMatch(matName, r.MaterialMustRegex)) return true; }
                catch { return false; }
            }
            if (!string.IsNullOrEmpty(r.MaterialMustNotRegex))
            {
                try { if (System.Text.RegularExpressions.Regex.IsMatch(matName, r.MaterialMustNotRegex)) return true; }
                catch { return false; }
            }
            if (!string.IsNullOrEmpty(r.MaterialClassRegex))
            {
                try { if (!System.Text.RegularExpressions.Regex.IsMatch(matClass, r.MaterialClassRegex)) return true; }
                catch { return false; }
            }
            return false;
        }

        private static (string name, string cls) ResolveMaterial(Document doc, Element el)
        {
            try
            {
                Parameter p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (mid != null && mid.Value > 0 && doc.GetElement(mid) is Material m)
                        return (m.Name ?? "", m.MaterialClass ?? "");
                }
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var mid in mats)
                        if (mid != null && mid.Value > 0 && doc.GetElement(mid) is Material m2)
                            return (m2.Name ?? "", m2.MaterialClass ?? "");
            }
            catch (Exception ex) { StingLog.WarnRateLimited("HCGate.ResolveMaterial", $"ResolveMaterial: {ex.Message}"); }
            return ("", "");
        }

        private static string ReadString(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return null;
                return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            }
            catch (Exception ex) { StingLog.WarnRateLimited("HCGate.ReadString", $"ReadString {paramName}: {ex.Message}"); return null; }
        }

        private static double ReadDouble(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("HCGate.ReadDouble", $"ReadDouble {paramName}: {ex.Message}"); }
            return 0;
        }
    }
}
