// ══════════════════════════════════════════════════════════════════════════
//  VariationLiabilityMap.cs — Config-driven contract-specific liability
//  defaults (Phase 184p / caveat #3 closure).
//
//  Loaded once from Data/STING_VARIATION_LIABILITY_MAP.json + a project
//  override at <project>/_BIM_COORD/variation_liability_map.json.
//
//  Lookup: "{ContractForm}|{Reason}" exact key, then fall back to
//  "default|{Reason}", then the C# SuggestLiability table. Lets a
//  FIDIC Yellow Book project route ErrorOmission → Contractor (because
//  the contractor owns the design risk) while JCT 2024 keeps the same
//  ErrorOmission → Designer default.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.Core.Variation
{
    public sealed class VariationLiabilityMap
    {
        private static readonly ConcurrentDictionary<string, VariationLiabilityMap> _cache
            = new ConcurrentDictionary<string, VariationLiabilityMap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Composite key "{ContractForm}|{Reason}" → liability label.</summary>
        public IReadOnlyDictionary<string, string> Rules { get; }

        private VariationLiabilityMap(Dictionary<string, string> rules)
        {
            Rules = rules;
        }

        public static VariationLiabilityMap Get(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Invalidate() => _cache.Clear();

        /// <summary>
        /// Resolve the default liability for a reason under a given
        /// contract form. Returns the C# code-default when the JSON map
        /// has no matching entry.
        /// </summary>
        public VariationLiability Resolve(string contractForm, VariationReason reason,
            VariationLiability codeDefault)
        {
            if (Rules == null || Rules.Count == 0) return codeDefault;
            string key = $"{contractForm}|{reason}";
            if (Rules.TryGetValue(key, out string match) && TryParseLiability(match, out var v))
                return v;
            string fallbackKey = $"default|{reason}";
            if (Rules.TryGetValue(fallbackKey, out string defaultMatch) &&
                TryParseLiability(defaultMatch, out var d))
                return d;
            return codeDefault;
        }

        private static bool TryParseLiability(string s, out VariationLiability v)
            => Enum.TryParse(s, true, out v);

        private static VariationLiabilityMap Load(Document doc)
        {
            var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Corporate baseline.
            LoadFile(StingToolsApp.FindDataFile("STING_VARIATION_LIABILITY_MAP.json"), rules);

            // Project override — entries here win by key.
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (!string.IsNullOrEmpty(bimDir))
                {
                    string parent = Path.GetDirectoryName(bimDir);
                    if (!string.IsNullOrEmpty(parent))
                        LoadFile(Path.Combine(parent, "_BIM_COORD", "variation_liability_map.json"), rules);
                }
            }
            catch (Exception ex) { StingLog.Warn($"VariationLiabilityMap project override: {ex.Message}"); }

            return new VariationLiabilityMap(rules);
        }

        private static void LoadFile(string path, Dictionary<string, string> sink)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                if (json["rules"] is JObject ruleObj)
                {
                    foreach (var prop in ruleObj.Properties())
                    {
                        // Skip comment / documentation keys prefixed with "_".
                        if (prop.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                        sink[prop.Name] = prop.Value?.Value<string>() ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VariationLiabilityMap.LoadFile({Path.GetFileName(path)}): {ex.Message}");
            }
        }
    }
}
