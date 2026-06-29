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
using System.Security.Cryptography;
using System.Text;
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
        /// <summary>WS J1 — true when the user explicitly set the grid/diesel factor
        /// (Supply tab), so the country cascade won't overwrite it.</summary>
        public bool GridCarbonExplicit { get; set; } = false;
        public bool DieselCarbonExplicit { get; set; } = false;
        public double DieselFraction { get; set; } = 0.0;
        public double GreywaterReuseFraction { get; set; } = 0.0;

        // ── WS C1 — heating source + fan-energy inputs (default to the legacy
        //    electric-resistance / 15%-of-cooling behaviour ⇒ no change unless set) ──
        /// <summary>Seasonal heating efficiency/COP: 1.0 electric resistance, ~0.9 gas
        /// boiler, 2.5–4 heat-pump. Divides the heating thermal demand.</summary>
        public double HeatingSeasonalEfficiency { get; set; } = 1.0;
        /// <summary>True = electric heating (drawn from grid/PV). False = a fuel
        /// (gas/oil) — its energy is excluded from electricity and its carbon uses
        /// <see cref="HeatingFuelCarbonKgco2eKwh"/>.</summary>
        public bool HeatingIsElectric { get; set; } = true;
        /// <summary>Carbon factor for non-electric heating fuel, kgCO₂e/kWh (gas ≈ 0.21).</summary>
        public double HeatingFuelCarbonKgco2eKwh { get; set; } = 0.21;
        /// <summary>Fan/pump energy as a fraction of cooling electricity (CIBSE rule of
        /// thumb 0.15; lower for high-efficiency / low-SFP systems).</summary>
        public double FanEnergyFraction { get; set; } = 0.15;
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

    /// <summary>WS B5 — the EDGE app's CERTIFIED savings %, entered by the user on
    /// the dashboard. Null ⇒ not recorded (the STING indicative figure stands). When
    /// a value is present it OVERRIDES the indicative for that gate AND counts as a
    /// certified (computed) number, so a delegated gate (EDGE materials) becomes
    /// evaluable and the determined EDGE level reflects the official figure.</summary>
    public class EdgeOfficialFigures
    {
        public double? EnergySavingsPct    { get; set; }
        public double? WaterSavingsPct     { get; set; }
        /// <summary>EDGE materials gate is expressed as embodied-energy %.</summary>
        public double? MaterialsSavingsPct { get; set; }

        [JsonIgnore]
        public bool Any => EnergySavingsPct.HasValue || WaterSavingsPct.HasValue || MaterialsSavingsPct.HasValue;

        /// <summary>Map the entered figures to the gate-metric ids the SchemeContext
        /// keys on. Only present values are included.</summary>
        public Dictionary<string, double> ToMetricOverrides()
        {
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (EnergySavingsPct.HasValue)    d["energy_savings_pct"]          = EnergySavingsPct.Value;
            if (WaterSavingsPct.HasValue)     d["water_savings_pct"]           = WaterSavingsPct.Value;
            if (MaterialsSavingsPct.HasValue) d["embodied_energy_savings_pct"] = MaterialsSavingsPct.Value;
            return d;
        }
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
        /// <summary>WS B5 — recorded EDGE-app certified figures (override the indicative).</summary>
        public EdgeOfficialFigures EdgeOfficial { get; set; } = new EdgeOfficialFigures();
        public SustainUnits Units { get; set; } = SustainUnits.SI;

        /// <summary>WS I1 — true when the building use was explicitly chosen by the user
        /// or resolved from the model. False on a fresh CreateDefault, so the readiness
        /// gate treats the seeded "office" as UNSET and blocks rather than presenting
        /// office numbers as the user's project.</summary>
        public bool UseExplicit { get; set; } = false;

        /// <summary>WS M2 — true only when the user actually typed a project occupancy
        /// total. False on a fresh CreateDefault / auto-seed, so the engine uses the
        /// model-derived (load-profile-density) occupancy instead of an estimate that
        /// would otherwise masquerade as the user's authoritative figure.</summary>
        public bool OccupancyExplicit { get; set; } = false;

        /// <summary>WS H4 — whole-life carbon study period (years). Default 60 matches
        /// CarbonStageTracker / RICS so the RIBA-stage view and the EDGE dashboard agree.
        /// WS I10 — the LCC analysis uses this SAME period (no separate hardcoded 25 yr).</summary>
        public int StudyPeriodYears { get; set; } = 60;

        /// <summary>WS I10 — LCC discount rate (%/yr) for the NPV of operational savings.
        /// 3.5% is a common public-sector real discount rate (UK Green Book); set 0 for
        /// undiscounted.</summary>
        public double DiscountRatePct { get; set; } = 3.5;

        /// <summary>WS O1 — the modelled occupancy is flagged "unusually dense" when its
        /// actual density (floor area / people) falls below this fraction of the resolved
        /// profile's expected density. Seed 0.5 (overridable per project); a flag never
        /// changes the number. 0 disables the dense check.</summary>
        public double OccupancyDenseFactor { get; set; } = 0.5;

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
            if (s.EdgeOfficial == null) s.EdgeOfficial = new EdgeOfficialFigures();
            if (s.Schemes == null || s.Schemes.Count == 0) s.Schemes = new List<string> { "EDGE" };
            if (s.TargetLevels == null) s.TargetLevels = new Dictionary<string, string>();
            return s;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>WS E1 — a deterministic content hash over the RESULT-AFFECTING
        /// fields only (UpdatedUtc is excluded, so re-saving an unchanged setup keeps
        /// the same key). Used to cache the whole run per (document, setup-hash) so
        /// the dashboard → export → LCC → publish chain doesn't re-walk the model for
        /// an identical setup. Pure + deterministic across processes (SHA-256 of a
        /// canonical string, not the process-randomised string.GetHashCode).</summary>
        public string ContentHash()
        {
            var sb = new StringBuilder();
            sb.Append("schemes=").Append(string.Join(",", Schemes ?? new List<string>())).Append('|');
            if (TargetLevels != null)
                foreach (var kv in TargetLevels.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            sb.Append("|country=").Append(Country)
              .Append("|site=").Append(ClimateSiteId)
              .Append("|zone=").Append(ClimateZone)
              .Append("|units=").Append(Units)
              .Append("|study=").Append(StudyPeriodYears)
              .Append("|discount=").Append(DiscountRatePct.ToString("R"))
              .Append("|useExplicit=").Append(UseExplicit)
              .Append("|occExplicit=").Append(OccupancyExplicit)
              .Append("|occDenseF=").Append(OccupancyDenseFactor.ToString("R")).Append('|');
            if (Zones != null)
                foreach (var z in Zones)
                    sb.Append(z.ZoneId).Append('/').Append(z.BuildingUse).Append('/')
                      .Append(z.FloorAreaM2.ToString("R")).Append('/')
                      .Append(z.Occupancy).Append('/').Append(z.CoolingCop.ToString("R")).Append(';');
            sb.Append('|');
            if (Supply != null)
                sb.Append(Supply.Mode).Append('/').Append(Supply.PvKwp.ToString("R")).Append('/')
                  .Append(Supply.PvPerformanceRatio.ToString("R")).Append('/')
                  .Append((Supply.PvYieldKwhPerKwpYr ?? 0).ToString("R")).Append('/')
                  .Append(Supply.GridCarbonKgco2eKwh.ToString("R")).Append('/')
                  .Append(Supply.GridCarbonExplicit).Append('/')
                  .Append(Supply.DieselCarbonKgco2eKwh.ToString("R")).Append('/')
                  .Append(Supply.DieselCarbonExplicit).Append('/')
                  .Append(Supply.DieselFraction.ToString("R")).Append('/')
                  .Append(Supply.GreywaterReuseFraction.ToString("R")).Append('/')
                  .Append(Supply.HeatingSeasonalEfficiency.ToString("R")).Append('/')
                  .Append(Supply.HeatingIsElectric).Append('/')
                  .Append(Supply.HeatingFuelCarbonKgco2eKwh.ToString("R")).Append('/')
                  .Append(Supply.FanEnergyFraction.ToString("R")).Append('/')
                  .Append(Supply.EnergyTariffPerKwh.ToString("R")).Append('/')
                  .Append(Supply.WaterTariffPerM3.ToString("R"));
            sb.Append('|');
            if (FactorSources != null)
                sb.Append(string.Join(",", FactorSources.EmbodiedCarbon ?? new List<string>())).Append('/')
                  .Append(string.Join(",", FactorSources.EmbodiedEnergy ?? new List<string>())).Append('/')
                  .Append(FactorSources.Region);
            sb.Append('|');
            if (EdgeOfficial != null)
                sb.Append(EdgeOfficial.EnergySavingsPct).Append('/')
                  .Append(EdgeOfficial.WaterSavingsPct).Append('/')
                  .Append(EdgeOfficial.MaterialsSavingsPct);

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

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
