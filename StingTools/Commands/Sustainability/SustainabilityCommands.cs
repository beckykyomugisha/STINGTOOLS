// StingTools — EDGE/LEED Sustainability commands (Phase 195, spec §11).
//
//   Sustain_ProjectSetup   the zero-hardcoding options surface (§2.5)
//   Sustain_Dashboard      run all selected schemes, render the pane, persist a snapshot
//   Sustain_SetBaseline    resolve + stamp baseline intensities + show proxy path
//   Sustain_SupplyConfig   edit PV / grid / diesel supply layer
//   Sustain_EdgeExport     ClosedXML workbook of model quantities for EDGE-app upload
//   Sustain_LccBenefit     per-measure life-cycle cost benefit -> BOQ Cost Manager
//
// Read-only commands carry [Transaction(ReadOnly)]; the stamping commands carry
// [Transaction(Manual)]. Built without dotnet build / Revit verification — Revit
// API calls use documented signatures; uncertain spots marked // TODO-VERIFY-API.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Sustainability;
using StingTools.UI;

namespace StingTools.Commands.Sustainability
{
    internal static class SustainCmdHelper
    {
        public static Document Doc(ExternalCommandData cmd)
        {
            // Commands dispatched from the dockable pane arrive with a null
            // ExternalCommandData; fall back to the primed UIApplication.
            try
            {
                var ctx = ParameterHelpers.GetContext(cmd);
                if (ctx?.Doc != null) return ctx.Doc;
            }
            catch { }
            try { return StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document; }
            catch { return null; }
        }

        public static SustainProjectSetup LoadSetup(Document doc)
        {
            string dir = SustainabilityRegistries.ProjectDir(doc);
            var setup = SustainProjectSetup.Load(dir, out bool found);
            if (!found)
            {
                // Seed area/occupancy from the model when no setup exists yet
                // (Spaces preferred, Rooms fallback for architectural models).
                double area = TotalFloorAreaM2(doc);
                setup = SustainProjectSetup.CreateDefault(area, EstimateOccupancy(doc, area));
                // Climate site stamped by the HVAC DocumentOpened auto-stamp.
                try
                {
                    string site = doc?.ProjectInformation?.LookupParameter("PRJ_CLIMATE_SITE_ID")?.AsString();
                    if (!string.IsNullOrWhiteSpace(site)) setup.ClimateSiteId = site;
                }
                catch { }
            }
            return setup;
        }

        public static double TotalSpaceAreaM2(Document doc)
        {
            try
            {
                double a = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Mechanical.Space>()
                    .Where(s => s.Area > 1e-6)
                    .Sum(s => UnitUtils.ConvertFromInternalUnits(s.Area, UnitTypeId.SquareMeters));
                return a;
            }
            catch { return 0; }
        }

