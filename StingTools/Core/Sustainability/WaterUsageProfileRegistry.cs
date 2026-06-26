// StingTools — Water usage profile registry (Phase 195, spec §6).
//
// Loads STING_WATER_USAGE_PROFILES.json + optional project override (merged by
// buildingUse). The water engine is identical across building types — the
// building use only SELECTS the profile. Pure POCO, no Revit dependency.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class FixtureUse
    {
        /// <summary>Uses per person per day (WC, urinal).</summary>
        public double Uses { get; set; }
        /// <summary>Minutes per use (taps, showers).</summary>
        public double MinPerUse { get; set; }
        /// <summary>Fraction of people using this fixture per day (showers).</summary>
        public double FracPeople { get; set; } = 1.0;
        public bool   HasFracPeople { get; set; }
        /// <summary>Direct litres/minutes-equivalent per person per day (kitchen tap).</summary>
        public double MinPerPersonDay { get; set; }
        public bool   HasMinPerPersonDay { get; set; }
    }

    public class WaterUsageProfile
    {
        public string BuildingUse { get; set; } = "office";
        public int    OperatingDaysPerYear { get; set; } = 250;
        public Dictionary<string, FixtureUse> Fixtures { get; }
            = new Dictionary<string, FixtureUse>(StringComparer.OrdinalIgnoreCase);
    }

    public class WaterUsageProfileRegistry
    {
        private readonly List<WaterUsageProfile> _profiles = new List<WaterUsageProfile>();

        public IReadOnlyList<WaterUsageProfile> All => _profiles;

        public WaterUsageProfile Get(string buildingUse)
        {
            if (string.IsNullOrWhiteSpace(buildingUse)) buildingUse = "office";
            return _profiles.FirstOrDefault(p =>
                       string.Equals(p.BuildingUse, buildingUse, StringComparison.OrdinalIgnoreCase))
                ?? _profiles.FirstOrDefault(p =>
                       string.Equals(p.BuildingUse, "office", StringComparison.OrdinalIgnoreCase))
                ?? _profiles.FirstOrDefault();
        }

        public static WaterUsageProfileRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new WaterUsageProfileRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            return reg;
        }

        public static WaterUsageProfileRegistry LoadFromFiles(string corporatePath, string projectPath)
            => LoadFromJson(SafeRead(corporatePath), SafeRead(projectPath));

        private static string SafeRead(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private void Apply(string json)
        {
            JObject root;
            try { root = JObject.Parse(json); } catch { return; }
            var arr = root["profiles"] as JArray;
            if (arr == null) return;
            foreach (var p in arr.OfType<JObject>())
            {
                var prof = ParseProfile(p);
                int existing = _profiles.FindIndex(x =>
                    string.Equals(x.BuildingUse, prof.BuildingUse, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) _profiles[existing] = prof;
                else _profiles.Add(prof);
            }
        }

        private static WaterUsageProfile ParseProfile(JObject p)
        {
            var prof = new WaterUsageProfile
            {
                BuildingUse = (string)p["buildingUse"] ?? "office",
                OperatingDaysPerYear = (int?)p["operatingDaysPerYear"] ?? 250
            };
            if (p["fixtureUse_per_person_day"] is JObject fx)
                foreach (var f in fx.Properties())
                {
                    var v = f.Value as JObject;
                    if (v == null) continue;
                    var fu = new FixtureUse
                    {
                        Uses      = (double?)v["uses"] ?? 0,
                        MinPerUse = (double?)v["min_per_use"] ?? 0
                    };
                    if (v["frac_people"] != null) { fu.FracPeople = (double)v["frac_people"]; fu.HasFracPeople = true; }
                    if (v["min_per_person_day"] != null) { fu.MinPerPersonDay = (double)v["min_per_person_day"]; fu.HasMinPerPersonDay = true; }
                    prof.Fixtures[f.Name] = fu;
                }
            return prof;
        }
    }
}
