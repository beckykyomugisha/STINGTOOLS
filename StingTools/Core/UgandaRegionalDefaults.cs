// UgandaRegionalDefaults.cs — Phase 189
//
// Loads STING_UGANDA_REGIONAL_LOADS.json and exposes a lookup-by-region
// helper. Drives ProjectLoadCombinationEngine fallback defaults and the
// SetUgandanDefaultsCommand which writes the regional values onto
// ProjectInformation.
//
// References:
//   Uganda NBC 2010, BS EN 1991-1-4 (wind), BS EN 1998-1 (EC8 seismic,
//   East African Rift), BS EN 1997-1 (EC7 soils), Uganda Met Dept IDF.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    public class UgandaRegionalProfile
    {
        public string Id              { get; set; } = "";
        public string Label           { get; set; } = "";
        public double WindBasicMps    { get; set; }
        public string WindTerrainCat  { get; set; } = "II";
        public double WindCdir        { get; set; } = 1.0;
        public double SeismicAgrG     { get; set; }
        public string GroundType      { get; set; } = "C";
        public double QFactor         { get; set; } = 1.5;
        public string ImportanceClass { get; set; } = "II";
        public double SoilBearingKpa  { get; set; }
        public bool   BlackCottonRisk { get; set; }
        public double RainIntensityMmh{ get; set; }
        public double LiveLoadKpa     { get; set; }
        public double DeadLoadKpa     { get; set; }
        public string SoilClass       { get; set; } = "";
        public string Notes           { get; set; } = "";
    }

    public static class UgandaRegionalDefaults
    {
        private static List<UgandaRegionalProfile> _profiles;
        private static Dictionary<string, double>  _occupancyLoads;
        private static readonly object _loadLock = new object();

        /// <summary>All known regional profiles (12 cities + Custom fallback).</summary>
        public static IReadOnlyList<UgandaRegionalProfile> All
        {
            get { EnsureLoaded(); return _profiles; }
        }

        /// <summary>Look up by region id (case-insensitive). Falls back to the
        /// "Custom" profile (conservative Kampala-equivalent) when not found.</summary>
        public static UgandaRegionalProfile ForRegion(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id))
                return _profiles.FirstOrDefault(p => p.Id == "Custom") ?? _profiles[0];
            var hit = _profiles.FirstOrDefault(p =>
                string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
            return hit ?? _profiles.FirstOrDefault(p => p.Id == "Custom") ?? _profiles[0];
        }

        /// <summary>EC1-1-1 / NBC occupancy live load (kPa). Returns 2.5 when unknown.</summary>
        public static double LiveLoadFor(string occupancy)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(occupancy)
                && _occupancyLoads.TryGetValue(occupancy, out double v)) return v;
            return 2.5;
        }

        public static void InvalidateCache()
        {
            lock (_loadLock) { _profiles = null; _occupancyLoads = null; }
        }

        private static void EnsureLoaded()
        {
            lock (_loadLock)
            {
                if (_profiles != null) return;
                _profiles = new List<UgandaRegionalProfile>();
                _occupancyLoads = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = StingTools.Core.StingToolsApp.FindDataFile("STING_UGANDA_REGIONAL_LOADS.json");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        StingLog.Warn("UgandaRegionalDefaults: STING_UGANDA_REGIONAL_LOADS.json not found — using hard-coded Kampala fallback.");
                        _profiles.Add(HardcodedKampalaFallback());
                        return;
                    }
                    var root = JObject.Parse(File.ReadAllText(path));
                    var regions = root["regions"] as JArray;
                    if (regions != null)
                    {
                        foreach (var r in regions)
                        {
                            try
                            {
                                _profiles.Add(new UgandaRegionalProfile
                                {
                                    Id              = (string)r["id"] ?? "",
                                    Label           = (string)r["label"] ?? "",
                                    WindBasicMps    = (double?)r["wind_basic_mps"] ?? 24,
                                    WindTerrainCat  = (string)r["wind_terrain_cat"] ?? "II",
                                    WindCdir        = (double?)r["wind_cdir"] ?? 1.0,
                                    SeismicAgrG     = (double?)r["seismic_agr_g"] ?? 0.08,
                                    GroundType      = (string)r["ground_type"] ?? "C",
                                    QFactor         = (double?)r["q_factor"] ?? 1.5,
                                    ImportanceClass = (string)r["importance_class"] ?? "II",
                                    SoilBearingKpa  = (double?)r["soil_bearing_kpa"] ?? 150,
                                    BlackCottonRisk = (bool?)r["black_cotton_risk"] ?? false,
                                    RainIntensityMmh= (double?)r["rain_intensity_mmh"] ?? 90,
                                    LiveLoadKpa     = (double?)r["live_load_kpa"] ?? 2.5,
                                    DeadLoadKpa     = (double?)r["dead_load_kpa"] ?? 4.0,
                                    SoilClass       = (string)r["soil_class"] ?? "",
                                    Notes           = (string)r["notes"] ?? ""
                                });
                            }
                            catch (Exception ex) { StingLog.Warn($"UgandaRegional row: {ex.Message}"); }
                        }
                    }
                    var occ = root["occupancy_live_loads_kpa"] as JObject;
                    if (occ != null)
                    {
                        foreach (var kv in occ)
                        {
                            if (kv.Key.StartsWith("_")) continue;
                            try { _occupancyLoads[kv.Key] = (double)kv.Value; }
                            catch (Exception ex) { StingLog.Warn($"UgandaRegional occ {kv.Key}: {ex.Message}"); }
                        }
                    }
                    StingLog.Info($"UgandaRegionalDefaults: {_profiles.Count} profiles loaded.");
                }
                catch (Exception ex)
                {
                    StingLog.Error("UgandaRegionalDefaults.EnsureLoaded", ex);
                    _profiles.Add(HardcodedKampalaFallback());
                }
            }
        }

        private static UgandaRegionalProfile HardcodedKampalaFallback() => new UgandaRegionalProfile
        {
            Id = "Custom", Label = "Custom (Kampala fallback)",
            WindBasicMps = 24, WindTerrainCat = "II", WindCdir = 1.0,
            SeismicAgrG = 0.08, GroundType = "C", QFactor = 1.5, ImportanceClass = "II",
            SoilBearingKpa = 150, BlackCottonRisk = false,
            RainIntensityMmh = 90,
            LiveLoadKpa = 2.5, DeadLoadKpa = 4.0,
            SoilClass = "Lateritic clay (Type C)",
            Notes = "Hard-coded fallback when data file is missing."
        };

        // ── EC8 + EC1-1-4 factor lookups (Phase 190) ────────────────────────
        // Static tables so the formulae below don't have to live in
        // ProjectLoadCombinationEngine and stay code-comment-authoritative.

        /// <summary>EC8-1 §3.2.2.2 Table 3.2 — ground-type spectral
        /// amplification factor S for Type 1 elastic response spectrum.</summary>
        public static double GroundFactorS(string groundType)
        {
            if (string.IsNullOrEmpty(groundType)) return 1.15;
            switch (groundType.Trim().ToUpperInvariant())
            {
                case "A":  return 1.00;
                case "B":  return 1.20;
                case "C":  return 1.15;
                case "D":  return 1.35;
                case "E":  return 1.40;
                default:   return 1.15;
            }
        }

        /// <summary>EC8-1 §4.2.5 — importance factor γi by class.
        /// I=ordinary 0.8, II=standard 1.0, III=important 1.2, IV=essential 1.4.</summary>
        public static double ImportanceFactor(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return 1.0;
            switch (cls.Trim().ToUpperInvariant())
            {
                case "I":   return 0.80;
                case "II":  return 1.00;
                case "III": return 1.20;
                case "IV":  return 1.40;
                default:    return 1.00;
            }
        }

        /// <summary>EC1-1-4 §4.3.2 + §4.5 — peak velocity pressure exposure
        /// factor ce(z) at z=10m by terrain category. Simplified scalar; full
        /// engine would parameterise z, but z=10m is the universal reference
        /// height for vb,0 so a single lookup is appropriate here.</summary>
        public static double WindCe10m(string terrainCat)
        {
            if (string.IsNullOrEmpty(terrainCat)) return 2.1;
            switch (terrainCat.Trim().ToUpperInvariant())
            {
                case "0":   return 3.00;  // sea or coastal exposed
                case "I":   return 2.70;  // lakes / no obstacles
                case "II":  return 2.10;  // open with low veg (default)
                case "III": return 1.60;  // suburban / industrial
                case "IV":  return 1.20;  // city with tall buildings
                default:    return 2.10;
            }
        }
    }
}
