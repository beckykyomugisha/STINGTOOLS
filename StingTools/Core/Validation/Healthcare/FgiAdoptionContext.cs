// Healthcare Pack HC-DEF-09 — FGI 2026 adoption escalation wiring.
//
// FgiAdoptionTracker (StingTools.Standards) knows which FGI clauses a US
// jurisdiction has made enforceable (Warning→Error) by a given date, but nothing
// invoked it. This context is the wiring: it resolves the project's US
// jurisdiction (PRJ_ORG_HEALTH_FGI_JURISDICTION_TXT) and design-freeze date
// (PRJ_ORG_HEALTH_DESIGN_FREEZE_DT) from ProjectInformation, loads a project-
// supplied finding-code→FGI-clause map (HEALTHCARE_FGI_CLAUSE_MAP.json + optional
// <project>/_BIM_COORD/fgi_clause_map.json override), and escalates a finding's
// severity when its mapped clause has been adopted by the freeze date.
//
// Mechanism only — the mapping data is project-supplied and empty by default, so
// with no jurisdiction / freeze date / mapping the behaviour is unchanged.

using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Validation;
using StingTools.Standards.FGI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StingTools.Core.Validation.Healthcare
{
    internal class FgiClauseMapDef
    {
        [JsonProperty("mappings")] public Dictionary<string, string> Mappings { get; set; }
            = new Dictionary<string, string>();
    }

    public class FgiAdoptionTable
    {
        public string Jurisdiction { get; }
        public DateTime? FreezeDate { get; }
        private readonly Dictionary<string, string> _map;

        public FgiAdoptionTable(string jurisdiction, DateTime? freeze, Dictionary<string, string> map)
        {
            Jurisdiction = jurisdiction ?? "";
            FreezeDate = freeze;
            _map = new Dictionary<string, string>(map ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }

        public bool Active => !string.IsNullOrWhiteSpace(Jurisdiction) && FreezeDate.HasValue && _map.Count > 0;
        public string ClauseFor(string findingCode) =>
            !string.IsNullOrEmpty(findingCode) && _map.TryGetValue(findingCode, out var c) ? c : null;
    }

    public static class FgiAdoptionContext
    {
        private const string CorporateFileName = "HEALTHCARE_FGI_CLAUSE_MAP.json";
        private const string ProjectFileName = "fgi_clause_map.json";

        private static readonly ConcurrentDictionary<string, FgiAdoptionTable> _cache
            = new ConcurrentDictionary<string, FgiAdoptionTable>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; }
            catch { return ""; }
        }

        public static FgiAdoptionTable Get(Document doc) => _cache.GetOrAdd(DocKey(doc), _ => Load(doc));
        public static void Reload(Document doc = null)
        {
            if (doc == null) _cache.Clear();
            else _cache.TryRemove(DocKey(doc), out _);
        }

        /// <summary>Escalates a finding's severity to Error when its mapped FGI clause has
        /// been adopted by the project jurisdiction on/before the design-freeze date.
        /// Returns <paramref name="current"/> unchanged when the seam is not configured.</summary>
        public static ValidationSeverity Escalate(Document doc, string findingCode, ValidationSeverity current)
        {
            if (current == ValidationSeverity.Error) return current;
            try
            {
                var t = Get(doc);
                if (!t.Active) return current;
                var clause = t.ClauseFor(findingCode);
                if (string.IsNullOrEmpty(clause)) return current;
                var sev = FgiAdoptionTracker.ResolveSeverity(clause, t.Jurisdiction, t.FreezeDate.Value);
                return sev == FgiSeverity.Error ? ValidationSeverity.Error : current;
            }
            catch (Exception ex) { StingLog.Warn($"FgiAdoptionContext.Escalate suppressed: {ex.Message}"); return current; }
        }

        private static FgiAdoptionTable Load(Document doc)
        {
            string jurisdiction = ReadProjString(doc, "PRJ_ORG_HEALTH_FGI_JURISDICTION_TXT");
            DateTime? freeze = null;
            var raw = ReadProjString(doc, "PRJ_ORG_HEALTH_DESIGN_FREEZE_DT");
            if (!string.IsNullOrWhiteSpace(raw) &&
                DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                freeze = dt;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string corp = StingToolsApp.FindDataFile(CorporateFileName);
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    Merge(map, JsonConvert.DeserializeObject<FgiClauseMapDef>(File.ReadAllText(corp)));
            }
            catch (Exception ex) { StingLog.Warn($"FgiAdoptionContext corporate load: {ex.Message}"); }
            try
            {
                string dir = DocKey(doc);
                if (!string.IsNullOrEmpty(dir))
                {
                    string proj = Path.Combine(dir, "_BIM_COORD", ProjectFileName);
                    if (File.Exists(proj))
                    {
                        Merge(map, JsonConvert.DeserializeObject<FgiClauseMapDef>(File.ReadAllText(proj)));
                        StingLog.Info($"FgiAdoptionContext: project overlay loaded from {proj}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FgiAdoptionContext overlay load: {ex.Message}"); }

            return new FgiAdoptionTable(jurisdiction, freeze, map);
        }

        private static void Merge(Dictionary<string, string> into, FgiClauseMapDef def)
        {
            foreach (var kv in def?.Mappings ?? new Dictionary<string, string>())
                if (!string.IsNullOrWhiteSpace(kv.Key)) into[kv.Key.Trim()] = kv.Value;
        }

        private static string ReadProjString(Document doc, string name)
        {
            try
            {
                var p = doc?.ProjectInformation?.LookupParameter(name);
                return (p != null && p.HasValue && p.StorageType == StorageType.String) ? (p.AsString() ?? "") : "";
            }
            catch { return ""; }
        }
    }
}