        public static double TotalRoomAreaM2(Document doc)
        {
            try
            {
                double a = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area > 1e-6)
                    .Sum(r => UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters));
                return a;
            }
            catch { return 0; }
        }

        /// <summary>Floor area from Spaces, falling back to Rooms (architectural
        /// models carry Rooms, not MEP Spaces).</summary>
        public static double TotalFloorAreaM2(Document doc)
        {
            double a = TotalSpaceAreaM2(doc);
            return a > 1e-6 ? a : TotalRoomAreaM2(doc);
        }

        /// <summary>Occupancy from Space "Number of People"; falls back to an area
        /// density estimate (1 person / 10 m²) when no occupancy data is modelled.</summary>
        public static int EstimateOccupancy(Document doc, double floorAreaM2)
        {
            try
            {
                int sumPeople = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Mechanical.Space>()
                    .Select(s => { var p = s.LookupParameter("Number of People");
                                   return (p != null && p.HasValue && p.StorageType == StorageType.Integer) ? p.AsInteger() : 0; })
                    .Sum();
                if (sumPeople > 0) return sumPeople;
            }
            catch { }
            // Documented density estimate (≈10 m²/person, ASHRAE 62.1 office) — an
            // estimate the user can override, not a silent constant.
            return floorAreaM2 > 0 ? (int)Math.Round(floorAreaM2 / 10.0) : 0;
        }

        /// <summary>The setup the user is currently looking at — prefer the live
        /// panel form (so GFA / occupancy edits apply without a Save), else disk.</summary>
        public static SustainProjectSetup EffectiveSetup(Document doc)
        {
            try
            {
                var panel = StingTools.UI.Sustainability.StingSustainabilityPanel.Instance;
                var fromForm = panel?.ReadSetupForm();
                if (fromForm != null && (fromForm.TotalFloorAreaM2 > 0 || fromForm.TotalOccupancy > 0
                    || !string.IsNullOrWhiteSpace(fromForm.ClimateZone)))
                    return fromForm;
            }
            catch { }
            return LoadSetup(doc);
        }

        public static void PushToPanel(SustainabilityRunResult res)
        {
            try { StingTools.UI.Sustainability.StingSustainabilityPanel.Instance?.RefreshFromRun(res); }
            catch (Exception ex) { StingLog.Warn($"Sustain push to panel: {ex.Message}"); }
        }
    }

    // ── Sustain_ProjectSetup — the options surface (§2.5) ────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainProjectSetupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            // The SETUP tab is the editing surface; this command persists the panel's
            // current setup to project_setup.json (and seeds defaults on first run).
            var panel = StingTools.UI.Sustainability.StingSustainabilityPanel.Instance;
            SustainProjectSetup setup = panel?.ReadSetupForm() ?? SustainCmdHelper.LoadSetup(doc);

            // WS I1 — saving from SETUP is an explicit use choice: the building use is
            // no longer the seeded "office" default, so the readiness gate stops blocking
            // on it. (Location is still gated separately on climate site/zone.)
            if (!string.IsNullOrWhiteSpace(setup.DominantBuildingUse)) setup.UseExplicit = true;

            // WS J1 — preserve any persisted explicit grid/diesel override, then cascade
            // the Country into the blank location + carbon fields so picking a country
            // auto-populates climate site / zone / grid / diesel that persist with setup.
            try
            {
                var persisted = SustainCmdHelper.LoadSetup(doc);
                if (setup.Supply != null && persisted?.Supply != null)
                {
                    if (persisted.Supply.GridCarbonExplicit)
                    { setup.Supply.GridCarbonExplicit = true; setup.Supply.GridCarbonKgco2eKwh = persisted.Supply.GridCarbonKgco2eKwh; }
                    if (persisted.Supply.DieselCarbonExplicit)
                    { setup.Supply.DieselCarbonExplicit = true; setup.Supply.DieselCarbonKgco2eKwh = persisted.Supply.DieselCarbonKgco2eKwh; }
                }
                var row = SustainabilityRegistries.Countries(doc).Resolve(setup.Country);
                var applied = CountryCascade.Apply(setup, row);
                if (applied.Count > 0)
                    StingLog.Info($"Sustain country cascade ({setup.Country}): filled {string.Join(", ", applied)}.");
            }
            catch (Exception ex) { StingLog.Warn($"Sustain country cascade: {ex.Message}"); }

            string dir = SustainabilityRegistries.ProjectDir(doc);
            if (string.IsNullOrEmpty(dir))
            {
                TaskDialog.Show("STING Sustainability",
                    "Save the project to disk first — project setup writes to <project>/_BIM_COORD/sustainability/.");
                return Result.Cancelled;
            }
            setup.Save(dir);
            // WS B4 — offer the data-driven building-use list (registry union) before
            // restoring the form so the saved use resolves against the live options.
            try
            {
                var uses = BuildingUseCatalog.Resolve(
                    SustainabilityRegistries.Baselines(doc).All.Select(b => b.Key?.BuildingUse),
                    SustainabilityRegistries.WaterProfiles(doc).All.Select(p => p.BuildingUse));
                panel?.PopulateBuildingUses(uses);
                // WS J2 — data-driven Country dropdown from the country seed.
                panel?.PopulateCountries(SustainabilityRegistries.Countries(doc).DropdownLabels());
            }
            catch (Exception ex) { StingLog.Warn($"Sustain building-use list: {ex.Message}"); }
            panel?.LoadSetupForm(setup);
            StingLog.Info($"Sustain_ProjectSetup: saved setup ({string.Join(",", setup.Schemes)} / {setup.DominantBuildingUse} / {setup.ClimateZone}).");
            TaskDialog.Show("STING Sustainability",
                $"Project setup saved.\nSchemes: {string.Join(", ", setup.Schemes)}\n" +
                $"Building use: {setup.DominantBuildingUse}\nClimate zone: {setup.ClimateZone}\n" +
                $"Occupancy: {setup.TotalOccupancy} · Area: {setup.TotalFloorAreaM2:0} m²");
            return Result.Succeeded;
        }
    }

    // ── Sustain_Dashboard — run + render + persist snapshot ──────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            // The dashboard is the explicit run — force a fresh model read; the export
            // / LCC / publish commands then reuse this cached pass (WS E1).
            var res = SustainabilityEngine.Run(doc, setup, forceRefresh: true);
            SustainCmdHelper.PushToPanel(res);

            // Persist an EdgeKpiSnapshot for trend / burn-down.
            string dir = SustainabilityRegistries.ProjectDir(doc);
            var edge = res.Schemes.FirstOrDefault(s => s.SchemeId == "EDGE");
            var snap = new EdgeKpiSnapshot
            {
                EnergyEuiKwhM2Yr   = Round(res.Energy?.DesignEuiKwhM2Yr),
                EnergySavingsPct    = Round(res.Energy?.EnergySavingsPct),
                WaterLPersonDay     = Round(res.Water?.DesignLPersonDay),
                // WS H5 — record the SAME inclusive water % the EDGE gate uses (fixture
                // + alternative water), so the trend agrees with the on-screen pass/fail.
                WaterSavingsPct     = Round(EdgeKpiSnapshot.GateWaterPct(res.Water)),
                MaterialCarbonKgM2  = Round(res.Materials?.CarbonIntensityKgM2),
                MaterialEnergyMjM2  = Round(res.Materials?.EnergyIntensityMjM2),
                MaterialEnergySavingsPct = Round(res.Materials?.EmbodiedEnergySavingsPct),
                GwpReductionPct      = Round(res.Materials?.GwpReductionPct),
                EdgeLevel            = edge?.AchievedLevel ?? "None",
                EdgePassed           = edge?.Passed ?? false,
                OperationalCarbonKgYr = Round(res.Energy?.OperationalCarbonKgYr),
                WholeLifeCarbonKgM2  = Round(res.WholeLife?.WholeLifeKgM2),
                StudyPeriodYears     = res.WholeLife?.StudyPeriodYears ?? setup.StudyPeriodYears,
                Occupancy            = setup.TotalOccupancy,
                FloorAreaM2          = Round(res.Energy?.FloorAreaM2),
                SupplyMode           = setup.Supply?.Mode ?? "grid_tied",
                ProxyPath            = res.Baseline?.Summary ?? "",
                Country              = setup.Country,
                ClimateZone          = setup.ClimateZone
            };
            EdgeKpiSnapshot.Append(dir, snap);

            // WS I13 — the result is now fresh; arm the stale marker so later
            // envelope/fixture edits flag the dashboard as out of date.
            try { StingTools.Core.Sustainability.SustainStaleUpdater.MarkFresh(); }
            catch (Exception ex) { StingLog.Warn($"Sustain MarkFresh: {ex.Message}"); }

            // Render a result panel mirroring KutKpiDashboard (in addition to the pane).
            RenderResultPanel(res, setup);
            StingLog.Info($"Sustain_Dashboard: energy {snap.EnergySavingsPct:F1}%, water {snap.WaterSavingsPct:F1}%, EDGE {snap.EdgeLevel}.");
            return Result.Succeeded;
        }

        private static double Round(double? v) => Math.Round(v ?? 0, 1);

        private static void RenderResultPanel(SustainabilityRunResult res, SustainProjectSetup setup)
        {
            var edge = res.Schemes.FirstOrDefault(s => s.SchemeId == "EDGE");
            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — EDGE / LEED Dashboard")
                .SetSubtitle(SustainHeader.Subtitle(
                    res.ResolvedUse?.Found == true ? res.ResolvedUse.Use : setup.DominantBuildingUse,
                    res.ResolvedUse?.Found ?? false,
                    setup.ClimateZone, setup.TotalFloorAreaM2, setup.TotalOccupancy))
                // WS J3 — don't headline a savings % the energy gate didn't compute
                // (floor area 0 / occupancy 0 → degenerate, not a result).
                .SetOverallPct(res.Energy?.Computed == true ? res.Energy.EnergySavingsPct : 0);

            // WS I1 — a location/use-unset model is a generic proxy, not the user's
            // project: banner it prominently and (when blocked) don't claim a level.
            if (res.Readiness != null && !string.IsNullOrEmpty(res.Readiness.Banner))
                b.AddSection(res.Readiness.Ready ? "⚠ Indicative" : "⛔ Generic proxy — not your project")
                 .Info(res.Readiness.Banner);

            // WS J3 — energy headline: show the EUI + savings only when computed; else
            // the not-computed state + reason (floor area / occupancy 0).
            if (res.Energy?.Computed == true)
            {
                b.AddSection("Energy")
                 .Metric("Design EUI", $"{res.Energy.DesignEuiKwhM2Yr:F1} kWh/m²·yr", $"baseline {res.Energy.BaselineEuiKwhM2Yr:F1}")
                 .Metric("Energy savings — indicative", $"{res.Energy.EnergySavingsPct:F1}%");
                // WS K5 — the resolved load profile + EDGE building-category mapping.
                if (res.Profile != null)
                    b.Metric("Load profile", res.Profile.ProfileId,
                             $"EDGE: {res.Profile.EdgeBuildingType} · DHW {res.Profile.DhwLPerPersonDay:0} L/p·d · {res.Profile.Source}");
                // WS L6 — when the baseline's building-use axis fell back, the savings %
                // is against a proxy-use baseline; call that out explicitly (no silent
                // climate-zone/use fallback for the savings number).
                if (res.Baseline != null &&
                    res.Baseline.FallbackAxes.Any(a => a.IndexOf("building use", StringComparison.OrdinalIgnoreCase) >= 0))
                    b.MetricWarn("Energy savings basis", "indicative — baseline use proxy",
                        $"no '{setup.DominantBuildingUse}' baseline; savings computed vs {res.Baseline.MatchedKey}");
            }
            else
                b.AddSection("Energy").MetricWarn("Energy", "not computed",
                    res.Energy != null && res.Energy.Occupancy <= 0 ? "occupancy is 0 — set occupancy/GFA in Setup"
                    : res.Energy != null && res.Energy.FloorAreaM2 <= 0 ? "floor area is 0 — set GFA in Setup"
                    : "add Spaces/GFA + occupancy, then re-run");

            b.AddSection("Baseline resolution (proxy log)")
             .Info(res.Baseline?.Summary ?? "no baseline resolved");
            // WS I2 — show the resolved key + an honest exact-vs-proxy flag so a
            // wildcard/defaulted resolution is never read as an exact match.
            if (res.Baseline != null && res.Baseline.Found)
            {
                if (res.Baseline.ExactMatch)
                    b.Metric("Baseline match", res.Baseline.MatchedKey, "exact match");
                else
                    b.MetricWarn("Baseline match", res.Baseline.MatchedKey,
                        "default proxy (not exact): " + string.Join("; ", res.Baseline.FallbackAxes));
            }

            if (edge != null)
            {
                b.AddSection($"EDGE gates (target {edge.TargetLevel}; achieved {edge.AchievedLevel})");
                foreach (var g in edge.Gates)
                {
                    string val = g.Delegated ? "→ EDGE app"
                               : !g.Computed ? "Not computed"
                               : $"{g.IndicativeValue:F1}{g.Unit} (target {g.Threshold:F0}{g.Unit})";
                    string note = g.Delegated ? "STING-indicative; EDGE app owns the official %"
                                : !g.Computed ? g.Note
                                : null;
                    if (g.Passed) b.MetricHighlight(g.Label + " — indicative", val, note);
                    else          b.MetricWarn(g.Label + " — indicative", val, note);
                }
            }

            foreach (var sc in res.Schemes.Where(s => s.SchemeId != "EDGE"))
                b.AddSection($"{sc.SchemeName}")
                 .Metric("Aggregation", sc.Aggregation)
                 .Metric("Points", sc.TotalPoints.ToString(), $"band {sc.Band}");

            b.AddSection("Materials (dual metric)")
             .Metric("Embodied carbon", $"{res.Materials?.CarbonIntensityKgM2:F1} kgCO2e/m²", "A1-A3 GWP (EN 15978)")
             .Metric("Embodied energy", $"{res.Materials?.EnergyIntensityMjM2:F0} MJ/m²", "CED — EDGE materials track (indicative)");
            // WS I5 — coverage + sanity, surfaced here (not only the Materials tab).
            if (res.Materials != null)
            {
                b.Metric("Coverage", res.Materials.CoverageSummary, "carbon-stamped vs measured; under-counts the rest");
                if (res.Materials.DominantHotspotImplausible)
                    b.MetricWarn("Carbon sanity", $"{res.Materials.DominantHotspotMaterial} {res.Materials.DominantHotspotSharePct:0}%",
                        "one line dominates — likely a quantity/factor error");
                if (res.Materials.IntensityImplausible)
                    b.MetricWarn("Carbon sanity", $"{res.Materials.CarbonIntensityKgM2:0} kgCO2e/m²",
                        "implausibly high — review quantities/factors");
            }
            if (res.Materials?.Hotspots?.Count > 0)
                b.Table(new[] { "Carbon hotspot", "kgCO2e", "%" },
                    res.Materials.Hotspots.Select(h =>
                        new[] { h.Material, $"{h.CarbonKg:F0}", $"{h.SharePct:F0}%" }).ToList());

            b.AddSection("Supply + carbon")
             .Metric("On-site PV", $"{res.Energy?.PvGenerationKwh:F0} kWh/yr")
             .Metric("Net import", $"{res.Energy?.NetImportKwh:F0} kWh/yr")
             .Metric("Operational carbon", $"{res.Energy?.OperationalCarbonKgYr:F0} kgCO2e/yr", $"supply mode {setup.Supply?.Mode}");
            // WS I3 — show the grid factor + its source; flag a default factor.
            if (res.GridCarbon != null)
            {
                string note = res.GridCarbon.Source;
                if (res.GridCarbon.IsDefault)
                    b.MetricWarn("Grid factor", $"{res.GridCarbon.Factor:0.00} kgCO2e/kWh", "default factor — set project country");
                else
                    b.Metric("Grid factor", $"{res.GridCarbon.Factor:0.00} kgCO2e/kWh", note);
            }

            // WS H4 — one whole-life carbon figure (embodied A1-A3 + operational over
            // the study period); carbon only. Study period matches the RIBA-stage view.
            if (res.WholeLife != null)
                b.AddSection($"Whole-life carbon ({res.WholeLife.StudyPeriodYears}-year study)")
                 .Metric("Embodied A1-A3", $"{res.WholeLife.EmbodiedKgM2:F0} kgCO2e/m²", "net, incl. biogenic credit")
                 .Metric("Operational", $"{res.WholeLife.OperationalKgM2Yr:F1} kgCO2e/m²·yr",
                         $"× {res.WholeLife.StudyPeriodYears} yr")
                 .Metric("Whole-life total", $"{res.WholeLife.WholeLifeKgM2:F0} kgCO2e/m²",
                         "aligns with the RIBA-stage carbon view");

            if (res.Warnings.Count > 0)
                b.AddSection("Notes").Info(string.Join("\n", res.Warnings.Distinct().Take(8)));

            b.Show();
        }
    }

    // ── Sustain_SetBaseline — resolve + stamp + show proxy path ──────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainSetBaselineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            var res = SustainabilityEngine.Run(doc, setup);

            // Stamp the resolved baseline intensities + achieved level onto ProjectInfo.
            // WS H3 — collect any target param that isn't bound so we warn clearly
            // instead of silently no-op'ing (the dashboard would work in-session but
            // nothing would persist to the model / schedules / IFC).
            var unbound = new List<string>();
            try
            {
                using (var t = new Transaction(doc, "STING Sustainability — Set Baseline"))
                {
                    t.Start();
                    var pi = doc.ProjectInformation;
                    if (!StampDouble(pi, ParamRegistry.SUS_ENERGY_KWH_M2, res.Energy?.DesignEuiKwhM2Yr ?? 0)) unbound.Add(ParamRegistry.SUS_ENERGY_KWH_M2);
                    if (!StampDouble(pi, ParamRegistry.SUS_WATER_L_PD, res.Water?.DesignLPersonDay ?? 0)) unbound.Add(ParamRegistry.SUS_WATER_L_PD);
                    if (!StampDouble(pi, ParamRegistry.SUS_MAT_CARBON_KGM2, res.Materials?.CarbonIntensityKgM2 ?? 0)) unbound.Add(ParamRegistry.SUS_MAT_CARBON_KGM2);
                    if (!StampDouble(pi, ParamRegistry.SUS_MAT_ENERGY_MJ_M2, res.Materials?.EnergyIntensityMjM2 ?? 0)) unbound.Add(ParamRegistry.SUS_MAT_ENERGY_MJ_M2);
                    var edge = res.Schemes.FirstOrDefault(s => s.SchemeId == "EDGE");
                    if (!StampText(pi, ParamRegistry.SUS_EDGE_LEVEL, edge?.AchievedLevel ?? "None")) unbound.Add(ParamRegistry.SUS_EDGE_LEVEL);
                    t.Commit();
                }
                if (unbound.Count > 0)
                    StingLog.Warn($"Sustain SetBaseline: {unbound.Count} SUS_* param(s) not bound — baseline did NOT persist: " +
                                  string.Join(", ", unbound) + ". Run 'Load Shared Parameters' first.");
            }
            catch (Exception ex) { StingLog.Warn($"Sustain SetBaseline stamp: {ex.Message}"); }

            SustainCmdHelper.PushToPanel(res);

            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — Baseline")
                .SetSubtitle($"{setup.Country}/{setup.ClimateZone}/{setup.DominantBuildingUse}");
            b.AddSection("Resolution path (proxy log)").Info(res.Baseline?.Summary ?? "no baseline resolved");
            if (res.Baseline?.Path != null)
                b.Table(new[] { "Hop", "Matched", "Detail" },
                    res.Baseline.Path.Select(h => new[] { h.Key, h.Matched ? "✓" : "—", h.Detail }).ToList());
            b.AddSection("Stamped baseline intensities")
             .Metric("Design EUI", $"{res.Energy?.DesignEuiKwhM2Yr:F1} kWh/m²·yr", $"baseline {res.Energy?.BaselineEuiKwhM2Yr:F1}")
             .Metric("Design water", $"{res.Water?.DesignLPersonDay:F1} L/person·day", $"baseline {res.Water?.BaselineLPersonDay:F1}")
             .Metric("Provenance", res.Baseline?.Provenance ?? "—", "never 'certified'");
            if (unbound.Count > 0)
                b.AddSection("⚠ Not persisted")
                 .Info($"{unbound.Count} sustainability parameter(s) are not bound, so the baseline was " +
                       "NOT written to the model: " + string.Join(", ", unbound) +
                       ". Run 'Load Shared Parameters' (it now binds the SUS_* group to Project Information), then re-run.");
            b.Show();

            StingLog.Info($"Sustain_SetBaseline: {res.Baseline?.Summary}");
            return Result.Succeeded;
        }

        /// <summary>Stamp a numeric value; returns false when the param isn't bound
        /// (LookupParameter null) so the caller can warn that the baseline didn't
        /// persist. Read-only counts as "present" (true) — it exists, just not writable.</summary>
        private static bool StampDouble(Element el, string name, double v)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return false;          // not bound — caller warns
                if (p.IsReadOnly) return true;
                if (p.StorageType == StorageType.Double) p.Set(v);
                else if (p.StorageType == StorageType.String) p.Set(v.ToString("F2"));
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"StampDouble {name}: {ex.Message}"); return false; }
        }

        private static bool StampText(Element el, string name, string v)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return false;          // not bound — caller warns
                if (!p.IsReadOnly && p.StorageType == StorageType.String) p.Set(v ?? "");
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"StampText {name}: {ex.Message}"); return false; }
        }
    }

    // ── Sustain_AutoFill — area + occupancy from the model into the SETUP form ─
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainAutoFillCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            double area = SustainCmdHelper.TotalFloorAreaM2(doc);
            int occ = SustainCmdHelper.EstimateOccupancy(doc, area);
            var panel = StingTools.UI.Sustainability.StingSustainabilityPanel.Instance;
            panel?.ApplyAutoFill(area, occ);
            // WS J2 — make sure the Country dropdown is data-driven from the seed.
            try { panel?.PopulateCountries(SustainabilityRegistries.Countries(doc).DropdownLabels()); }
            catch (Exception ex) { StingLog.Warn($"Sustain populate countries: {ex.Message}"); }

            string src = SustainCmdHelper.TotalSpaceAreaM2(doc) > 1e-6 ? "MEP Spaces" : "Rooms";
            if (area <= 0)
                TaskDialog.Show("STING Sustainability",
                    "No MEP Spaces or Rooms with area found — enter floor area (GFA) manually in Setup.");
            else
                TaskDialog.Show("STING Sustainability",
                    $"Auto-filled from model ({src}):\nFloor area: {area:0} m²\nOccupancy (estimate): {occ}\n\n" +
                    "Review the values on the SETUP tab, then Save setup + Run dashboard.");
            return Result.Succeeded;
        }
    }

    // ── Sustain_ReadinessCheck — model-health dimension (WS I11) ──────────────
    // Surfaces "location / use / occupancy / fixtures incomplete" so a mis-set
    // project is caught (morning health check + status bar) before someone opens
    // the dashboard and reads generic-proxy numbers as real.
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainReadinessCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            var res = SustainabilityEngine.Run(doc, setup);   // cached; reuses the I1 readiness gate
            var rd = res.Readiness ?? SustainReadiness.Evaluate(false, false, false, false);
            string line = SustainReadiness.StatusLine(rd);

            try { StingTools.UI.Sustainability.StingSustainabilityPanel.Instance?.UpdateStatus(line); }
            catch (Exception ex) { StingLog.Warn($"Sustain readiness status: {ex.Message}"); }

            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — Readiness")
                .SetSubtitle(line);
            b.AddSection("Readiness")
             .PassFail("Location set (climate site/zone)", rd.LocationSet)
             .PassFail("Building use resolved", rd.UseSet)
             .PassFail("Occupancy set", rd.OccupancySet)
             .PassFail("Plumbing fixtures modelled", rd.FixturesModelled);
            if (!string.IsNullOrEmpty(rd.Banner))
                b.AddSection(rd.Ready ? "Note" : "⛔ Blocked").Info(rd.Banner);
            b.Show();

            StingLog.Info($"Sustain_ReadinessCheck: {line}");
            return Result.Succeeded;
        }
    }

    // ── Sustain_SupplyConfig — edit PV / grid / diesel supply layer ──────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainSupplyConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            // The SUPPLY card on the SETUP tab edits these inline; this command
            // persists the panel's current supply config into project_setup.json.
            var panel = StingTools.UI.Sustainability.StingSustainabilityPanel.Instance;
            var setup = panel?.ReadSetupForm() ?? SustainCmdHelper.LoadSetup(doc);

            // WS J1 — saving the Supply card is an EXPLICIT grid/diesel override, so the
            // country cascade won't overwrite the user's chosen factors on later runs.
            if (setup.Supply != null) { setup.Supply.GridCarbonExplicit = true; setup.Supply.DieselCarbonExplicit = true; }

            string dir = SustainabilityRegistries.ProjectDir(doc);
            if (string.IsNullOrEmpty(dir))
            {
                TaskDialog.Show("STING Sustainability", "Save the project to disk first.");
                return Result.Cancelled;
            }
            setup.Save(dir);
            TaskDialog.Show("STING Sustainability",
                $"Supply saved (grid/diesel locked as explicit override).\nMode: {setup.Supply.Mode}\nPV: {setup.Supply.PvKwp:0} kWp (PR {setup.Supply.PvPerformanceRatio:0.00})\n" +
                $"Grid factor: {setup.Supply.GridCarbonKgco2eKwh:0.00} · Diesel factor: {setup.Supply.DieselCarbonKgco2eKwh:0.00} · " +
                $"Diesel fraction: {setup.Supply.DieselFraction:0.00}");
            StingLog.Info($"Sustain_SupplyConfig: mode {setup.Supply.Mode}, PV {setup.Supply.PvKwp} kWp.");
            return Result.Succeeded;
        }
    }
}
