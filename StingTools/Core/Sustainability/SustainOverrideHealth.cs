// StingTools — SustainOverrideHealth (SUS-5).
//
// The sustainability registries (grid carbon, baselines, schemes, measures, water
// profiles, ICE energy) each load a corporate baseline + an optional project override
// from <project>/_BIM_COORD/sustainability/. Previously a malformed override (bad JSON)
// or an unreadable file was swallowed silently (`catch { return; }`), so the in-code
// default (e.g. grid 0.45 kgCO2e/kWh) masqueraded as a real factor with no warning.
//
// This collector lets each registry record a load failure; the engine drains it into
// the run result's Warnings each Compute, so the dashboard / report / export all show
// "your override failed to load - using default" instead of silently degrading.
//
// Thread-safe; pure (no Revit / logging dependency, so the unit-test project can link
// it). The engine logs each drained issue via StingLog at the drain point.

using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    public static class SustainOverrideHealth
    {
        private static readonly object _lock = new object();
        private static readonly List<string> _issues = new List<string>();

        /// <summary>Record a registry override/data load failure (deduplicated).</summary>
        public static void Report(string registry, string detail)
        {
            string m = $"{registry}: {detail} - using the in-code default (the factor is NOT from your data; fix the file and re-run).";
            lock (_lock) { if (!_issues.Contains(m)) _issues.Add(m); }
        }

        /// <summary>SUS-7 — schema-version gate. Warns when a data/override file's "schema"
        /// field doesn't match the expected family prefix, so a future incompatible schema is
        /// surfaced (fields may be read wrong) instead of silently mis-parsed. Empty schema =
        /// legacy file, tolerated.</summary>
        public static void CheckSchema(string registry, string schemaValue, string expectedPrefix)
        {
            if (!string.IsNullOrEmpty(schemaValue) &&
                !schemaValue.StartsWith(expectedPrefix, System.StringComparison.OrdinalIgnoreCase))
                Report(registry, $"schema '{schemaValue}' is not '{expectedPrefix}*' - fields may be read incorrectly");
        }

        /// <summary>Return + clear the accumulated issues (the engine folds these into the
        /// run Warnings each Compute, so a later fixed load shows no stale warning).</summary>
        public static List<string> Drain()
        {
            lock (_lock) { var c = new List<string>(_issues); _issues.Clear(); return c; }
        }
    }
}
