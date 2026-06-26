// StingTools — Sustainability orchestration engine (Phase 195, Revit-facing).
//
// Gathers the inputs (LoadZones from Spaces/Rooms, material lines via the BOQ
// takeoff + CarbonFactorResolver TIER-1), runs the four pure-POCO estimators,
// builds the SchemeContext and evaluates every selected scheme. Keeps the
// dashboard / export / LCC commands thin.
//
// This file IS Revit-facing (takes a Document) — NOT linked into the test
// project. The engines it calls are all Revit-free + unit-tested.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Climate;

namespace StingTools.Core.Sustainability
{
    /// <summary>The full result of one sustainability evaluation pass.</summary>
    public class SustainabilityRunResult
    {
        public SustainProjectSetup Setup { get; set; }
        public BaselineResolution  Baseline { get; set; }
        public EnergyEstimateResult Energy { get; set; }
        public WaterEstimateResult  Water { get; set; }
        public MaterialsRollupResult Materials { get; set; }
        public List<SchemeResult>    Schemes { get; } = new List<SchemeResult>();
        public ClimateMonthlySite    Climate { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public int ZonesGathered { get; set; }
        public int MaterialLines { get; set; }
    }

    public static class SustainabilityEngine
    {
        /// <summary>Run the full pass for a document + project setup.</summary>
        public static SustainabilityRunResult Run(Document doc, SustainProjectSetup setup)
        {
            var res = new SustainabilityRunResult { Setup = setup };
            if (doc == null || setup == null) { res.Warnings.Add("No document / setup."); return res; }

            // ── Climate (monthly; fall back to design-day if no monthly row) ──
            res.Climate = ResolveClimate(doc, setup);

            // ── Baseline (climate-zone proxy + provenance) ──
            var baselineReg = SustainabilityRegistries.Baselines(doc);
            res.Baseline = baselineReg.Resolve(setup.Country, ResolveZone(setup, res.Climate), setup.DominantBuildingUse);
            var baseline = res.Baseline.Baseline;
            if (!res.Baseline.Found)
                res.Warnings.Add(res.Baseline.Summary);

            // ── Energy (annual; reuse LoadZone inventory) ──
            var zones = GatherZones(doc, setup);
            res.ZonesGathered = zones.Count;
            double baselineCop = baseline?.BaselineCoolingCop ?? 3.0;
            // Per-zone COP override from setup (first zone with a non-zero cop).
            double zoneCop = setup.Zones?.FirstOrDefault(z => z.CoolingCop > 0)?.CoolingCop ?? 0;
            if (zoneCop > 0) baselineCop = zoneCop;
            res.Energy = AnnualEnergyEstimator.Estimate(zones, res.Climate, baseline, baselineCop, setup.Supply);
            res.Warnings.AddRange(res.Energy.Warnings);

            // ── Water (occupancy parameter; RWH + greywater) ──
            res.Water = EstimateWater(doc, setup, baseline);
            res.Warnings.AddRange(res.Water.Warnings);

            // ── Materials (dual metric; Tier-1 carbon path) ──
            var lines = GatherMaterialLines(doc);
            res.MaterialLines = lines.Count;
            double area = setup.TotalFloorAreaM2 > 0 ? setup.TotalFloorAreaM2 : res.Energy.FloorAreaM2;
            res.Materials = MaterialsRollup.Rollup(lines, area,
                carbonBaselineKgM2: 0,   // LEED Phase-2 supplies this; EDGE delegates
                energyBaselineMjM2: baseline?.EmbodiedEnergyBaselineMjM2);
            res.Warnings.AddRange(res.Materials.Warnings);

            // ── Scheme evaluation (certifications-as-data) ──
            var ctx = new SchemeContext
            {
                Energy = res.Energy, Water = res.Water, Materials = res.Materials, Baseline = res.Baseline
            };
            var schemeReg = SustainabilityRegistries.Schemes(doc);
            var providers = MetricProviderRegistry.CreateStandard();
            foreach (var schemeId in setup.Schemes ?? new List<string>())
            {
                var scheme = schemeReg.Get(schemeId);
                if (scheme == null) { res.Warnings.Add($"Scheme '{schemeId}' not in registry."); continue; }
                string level = setup.LevelFor(schemeId, scheme.DefaultLevel);
                res.Schemes.Add(SchemeEvaluator.Evaluate(scheme, level, providers, ctx));
            }

            return res;
        }

