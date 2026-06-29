// StingTools — Per-space-type load profile registry.
//
// Closes the "schedules + densities are hardcoded to office" gap:
// loads now come from a corporate-baseline JSON keyed by space type,
// with per-project overrides at <project>/_BIM_COORD/load_profiles.json.
//
// HvacBlockLoadCommand.ZoneFromSpace looks up the profile via
// HVC_SPACE_TYPE_TXT (preferred) or the Revit SpaceType.Name (fallback)
// and seeds LoadZone with the matching schedules, densities, OA and
// setpoints. When no profile matches, the default "Office" profile is
// returned so legacy projects still get a valid number.
//
// Sources cited in STING_LOAD_PROFILES.json header.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Hvac.Loads
{
    // LoadProfile + LoadProfileLibrary moved to LoadProfileModels.cs (pure, unit-tested
    // resolution). This file keeps the Document-facing loader only. WS K2.

    public static class LoadProfileRegistry
    {
        public const string DataFileName = "STING_LOAD_PROFILES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/load_profiles.json";

        private static readonly ConcurrentDictionary<string, LoadProfileLibrary> _cache
            = new ConcurrentDictionary<string, LoadProfileLibrary>(StringComparer.OrdinalIgnoreCase);

        public static LoadProfileLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()             => _cache.Clear();
        public static void Reload(Document doc) => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static LoadProfileLibrary Load(Document doc)
        {
            var lib = new LoadProfileLibrary();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), lib);

                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), lib);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Error("LoadProfileRegistry.Load", ex); }

            // Guarantee an Office profile exists so the fallback path never fails.
            if (!lib.ById.ContainsKey("Office"))
                lib.ById["Office"] = new LoadProfile { Id = "Office", Label = "Office (default)" };
            return lib;
        }

        private static void Apply(JObject j, LoadProfileLibrary lib)
        {
            var profiles = j["profiles"] as JArray;
            if (profiles == null) return;
            foreach (var p in profiles.OfType<JObject>())
            {
                var profile = new LoadProfile
                {
                    Id    = (string)p["id"] ?? "",
                    Label = (string)p["label"] ?? "",
                    OccupantDensityM2PerPerson = (double?)p["occupantDensityM2PerPerson"] ?? 10.0,
                    OccupantSensibleW          = (double?)p["occupantSensibleW"]          ?? 75,
                    OccupantLatentW            = (double?)p["occupantLatentW"]            ?? 55,
                    LightingWPerM2             = (double?)p["lightingWPerM2"]             ?? 7.6,
                    EquipmentWPerM2            = (double?)p["equipmentWPerM2"]            ?? 8.0,
                    OaLpsPerPerson             = (double?)p["oaLpsPerPerson"]             ?? 10.0,
                    OaLpsPerM2                 = (double?)p["oaLpsPerM2"]                 ?? 0.3,
                    CoolingSetpointC           = (double?)p["coolingSetpointC"]           ?? 24,
                    HeatingSetpointC           = (double?)p["heatingSetpointC"]           ?? 21,
                    InfiltrationAch            = (double?)p["infiltrationAch"]            ?? 0.3,
                    // WS K1/K5 — new fields (back-compat defaults when absent).
                    DhwLPerPersonDay           = (double?)p["dhwLPerPersonDay"]           ?? 5.0,
                    OperatingDaysPerYear       = (int?)p["operatingDaysPerYear"]          ?? 250,
                    Source                     = (string)p["source"]                     ?? "",
                    EdgeBuildingType           = (string)p["edgeBuildingType"]            ?? ""
                };
                if (p["aliases"] is JArray al)
                    profile.Aliases = al.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                profile.OccupancySchedule = ReadSchedule(p["occupancySchedule"], LoadZone.DefaultOfficeOccupancy());
                profile.LightingSchedule  = ReadSchedule(p["lightingSchedule"],  LoadZone.DefaultOfficeLighting());
                profile.EquipmentSchedule = ReadSchedule(p["equipmentSchedule"], LoadZone.DefaultOfficeEquipment());

                if (!string.IsNullOrEmpty(profile.Id))
                    lib.ById[profile.Id] = profile;
            }
        }

        private static double[] ReadSchedule(JToken token, double[] fallback)
        {
            if (token is JArray arr && arr.Count == 24)
            {
                try { return arr.Select(v => (double)v).ToArray(); }
                catch { }
            }
            return fallback;
        }
    }
}
