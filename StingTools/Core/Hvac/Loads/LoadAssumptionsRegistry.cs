// StingTools — Load assumptions registry (Tier-2 item 2.4).
//
// Exposes the design-day load-calc constants that were previously
// hardcoded literals in BlockLoadEngine as project-tunable values:
//   - cooling / heating design day-of-year (202 / 21)
//   - outdoor daily dry-bulb range (8 K)
//   - diffuse-on-horizontal fraction of direct-normal (0.15)
//   - windward-wall pressure coefficient for infiltration (0.6)
//
// Layered exactly like ClimateRegistry / ConstructionProfileRegistry:
//   corporate baseline → Data/STING_LOAD_ASSUMPTIONS.json
//   project override   → <project>/_BIM_COORD/load_assumptions.json
//
// The defaults reproduce the engine's prior hardcoded behaviour, so a
// project with no override computes an identical load.

using System;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Hvac.Loads
{
    /// <summary>Design-day load-calc assumptions (Tier-2 2.4).</summary>
    public class LoadAssumptions
    {
        /// <summary>Cooling design day-of-year (ASHRAE July 21 = 202).</summary>
        public int CoolingDesignDoy { get; set; } = 202;
        /// <summary>Heating design day-of-year (ASHRAE January 21 = 21).</summary>
        public int HeatingDesignDoy { get; set; } = 21;
        /// <summary>Outdoor dry-bulb daily range, K (CIBSE Guide A 2.4 default 8 K).</summary>
        public double OutdoorDailyRangeK { get; set; } = 8.0;
        /// <summary>Diffuse-on-horizontal as a fraction of direct-normal irradiance.</summary>
        public double DiffuseFraction { get; set; } = 0.15;
        /// <summary>Windward-wall pressure coefficient for the CIBSE §4.6
        /// wind-driven infiltration term.</summary>
        public double InfiltrationWindwardCp { get; set; } = 0.6;
    }

    public static class LoadAssumptionsRegistry
    {
        public const string DataFileName = "STING_LOAD_ASSUMPTIONS.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/load_assumptions.json";

        private static readonly ConcurrentDictionary<string, LoadAssumptions> _cache
            = new ConcurrentDictionary<string, LoadAssumptions>(StringComparer.OrdinalIgnoreCase);

        public static LoadAssumptions Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()             => _cache.Clear();
        public static void Reload(Document doc) => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static LoadAssumptions Load(Document doc)
        {
            // Start from defaults (== the engine's prior hardcoded values), then
            // layer the corporate baseline, then the project override on top.
            var la = new LoadAssumptions();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), la);

                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), la);
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("LoadAssumptionsRegistry.Load", ex);
            }
            return la;
        }

        private static void Apply(JObject j, LoadAssumptions la)
        {
            // Accept either a top-level "loads" object or the keys at the root.
            var loads = j["loads"] as JObject ?? j;
            if (loads == null) return;

            if (loads["coolingDesignDoy"] != null)
                la.CoolingDesignDoy = (int?)loads["coolingDesignDoy"] ?? la.CoolingDesignDoy;
            if (loads["heatingDesignDoy"] != null)
                la.HeatingDesignDoy = (int?)loads["heatingDesignDoy"] ?? la.HeatingDesignDoy;
            if (loads["outdoorDailyRangeK"] != null)
                la.OutdoorDailyRangeK = (double?)loads["outdoorDailyRangeK"] ?? la.OutdoorDailyRangeK;
            if (loads["diffuseFraction"] != null)
                la.DiffuseFraction = (double?)loads["diffuseFraction"] ?? la.DiffuseFraction;
            if (loads["infiltrationWindwardCp"] != null)
                la.InfiltrationWindwardCp = (double?)loads["infiltrationWindwardCp"] ?? la.InfiltrationWindwardCp;
        }
    }
}