        // ── Climate resolution ────────────────────────────────────────────

        private static ClimateMonthlySite ResolveClimate(Document doc, SustainProjectSetup setup)
        {
            var monthlyReg = SustainabilityRegistries.Monthly(doc);
            string siteId = !string.IsNullOrWhiteSpace(setup.ClimateSiteId)
                ? setup.ClimateSiteId
                : ClimateRegistry.ActiveSite(doc)?.Id;
            var hit = monthlyReg.Get(siteId);
            if (hit != null) return hit;

            // Synthesise from the design-day site (logged warning inside).
            var ds = ClimateRegistry.ActiveSite(doc);
            return monthlyReg.ResolveOrSynthesise(
                siteId ?? ds?.Id ?? "fallback",
                ds?.Label ?? siteId ?? "Fallback",
                ds?.Cooling996DbC ?? 30, ds?.Heating996DbC ?? 0,
                climateZone: setup.ClimateZone);
        }

        private static string ResolveZone(SustainProjectSetup setup, ClimateMonthlySite climate)
        {
            if (!string.IsNullOrWhiteSpace(setup.ClimateZone)) return setup.ClimateZone;
            if (climate != null && !string.IsNullOrWhiteSpace(climate.ClimateZone)) return climate.ClimateZone;
            return "*";
        }

        // ── Zone gathering (mirrors HvacBlockLoadCommand) ──────────────────

