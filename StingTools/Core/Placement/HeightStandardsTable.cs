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
