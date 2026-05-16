// StingTools v4 MVP — routing rule loader (separation + service corridors).
//
// Loads STING_SEPARATION_RULES.json and STING_SERVICE_CORRIDORS.json
// from Data/Routing/, caches the parsed structures for the lifetime
// of the plugin, and exposes them as typed POCOs so the drop engines
// can enforce both BS EN 50174-2 service separations and suspended-
// ceiling corridor band allocations without re-parsing the JSON on
// every drop.
//
// Phase A: loader + lookup helpers. Full band-conflict auto-correction
// is Phase C (requires the A* voxel solver to re-path around an
// out-of-band drop).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Routing
{
    public class SeparationRule
    {
        public string Id              { get; set; } = "";
        public string SourceService   { get; set; } = "";
        public string TargetService   { get; set; } = "";
        public string Geometry        { get; set; } = "any";
        public double MinSeparationMm { get; set; }
        public bool?  BothEnclosedMetal { get; set; }
        public bool?  ShareContainment  { get; set; }
        public string Rationale       { get; set; } = "";

        public bool AppliesTo(string sourceService, string targetService)
        {
            if (string.IsNullOrEmpty(sourceService) || string.IsNullOrEmpty(targetService))
                return false;
            bool srcMatches =
                SourceService == "*" ||
                string.Equals(SourceService, sourceService, StringComparison.OrdinalIgnoreCase);
            bool tgtMatches =
                TargetService == "*" ||
                string.Equals(TargetService, targetService, StringComparison.OrdinalIgnoreCase);
            // Separation rules are symmetric — A↔B separation is also B↔A.
            if (srcMatches && tgtMatches) return true;
            bool srcMatchesB =
                SourceService == "*" ||
                string.Equals(SourceService, targetService, StringComparison.OrdinalIgnoreCase);
            bool tgtMatchesA =
                TargetService == "*" ||
                string.Equals(TargetService, sourceService, StringComparison.OrdinalIgnoreCase);
            return srcMatchesB && tgtMatchesA;
        }
    }

    public class CorridorBand
    {
        public string  Id              { get; set; } = "";
        public string  Label           { get; set; } = "";
        public double  MinMm           { get; set; }
        public double  MaxMm           { get; set; }
        public List<string> AllowedServices { get; set; } = new List<string>();
        public string  Notes           { get; set; } = "";

        public bool PermitsService(string serviceId)
        {
            if (AllowedServices == null || AllowedServices.Count == 0) return true;
            foreach (var s in AllowedServices)
                if (string.Equals(s, serviceId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public double CentreMm => 0.5 * (MinMm + MaxMm);
    }

    public static class RoutingRules
    {
        private static readonly object _lock = new object();
        private static List<SeparationRule>   _sepRules;
        private static List<CorridorBand>     _bands;
        private static bool _loaded;

        public static List<SeparationRule> SeparationRules
        {
            get { EnsureLoaded(); return _sepRules ?? new List<SeparationRule>(); }
        }

        public static List<CorridorBand> CorridorBands
        {
            get { EnsureLoaded(); return _bands ?? new List<CorridorBand>(); }
        }

        public static void Reload()
        {
            lock (_lock) { _loaded = false; }
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _sepRules = LoadSeparationRules();
                _bands    = LoadCorridorBands();
                _loaded   = true;
            }
        }

        private static List<SeparationRule> LoadSeparationRules()
        {
            var rules = new List<SeparationRule>();
            try
            {
                var path = FindFile("Routing/STING_SEPARATION_RULES.json")
                           ?? FindFile("STING_SEPARATION_RULES.json");
                if (path == null) return rules;
                var json  = File.ReadAllText(path);
                var root  = JObject.Parse(json);
                var arr   = root["rules"] as JArray;
                if (arr == null) return rules;
                foreach (var item in arr)
                {
                    try { rules.Add(item.ToObject<SeparationRule>()); }
                    catch (Exception ex)
                    { StingLog.Warn($"RoutingRules: separation item parse failed: {ex.Message}"); }
                }
                StingLog.Info($"RoutingRules: loaded {rules.Count} separation rules from {path}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RoutingRules: separation load failed: {ex.Message}");
            }
            return rules;
        }

        private static List<CorridorBand> LoadCorridorBands()
        {
            var bands = new List<CorridorBand>();
            try
            {
                var path = FindFile("Routing/STING_SERVICE_CORRIDORS.json")
                           ?? FindFile("STING_SERVICE_CORRIDORS.json");
                if (path == null) return bands;
                var json = File.ReadAllText(path);
                var root = JObject.Parse(json);
                var arr  = root["bands"] as JArray;
                if (arr == null) return bands;
                foreach (var item in arr)
                {
                    try { bands.Add(item.ToObject<CorridorBand>()); }
                    catch (Exception ex)
                    { StingLog.Warn($"RoutingRules: band item parse failed: {ex.Message}"); }
                }
                StingLog.Info($"RoutingRules: loaded {bands.Count} corridor bands from {path}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RoutingRules: bands load failed: {ex.Message}");
            }
            return bands;
        }

        private static string FindFile(string relative)
        {
            try { return Core.StingToolsApp.FindDataFile(relative); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Find the corridor band whose allowed-services list contains
        /// the given service id. Returns the first match (bands are
        /// evaluated in JSON order). Null when no band claims the
        /// service.
        /// </summary>
        public static CorridorBand FindBandForService(string serviceId)
        {
            if (string.IsNullOrEmpty(serviceId)) return null;
            foreach (var b in CorridorBands)
                if (b.PermitsService(serviceId)) return b;
            return null;
        }

        /// <summary>
        /// Aggregate separation rules matching the given (source,target)
        /// service pair into the maximum required separation in mm.
        /// Returns 0 when no rules apply.
        /// </summary>
        public static double RequiredSeparationMm(string sourceService, string targetService)
        {
            double max = 0;
            foreach (var r in SeparationRules)
                if (r.AppliesTo(sourceService, targetService) && r.MinSeparationMm > max)
                    max = r.MinSeparationMm;
            return max;
        }
    }
}
