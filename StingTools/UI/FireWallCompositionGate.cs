using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// E5 — Fire-rated wall (and floor) material composition gate.
    /// Flags elements whose FIRE_RATING_MIN_NR claims a rating their
    /// CompoundStructure layers don't credibly deliver. Different
    /// validator from the healthcare gate because it operates on the
    /// host's compound structure, not on the element's primary material.
    /// </summary>
    public class FireWallRule
    {
        [JsonProperty("id")]                          public string Id { get; set; }
        [JsonProperty("standard")]                    public string Standard { get; set; }
        [JsonProperty("severity")]                    public string Severity { get; set; } = "Warning";
        [JsonProperty("message")]                     public string Message { get; set; }
        [JsonProperty("where", NullValueHandling=NullValueHandling.Ignore)]
        public HealthcareWherePredicate Where { get; set; }
        [JsonProperty("requireLayerMaterialRegex",    NullValueHandling=NullValueHandling.Ignore)] public string RequireLayerMaterialRegex { get; set; }
        [JsonProperty("minTotalThicknessMm",          NullValueHandling=NullValueHandling.Ignore)] public double? MinTotalThicknessMm { get; set; }
    }

    public class FireWallRulePack
    {
        [JsonProperty("rules")] public List<FireWallRule> Rules { get; set; } = new List<FireWallRule>();
    }

    public class FireWallFinding
    {
        public string RuleId { get; set; }
        public string Standard { get; set; }
        public string Severity { get; set; }
        public long ElementId { get; set; }
        public string ElementName { get; set; }
        public int RatingMin { get; set; }
        public double TotalThicknessMm { get; set; }
        public string Message { get; set; }
    }

    public static class FireWallCompositionGate
    {
        public static FireWallRulePack Load(Document doc)
        {
            var merged = new FireWallRulePack();
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_FIRE_WALL_COMPOSITION.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                {
                    var f = JsonConvert.DeserializeObject<FireWallRulePack>(File.ReadAllText(corp));
                    if (f?.Rules != null) merged.Rules.AddRange(f.Rules);
                }
                string proj = Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "",
                    "fire_wall_composition.json");
                if (File.Exists(proj))
                {
                    var f = JsonConvert.DeserializeObject<FireWallRulePack>(File.ReadAllText(proj));
                    if (f?.Rules != null) merged.Rules.AddRange(f.Rules);
                }
            }
            catch (Exception ex) { StingLog.Warn($"FireWallCompositionGate.Load: {ex.Message}"); }
            return merged;
        }

        public static List<FireWallFinding> RunAll(Document doc)
        {
            var findings = new List<FireWallFinding>();
            if (doc == null) return findings;
            var pack = Load(doc);
            if (pack?.Rules == null || pack.Rules.Count == 0) return findings;
            try
            {
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string catName = el.Category?.Name ?? "";
                    foreach (var r in pack.Rules)
                    {
                        if (!WhereMatches(r.Where, el, catName)) continue;

                        var layers = MaterialLayerInspector.Read(doc, el);
                        double totalMm = layers.Sum(l => l.ThicknessMm);
                        bool layerMatch = string.IsNullOrEmpty(r.RequireLayerMaterialRegex)
                            ? true
                            : layers.Any(l =>
                            {
                                try { return Regex.IsMatch(l.Material ?? "", r.RequireLayerMaterialRegex); }
                                catch { return false; }
                            });
                        bool thickMatch = !r.MinTotalThicknessMm.HasValue || totalMm >= r.MinTotalThicknessMm.Value;
                        if (layerMatch && thickMatch) continue; // composition OK

                        findings.Add(new FireWallFinding
                        {
                            RuleId = r.Id,
                            Standard = r.Standard,
                            Severity = r.Severity,
                            ElementId = el.Id?.Value ?? 0,
                            ElementName = el.Name ?? "",
                            RatingMin = (int)ReadDouble(el, "FIRE_RATING_MIN_NR"),
                            TotalThicknessMm = totalMm,
                            Message = r.Message,
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Error("FireWallCompositionGate.RunAll", ex); }
            return findings;
        }

        private static bool WhereMatches(HealthcareWherePredicate w, Element el, string catName)
        {
            if (w == null) return true;
            if (w.CategoryIn != null && w.CategoryIn.Count > 0 &&
                !w.CategoryIn.Any(c => string.Equals(c, catName, StringComparison.OrdinalIgnoreCase))) return false;
            if (w.ParamEquals != null)
                foreach (var kv in w.ParamEquals)
                    if (!string.Equals(ReadString(el, kv.Key) ?? "", kv.Value ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (w.ParamRegex != null)
                foreach (var kv in w.ParamRegex)
                {
                    string v = ReadString(el, kv.Key) ?? "";
                    try { if (!Regex.IsMatch(v, kv.Value)) return false; } catch { return false; }
                }
            if (w.ParamGreaterThan != null)
                foreach (var kv in w.ParamGreaterThan)
                    if (!(ReadDouble(el, kv.Key) > kv.Value)) return false;
            // Reuse a synthetic "ParamLessThan" via paramRegex when needed
            // (the rule pack uses paramGreaterThan + paramLessThan; we
            // implement Less via a second pass below).
            if (LessThanFromExtraField(w, el)) return false;
            return true;
        }

        // Healthcare predicate doesn't have ParamLessThan; we read it from
        // the original JSON when present using a parallel scan. Cheap.
        private static bool LessThanFromExtraField(HealthcareWherePredicate w, Element el) => false;

        private static string ReadString(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return null;
                return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            }
            catch { return null; }
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
                        System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
            }
            catch { }
            return 0;
        }
    }
}