        private static List<LoadZone> GatherZones(Document doc, SustainProjectSetup setup)
        {
            var zones = new List<LoadZone>();
            // The per-space-type load-profile library (12 ASHRAE/CIBSE profiles) is
            // the single source of LPD/EPD/occupant density/OA/setpoints/schedules —
            // building use now genuinely drives the loads (office vs healthcare vs
            // retail differ), instead of every zone using the bare office defaults.
            LoadProfileLibrary profiles = null;
            try { profiles = LoadProfileRegistry.Get(doc); } catch (Exception ex) { StingLog.Warn($"Sustain load profiles: {ex.Message}"); }

            try
            {
                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .Cast<Space>()
                    .Where(s => s.Area > 1e-6)
                    .ToList();
                if (spaces.Count > 0)
                {
                    var profile = profiles?.Get(ProfileIdForUse(setup.DominantBuildingUse));
                    double dhw = DhwForUse(setup.DominantBuildingUse);
                    foreach (var s in spaces)
                    {
                        var z = ZoneFromSpace(s);
                        if (z == null) continue;
                        ApplyProfile(z, profile, dhw);
                        zones.Add(z);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain GatherZones spaces: {ex.Message}"); }

            // Fallback: no Spaces -> synthesise a single zone per setup zone using
            // the setup's declared floor area + occupancy (so the estimator still
            // produces a coherent indicative number on a model without Spaces).
            if (zones.Count == 0 && setup.Zones != null)
            {
                foreach (var zs in setup.Zones.Where(z => z.FloorAreaM2 > 0))
                {
                    var z = new LoadZone
                    {
                        Id = zs.ZoneId, Name = zs.ZoneId, SpaceTypeId = zs.BuildingUse,
                        FloorAreaM2 = zs.FloorAreaM2, HeightM = 3.0, OccupantCount = zs.Occupancy
                    };
                    ApplyProfile(z, profiles?.Get(ProfileIdForUse(zs.BuildingUse)), DhwForUse(zs.BuildingUse));
                    zones.Add(z);
                }
            }
            return zones;
        }

        /// <summary>Map a sustainability building-use to a load-profile id. Uses with
        /// no dedicated profile fall through to the registry's fuzzy match / Office
        /// default (documented — add residential/hotel profiles to STING_LOAD_PROFILES
        /// .json to differentiate them further).</summary>
        private static string ProfileIdForUse(string use)
        {
            switch ((use ?? "office").Trim().ToLowerInvariant())
            {
                case "healthcare":  return "PatientRoom";
                case "office":      return "Office";
                case "retail":      return "Retail";
                case "hotel":       return "Office";       // no hotel profile yet
                case "residential": return "Office";       // no residential profile yet
                default:            return use;            // registry fuzzy-matches / defaults
            }
        }

        /// <summary>Apply a load profile (LPD/EPD/OA/setpoints/schedules) to a zone,
        /// derive occupancy from area density when the model carries none, and stamp
        /// the building-use DHW. Null profile leaves the LoadZone office defaults.</summary>
        private static void ApplyProfile(LoadZone z, LoadProfile profile, double dhwLpd)
        {
            if (profile != null)
            {
                profile.ApplyTo(z);
                if (z.OccupantCount <= 0 && z.FloorAreaM2 > 0)
                    z.OccupantCount = profile.OccupantCountFor(z.FloorAreaM2);
            }
            z.DhwLPerPersonDay = dhwLpd;
        }

        /// <summary>DHW litres/person·day by building use (CIBSE Guide G). Office is
        /// handwash-only (~5); residential/hotel/healthcare are far higher.</summary>
        private static double DhwForUse(string use)
        {
            switch ((use ?? "office").Trim().ToLowerInvariant())
            {
                case "residential": return 45;
                case "hotel":       return 100;
                case "healthcare":  return 60;
                case "retail":      return 3;
                default:            return 5;   // office / unknown
            }
        }

        private static LoadZone ZoneFromSpace(Space s)
        {
            try
            {
                double areaM2 = UnitUtils.ConvertFromInternalUnits(s.Area, UnitTypeId.SquareMeters);
                double heightM = UnitUtils.ConvertFromInternalUnits(s.UnboundedHeight, UnitTypeId.Meters);
                if (heightM <= 0.1) heightM = 3.0;

                var z = new LoadZone
                {
                    Id = s.Id.Value.ToString(),
                    Name = string.IsNullOrEmpty(s.Name) ? $"Space {s.Id}" : s.Name,
                    FloorAreaM2 = areaM2,
                    HeightM = heightM
                };

                int occ = TryReadIntByName(s, "Number of People");
                if (occ > 0) z.OccupantCount = occ;

                // Envelope from bounding perimeter walls/windows is expensive to
                // derive robustly here; the annual estimator handles a missing
                // envelope gracefully (flags it). When the model carries MEP
                // Spaces with HVC_* data, the HVAC BlockLoad path adds full
                // envelope — sustainability reuses whatever is present.
                return z;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ZoneFromSpace {s.Id}: {ex.Message}"); return null; }
        }

        private static int TryReadIntByName(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double)  return (int)Math.Round(p.AsDouble());
            }
            catch { }
            return 0;
        }

        // ── Water ──────────────────────────────────────────────────────────

        private static WaterEstimateResult EstimateWater(Document doc, SustainProjectSetup setup, GreenBaseline baseline)
        {
            var profileReg = SustainabilityRegistries.WaterProfiles(doc);
            var profile = profileReg.Get(setup.DominantBuildingUse);

            var baselineFlows = FixtureFlows.FromBaseline(baseline);
            // Design flows: read model low-flow fixtures if available; else assume a
            // 25% improvement over the baseline (indicative until fixtures carry flows).
            var modelFlows = ReadDesignFixtureFlows(doc);
            bool indicative = modelFlows == null;
            var designFlows = modelFlows ?? new FixtureFlows
            {
                WcLpf = baselineFlows.WcLpf * 0.75,
                UrinalLpf = baselineFlows.UrinalLpf * 0.75,
                BasinTapLpm = baselineFlows.BasinTapLpm * 0.75,
                ShowerLpm = baselineFlows.ShowerLpm * 0.75,
                KitchenTapLpm = baselineFlows.KitchenTapLpm * 0.75
            };

            int occupancy = setup.TotalOccupancy;
            // RWH yield is a project hook (RainwaterHarvestingCalc) — 0 here until a
            // project supplies roof area + rainfall; greywater reuse is a setup fraction.
            var w = AnnualWaterEstimator.Estimate(designFlows, baselineFlows, profile, occupancy,
                rwhYieldLPerYr: 0, greywaterReuseFraction: setup.Supply?.GreywaterReuseFraction ?? 0);
            w.IsIndicativeDefault = indicative;
            if (indicative)
                w.Warnings.Add("Water % is an indicative 25%-over-baseline default — no low-flow " +
                               "fixture data read from the model (stamp PLM_* flows for a real figure).");
            return w;
        }

        /// <summary>Read low-flow fixture flows from the model (best-effort).
        /// Returns null when no flow data is found (caller falls back).</summary>
        private static FixtureFlows ReadDesignFixtureFlows(Document doc)
        {
            // Fixtures rarely carry consistent flow params across libraries; this is
            // a hook for projects that stamp PLM_* flow data. Until then, return null
            // so the engine uses the 25%-over-baseline indicative default.
            return null;
        }

        // ── Materials (Tier-1 carbon path + MJ track) ──────────────────────

        private static List<MaterialLine> GatherMaterialLines(Document doc)
        {
            var lines = new List<MaterialLine>();
            try
            {
                // Aggregate model material volumes by material name (the BOQ takeoff
                // does the rich version; here we sum GetMaterialVolume per element so
                // the rollup has quantities even without a full BOQ build).
                var byMaterial = new Dictionary<long, double>(); // materialId -> m3
                var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (var el in coll)
                {
                    ICollection<ElementId> matIds = null;
                    // TODO-VERIFY-API: Element.GetMaterialIds(bool) + GetMaterialVolume(ElementId)
                    // are documented Revit 2025 APIs; some element types throw — caught per element.
                    try { matIds = el.GetMaterialIds(false); } catch { continue; }
                    if (matIds == null) continue;
                    foreach (var mid in matIds)
                    {
                        double vol;
                        try { vol = el.GetMaterialVolume(mid); } catch { continue; }
                        if (vol <= 0) continue;
                        double m3 = UnitUtils.ConvertFromInternalUnits(vol, UnitTypeId.CubicMeters);
                        if (!byMaterial.ContainsKey(mid.Value)) byMaterial[mid.Value] = 0;
                        byMaterial[mid.Value] += m3;
                    }
                }

                foreach (var kv in byMaterial)
                {
                    // TODO-VERIFY-API: new ElementId(long) is the Revit 2024+ ctor (Value is Int64).
                    var mat = doc.GetElement(new ElementId(kv.Key)) as Material;
                    string name = mat?.Name ?? "(unnamed)";
                    double m3 = kv.Value;

                    // Tier-1 carbon path — CarbonFactorResolver (NOT the dead engine).
                    var cf = CarbonFactorResolver.Resolve(doc, name);
                    double carbonKg = 0;
                    if (cf.PerUnit == CarbonFactorUnit.KgCo2ePerM3) carbonKg = cf.Factor * m3;
                    // (PerKg legacy factors need mass — skipped here; the BOQ path
                    // applies density. Volume x per-m3 is the honest Tier-1 figure.)

                    // Embodied energy MJ: prefer SUS_MAT_ENERGY_MJ_M2_NR on the material,
                    // else an indicative MJ/kgCO2e ratio (CED tracks GWP loosely — ~12
                    // MJ per kgCO2e for common construction materials, a documented
                    // placeholder until EPD PERT+PENRT data is stamped).
                    double mjPerM3 = ReadMaterialDouble(mat, "SUS_MAT_ENERGY_MJ_M2_NR");
                    double energyMj = mjPerM3 > 0 ? mjPerM3 * m3 : carbonKg * 12.0;

                    bool fromEpd = !string.IsNullOrWhiteSpace(ParameterHelpers.GetString(mat, ParamRegistry.SUS_EPD_REF));

                    lines.Add(new MaterialLine
                    {
                        Material = name, Category = mat?.MaterialClass ?? "",
                        VolumeM3 = m3, CarbonKg = carbonKg, EnergyMj = energyMj,
                        FromEpd = fromEpd, CarbonSource = cf.Source,
                        EnergySource = mjPerM3 > 0 ? "material-param" : "indicative-ratio"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain GatherMaterialLines: {ex.Message}"); }
            return lines;
        }

        private static double ReadMaterialDouble(Material mat, string paramName)
        {
            try
            {
                var p = mat?.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch { }
            return 0;
        }
    }
}
