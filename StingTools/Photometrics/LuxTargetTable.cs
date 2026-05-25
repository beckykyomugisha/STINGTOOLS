using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Photometrics
{
    /// <summary>
    /// Single source of truth for room maintained-illuminance targets.
    /// Loaded once per session from <c>STING_LUX_TARGETS.json</c>; falls
    /// back to a hard-coded baseline if the file is missing. Replaces
    /// the duplicated lux-target tables that previously sat in
    /// <c>ElectricalSnapshotBuilder.LuxTargetFor</c> and
    /// <c>PhotometricDesignReviewCommand.BuildLuxTargets</c> (which had
    /// inconsistent default values — review found this).
    /// </summary>
    public class LuxTargetTable
    {
        public class Row
        {
            public string Type            { get; set; } = "";
            public List<string> Patterns  { get; set; } = new List<string>();
            public double LuxTarget       { get; set; }
            public double UniformityMin   { get; set; }
        }

        public double DefaultLuxTarget { get; private set; } = 300;
        public List<Row> Rows          { get; } = new List<Row>();

        private static LuxTargetTable _cache;
        private static readonly object _lock = new object();

        public static LuxTargetTable Load()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                var t = new LuxTargetTable();
                try
                {
                    string path = StingTools.Core.StingToolsApp.FindDataFile("STING_LUX_TARGETS.json");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var root = JObject.Parse(File.ReadAllText(path));
                        t.DefaultLuxTarget = root["defaultLuxTarget"]?.Value<double>() ?? 300;
                        foreach (var p in root["patterns"] as JArray ?? new JArray())
                        {
                            var pat = (p["patterns"] as JArray)?.Select(x => (x.ToString() ?? "").ToLowerInvariant()).ToList()
                                      ?? new List<string>();
                            t.Rows.Add(new Row
                            {
                                Type = p["type"]?.ToString() ?? "",
                                Patterns = pat,
                                LuxTarget = p["luxTarget"]?.Value<double>() ?? 0,
                                UniformityMin = p["uniformityMin"]?.Value<double>() ?? 0
                            });
                        }
                    }
                    else
                    {
                        t.SeedFallback();
                    }
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"LuxTargetTable.Load: {ex.Message}");
                    t.SeedFallback();
                }
                _cache = t;
                return _cache;
            }
        }

        public static void InvalidateCache() { lock (_lock) _cache = null; }

        /// <summary>Match a room name against patterns and return the lux target.
        /// Returns the default when no pattern matches.</summary>
        public double TargetFor(string roomName)
        {
            string n = (roomName ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(n)) return DefaultLuxTarget;
            foreach (var row in Rows)
                if (row.Patterns.Any(p => !string.IsNullOrEmpty(p) && n.Contains(p)))
                    return row.LuxTarget;
            return DefaultLuxTarget;
        }

        /// <summary>Match a room name and return (target, uniformityMin) tuple.</summary>
        public (double target, double uniformity) TargetAndUniformityFor(string roomName)
        {
            string n = (roomName ?? "").ToLowerInvariant();
            foreach (var row in Rows)
                if (row.Patterns.Any(p => !string.IsNullOrEmpty(p) && n.Contains(p)))
                    return (row.LuxTarget, row.UniformityMin);
            return (DefaultLuxTarget, 0.4);
        }

        private void SeedFallback()
        {
            DefaultLuxTarget = 300;
            void Add(string t, double lx, double uo, params string[] pats)
                => Rows.Add(new Row { Type = t, LuxTarget = lx, UniformityMin = uo, Patterns = new List<string>(pats) });
            Add("Office", 500, 0.6, "office", "open plan", "bullpen", "admin");
            Add("Conference", 500, 0.6, "conference", "meeting", "boardroom");
            Add("Corridor", 100, 0.4, "corridor", "passage", "circulation", "hallway");
            Add("Storage", 100, 0.4, "store", "storage", "riser");
            Add("Plant Room", 200, 0.4, "plant", "mechanical", "boiler", "switchgear");
        }
    }
}
