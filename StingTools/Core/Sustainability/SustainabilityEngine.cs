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
                        var profile = profiles?.Get(ProfileIdForUse(setup.DominantBuildingUse));
                        double dhw = DhwForUse(setup.DominantBuildingUse);
                        foreach (var r in rooms)
                        {
                            var z = ZoneFromRoom(r);
                            if (z == null) continue;
                            ApplyProfile(z, profile, dhw);
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
                        FloorAreaM2 = zs.FloorAreaM2, HeightM = 3.0, OccupantCount = zs.Occupancy
                    };
                    ApplyProfile(z, profiles?.Get(ProfileIdForUse(zs.BuildingUse)), DhwForUse(zs.BuildingUse));
                    zones.Add(z);
                    // No level for a synthetic setup zone ⇒ left out of levelOf ⇒
                    // treated as top level (includes a roof segment).
                }
            }

            // ── Gap fix #1: synthesise a representative envelope for any zone that
            //    has none, so energy isn't fabric-blind. Conduction + solar are now
            //    counted using the active construction profile's U-values. ──
            EnsureEnvelopes(doc, zones, levelOf, warnings);
            return zones;
        }

        /// <summary>Resolve the active HVAC construction profile and add a
        /// floor-area-derived envelope to every zone that has none. Top-level zones
        /// also get a roof segment. Adds a one-time "synthesised" note.</summary>
        private static void EnsureEnvelopes(Document doc, List<LoadZone> zones,
            Dictionary<LoadZone, ElementId> levelOf, List<string> warnings)
        {
            try
            {
                var inp = ResolveEnvelopeInputs(doc);
                ElementId topId = TopLevelId(doc);
                bool synthesised = false;
                foreach (var z in zones)
                {
                    if (z.Envelope.Count > 0) continue;   // measured envelope present
                    bool top = !levelOf.TryGetValue(z, out var lid)
                               || lid == null || lid == ElementId.InvalidElementId
                               || (topId != null && topId != ElementId.InvalidElementId && lid == topId);
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

        // Per-document cache of the highest-elevation Level id (roof detection).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ElementId> _topLevelCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, ElementId>();

        private static ElementId TopLevelId(Document doc)
        {
            if (doc == null) return ElementId.InvalidElementId;
            return _topLevelCache.GetOrAdd(doc.PathName ?? "<no-doc>", _ =>
            {
                try
                {
                    var top = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderByDescending(l => l.Elevation).FirstOrDefault();
                    return top?.Id ?? ElementId.InvalidElementId;
                }
                catch { return ElementId.InvalidElementId; }
            });
        }

        /// <summary>Drop the cached top-level lookup for a closing document.</summary>
        public static void InvalidateCaches(Document doc)
        {
            try { _topLevelCache.TryRemove(doc?.PathName ?? "<no-doc>", out _); } catch { }
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

        /// <summary>Build a LoadZone from a Room (WS D2) — real per-room area + height;
        /// occupancy from "Number of People" when present, else the profile density.</summary>
        private static LoadZone ZoneFromRoom(Room r)
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

            int occupancy = setup.TotalOccupancy;
            // RWH yield is a project hook (RainwaterHarvestingCalc) — 0 here until a
            // project supplies roof area + rainfall; greywater reuse is a setup fraction.
            var w = AnnualWaterEstimator.Estimate(designFlows, baselineFlows, profile, occupancy,
                rwhYieldLPerYr: 0, greywaterReuseFraction: setup.Supply?.GreywaterReuseFraction ?? 0);
            w.IsIndicativeDefault = indicative;
            if (indicative)
                w.Warnings.Add("Water % is an indicative default of 25% below baseline (i.e. a 25% saving) — " +
                               "no low-flow fixture data was read from the model (name the fixture types with " +
                               "their ratings, e.g. \"WC Dual Flush 6/4L\", or stamp PLM_* flows for a real figure).");
            else if (!string.IsNullOrEmpty(flowNote))
                w.Warnings.Add(flowNote);
            return w;
        }

        /// <summary>Read low-flow fixture flows from the model (gap fix #2). Scans
        /// plumbing fixtures, classifies each (WC / urinal / basin / shower / kitchen),
        /// reads an explicitly-stamped flow param when present and in-band, else parses
        /// the rating off the fixture TYPE / family name (e.g. "Basin Mixer 5 L/min"),
        /// and aggregates a median per kind. Returns null only when NO fixture yielded
        /// a rating (caller then uses the indicative default). <paramref name="note"/>
        /// records which kinds were read.</summary>
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
                    double? v = ReadFixtureFlowParam(el, kind) ?? FixtureFlowReader.ParseFlow(kind, name);
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
                       " (kinds with no rating in the schedule name kept the standard low-flow default).";
                return f;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain ReadDesignFixtureFlows: {ex.Message}"); note = null; return null; }
        }

        /// <summary>Candidate explicit flow-param names a plumbing-aware project may
        /// stamp, per fixture kind (read as a plain number, band-guarded).</summary>
        private static readonly Dictionary<FixtureKind, string[]> FlowParamNames =
            new Dictionary<FixtureKind, string[]>
            {
                [FixtureKind.Wc]         = new[] { "PLM_WC_LPF", "WC_FLUSH_L", "Flush Volume", "Full Flush" },
                [FixtureKind.Urinal]     = new[] { "PLM_URINAL_LPF", "Urinal Flush" },
                [FixtureKind.Basin]      = new[] { "PLM_TAP_LPM", "Basin Flow", "Flow Rate" },
                [FixtureKind.Shower]     = new[] { "PLM_SHOWER_LPM", "Shower Flow", "Flow Rate" },
                [FixtureKind.KitchenTap] = new[] { "PLM_KITCHEN_LPM", "Sink Flow", "Flow Rate" },
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
                        EnergyMjPerKg = 0
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
