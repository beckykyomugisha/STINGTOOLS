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
    public class LoadProfile
    {
        public string Id    { get; set; } = "";
        public string Label { get; set; } = "";

        public double OccupantDensityM2PerPerson { get; set; } = 10.0;
        public double OccupantSensibleW          { get; set; } = 75;
        public double OccupantLatentW            { get; set; } = 55;
        public double LightingWPerM2             { get; set; } = 7.6;
        public double EquipmentWPerM2            { get; set; } = 8.0;
        public double OaLpsPerPerson             { get; set; } = 10.0;
        public double OaLpsPerM2                 { get; set; } = 0.3;
        public double CoolingSetpointC           { get; set; } = 24;
        public double HeatingSetpointC           { get; set; } = 21;
        public double InfiltrationAch            { get; set; } = 0.3;

        public double[] OccupancySchedule { get; set; } = LoadZone.DefaultOfficeOccupancy();
        public double[] LightingSchedule  { get; set; } = LoadZone.DefaultOfficeLighting();
        public double[] EquipmentSchedule { get; set; } = LoadZone.DefaultOfficeEquipment();

        /// <summary>Apply this profile's values onto a LoadZone.</summary>
        public void ApplyTo(LoadZone z)
        {
            z.OccupantSensibleW = OccupantSensibleW;
            z.OccupantLatentW   = OccupantLatentW;
            z.LightingWPerM2    = LightingWPerM2;
            z.EquipmentWPerM2   = EquipmentWPerM2;
            z.OaLpsPerPerson    = OaLpsPerPerson;
            z.OaLpsPerM2        = OaLpsPerM2;
            z.CoolingSetpointC  = CoolingSetpointC;
            z.HeatingSetpointC  = HeatingSetpointC;
            z.InfiltrationAch   = InfiltrationAch;
            if (OccupancySchedule?.Length == 24) z.OccupancySchedule = OccupancySchedule;
            if (LightingSchedule?.Length  == 24) z.LightingSchedule  = LightingSchedule;
            if (EquipmentSchedule?.Length == 24) z.EquipmentSchedule = EquipmentSchedule;
        }

        /// <summary>Derived occupant count from area + density (people per
        /// space). Clamped at 1 so a small unloaded room still gets a calc.</summary>
        public int OccupantCountFor(double areaM2)
        {
            if (OccupantDensityM2PerPerson <= 0) return 1;
            return Math.Max(1, (int)Math.Round(areaM2 / OccupantDensityM2PerPerson));
        }
    }

    public class LoadProfileLibrary
    {
        public Dictionary<string, LoadProfile> ById { get; }
            = new Dictionary<string, LoadProfile>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolve a profile by id with case-insensitive fuzzy fallback.</summary>
        public LoadProfile Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return ById.TryGetValue("Office", out var def) ? def : new LoadProfile { Id = "Office" };
            if (ById.TryGetValue(id, out var hit)) return hit;
            // Loose match — handles things like "Meeting Room" vs "MeetingRoom"
            string norm = id.Replace(" ", "").Replace("-", "").Replace("_", "");
            foreach (var kv in ById)
            {
                string k = kv.Key.Replace(" ", "").Replace("-", "").Replace("_", "");
                if (string.Equals(k, norm, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return ById.TryGetValue("Office", out var fallback) ? fallback : new LoadProfile { Id = "Office" };
        }
    }

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
                    InfiltrationAch            = (double?)p["infiltrationAch"]            ?? 0.3
                };
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
