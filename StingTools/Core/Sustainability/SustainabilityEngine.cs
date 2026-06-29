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
using Autodesk.Revit.DB.Architecture;   // Room (WS D2)
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
            res.Baseline = baselineReg.Resolve(setup.Country, ResolveZone(setup, res.Climate, doc, res.Warnings), setup.DominantBuildingUse);
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

            // ── Materials (dual metric; full BOQ carbon path) ──
            var lines = GatherMaterialLines(doc, setup);
            res.MaterialLines = lines.Count;
            double area = setup.TotalFloorAreaM2 > 0 ? setup.TotalFloorAreaM2 : res.Energy.FloorAreaM2;
            res.Materials = MaterialsRollup.Rollup(lines, area,
                carbonBaselineKgM2: 0,   // LEED Phase-2 supplies this; EDGE delegates
                energyBaselineMjM2: baseline?.EmbodiedEnergyBaselineMjM2);
            res.Warnings.AddRange(res.Materials.Warnings);

            // ── Scheme evaluation (certifications-as-data) ──
            var ctx = new SchemeContext
            {
                Energy = res.Energy, Water = res.Water, Materials = res.Materials, Baseline = res.Baseline,
                // WS B5 — feed any recorded EDGE-app official % into the gate evaluation.
                OfficialOverrides = setup.EdgeOfficial?.ToMetricOverrides()
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
            // WS A1 — synthesise the monthly profile from the single design-day
            // registry, using the site's latitude (hemisphere + GHI seasonality).
            return monthlyReg.ResolveOrSynthesise(
                siteId ?? ds?.Id ?? "fallback",
                ds?.Label ?? siteId ?? "Fallback",
                ds?.Cooling996DbC ?? 30, ds?.Heating996DbC ?? 0,
                latDeg: ds?.Lat ?? 0,
                climateZone: setup.ClimateZone);
        }

        private static string ResolveZone(SustainProjectSetup setup, ClimateMonthlySite climate,
                                          Document doc, List<string> warnings)
        {
            if (!string.IsNullOrWhiteSpace(setup.ClimateZone)) return setup.ClimateZone;
            if (climate != null && !string.IsNullOrWhiteSpace(climate.ClimateZone)) return climate.ClimateZone;

            // WS F — auto-derive the ASHRAE 169 zone from the resolved site's
            // degree-days instead of silently defaulting to temperate; surface it.
            try
            {
                var ds = ClimateRegistry.ActiveSite(doc);
                if (ds != null && (ds.Cdd10 > 0 || ds.Hdd18 > 0))
                {
                    string zone = AshraeClimateZone.Classify(ds.Cdd10, ds.Hdd18);
                    warnings?.Add($"Climate zone not set — auto-derived '{zone}' from {ds.Label} " +
                                  $"(CDD10 {ds.Cdd10:0}, HDD18 {ds.Hdd18:0}); moisture sub-type assumed humid (A). " +
                                  "Set the zone in Setup to override.");
                    return zone;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ResolveZone derive: {ex.Message}"); }
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
            // WS A2 — project-tunable construction U-values/SHGC drive the shared
            // envelope detector so the annual energy estimate has real conduction +
            // per-façade solar (not just internal gains).
            ConstructionProfile construction = null;
            try { construction = ConstructionProfileRegistry.Active(doc); } catch (Exception ex) { StingLog.Warn($"Sustain construction profile: {ex.Message}"); }

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
                        var z = ZoneFromSpace(s, construction);
                        if (z == null) continue;
                        ApplyProfile(z, profile, dhw);
                        zones.Add(z);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain GatherZones spaces: {ex.Message}"); }

            // WS D2 — Room-based architectural model: when there are no MEP Spaces,
            // use Rooms as real zones (one LoadZone per Room, real per-room area)
            // instead of jumping straight to the single synthetic setup zone. Spaces
            // stay preferred above. Per-room USE-from-name is a documented follow-on;
            // the dominant-use profile is applied per room (same as the Spaces path).
            if (zones.Count == 0)
            {
                try
                {
                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r != null && r.Area > 1e-6)
                        .ToList();
                    if (rooms.Count > 0)
                    {
                        var profile = profiles?.Get(ProfileIdForUse(setup.DominantBuildingUse));
                        double dhw = DhwForUse(setup.DominantBuildingUse);
                        foreach (var r in rooms)
                        {
                            var z = ZoneFromRoom(r, construction);
                            if (z == null) continue;
                            ApplyProfile(z, profile, dhw);
                            zones.Add(z);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Sustain GatherZones rooms: {ex.Message}"); }
            }

            // Fallback: no Spaces or Rooms -> synthesise a single zone per setup zone using
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

        private static LoadZone ZoneFromSpace(Space s, ConstructionProfile construction)
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

                // WS A2 — derive real envelope (net wall + glazing + orientation +
                // roof) via the shared detector so conduction + per-façade solar are
                // counted. Falls back to a generic ratio when geometry doesn't yield;
                // the estimator still flags any zone with no envelope.
                if (construction != null)
                    EnvelopeDetector.AddPerimeterEnvelope(s, z, construction);
                return z;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ZoneFromSpace {s.Id}: {ex.Message}"); return null; }
        }

        /// <summary>Build a LoadZone from a Room (WS D2) — real per-room area + height;
        /// occupancy from "Number of People" when present, else the profile density.</summary>
        private static LoadZone ZoneFromRoom(Room r, ConstructionProfile construction)
        {
            try
            {
                double areaM2 = UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters);
                double heightM = UnitUtils.ConvertFromInternalUnits(r.UnboundedHeight, UnitTypeId.Meters);
                if (heightM <= 0.1) heightM = 3.0;

                var z = new LoadZone
                {
                    Id = r.Id.Value.ToString(),
                    Name = string.IsNullOrEmpty(r.Name) ? $"Room {r.Id}" : r.Name,
                    FloorAreaM2 = areaM2,
                    HeightM = heightM
                };
                int occ = TryReadIntByName(r, "Number of People");
                if (occ > 0) z.OccupantCount = occ;
                // WS A2 — same shared envelope detector for the Room-based path.
                if (construction != null)
                    EnvelopeDetector.AddPerimeterEnvelope(r, z, construction);
                return z;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ZoneFromRoom {r.Id}: {ex.Message}"); return null; }
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

        // ── Materials — share the BOQ carbon path (WS A3) ──────────────────
        //
        // Single source of carbon truth with the BOQ: the SAME CarbonFactorResolver
        // chain (per-m³ AND per-kg-via-density), the SAME WasteFactor knob
        // (COST_DEFAULT_WASTE_PCT), and the SAME fossil/biogenic WLCA split. The
        // per-element material-volume sum is the take-off quantity basis; routing it
        // through SustainMaterialCarbon.Compute makes the resulting kgCO₂e match
        // BOQCostManager.ComputeElementCarbon for the same material + density.

        // WBLCA scope (LEED v5 / RICS): structure + enclosure + reinforcement. Scoping
        // to physical categories (vs. every non-type element) also fixes the old O(n)
        // all-element walk (WS E1) and drops non-physical elements (WS D1).
        private static readonly BuiltInCategory[] WblcaCategories =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Columns, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Ramps,
            BuiltInCategory.OST_CurtainWallPanels, BuiltInCategory.OST_CurtainWallMullions,
            BuiltInCategory.OST_Rebar, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows
        };

        private static List<MaterialLine> GatherMaterialLines(Document doc, SustainProjectSetup setup)
        {
            var lines = new List<MaterialLine>();
            try
            {
                double wastePct = TagConfig.GetConfigDouble("COST_DEFAULT_WASTE_PCT", 5.0);
                var order = setup?.FactorSources ?? new FactorSourceOrder();
                // WS C3 — real embodied-energy (MJ/kg) seed, so a material with no
                // stamped per-m³ EPD energy gets an ICE-v3 cradle-to-gate figure
                // instead of the carbonKg×12 ratio fallback. The per-kg path is still
                // gated by FactorSources.EmbodiedEnergy inside SustainMaterialCarbon.
                var iceEnergy = SustainabilityRegistries.IceEnergy(doc);

                // Aggregate model material volumes by material name, scoped to the
                // WBLCA physical categories (matches the BOQ take-off scope).
                var byMaterial = new Dictionary<long, double>(); // materialId -> m3
                var filter = new ElementMulticategoryFilter(WblcaCategories);
                var coll = new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType();
                foreach (var el in coll)
                {
                    ICollection<ElementId> matIds = null;
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
                    var mat = doc.GetElement(new ElementId(kv.Key)) as Material;
                    string name = mat?.Name ?? "(unnamed)";
                    double m3 = kv.Value;

                    // Full carbon-factor chain (per-m³ AND per-kg) + WLCA split — the
                    // BOQ-shared resolver, NOT the dead CarbonTrackingEngine.
                    var cf = CarbonFactorResolver.Resolve(doc, name);
                    var input = new MaterialCarbonInputs
                    {
                        Material = name,
                        VolumeM3 = m3,
                        DensityKgM3 = DensityFor(name),
                        WastePercent = wastePct,
                        NetFactorPerM3 = cf.PerUnit == CarbonFactorUnit.KgCo2ePerM3 ? cf.Factor : 0,
                        NetFactorPerKg = cf.PerUnit == CarbonFactorUnit.KgCo2ePerKg ? cf.Factor : 0,
                        FactorIsEpdSpecific = cf.Source == "material-param"
                            || !string.IsNullOrWhiteSpace(ParameterHelpers.GetString(mat, ParamRegistry.SUS_EPD_REF)),
                        FossilFactorPerM3 = CarbonFactorResolver.GetCarbonFossilPerM3(doc, name),
                        BiogenicFactorPerM3 = CarbonFactorResolver.GetCarbonBiogenicPerM3(doc, name),
                        // Embodied energy: prefer a material-stamped MJ/m³ (EPD PERT+PENRT);
                        // per-kg ICE-MJ is not stamped on materials today → 0 ⇒ ratio fallback.
                        EnergyMjPerM3 = ReadMaterialDouble(mat, ParamRegistry.SUS_MAT_ENERGY_MJ_M2),
                        // WS C3 — per-kg cradle-to-gate MJ from the ICE seed/override
                        // (0 when the material isn't in the dataset ⇒ documented ratio
                        // fallback in SustainMaterialCarbon, never an invented number).
                        EnergyMjPerKg = iceEnergy?.GetMjPerKg(name) ?? 0
                    };

                    var outp = SustainMaterialCarbon.Compute(input, order);

                    lines.Add(new MaterialLine
                    {
                        Material = name, Category = mat?.MaterialClass ?? "",
                        VolumeM3 = m3, MassKg = outp.MassKg, WastePercent = wastePct,
                        CarbonKg = outp.NetCarbonKg,
                        FossilCarbonKg = outp.FossilCarbonKg,
                        BiogenicCarbonKg = outp.BiogenicCarbonKg,
                        EnergyMj = outp.EnergyMj,
                        FromEpd = outp.FromEpd,
                        CarbonSource = outp.CarbonSource,
                        EnergySource = outp.EnergySource
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain GatherMaterialLines: {ex.Message}"); }
            return lines;
        }

        /// <summary>Resolve a material density (kg/m³) for the per-kg carbon path,
        /// mirroring BOQCostManager.EstimateDensityKgPerM3 so the cost-mass and
        /// carbon-mass paths use one density. Corporate library wins; a small
        /// keyword fallback covers common construction materials.</summary>
        private static double DensityFor(string material)
        {
            try
            {
                double libVal = StingTools.UI.MaterialLookupCsv.GetDensity(material);
                if (libVal > 0) return libVal;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("Sustain.Density", $"density lookup: {ex.Message}"); }

            string lc = (material ?? "").ToLowerInvariant();
            if (lc.Contains("reinforced") && lc.Contains("concrete")) return 2450;
            if (lc.Contains("concrete")) return 2400;
            if (lc.Contains("steel")) return 7850;
            if (lc.Contains("hardwood")) return 700;
            if (lc.Contains("timber") || lc.Contains("wood") || lc.Contains("softwood")) return 480;
            if (lc.Contains("alumin")) return 2700;
            if (lc.Contains("glass")) return 2500;
            if (lc.Contains("brick")) return 1920;
            if (lc.Contains("insulation")) return 40;
            return 0;   // unknown ⇒ per-kg path skipped (documented)
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
