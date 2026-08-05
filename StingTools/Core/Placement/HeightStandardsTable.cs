using StingTools.Core;
// Phase 139 — Height standards lookup table.
//
// Loaded once from Data/Placement/STING_HEIGHT_STANDARDS.json (alongside
// the plug-in DLL). Keyed by HeightStandard identifier (e.g.
// "BS8300_SWITCH_1200_1400"). Used by AccessibilityAuditor to verify
// placed elements fall within Min/Max range.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Placement
{
    public class HeightStandardEntry
    {
        public double MinMm    { get; set; }
        public double MaxMm    { get; set; }
        public string Standard { get; set; } = "";
        public string Notes    { get; set; } = "";
    }

    /// <summary>
    /// Static cache of height-standards lookup table.  Reloads on demand
    /// via <see cref="Reload"/>.  Never throws — failure logs a warning
    /// and returns null lookups.
    /// </summary>
    public static class HeightStandardsTable
    {
        private const string FileName = "STING_HEIGHT_STANDARDS.json";
        private static readonly object _lock = new object();
        private static Dictionary<string, HeightStandardEntry> _cache;

        public static IReadOnlyDictionary<string, HeightStandardEntry> All
        {
            get
            {
                EnsureLoaded();
                return _cache;
            }
        }

        public static HeightStandardEntry Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureLoaded();
            return _cache.TryGetValue(key, out var e) ? e : null;
        }

        public static void Reload()
        {
            lock (_lock) { _cache = null; }
        }

        /// <summary>
        /// Phase 139.27 (N-05) — light validation pass over a rule library.
        /// For every rule whose <c>HeightStandard</c> field references an
        /// entry in this table, check that <c>MountingHeightMm</c> falls
        /// inside MinMm..MaxMm. Returns a list of human-readable warnings;
        /// callers (engine pre-flight, Placement Centre's audit button)
        /// surface them in the result panel. Pre-139.27 the table existed
        /// but no caller consulted it, so accessibility-non-compliant
        /// rules shipped silently.
        /// </summary>
        public static List<string> ValidateRulesAgainstStandards(IEnumerable<PlacementRule> rules)
        {
            var warnings = new List<string>();
            if (rules == null) return warnings;
            foreach (var r in rules)
            {
                if (r == null || string.IsNullOrEmpty(r.HeightStandard)) continue;
                var entry = Get(r.HeightStandard);
                if (entry == null)
                {
                    warnings.Add($"HeightStandards: rule '{r.MergeKey}' references unknown HeightStandard '{r.HeightStandard}' — add it to STING_HEIGHT_STANDARDS.json or clear the rule field.");
                    continue;
                }
                if (r.MountingHeightMm <= 0) continue; // rule defers to family-side default
                if (entry.MinMm > 0 && r.MountingHeightMm < entry.MinMm)
                    warnings.Add($"HeightStandards: rule '{r.MergeKey}' MountingHeightMm={r.MountingHeightMm:F0}mm is BELOW {r.HeightStandard} minimum ({entry.MinMm:F0}mm, {entry.Standard}).");
                if (entry.MaxMm > 0 && r.MountingHeightMm > entry.MaxMm)
                    warnings.Add($"HeightStandards: rule '{r.MergeKey}' MountingHeightMm={r.MountingHeightMm:F0}mm is ABOVE {r.HeightStandard} maximum ({entry.MaxMm:F0}mm, {entry.Standard}).");
            }
            return warnings;
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_cache != null) return;
                _cache = TryLoad() ?? new Dictionary<string, HeightStandardEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, HeightStandardEntry> TryLoad()
        {
            try
            {
                string path = StingToolsApp.FindDataFile(FileName);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn($"HeightStandardsTable: {FileName} not found in DataPath");
                    return null;
                }
                var json = JObject.Parse(File.ReadAllText(path));
                var standards = json["Standards"] as JObject;
                if (standards == null) return null;
                var dict = new Dictionary<string, HeightStandardEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in standards.Properties())
                {
                    var entry = prop.Value.ToObject<HeightStandardEntry>();
                    if (entry != null) dict[prop.Name] = entry;
                }
                return dict;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HeightStandardsTable.TryLoad failed: {ex.Message}");
                return null;
            }
        }
    }
}
