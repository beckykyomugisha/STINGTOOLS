// StingTools — Sustainability project setup (Phase 195, spec §2.5).
//
// The zero-hardcoding project-options surface. Nothing that defines a project
// is a literal in engine code — scheme(s), level, country, climate zone,
// building use(s) (single or per-zone mixed-use), occupancy, plant COP/SEER,
// supply mode, factor datasets and units all live HERE, in a JSON file the
// SETUP tab / Sustain_ProjectSetup command writes.
//
// Pure POCO + Newtonsoft (de)serialiser. No Revit dependency — unit-testable.
// Persisted to <project>/_BIM_COORD/sustainability/project_setup.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Sustainability
{
    public enum SustainUnits { SI, IP }

    public class SupplyConfig
    {
        public string Mode { get; set; } = "grid_tied";   // grid_tied | off_grid | hybrid
        public double PvKwp { get; set; } = 0;
        public double PvPerformanceRatio { get; set; } = 0.75;
        /// <summary>Optional fixed PV yield kWh/kWp.yr; when null the estimator
        /// derives it from the climate registry's annual GHI x PR.</summary>
        public double? PvYieldKwhPerKwpYr { get; set; } = null;
        public double GridCarbonKgco2eKwh { get; set; } = 0.45;
        public double DieselCarbonKgco2eKwh { get; set; } = 0.8;
        public double DieselFraction { get; set; } = 0.0;
        public double GreywaterReuseFraction { get; set; } = 0.0;
        /// <summary>Energy tariff used for the LCC roll-up (currency-neutral).</summary>
        public double EnergyTariffPerKwh { get; set; } = 0.15;
        public double WaterTariffPerM3 { get; set; } = 1.5;
    }

    /// <summary>One zone of a (possibly mixed-use) building. A single-use building
    /// is one zone with the whole floor area.</summary>
    public class ZoneSetup
    {
        public string ZoneId { get; set; } = "whole-building";
        public string BuildingUse { get; set; } = "office";
        public double FloorAreaM2 { get; set; } = 0;
        public int    Occupancy   { get; set; } = 0;
        /// <summary>Seasonal cooling COP/SEER for this zone's plant.</summary>
        public double CoolingCop  { get; set; } = 0;   // 0 => use baseline COP
    }

    public class FactorSourceOrder
    {
        public List<string> EmbodiedCarbon { get; set; } = new List<string> { "EPD_specific", "EC3_regional", "ICE_v3", "Ecoinvent" };
        public List<string> EmbodiedEnergy { get; set; } = new List<string> { "EPD_PERT_PENRT", "ICE_v3_MJ", "regional_db" };
        public string Region { get; set; } = "Global";
    }

    public class SustainProjectSetup
    {
        public const string RelPath = "_BIM_COORD/sustainability/project_setup.json";

        /// <summary>Certification schemes to evaluate (ids into GreenSchemeRegistry).
        /// Multi-select; the dashboard shows each.</summary>
        public List<string> Schemes { get; set; } = new List<string> { "EDGE" };

        /// <summary>Target level per scheme, keyed by scheme id (e.g. EDGE -> Advanced).</summary>
        public Dictionary<string, string> TargetLevels { get; set; } = new Dictionary<string, string>();

        public string Country { get; set; } = "*";
        public string ClimateSiteId { get; set; } = "";   // resolves monthly climate
        public string ClimateZone { get; set; } = "";      // ASHRAE 169 zone; auto-suggested

        /// <summary>Per-zone building use + area + occupancy. Always at least one
        /// entry. A single-use building has exactly one zone.</summary>
        public List<ZoneSetup> Zones { get; set; } = new List<ZoneSetup>();

        public SupplyConfig Supply { get; set; } = new SupplyConfig();
        public FactorSourceOrder FactorSources { get; set; } = new FactorSourceOrder();
        public SustainUnits Units { get; set; } = SustainUnits.SI;

        public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // ── Derived helpers (used by the building-level rollup; spec §2.5 rule 4) ──

        /// <summary>Total floor area across all zones.</summary>
        [JsonIgnore]
        public double TotalFloorAreaM2 => Zones?.Sum(z => z.FloorAreaM2) ?? 0;

        /// <summary>Total occupancy across all zones.</summary>
        [JsonIgnore]
        public int TotalOccupancy => Zones?.Sum(z => z.Occupancy) ?? 0;

        /// <summary>The dominant building use by floor area (for baseline resolution
        /// when only a single building-use key is needed).</summary>
        [JsonIgnore]
        public string DominantBuildingUse
        {
            get
            {
                if (Zones == null || Zones.Count == 0) return "office";
                return Zones
                    .GroupBy(z => string.IsNullOrWhiteSpace(z.BuildingUse) ? "office" : z.BuildingUse)
                    .OrderByDescending(g => g.Sum(z => z.FloorAreaM2))
                    .First().Key;
            }
        }

        public string LevelFor(string schemeId, string defaultLevel)
        {
            if (TargetLevels != null && schemeId != null &&
                TargetLevels.TryGetValue(schemeId, out var lvl) && !string.IsNullOrWhiteSpace(lvl))
                return lvl;
            return defaultLevel;
        }

        /// <summary>Build a sensible default single-zone setup (office, whole building).</summary>
        public static SustainProjectSetup CreateDefault(double floorAreaM2 = 0, int occupancy = 0)
        {
            var s = new SustainProjectSetup();
            s.TargetLevels["EDGE"] = "Advanced";
            s.Zones.Add(new ZoneSetup
            {
                ZoneId = "whole-building",
                BuildingUse = "office",
                FloorAreaM2 = floorAreaM2,
                Occupancy = occupancy
            });
            return s;
        }

        // ── Persistence ──────────────────────────────────────────────────

        public static SustainProjectSetup Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return CreateDefault();
            var s = JsonConvert.DeserializeObject<SustainProjectSetup>(json) ?? CreateDefault();
            if (s.Zones == null || s.Zones.Count == 0)
                s.Zones = new List<ZoneSetup> { new ZoneSetup() };
            if (s.Supply == null) s.Supply = new SupplyConfig();
            if (s.FactorSources == null) s.FactorSources = new FactorSourceOrder();
            if (s.Schemes == null || s.Schemes.Count == 0) s.Schemes = new List<string> { "EDGE" };
            if (s.TargetLevels == null) s.TargetLevels = new Dictionary<string, string>();
            return s;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>Load from a project directory (returns default + Found=false if absent).</summary>
        public static SustainProjectSetup Load(string projectDir, out bool found)
        {
            found = false;
            try
            {
                if (string.IsNullOrEmpty(projectDir)) return CreateDefault();
                string path = Path.Combine(projectDir, RelPath);
                if (!File.Exists(path)) return CreateDefault();
                found = true;
                return Parse(File.ReadAllText(path));
            }
            catch { return CreateDefault(); }
        }

        public void Save(string projectDir)
        {
            if (string.IsNullOrEmpty(projectDir)) return;
            string dir = Path.Combine(projectDir, "_BIM_COORD", "sustainability");
            Directory.CreateDirectory(dir);
            UpdatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            File.WriteAllText(Path.Combine(dir, "project_setup.json"), ToJson());
        }
    }
}
