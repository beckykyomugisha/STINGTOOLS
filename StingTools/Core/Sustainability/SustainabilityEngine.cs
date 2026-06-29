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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Architecture;   // Room (WS D2)
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Climate;
using StingTools.Core.Plumbing;   // RainwaterHarvestingCalc (WS A4)

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
        /// <summary>WS H4 — whole-life carbon roll-up (embodied A1–A3 + operational
        /// over the study period). Carbon only.</summary>
        public WholeLifeCarbonResult WholeLife { get; set; }
        /// <summary>WS I1 — location/use readiness gate. When not Ready the dashboard
        /// banners and refuses to claim an EDGE level.</summary>
        public SustainReadinessResult Readiness { get; set; }
        /// <summary>WS I1 — how the building use was resolved (setup / model / unset).</summary>
        public BuildingUseResolution ResolvedUse { get; set; }
        /// <summary>WS I3 — the grid carbon factor used + whether it's a labelled default.</summary>
        public GridCarbonResolution GridCarbon { get; set; }
        public List<SchemeResult>    Schemes { get; } = new List<SchemeResult>();
        public ClimateMonthlySite    Climate { get; set; }
        /// <summary>WS K5 — the resolved dominant load profile's full design context
        /// (id + EDGE building type + provenance + DHW + operating days), so the run
        /// maps cleanly onto the EDGE app's building category.</summary>
        public ResolvedProfileInfo   Profile { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public int ZonesGathered { get; set; }
        public int MaterialLines { get; set; }
    }

    /// <summary>WS K5 — the resolved load profile's design context for the dashboard
    /// + EDGE mapping. Populated from the dominant building use.</summary>
    public class ResolvedProfileInfo
    {
        public string ProfileId         { get; set; } = "";
        public string EdgeBuildingType  { get; set; } = "";
        public string Source            { get; set; } = "";
        public double DhwLPerPersonDay  { get; set; }
        public int    OperatingDaysPerYear { get; set; }
        public bool   IsFallback        { get; set; }
        public string RequestedUse      { get; set; } = "";
    }

    public static class SustainabilityEngine
    {
        // ── WS E1 — caching ─────────────────────────────────────────────────
        // The materials take-off is the expensive walk; the supply/scheme layers
        // are cheap. We cache (1) the whole run per (document, setup-hash) so the
        // dashboard → export → LCC → publish chain reuses one pass for an identical
        // setup, and (2) the material lines per (document, factor-sources) so a
        // change that only affects energy/water (occupancy, supply, climate, target
        // level) does NOT re-walk the model. A short stale window bounds how long a
        // model edit can be masked (mirrors ComplianceScan); the dashboard's explicit
        // run forces a fresh read, and Invalidate() clears on document close.
        private const int CacheStaleSeconds = 60;
        private static readonly ConcurrentDictionary<string, (SustainabilityRunResult res, DateTime ts)> _runCache
            = new ConcurrentDictionary<string, (SustainabilityRunResult, DateTime)>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, (List<MaterialLine> lines, DateTime ts)> _materialCache
            = new ConcurrentDictionary<string, (List<MaterialLine>, DateTime)>(StringComparer.Ordinal);

        /// <summary>Drop all cached runs + material take-offs for a document (called
        /// on document close and by an explicit refresh). Also flushes the shared
        /// envelope top-level cache.</summary>
        public static void Invalidate(Document doc)
        {
            string prefix = (doc?.PathName ?? "<no-doc>") + "|";
            foreach (var k in _runCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _runCache.TryRemove(k, out _);
            foreach (var k in _materialCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _materialCache.TryRemove(k, out _);
            try { EnvelopeDetector.InvalidateTopLevelCache(doc); } catch { }
        }

        /// <summary>Alias kept for the document-close hook / panel refresh.</summary>
        public static void InvalidateCaches(Document doc) => Invalidate(doc);

        /// <summary>Run the full pass for a document + project setup. Reuses a cached
        /// result for an identical (document, setup) within the stale window unless
        /// <paramref name="forceRefresh"/> is set (the dashboard forces a fresh read;
        /// secondary consumers reuse it). WS E1.</summary>
        public static SustainabilityRunResult Run(Document doc, SustainProjectSetup setup, bool forceRefresh = false)
        {
            if (doc == null || setup == null)
            {
                var bad = new SustainabilityRunResult { Setup = setup };
                bad.Warnings.Add("No document / setup.");
                return bad;
            }

            // WS J1 — cascade the Country into a clone (climate site/zone + grid/diesel)
            // BEFORE the cache key, so a Country change re-keys the run (WS J3) and the
            // result reflects the country. User-typed values are preserved by the cascade.
            setup = CascadeCountry(doc, setup);

            string key = (doc.PathName ?? "<no-doc>") + "|" + setup.ContentHash();
            if (forceRefresh) Invalidate(doc);
            else if (_runCache.TryGetValue(key, out var hit)
                     && (DateTime.UtcNow - hit.ts).TotalSeconds < CacheStaleSeconds)
                return hit.res;

            var computed = Compute(doc, setup, forceRefresh);
            _runCache[key] = (computed, DateTime.UtcNow);
            return computed;
        }

        private static SustainabilityRunResult Compute(Document doc, SustainProjectSetup setup, bool forceRefresh)
        {
            var res = new SustainabilityRunResult { Setup = setup };

            // ── Climate (monthly; fall back to design-day if no monthly row) ──
            res.Climate = ResolveClimate(doc, setup);

            // ── WS I1 — resolve the building use from the model; NEVER default to
            //    office. When the user picked one it wins; else derive from project
            //    info / room program; else unset (the readiness gate then blocks). ──
            res.ResolvedUse = ResolveUse(doc, setup);
            if (res.ResolvedUse.Found && res.ResolvedUse.Source == "model"
                && setup.Zones != null && setup.Zones.Count > 0 && !setup.UseExplicit)
            {
                var dom = setup.Zones.OrderByDescending(z => z.FloorAreaM2).First();
                dom.BuildingUse = res.ResolvedUse.Use;   // drive profiles + water off the derived use
            }

            // ── Baseline (climate-zone proxy + provenance) ──
            // When the use is unset, resolve against "*" (global) rather than the
            // seeded "office", so a blocked run never proxies as an office baseline.
            var baselineReg = SustainabilityRegistries.Baselines(doc);
            string baseUse = res.ResolvedUse.Found ? setup.DominantBuildingUse : "*";
            res.Baseline = baselineReg.Resolve(setup.Country, ResolveZone(setup, res.Climate, doc, res.Warnings), baseUse);
            var baseline = res.Baseline.Baseline;
            if (!res.Baseline.Found)
                res.Warnings.Add(res.Baseline.Summary);

            // The baseline is keyed on climate zone + building use. When NEITHER a
            // climate site NOR a zone is set, the zone is a best-guess derived from
            // the project's stamped / fallback climate site — which can default to a
            // temperate zone for a project that is anything but. Surface it loudly so
            // an indicative figure for the wrong climate isn't mistaken for the real
            // one (a hot/tropical site has a very different base case from temperate 4A).
            if (string.IsNullOrWhiteSpace(setup.ClimateZone) && string.IsNullOrWhiteSpace(setup.ClimateSiteId))
                res.Warnings.Insert(0,
                    $"No climate site or zone set — the baseline location is a best-guess " +
                    $"({res.Baseline?.Summary}). Set Climate site id and/or Climate zone in Setup for a " +
                    "defensible baseline; hot / tropical sites differ materially from the temperate default.");

            // ── WS I3 / J1 — grid carbon factor from the project country (not a
            //    hardcoded 0.45). Priority: explicit user supply override → the country
            //    seed (cascade) → the legacy per-ISO2 grid registry → labelled default.
            //    Feeds the supply layer. ──
            var gridReg = SustainabilityRegistries.GridCarbon(doc);
            var countryRow = SustainabilityRegistries.Countries(doc).Resolve(setup.Country);
            if (setup.Supply != null && setup.Supply.GridCarbonExplicit)
            {
                res.GridCarbon = new GridCarbonResolution
                {
                    Country = setup.Country, Factor = setup.Supply.GridCarbonKgco2eKwh,
                    Source = "user override", IsDefault = false
                };
            }
            else if (countryRow != null && !countryRow.IsDefault && countryRow.GridKgCo2ePerKwh > 0)
            {
                res.GridCarbon = new GridCarbonResolution
                {
                    Country = countryRow.Iso3, Factor = countryRow.GridKgCo2ePerKwh,
                    Source = $"country: {countryRow.Label} (seed — {countryRow.Source})", IsDefault = false
                };
                if (setup.Supply != null) setup.Supply.GridCarbonKgco2eKwh = res.GridCarbon.Factor;
            }
            else
            {
                res.GridCarbon = gridReg.Resolve(setup.Country);
                if (setup.Supply != null) setup.Supply.GridCarbonKgco2eKwh = res.GridCarbon.Factor;
            }
            if (res.GridCarbon.IsDefault)
                res.Warnings.Add($"Grid carbon factor is a default ({res.GridCarbon.Factor:0.00} kgCO₂e/kWh) — " +
                                 "set the project country (or override grid_carbon_factors.json) for a real grid factor.");

            // ── WS K5 — resolve the dominant load profile's full design context for
            //    the dashboard + EDGE-app building-category mapping. ──
            try
            {
                var lib = LoadProfileRegistry.Get(doc);
                var pr = lib?.ResolveForUse(setup.DominantBuildingUse);
                if (pr?.Profile != null)
                    res.Profile = new ResolvedProfileInfo
                    {
                        ProfileId = pr.Profile.Id, EdgeBuildingType = pr.Profile.EdgeBuildingType,
                        Source = pr.Profile.Source, DhwLPerPersonDay = pr.Profile.DhwLPerPersonDay,
                        OperatingDaysPerYear = pr.Profile.OperatingDaysPerYear,
                        IsFallback = pr.IsFallback, RequestedUse = pr.RequestedUse
                    };
            }
            catch (Exception ex) { StingLog.Warn($"Sustain resolve profile info: {ex.Message}"); }

            // ── Energy (annual; reuse LoadZone inventory) ──
            var zones = GatherZones(doc, setup, res.Warnings);
            res.ZonesGathered = zones.Count;
            double baselineCop = baseline?.BaselineCoolingCop ?? 3.0;
            // Per-zone COP override from setup (first zone with a non-zero cop).
            double zoneCop = setup.Zones?.FirstOrDefault(z => z.CoolingCop > 0)?.CoolingCop ?? 0;
            if (zoneCop > 0) baselineCop = zoneCop;
            res.Energy = AnnualEnergyEstimator.Estimate(zones, res.Climate, baseline, baselineCop, setup.Supply);
            res.Warnings.AddRange(res.Energy.Warnings);

            // ── Water (occupancy parameter; RWH + greywater) ──
            // WS H2 — one project occupancy fed to BOTH estimators. Energy already
            // used Σ per-zone occupants (model / load-profile density); water now uses
            // the same population unless the user typed an explicit setup total.
            int zoneOccupants = zones.Sum(z => z.OccupantCount);
            // WS M2 — only a user-typed total wins; otherwise the model-derived
            // (profile-density) population, so the source label is honest.
            var occ = SustainOccupancy.Resolve(setup.TotalOccupancy, zoneOccupants, setup.OccupancyExplicit);
            res.Water = EstimateWater(doc, setup, baseline, res.Climate, occ.Occupancy);
            res.Warnings.AddRange(res.Water.Warnings);
            if (occ.Occupancy > 0)
                res.Warnings.Add($"Occupancy {occ.Occupancy} (source: {occ.Source}) — used for both the energy and " +
                                 "water estimates so the two gates share one population.");

            // ── Materials (dual metric; full BOQ carbon path) ──
            var lines = GatherMaterialLines(doc, setup, forceRefresh);
            res.MaterialLines = lines.Count;
            double area = setup.TotalFloorAreaM2 > 0 ? setup.TotalFloorAreaM2 : res.Energy.FloorAreaM2;
            res.Materials = MaterialsRollup.Rollup(lines, area,
                carbonBaselineKgM2: 0,   // LEED supplies this later; EDGE delegates
                energyBaselineMjM2: baseline?.EmbodiedEnergyBaselineMjM2);
            res.Warnings.AddRange(res.Materials.Warnings);

            // ── Whole-life carbon (WS H4) — embodied A1–A3 (net, incl. biogenic) +
            //    operational over the study period. Carbon only; aligns with
            //    CarbonStageTracker's 60-year basis (study period is data-driven). ──
            res.WholeLife = WholeLifeCarbon.Compute(
                embodiedA1A3Kg: res.Materials?.TotalCarbonKg ?? 0,
                operationalKgPerYr: res.Energy?.OperationalCarbonKgYr ?? 0,
                studyPeriodYears: setup.StudyPeriodYears,
                floorAreaM2: area);

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

            // ── WS I1 — readiness gate. Location (site/zone) + a resolved use are the
            //    hard axes; occupancy + fixtures are softer. When not ready, banner +
            //    BLOCK: no EDGE level is claimed and gates render "not your project". ──
            bool locationSet = !string.IsNullOrWhiteSpace(setup.ClimateSiteId)
                            || !string.IsNullOrWhiteSpace(setup.ClimateZone)
                            || HasActiveClimateSite(doc);
            res.Readiness = SustainReadiness.Evaluate(
                locationSet, res.ResolvedUse.Found, occ.Occupancy > 0, HasPlumbingFixtures(doc));
            if (!string.IsNullOrEmpty(res.Readiness.Banner))
                res.Warnings.Insert(0, res.Readiness.Banner);
            if (!res.Readiness.Ready)
                foreach (var sc in res.Schemes)
                {
                    sc.Passed = false;
                    sc.AchievedLevel = "None — location/use not set";
                    foreach (var g in sc.Gates)
                    {
                        g.Passed = false; g.Computed = false; g.NotEvaluated = true;
                        if (string.IsNullOrEmpty(g.Note)) g.Note = "location/use not set — generic proxy, not your project";
                    }
                }

            return res;
        }

        // ── WS J1 — country cascade ────────────────────────────────────────

        /// <summary>Clone the setup and fill its climate site / zone / grid / diesel
        /// from the country seed (without clobbering user-typed values), so picking a
        /// Country drives the run. The clone keeps the caller's setup object untouched.</summary>
        private static SustainProjectSetup CascadeCountry(Document doc, SustainProjectSetup setup)
        {
            try
            {
                var row = SustainabilityRegistries.Countries(doc).Resolve(setup.Country);
                if (row == null || row.IsDefault) return setup;
                var clone = SustainProjectSetup.Parse(setup.ToJson());   // deep copy via JSON
                CountryCascade.Apply(clone, row);
                return clone;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain CascadeCountry: {ex.Message}"); return setup; }
        }

        // ── WS I1 — building-use + readiness resolution (Revit-facing) ─────

        /// <summary>Resolve the building use: an explicit user choice wins; else derive
        /// from ProjectInformation hints + the room program via BuildingUseResolver;
        /// else unset (caller blocks — never default to office).</summary>
        private static BuildingUseResolution ResolveUse(Document doc, SustainProjectSetup setup)
        {
            try
            {
                // 1) Explicit user choice — accept a canonical catalogue use directly.
                if (setup.UseExplicit && !string.IsNullOrWhiteSpace(setup.DominantBuildingUse))
                {
                    string u = setup.DominantBuildingUse.Trim().ToLowerInvariant();
                    if (BuildingUseCatalog.CommonUses.Contains(u))
                        return new BuildingUseResolution { Use = u, Source = "setup", Found = true };
                    var mapped = BuildingUseResolver.MapText(u);
                    if (mapped != null) return new BuildingUseResolution { Use = mapped, Source = "setup", Found = true };
                }

                // 2) Model signals — ProjectInformation hints, then the room program.
                var signals = new List<(string, string)>();
                try
                {
                    var pi = doc.ProjectInformation;
                    foreach (var pn in new[] { "PRJ_BUILDING_USE_TXT", "Building Type", "Building Name", "Project Name", "Project Status" })
                    {
                        string v = pi?.LookupParameter(pn)?.AsString();
                        if (!string.IsNullOrWhiteSpace(v)) signals.Add(("model", v));
                    }
                }
                catch { }
                try
                {
                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                        .Cast<Room>().Where(r => r != null && r.Area > 1e-6).Take(400).ToList();
                    foreach (var r in rooms)
                    {
                        if (!string.IsNullOrWhiteSpace(r.Name)) signals.Add(("model", r.Name));
                        string dept = r.LookupParameter("Department")?.AsString();
                        if (!string.IsNullOrWhiteSpace(dept)) signals.Add(("model", dept));
                    }
                }
                catch { }

                return BuildingUseResolver.Resolve(signals, BuildingUseCatalog.CommonUses);
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ResolveUse: {ex.Message}"); return new BuildingUseResolution(); }
        }

        private static bool HasActiveClimateSite(Document doc)
        {
            try { return !string.IsNullOrWhiteSpace(ClimateRegistry.ActiveSite(doc)?.Id); }
            catch { return false; }
        }

        private static bool HasPlumbingFixtures(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType().Any();
            }
            catch { return false; }
        }

        // ── Climate resolution ────────────────────────────────────────────

        private static ClimateMonthlySite ResolveClimate(Document doc, SustainProjectSetup setup)
        {
            var monthlyReg = SustainabilityRegistries.Monthly(doc);
            string siteId = !string.IsNullOrWhiteSpace(setup.ClimateSiteId)
                ? setup.ClimateSiteId
                : ClimateRegistry.ActiveSite(doc)?.Id;
            var ds = ClimateRegistry.ActiveSite(doc);
            var hit = monthlyReg.Get(siteId);
            if (hit != null)
            {
                // WS I7 — a real monthly row with no rainfall still gets the design-day
                // site's annual rainfall so RWH isn't silently 0 on a rainy site.
                if (hit.AnnualRainfallMm <= 0 && ds != null && ds.RainfallMmYr > 0)
                    for (int m = 0; m < 12; m++) hit.RainfallMm[m] = ds.RainfallMmYr / 12.0;
                return hit;
            }

            // Synthesise from the design-day site (logged warning inside).
            // WS A1 — synthesise the monthly profile from the single design-day
            // registry, using the site's latitude (hemisphere + GHI seasonality).
            // WS I7 — carry the site's real annual rainfall into the synthesis so
            // RainwaterHarvestingCalc yields a real number; falls back to flagged 1,000 mm.
            // WS J1 — when there's no design-day site (only a Country picked), use the
            // country capital's latitude so the synthesised climate matches the country.
            double rainFallback = (ds != null && ds.RainfallMmYr > 0) ? ds.RainfallMmYr : 1000;
            double lat = (ds != null && System.Math.Abs(ds.Lat) > 1e-6) ? ds.Lat : 0;
            string label = ds?.Label ?? siteId ?? "Fallback";
            if (System.Math.Abs(lat) < 1e-6)
            {
                try
                {
                    var row = SustainabilityRegistries.Countries(doc).Resolve(setup.Country);
                    if (row != null && !row.IsDefault && System.Math.Abs(row.Lat) > 1e-6)
                    {
                        lat = row.Lat;
                        if (string.IsNullOrWhiteSpace(ds?.Label)) label = row.DefaultCity;
                    }
                }
                catch { }
            }
            return monthlyReg.ResolveOrSynthesise(
                siteId ?? ds?.Id ?? label,
                label,
                ds?.Cooling996DbC ?? 30, ds?.Heating996DbC ?? 0,
                latDeg: lat,
                annualRainfallMmFallback: rainFallback,
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
                // WS I9 — global fallback: ANY site with a latitude resolves a real
                // climate zone (latitude-band heuristic) instead of a temperate default.
                if (ds != null && System.Math.Abs(ds.Lat) > 1e-6)
                {
                    string zone = AshraeClimateZone.ClassifyByLatitude(ds.Lat);
                    warnings?.Add($"Climate zone not set — auto-derived '{zone}' from {ds.Label} latitude " +
                                  $"({ds.Lat:0.0}°); coarse latitude estimate, moisture assumed humid (A). " +
                                  "Set the zone (or add degree-days) in Setup to override.");
                    return zone;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ResolveZone derive: {ex.Message}"); }
            return "*";
        }

        // ── Zone gathering (mirrors HvacBlockLoadCommand) ──────────────────

        private static List<LoadZone> GatherZones(Document doc, SustainProjectSetup setup, List<string> warnings)
        {
            var zones = new List<LoadZone>();
            // Maps each gathered zone to its Level (for top-level roof detection in
            // the synthesised envelope). Absent ⇒ treated as top level (worst case).
            var levelOf = new Dictionary<LoadZone, ElementId>();
            // The per-space-type load-profile library (12 ASHRAE/CIBSE profiles) is
            // the single source of LPD/EPD/occupant density/OA/setpoints/schedules —
            // building use now genuinely drives the loads (office vs healthcare vs
            // retail differ), instead of every zone using the bare office defaults.
            LoadProfileLibrary profiles = null;
            try { profiles = LoadProfileRegistry.Get(doc); } catch (Exception ex) { StingLog.Warn($"Sustain load profiles: {ex.Message}"); }
            var noted = new HashSet<string>();   // WS K2/K4 — de-dup fallback notes per run
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
                    var pr = ResolveProfile(profiles, setup.DominantBuildingUse, warnings, noted);
                    foreach (var s in spaces)
                    {
                        var z = ZoneFromSpace(s, construction);
                        if (z == null) continue;
                        ApplyProfile(z, pr);
                        zones.Add(z);
                        try { levelOf[z] = s.LevelId; } catch { }
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
                        var pr = ResolveProfile(profiles, setup.DominantBuildingUse, warnings, noted);
                        foreach (var r in rooms)
                        {
                            var z = ZoneFromRoom(r, construction);
                            if (z == null) continue;
                            ApplyProfile(z, pr);
                            zones.Add(z);
                            try { levelOf[z] = r.LevelId; } catch { }
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
                        FloorAreaM2 = zs.FloorAreaM2, HeightM = 3.0,
                        // WS M2 — only a user-typed occupancy seeds the zone; otherwise 0
                        // so ApplyProfile derives it from the load-profile density (the
                        // residential-vs-office fix). A stale office-density estimate in
                        // the setup must NOT override the per-use model occupancy.
                        OccupantCount = setup.OccupancyExplicit ? zs.Occupancy : 0
                    };
                    ApplyProfile(z, ResolveProfile(profiles, zs.BuildingUse, warnings, noted));
                    zones.Add(z);
                    // No level for a synthetic setup zone ⇒ left out of levelOf ⇒
                    // treated as top level (includes a roof segment when synthesised).
                }
            }

            // WS A2 — any zone that still has NO envelope (a floor-area-only setup
            // zone, or a Space/Room whose geometry didn't yield) gets a representative
            // envelope synthesised from floor area so energy isn't fabric-blind.
            // Measured geometry (EnvelopeDetector, in ZoneFromSpace/Room) is preferred
            // and left untouched; this only fills the gaps.
            EnsureEnvelopes(doc, zones, levelOf, warnings);
            return zones;
        }

        /// <summary>Add a floor-area-derived envelope to every zone that has none, so
        /// conduction + solar are counted even without measured geometry. Top-level
        /// zones also get a roof segment. Adds a one-time "synthesised" note. WS A2.</summary>
        private static void EnsureEnvelopes(Document doc, List<LoadZone> zones,
            Dictionary<LoadZone, ElementId> levelOf, List<string> warnings)
        {
            try
            {
                var inp = ResolveEnvelopeInputs(doc);
                bool synthesised = false;
                foreach (var z in zones)
                {
                    if (z.Envelope.Count > 0) continue;   // measured/fallback envelope present
                    bool top = !levelOf.TryGetValue(z, out var lid)
                               || lid == null || lid == ElementId.InvalidElementId
                               || EnvelopeDetector.IsTopLevelId(doc, lid);
                    var segs = SustainEnvelopeSynth.FromFloorArea(z.FloorAreaM2, z.HeightM, top, inp);
                    if (segs.Count > 0) { z.Envelope.AddRange(segs); synthesised = true; }
                }
                if (synthesised)
                    warnings?.Add("Envelope synthesised from floor area (representative estimate using the active " +
                                  "construction profile's U-values) — conduction + solar are now counted. This is an " +
                                  "estimate, not measured per-wall geometry; model exterior walls + windows for an exact figure.");
            }
            catch (Exception ex) { StingLog.Warn($"Sustain EnsureEnvelopes: {ex.Message}"); }
        }

        /// <summary>Map the active <see cref="ConstructionProfile"/> (Part L / Passivhaus
        /// / IECC / etc.) onto the Revit-free <see cref="EnvelopeSynthInputs"/>.</summary>
        private static EnvelopeSynthInputs ResolveEnvelopeInputs(Document doc)
        {
            var inp = new EnvelopeSynthInputs();
            try
            {
                var cp = ConstructionProfileRegistry.Active(doc);
                if (cp != null)
                {
                    inp.WallUvalue    = cp.WallUvalue;
                    inp.RoofUvalue    = cp.RoofUvalue;
                    inp.WindowUvalue  = cp.WindowUvalue;
                    inp.WindowShgc    = cp.WindowSHGC;
                    inp.WindowShading = cp.WindowShadingFactor;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ResolveEnvelopeInputs: {ex.Message}"); }
            return inp;
        }

        /// <summary>DHW litres/person·day used ONLY when no profile resolves (the
        /// per-use values now live in the load profiles — WS K2). WS L4 — the old
        /// DhwForUse C# switch is fully removed; DHW resolves solely from the profile
        /// (e.g. hotel is now 120 L/p·d, superseding the old switch's 100).</summary>
        private const double DhwFallback = 5.0;

        /// <summary>WS K2 — data-driven use→profile resolution (id → alias → loose →
        /// nearest sibling → Office). A fallback is surfaced as a NOTE (never a silent
        /// office swap) and de-duped per run.</summary>
        private static ProfileResolution ResolveProfile(LoadProfileLibrary profiles, string use,
            List<string> warnings, HashSet<string> noted)
        {
            var r = profiles?.ResolveForUse(use)
                    ?? new ProfileResolution { Profile = new LoadProfile { Id = "Office" }, IsFallback = true, RequestedUse = use };
            if (r.IsFallback && warnings != null && (noted == null || noted.Add("profile|" + (use ?? ""))))
            {
                string note = $"ℹ {(string.IsNullOrWhiteSpace(use) ? "(unset)" : use)} load profile resolved by fallback " +
                              $"({r.FromTo}) — indicative";
                warnings.Add(note);
                StingLog.Info("Sustain " + note);
            }
            return r;
        }

        /// <summary>Apply a resolved load profile (LPD/EPD/OA/setpoints/schedules + DHW)
        /// to a zone, deriving occupancy from the profile density when the model carries
        /// none. DHW comes from the profile (WS K2), not a C# switch.</summary>
        private static void ApplyProfile(LoadZone z, ProfileResolution r)
        {
            var profile = r?.Profile;
            if (profile != null)
            {
                profile.ApplyTo(z);
                if (z.OccupantCount <= 0 && z.FloorAreaM2 > 0)
                    z.OccupantCount = profile.OccupantCountFor(z.FloorAreaM2);
                z.DhwLPerPersonDay = profile.DhwLPerPersonDay;   // per-use DHW from the profile data
            }
            else z.DhwLPerPersonDay = DhwFallback;
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
                // a zone that still ends up envelope-less is synthesised in EnsureEnvelopes.
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

        private static WaterEstimateResult EstimateWater(Document doc, SustainProjectSetup setup,
                                                         GreenBaseline baseline, ClimateMonthlySite climate,
                                                         int occupancy)
        {
            var profileReg = SustainabilityRegistries.WaterProfiles(doc);
            var profile = profileReg.Get(setup.DominantBuildingUse);

            var baselineFlows = FixtureFlows.FromBaseline(baseline);
            // WS K4 — surface a water-profile fallback as a visible NOTE (never silent).
            string useForWater = setup.DominantBuildingUse;
            bool waterFallback = !string.IsNullOrWhiteSpace(useForWater) && !profileReg.Has(useForWater);
            // WS A4 / D3 — read real low-flow fixture flows from OST_PlumbingFixtures.
            // Only fall back to the 25%-below-baseline indicative default when the model
            // carries no fixture flow data (the IsIndicativeDefault flag stays honest).
            var modelFlows = ReadDesignFixtureFlows(doc, out string flowNote);
            bool indicative = modelFlows == null;
            var designFlows = modelFlows ?? new FixtureFlows
            {
                WcLpf = baselineFlows.WcLpf * 0.75,
                UrinalLpf = baselineFlows.UrinalLpf * 0.75,
                BasinTapLpm = baselineFlows.BasinTapLpm * 0.75,
                ShowerLpm = baselineFlows.ShowerLpm * 0.75,
                KitchenTapLpm = baselineFlows.KitchenTapLpm * 0.75
            };

            // WS H2 — occupancy is the unified project population resolved by the caller.

            // WS A4 — real RWH yield via RainwaterHarvestingCalc (BS 8515): roof area
            // (PLM_STORM_ROOF_M2 or summed OST_Roofs) + rainfall from the single
            // climate source, sized against the non-potable (WC+urinal) demand RWH
            // serves. EDGE credits this toward the water gate (WaterSavingsInclAltPct).
            double rwhYieldL = ComputeRwhYieldL(doc, climate, designFlows, profile, occupancy, out var rwhWarn);

            var w = AnnualWaterEstimator.Estimate(designFlows, baselineFlows, profile, occupancy,
                rwhYieldLPerYr: rwhYieldL, greywaterReuseFraction: setup.Supply?.GreywaterReuseFraction ?? 0);
            w.IsIndicativeDefault = indicative;
            if (waterFallback)
                w.Warnings.Add($"ℹ {useForWater} water profile resolved by fallback ({useForWater} → office) — indicative");
            if (indicative)
                w.Warnings.Add("Water % is an indicative default of 25% below baseline (i.e. a 25% saving) — " +
                               "no low-flow fixture data was read from the model (name the fixture types with " +
                               "their ratings, e.g. \"WC Dual Flush 6/4L\", or stamp the flush/flow values for a real figure).");
            else if (!string.IsNullOrEmpty(flowNote))
                w.Warnings.Add(flowNote);
            if (!string.IsNullOrEmpty(rwhWarn)) w.Warnings.Add(rwhWarn);
            return w;
        }

        /// <summary>Read low-flow fixture flows from the model (WS A4 / D3). Scans
        /// plumbing fixtures, classifies each (WC / urinal / basin / shower / kitchen),
        /// reads an explicitly-stamped flow param when present + in-band, else the
        /// largest supply MEP-connector flow (taps/showers), else parses the rating off
        /// the fixture TYPE / family name (e.g. "Basin Mixer 5 L/min"), and aggregates
        /// a median per kind. Returns null only when NO fixture yielded a rating (caller
        /// then uses the indicative default). <paramref name="note"/> records which kinds
        /// were read.</summary>
        private static FixtureFlows ReadDesignFixtureFlows(Document doc, out string note)
        {
            note = null;
            try
            {
                var fixtures = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType()
                    .ToList();
                if (fixtures.Count == 0) return null;

                var byKind = new Dictionary<FixtureKind, List<double>>();
                foreach (var el in fixtures)
                {
                    string name = FixtureNameText(doc, el);
                    var kind = FixtureFlowReader.ClassifyKind(name);
                    if (kind == FixtureKind.Unknown) continue;

                    // Three real-data sources, best first: a stamped + in-band flow
                    // param → the largest supply connector flow (taps/showers) → the
                    // rating parsed off the schedule name.
                    double? v = ReadFixtureFlowParam(el, kind);
                    if (!v.HasValue && (kind == FixtureKind.Basin || kind == FixtureKind.Shower || kind == FixtureKind.KitchenTap))
                    {
                        double lpm = ConnectorFlowLpm(el);
                        if (lpm > 0 && FixtureFlowReader.InBand(kind, lpm)) v = lpm;
                    }
                    if (!v.HasValue) v = FixtureFlowReader.ParseFlow(kind, name);

                    if (v.HasValue && v.Value > 0)
                    {
                        if (!byKind.TryGetValue(kind, out var list)) { list = new List<double>(); byKind[kind] = list; }
                        list.Add(v.Value);
                    }
                }
                if (byKind.Count == 0) return null;

                var f = new FixtureFlows();   // class low-flow defaults 6/4/8/10/8
                var got = new List<string>();
                if (TryMedian(byKind, FixtureKind.Wc, out var wc))        { f.WcLpf = wc;          got.Add($"WC {wc:0.#} L/flush"); }
                if (TryMedian(byKind, FixtureKind.Urinal, out var ur))    { f.UrinalLpf = ur;      got.Add($"urinal {ur:0.#} L/flush"); }
                if (TryMedian(byKind, FixtureKind.Basin, out var ba))     { f.BasinTapLpm = ba;    got.Add($"basin tap {ba:0.#} L/min"); }
                if (TryMedian(byKind, FixtureKind.Shower, out var sh))    { f.ShowerLpm = sh;      got.Add($"shower {sh:0.#} L/min"); }
                if (TryMedian(byKind, FixtureKind.KitchenTap, out var kt)){ f.KitchenTapLpm = kt;  got.Add($"kitchen tap {kt:0.#} L/min"); }
                if (got.Count == 0) return null;

                note = "Design fixture flows read from the model: " + string.Join(", ", got) +
                       " (kinds with no rating in the schedule kept the standard low-flow default).";
                return f;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ReadDesignFixtureFlows: {ex.Message}"); note = null; return null; }
        }

        /// <summary>Candidate explicit flow-param names a plumbing-aware project may
        /// stamp, per fixture kind (read as a plain number, band-guarded). These are
        /// integration hooks (like NativeParamMapper's Revit-name list), not
        /// project-defining values.</summary>
        private static readonly Dictionary<FixtureKind, string[]> FlowParamNames =
            new Dictionary<FixtureKind, string[]>
            {
                [FixtureKind.Wc]         = new[] { "SUS_FIXTURE_FLUSH_L", "PLM_WC_LPF", "WC_FLUSH_L", "Flush Volume", "Full Flush" },
                [FixtureKind.Urinal]     = new[] { "SUS_FIXTURE_FLUSH_L", "PLM_URINAL_LPF", "Urinal Flush" },
                [FixtureKind.Basin]      = new[] { "SUS_FIXTURE_FLOW_LPM", "PLM_TAP_LPM", "Basin Flow", "Flow Rate" },
                [FixtureKind.Shower]     = new[] { "SUS_FIXTURE_FLOW_LPM", "PLM_SHOWER_LPM", "Shower Flow", "Flow Rate" },
                [FixtureKind.KitchenTap] = new[] { "SUS_FIXTURE_FLOW_LPM", "PLM_KITCHEN_LPM", "Sink Flow", "Flow Rate" },
            };

        private static double? ReadFixtureFlowParam(Element el, FixtureKind kind)
        {
            if (!FlowParamNames.TryGetValue(kind, out var names)) return null;
            foreach (var pn in names)
            {
                try
                {
                    var p = el.LookupParameter(pn);
                    if (p == null || !p.HasValue) continue;
                    double v = 0; bool got = false;
                    if (p.StorageType == StorageType.Double)  { v = p.AsDouble(); got = true; }
                    else if (p.StorageType == StorageType.Integer) { v = p.AsInteger(); got = true; }
                    else if (p.StorageType == StorageType.String)
                        got = double.TryParse((p.AsString() ?? "").Trim(),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
                    if (got && FixtureFlowReader.InBand(kind, v)) return v;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Largest supply MEP-connector flow on a fixture, L/min (0 when none).</summary>
        private static double ConnectorFlowLpm(Element el)
        {
            try
            {
                var fi = el as FamilyInstance;
                var cm = fi?.MEPModel?.ConnectorManager;
                if (cm == null) return 0;
                double maxLs = 0;
                foreach (Connector c in cm.Connectors)
                {
                    try
                    {
                        if (c.Domain != Domain.DomainPiping) continue;
                        double ls = UnitUtils.ConvertFromInternalUnits(c.Flow, UnitTypeId.LitersPerSecond);
                        if (ls > maxLs) maxLs = ls;
                    }
                    catch { }
                }
                return maxLs * 60.0;   // L/s → L/min
            }
            catch { return 0; }
        }

        private static bool TryMedian(Dictionary<FixtureKind, List<double>> byKind, FixtureKind kind, out double median)
        {
            median = 0;
            if (!byKind.TryGetValue(kind, out var list) || list.Count == 0) return false;
            var sorted = list.OrderBy(x => x).ToList();
            int n = sorted.Count;
            median = (n % 2 == 1) ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
            return true;
        }

        /// <summary>Combined searchable name text for a fixture: family + type +
        /// instance name + Mark — whichever carries the low-flow rating.</summary>
        private static string FixtureNameText(Document doc, Element el)
        {
            var parts = new List<string>();
            try
            {
                if (doc.GetElement(el.GetTypeId()) is ElementType sym)
                { parts.Add(sym.FamilyName ?? ""); parts.Add(sym.Name ?? ""); }
            }
            catch { }
            try { parts.Add(el.Name ?? ""); } catch { }
            try { parts.Add(ParameterHelpers.GetString(el, "Mark")); } catch { }
            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /// <summary>Annual RWH yield (litres) via RainwaterHarvestingCalc (BS 8515),
        /// sized against the non-potable (WC+urinal) demand. Roof area from
        /// PLM_STORM_ROOF_M2 on ProjectInformation, else summed OST_Roofs. Rainfall
        /// from the climate registry (monthly when available). WS A4.</summary>
        private static double ComputeRwhYieldL(Document doc, ClimateMonthlySite climate,
            FixtureFlows designFlows, WaterUsageProfile profile, int occupancy, out string warning)
        {
            warning = null;
            try
            {
                if (climate == null || occupancy <= 0) return 0;
                double roofM2 = ResolveRoofAreaM2(doc);
                if (roofM2 <= 0) return 0;

                double annualRainMm = climate.AnnualRainfallMm;
                double[] monthlyMm = (climate.RainfallMm != null && climate.RainfallMm.Length == 12 && climate.AnnualRainfallMm > 0)
                    ? climate.RainfallMm : null;
                if (annualRainMm <= 0) { warning = "RWH not computed — no rainfall data for the resolved climate site."; return 0; }

                double nonPotableLpd = AnnualWaterEstimator.NonPotableLPersonDay(designFlows, profile);
                double dailyDemandM3 = nonPotableLpd * occupancy / 1000.0;
                if (dailyDemandM3 <= 0) return 0;

                // runoff + filter efficiency default inside the calc (BS 8515) when ≤ 0.
                var rwh = RainwaterHarvestingCalc.Calculate(
                    roofAreaM2: roofM2, annualRainfallMm: annualRainMm,
                    runoffCoefficient: 0, filterEfficiency: 0,
                    dailyDemandM3: dailyDemandM3, monthlyRainfallMm: monthlyMm);
                return rwh.AnnualYieldM3 * 1000.0;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ComputeRwhYieldL: {ex.Message}"); return 0; }
        }

        /// <summary>Roof catchment area, m² — PLM_STORM_ROOF_M2 on ProjectInformation
        /// first (lets a project set it directly), else the summed OST_Roofs area.</summary>
        private static double ResolveRoofAreaM2(Document doc)
        {
            try
            {
                double stamped = ReadFirstDouble(doc.ProjectInformation, new[] { "PLM_STORM_ROOF_M2" });
                if (stamped > 0) return stamped;
            }
            catch { }
            try
            {
                double sumFt2 = 0;
                var roofs = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Roofs)
                    .WhereElementIsNotElementType();
                foreach (var r in roofs)
                {
                    var p = r.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (p != null && p.HasValue) sumFt2 += p.AsDouble();
                }
                return sumFt2 > 0 ? UnitUtils.ConvertFromInternalUnits(sumFt2, UnitTypeId.SquareMeters) : 0;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ResolveRoofAreaM2: {ex.Message}"); return 0; }
        }

        private static double ReadFirstDouble(Element el, string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var p = el.LookupParameter(n);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                    {
                        double v = p.AsDouble();
                        if (v > 0) return v;
                    }
                }
                catch { }
            }
            return 0;
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

        private static List<MaterialLine> GatherMaterialLines(Document doc, SustainProjectSetup setup, bool forceRefresh)
        {
            double wastePctCfg = TagConfig.GetConfigDouble("COST_DEFAULT_WASTE_PCT", 5.0);
            var fs = setup?.FactorSources ?? new FactorSourceOrder();
            // The take-off depends only on the MODEL + the carbon/energy dataset order
            // + waste — NOT on zones/occupancy/supply/target level. Key the sub-cache
            // accordingly so an energy/water-only change reuses it (WS E1).
            string matKey = (doc.PathName ?? "<no-doc>") + "|mat|"
                + string.Join(",", fs.EmbodiedCarbon ?? new List<string>()) + "|"
                + string.Join(",", fs.EmbodiedEnergy ?? new List<string>()) + "|"
                + fs.Region + "|" + wastePctCfg.ToString("R");
            if (!forceRefresh && _materialCache.TryGetValue(matKey, out var mhit)
                && (DateTime.UtcNow - mhit.ts).TotalSeconds < CacheStaleSeconds)
                return mhit.lines;

            var lines = ComputeMaterialLines(doc, setup, wastePctCfg);
            _materialCache[matKey] = (lines, DateTime.UtcNow);
            return lines;
        }

        private static List<MaterialLine> ComputeMaterialLines(Document doc, SustainProjectSetup setup, double wastePct)
        {
            var lines = new List<MaterialLine>();
            try
            {
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
                        // else the ICE seed per-kg figure (C3); else the documented ratio.
                        EnergyMjPerM3 = ReadMaterialDouble(mat, ParamRegistry.SUS_MAT_ENERGY_MJ_M2),
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
                        EnergySource = outp.EnergySource,
                        IndicativeOnly = outp.IndicativeOnly
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain GatherMaterialLines: {ex.Message}"); }
            return lines;
        }

        /// <summary>Resolve a material density (kg/m³) — single source shared with the
        /// per-element heat-map (WS H4) via SustainElementCarbon.DensityFor.</summary>
        private static double DensityFor(string material) => SustainElementCarbon.DensityFor(material);

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
