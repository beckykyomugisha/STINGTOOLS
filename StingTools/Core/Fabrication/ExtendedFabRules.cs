using StingTools.Core;
// StingTools v4 MVP — Phase I.4 extended fabrication rules loader.
//
// Reads STING_FAB_RULES_EXT.json and exposes typed access. The 38
// rules span AWS D1.1 / ASME B31.3 / ASME B16.5 / BS EN 13480 /
// SMACNA 4e / DW/144 / EN 1507 / BESA TR/19 / NFPA 90A / BS EN
// 61386 / BS 7671 / BS EN 61537 / BS EN 50174-2 / BS EN 60079-14 /
// BS 5839-1 / Polywater + HSE L23 — the landscape the research
// brief identified as the gap between v4's basic MaxLength/MaxBends
// checks and a production fabrication rule engine.
//
// AssemblyGrouper can opt into these rules by passing
// ExtendedFabRules.All() into a new break-rule interpreter (Phase
// I.4a — next commit). Phase I.4 ships the data + loader so every
// downstream consumer (rule evaluator, QA audit, cost engine) sees
// the same source of truth.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Fabrication
{
    public class ExtendedFabRule
    {
        public string Id        { get; set; } = "";
        public string Scope     { get; set; } = "";      // Piping / Ducting / Electrical / Cross
        public int    Priority  { get; set; } = 50;      // evaluated high → low
        public string Severity  { get; set; } = "info";  // error / warning / info
        public string Basis     { get; set; } = "";      // standard citation
        public string Rationale { get; set; } = "";
        public string Break     { get; set; } = "";      // token naming the break-condition family
        public List<string> MaterialFrom { get; set; } = new List<string>();
        public List<string> MaterialTo   { get; set; } = new List<string>();
        public double? MaxWMm   { get; set; }
        public double? MaxHMm   { get; set; }
        public double? MaxLMm   { get; set; }
        public List<double> TiersKg { get; set; } = new List<double>();

        public bool IsError   => string.Equals(Severity, "error",   StringComparison.OrdinalIgnoreCase);
        public bool IsWarning => string.Equals(Severity, "warning", StringComparison.OrdinalIgnoreCase);
        public bool IsInfo    => string.Equals(Severity, "info",    StringComparison.OrdinalIgnoreCase);
    }

    public static class ExtendedFabRules
    {
        private static readonly object _lock = new object();
        private static List<ExtendedFabRule> _cache;
        private static bool _loaded;

        public static List<ExtendedFabRule> All()
        {
            lock (_lock)
            {
                if (_loaded) return _cache ?? new List<ExtendedFabRule>();
                _cache  = Load();
                _loaded = true;
                return _cache;
            }
        }

        public static void Reload()
        {
            lock (_lock) { _loaded = false; }
            All();
        }

        private static List<ExtendedFabRule> Load()
        {
            var list = new List<ExtendedFabRule>();
            try
            {
                var path = Core.StingToolsApp.FindDataFile("Fabrication/STING_FAB_RULES_EXT.json")
                        ?? Core.StingToolsApp.FindDataFile("STING_FAB_RULES_EXT.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("ExtendedFabRules: STING_FAB_RULES_EXT.json not found; empty list");
                    return list;
                }
                var root = JObject.Parse(File.ReadAllText(path));
                var arr  = root["rules"] as JArray;
                if (arr == null) return list;
                foreach (var item in arr)
                {
                    try
                    {
                        var r = new ExtendedFabRule
                        {
                            Id         = item.Value<string>("id")        ?? "",
                            Scope      = item.Value<string>("scope")     ?? "",
                            Priority   = item.Value<int?>("priority")    ?? 50,
                            Severity   = item.Value<string>("severity")  ?? "info",
                            Basis      = item.Value<string>("basis")     ?? "",
                            Rationale  = item.Value<string>("rationale") ?? "",
                            Break      = item.Value<string>("break")     ?? "",
                            MaxWMm     = item["max_w_mm"]?.ToObject<double?>(),
                            MaxHMm     = item["max_h_mm"]?.ToObject<double?>(),
                            MaxLMm     = item["max_l_mm"]?.ToObject<double?>(),
                        };
                        if (item["material_from"] is JArray mf)
                            foreach (var t in mf) r.MaterialFrom.Add(t?.ToString() ?? "");
                        if (item["material_to"] is JArray mt)
                            foreach (var t in mt) r.MaterialTo.Add(t?.ToString() ?? "");
                        if (item["tiers_kg"] is JArray tiers)
                            foreach (var t in tiers)
                                if (t?.Type == JTokenType.Integer || t?.Type == JTokenType.Float)
                                    r.TiersKg.Add(t.ToObject<double>());
                        list.Add(r);
                    }
                    catch (Exception ex)
                    { StingLog.Warn($"ExtendedFabRules: rule parse failed: {ex.Message}"); }
                }
                list = list.OrderByDescending(r => r.Priority).ToList();
                StingLog.Info($"ExtendedFabRules: loaded {list.Count} rules from {path}");
            }
            catch (Exception ex)
            { StingLog.Warn($"ExtendedFabRules.Load: {ex.Message}"); }
            return list;
        }

        /// <summary>
        /// Filter by scope. Scopes: Piping / Ducting / Electrical /
        /// Cross. Cross rules apply to every discipline.
        /// </summary>
        public static IEnumerable<ExtendedFabRule> ForScope(string scope)
        {
            foreach (var r in All())
                if (string.Equals(r.Scope, scope, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.Scope, "Cross", StringComparison.OrdinalIgnoreCase))
                    yield return r;
        }
    }
}
