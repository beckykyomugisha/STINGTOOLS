// ══════════════════════════════════════════════════════════════════════════
//  RatePolicy.cs — Project-scoped overlay for the rate-provider waterfall.
//
//  Phase 195. The rate-provider priorities baked into each IRateProvider are
//  the corporate baseline. A project that wants a different commercial
//  ordering (or wants to disable a provider entirely — e.g. switch off the
//  live BCIS HTTP feed so a tender runs deterministically offline) drops a
//  `<project>/_BIM_COORD/boq_rate_policy.json` file:
//
//    {
//      "providers": {
//        "bcis-http":         { "enabled": false },
//        "es-override":       { "priority": 98 },
//        "project-rate-card": { "priority": 93 },
//        "material-library":  { "priority": 85 }
//      }
//    }
//
//  Every field is optional. Omitting a provider id leaves its baseline
//  priority + enabled state untouched. `enabled` defaults to true; `priority`
//  defaults to the provider's compiled-in baseline.
//
//  HOST-FREE — no Autodesk.Revit references — so it links into
//  StingTools.Boq.Tests and is unit-verified independently of Revit.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.BOQ.Rates
{
    /// <summary>
    /// Per-provider overlay entry. Both fields nullable so "unset" is
    /// distinguishable from "explicitly set to the default value".
    /// </summary>
    public sealed class RatePolicyEntry
    {
        [JsonProperty("enabled")]
        public bool? Enabled { get; set; }

        [JsonProperty("priority")]
        public int? Priority { get; set; }
    }

    /// <summary>
    /// The deserialised <c>boq_rate_policy.json</c>. An empty policy (no file,
    /// or an empty <c>providers</c> map) is a no-op overlay — every provider
    /// keeps its baseline priority and stays enabled.
    /// </summary>
    public sealed class RatePolicy
    {
        [JsonProperty("providers")]
        public Dictionary<string, RatePolicyEntry> Providers { get; set; }
            = new Dictionary<string, RatePolicyEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>An overlay that changes nothing — used when no file exists.</summary>
        public static RatePolicy Empty => new RatePolicy();

        /// <summary>
        /// Parse a policy from raw JSON. Returns an empty (no-op) policy on
        /// null/blank input or malformed JSON — a broken policy file must
        /// never break costing, it just falls back to the baseline order.
        /// </summary>
        public static RatePolicy Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Empty;
            try
            {
                var p = JsonConvert.DeserializeObject<RatePolicy>(json);
                if (p == null) return Empty;
                if (p.Providers == null)
                    p.Providers = new Dictionary<string, RatePolicyEntry>(StringComparer.OrdinalIgnoreCase);
                else if (!(p.Providers.Comparer is StringComparer sc) || sc != StringComparer.OrdinalIgnoreCase)
                {
                    // Newtonsoft builds a case-sensitive dictionary by default;
                    // re-key case-insensitively so "ES-Override" matches "es-override".
                    var ci = new Dictionary<string, RatePolicyEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in p.Providers) ci[kv.Key] = kv.Value;
                    p.Providers = ci;
                }
                return p;
            }
            catch
            {
                return Empty;
            }
        }

        /// <summary>
        /// Load the policy from <c>&lt;projectDir&gt;/_BIM_COORD/boq_rate_policy.json</c>.
        /// Returns an empty (no-op) policy when the directory is unknown or the
        /// file is absent. Never throws.
        /// </summary>
        public static RatePolicy Load(string projectDir)
        {
            if (string.IsNullOrWhiteSpace(projectDir)) return Empty;
            try
            {
                string path = Path.Combine(projectDir, "_BIM_COORD", "boq_rate_policy.json");
                if (!File.Exists(path)) return Empty;
                return Parse(File.ReadAllText(path));
            }
            catch
            {
                return Empty;
            }
        }

        /// <summary>
        /// True unless the provider is explicitly disabled in the policy.
        /// Unknown ids are enabled (the policy only ever subtracts what it names).
        /// </summary>
        public bool IsEnabled(string providerId)
        {
            if (string.IsNullOrEmpty(providerId)) return true;
            if (Providers != null && Providers.TryGetValue(providerId, out var e) && e?.Enabled == false)
                return false;
            return true;
        }

        /// <summary>
        /// The effective priority for a provider: the policy override when set,
        /// otherwise the compiled-in baseline.
        /// </summary>
        public int EffectivePriority(string providerId, int baseline)
        {
            if (!string.IsNullOrEmpty(providerId) && Providers != null &&
                Providers.TryGetValue(providerId, out var e) && e?.Priority != null)
                return e.Priority.Value;
            return baseline;
        }
    }
}
