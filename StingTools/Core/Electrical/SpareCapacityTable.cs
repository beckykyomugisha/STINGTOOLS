// SpareCapacityTable — loads the per-sector spare-capacity targets.
//
// Split from SpareCapacityTarget purely so that file has no dependency on
// StingToolsApp and can be compiled into StingTools.Tags.Tests. The rule for
// choosing a number is the part worth testing; finding the file is not.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Electrical
{
    /// <summary>Reads spareTargetsPct from STING_DIVERSITY_FACTORS.json.</summary>
    public static class SpareCapacityTable
    {
        private static Dictionary<string, double> _table;
        private static readonly object _lock = new object();

        /// <summary>Drops the cache so an edited data file is picked up without restarting Revit.</summary>
        public static void Reload() { lock (_lock) { _table = null; } }

        /// <summary>Sector name to target percentage. Empty when the file is missing.</summary>
        public static Dictionary<string, double> Load()
        {
            lock (_lock)
            {
                if (_table != null) return _table;
                var table = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_DIVERSITY_FACTORS.json");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        StingLog.Warn("SpareCapacityTable: STING_DIVERSITY_FACTORS.json not found — every " +
                                      $"sector falls back to {SpareCapacityTarget.FallbackPct}%");
                    }
                    else
                    {
                        var root = JObject.Parse(File.ReadAllText(path));
                        foreach (var s in (root["spareTargetsPct"] as JObject)?.Properties()
                                          ?? Enumerable.Empty<JProperty>())
                            table[s.Name] = s.Value.Value<double>();

                        if (table.Count == 0)
                            StingLog.Warn("SpareCapacityTable: spareTargetsPct is empty or missing — every " +
                                          $"sector falls back to {SpareCapacityTarget.FallbackPct}%");
                        else
                            StingLog.Info($"SpareCapacityTable: {table.Count} sector target(s) loaded");
                    }
                }
                catch (Exception ex)
                {
                    // Cached anyway, deliberately: an unreadable file would
                    // otherwise be re-read and re-logged on every warning
                    // evaluation, on every element, for the life of the session.
                    StingLog.Warn($"SpareCapacityTable.Load: {ex.Message} — " +
                                  $"falling back to {SpareCapacityTarget.FallbackPct}%");
                }
                _table = table;
                return _table;
            }
        }

        /// <summary>The target for a sector, using the shipped table.</summary>
        public static double TargetPct(string sector)
            => SpareCapacityTarget.TargetPct(sector, Load());
    }
}
