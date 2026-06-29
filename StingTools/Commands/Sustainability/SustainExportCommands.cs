// StingTools — EDGE export + LCC benefit commands (Phase 195, spec §11).
//
//   Sustain_EdgeExport  ClosedXML workbook of model quantities + selections for
//                       upload to the official EDGE app (EDGE owns the certified
//                       number — STING figures are labelled "indicative").
//   Sustain_LccBenefit  per-measure life-cycle cost benefit -> BOQ Cost Manager
//                       -> the Design Development Cost/Budget Estimate.

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
using StingTools.BOQ;
using StingTools.UI;

namespace StingTools.Commands.Sustainability
{
    // ── Sustain_EdgeExport ───────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainEdgeExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            var res = SustainabilityEngine.Run(doc, setup);

            string path;
            try
            {
                using (var wb = new XLWorkbook())
                {
                    // WS H6 — the whole workbook honours the project's SI/IP units choice.
                    var units = setup.Units;
                    // WS I4 — a blocked/proxy run (location/use unset) is never computed,
                    // so no gate cell prints a bare number a user could paste into EDGE.
                    bool ready = res.Readiness?.Ready ?? true;
                    BuildProjectSheet(wb, setup, res, units);
                    BuildEnergySheet(wb, res, units, ready);
                    BuildWaterSheet(wb, setup, res, units, ready);
                    BuildMaterialsSheet(wb, doc, res, units);

                    path = OutputLocationHelper.GetOutputPath(doc,
                        $"STING_EDGE_Export_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx");
                    wb.SaveAs(path);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Sustain_EdgeExport", ex);
                TaskDialog.Show("STING Sustainability", "EDGE export failed: " + ex.Message);
                return Result.Failed;
            }

            StingLog.Info($"Sustain_EdgeExport: wrote {path}");
            TaskDialog.Show("STING Sustainability",
                $"EDGE-app upload workbook written:\n{path}\n\n" +
                "These are STING-INDICATIVE quantities + selections. The EDGE app computes the " +
                "certified energy / water / materials %.");
            return Result.Succeeded;
        }

        private static void BuildProjectSheet(XLWorkbook wb, SustainProjectSetup setup, SustainabilityRunResult res, SustainUnits u)
        {
            var ws = wb.Worksheets.Add("Project");
            int r = 1;
            ws.Cell(r, 1).Value = "STING EDGE Export — indicative inputs for the EDGE app"; ws.Cell(r, 1).Style.Font.Bold = true; r += 2;
            void Row(string k, string v) { ws.Cell(r, 1).Value = k; ws.Cell(r, 2).Value = v; r++; }
            Row("Units", SustainUnitConverter.IsIp(u) ? "IP (imperial)" : "SI (metric)");
            Row("Country", setup.Country);
            Row("Climate zone", setup.ClimateZone);
            Row("Dominant building use", setup.DominantBuildingUse);
            Row($"Floor area ({SustainUnitConverter.AreaUnit(u)})",
                SustainUnitConverter.Area(setup.TotalFloorAreaM2, u).ToString("0"));
            Row("Occupancy", setup.TotalOccupancy.ToString());
            Row("Supply mode", setup.Supply?.Mode ?? "");
            Row("PV (kWp)", (setup.Supply?.PvKwp ?? 0).ToString("0"));
            Row("Baseline provenance", res.Baseline?.Provenance ?? "");
            Row("Baseline resolution", res.Baseline?.Summary ?? "");
            ws.Columns().AdjustToContents();
        }

        private static void BuildEnergySheet(XLWorkbook wb, SustainabilityRunResult res, SustainUnits u, bool ready)
        {
            var ws = wb.Worksheets.Add("Energy");
            string absU = SustainUnitConverter.EnergyAbsUnit(u);
            string euiU = SustainUnitConverter.EuiUnit(u);
            ws.Cell(1, 1).Value = "End use"; ws.Cell(1, 2).Value = $"Annual {absU}";
            ws.Row(1).Style.Font.Bold = true;
            var e = res.Energy?.Design;
            int r = 2;
            double A(double kwh) => SustainUnitConverter.EnergyAbs(kwh, u);
            void Row(string k, double v) { ws.Cell(r, 1).Value = k; ws.Cell(r, 2).Value = v; r++; }
            if (e != null)
            {
                Row("Cooling", A(e.CoolingKwh));
                Row("Heating", A(e.HeatingKwh));
                Row("Fans/pumps", A(e.FansKwh));
                Row("Lighting", A(e.LightingKwh));
                Row("Equipment", A(e.EquipmentKwh));
                Row("DHW", A(e.DhwKwh));
                Row("TOTAL", A(e.TotalKwh));
            }
            r++;
            Row($"Design EUI ({euiU}) — indicative", SustainUnitConverter.Eui(res.Energy?.DesignEuiKwhM2Yr ?? 0, u));
            Row($"Baseline EUI ({euiU})", SustainUnitConverter.Eui(res.Energy?.BaselineEuiKwhM2Yr ?? 0, u));
            // WS I4 — only print the savings % when the gate was computed (and the run
            // isn't blocked); else "not computed — indicative default", matching the dashboard.
            bool eComputed = ready && (res.Energy?.Computed ?? false);
            ws.Cell(r, 1).Value = "Energy savings % — indicative";
            ws.Cell(r, 2).Value = SustainExportFormat.GateValue(eComputed, res.Energy?.EnergySavingsPct ?? 0, "0.0", "%"); r++;
            Row($"PV generation ({absU})", A(res.Energy?.PvGenerationKwh ?? 0));
            Row($"Net import ({absU})", A(res.Energy?.NetImportKwh ?? 0));
            ws.Columns().AdjustToContents();
        }

        private static void BuildWaterSheet(XLWorkbook wb, SustainProjectSetup setup, SustainabilityRunResult res, SustainUnits u, bool ready)
        {
            var ws = wb.Worksheets.Add("Water");
            string ppU = SustainUnitConverter.WaterPerPersonDayUnit(u);
            string volU = SustainUnitConverter.WaterVolumeUnit(u);
            int r = 1;
            void Row(string k, string v) { ws.Cell(r, 1).Value = k; ws.Cell(r, 2).Value = v; r++; }
            Row($"Design {ppU} — indicative", SustainUnitConverter.WaterPerPersonDay(res.Water?.DesignLPersonDay ?? 0, u).ToString("0.0"));
            Row($"Baseline {ppU}", SustainUnitConverter.WaterPerPersonDay(res.Water?.BaselineLPersonDay ?? 0, u).ToString("0.0"));
            // WS I4 — the water gate is the inclusive %; print it only when computed
            // (real fixtures) and not blocked — else "not computed", matching the dashboard.
            bool wComputed = ready && (res.Water?.Computed ?? false);
            Row("Water savings % — indicative", SustainExportFormat.GateValue(wComputed, res.Water?.WaterSavingsInclAltPct ?? 0, "0.0", "%"));
            Row($"Annual demand ({volU})", SustainUnitConverter.WaterVolume(res.Water?.AnnualDemandL ?? 0, u).ToString("0"));
            Row($"RWH yield ({volU}/yr)", SustainUnitConverter.WaterVolume(res.Water?.RwhYieldL ?? 0, u).ToString("0"));
            Row($"Net demand ({volU})", SustainUnitConverter.WaterVolume(res.Water?.NetDemandL ?? 0, u).ToString("0"));
            ws.Columns().AdjustToContents();
        }

        private static void BuildMaterialsSheet(XLWorkbook wb, Document doc, SustainabilityRunResult res, SustainUnits u)
        {
            var ws = wb.Worksheets.Add("Materials");
            ws.Cell(1, 1).Value = "Material";
            ws.Cell(1, 2).Value = $"{SustainUnitConverter.CarbonIntensityUnit(u)} (indicative)";
            ws.Cell(1, 3).Value = $"{SustainUnitConverter.EnergyIntensityUnit(u)} (indicative)";
            ws.Row(1).Style.Font.Bold = true;
            int r = 2;
            ws.Cell(r, 1).Value = "BUILDING TOTAL";
            ws.Cell(r, 2).Value = SustainUnitConverter.CarbonIntensity(res.Materials?.CarbonIntensityKgM2 ?? 0, u);
            ws.Cell(r, 3).Value = SustainUnitConverter.EnergyIntensityMj(res.Materials?.EnergyIntensityMjM2 ?? 0, u);
            r++;
            // WS I5 — coverage + sanity, on the export (not only the dashboard).
            ws.Cell(r, 1).Value = "Coverage"; ws.Cell(r, 2).Value = res.Materials?.CoverageSummary ?? "—"; r++;
            if (res.Materials?.DominantHotspotImplausible == true)
            { ws.Cell(r, 1).Value = "⚠ Carbon sanity";
              ws.Cell(r, 2).Value = $"'{res.Materials.DominantHotspotMaterial}' is {res.Materials.DominantHotspotSharePct:0}% of the total — likely a quantity/factor error"; r++; }
            if (res.Materials?.IntensityImplausible == true)
            { ws.Cell(r, 1).Value = "⚠ Carbon sanity";
              ws.Cell(r, 2).Value = $"{res.Materials.CarbonIntensityKgM2:0} kgCO2e/m² is implausibly high — review quantities/factors"; r++; }
            r++;
            ws.Cell(r, 1).Value = $"Carbon hotspots (A1-A3 GWP, {SustainUnitConverter.MassCarbonUnit(u)})"; ws.Cell(r, 1).Style.Font.Bold = true; r++;
            if (res.Materials?.Hotspots != null)
                foreach (var h in res.Materials.Hotspots)
                {
                    ws.Cell(r, 1).Value = h.Material;
                    ws.Cell(r, 2).Value = SustainUnitConverter.MassCarbon(h.CarbonKg, u);   // absolute carbon mass
                    ws.Cell(r, 3).Value = $"{h.SharePct:0}%";
                    r++;
                }
            ws.Columns().AdjustToContents();
        }
    }

    // ── Sustain_LccBenefit — per-measure life-cycle cost benefit ─────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainLccBenefitCommand : IExternalCommand
    {
        // Default analysis period (years) for the simple LCC roll-up; overridable
        // via the SETUP tab in a future iteration.
        private const int AnalysisYears = 25;

        // WS A5 — manual BOQ rows minted by this command carry this RateSource so a
        // re-run replaces them idempotently (never duplicates, never clobbers the
        // user's own manual / provisional-sum rows).
        private const string SustainRateSource = "Sustainability";

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            var res = SustainabilityEngine.Run(doc, setup);
            var measures = SustainabilityRegistries.Measures(doc);

            // Real model quantities sized once for every measure (WS A5 — replaces
            // the crude floor-area proxies in the old EstimateCapex).
            var ctx = BuildQuantityContext(doc, setup, res);

            var rows = new List<string[]>();         // raw numerics → portable CSV
            var displayRows = new List<string[]>();  // grouped thousands → panel + grid
            var boqRows = new List<BOQLineItem>();
            double totalCapex = 0, totalLifetimeSaving = 0;
            int proxySized = 0;

            // WS I6 — a measure's saving is only credible when the gate it draws from
            // was actually computed (and the run isn't blocked).
            bool ready = res.Readiness?.Ready ?? true;
            bool eC = res.Energy?.Computed ?? false, wC = res.Water?.Computed ?? false, mC = res.Materials?.Computed ?? false;
            int measuresOnNotComputedGate = 0;

            foreach (var m in measures.All)
            {
                var sizing = SustainMeasureCapex.Compute(m, ctx);
                double capex = sizing.Capex;
                double annualSaving = EstimateAnnualSaving(m, setup, res);
                double lifetimeSaving = annualSaving * AnalysisYears;
                double netBenefit = lifetimeSaving - capex;
                totalCapex += capex;
                totalLifetimeSaving += lifetimeSaving;
                if (!sizing.UsedModelQuantity) proxySized++;

                bool gateComputed = SustainLccHealth.GateComputed(m.Gate, ready, eC, wC, mC);
                if (!gateComputed) measuresOnNotComputedGate++;
                string netCell = gateComputed ? $"{netBenefit:0}" : $"{netBenefit:0} (indicative — gate not computed)";
                string netDisp = gateComputed ? $"{netBenefit:N0}" : $"{netBenefit:N0} (indicative — gate not computed)";

                rows.Add(new[]
                {
                    m.Name, m.Gate, sizing.BasisLabel,
                    $"{capex:0}", $"{annualSaving:0}/yr",
                    $"{lifetimeSaving:0}", netCell
                });
                displayRows.Add(new[]
                {
                    m.Name, m.Gate, sizing.BasisLabel,
                    $"{capex:N0}", $"{annualSaving:N0}/yr",
                    $"{lifetimeSaving:N0}", netDisp
                });

                boqRows.Add(BuildBoqRow(m, sizing, lifetimeSaving));
            }

            // No operational saving on ANY measure ⇒ the Dashboard hasn't produced
            // energy/water savings yet (no GFA / occupancy), so every "net benefit"
            // is just −capex and reads as a loss. Flag it loudly rather than letting
            // capex-only rows look like the whole-life picture.
            bool noSavings = totalLifetimeSaving <= 0.0;

            // Push the rows into the COST tab grid (not just the popup).
            try { StingTools.UI.Sustainability.StingSustainabilityPanel.Instance?.ApplyLcc(displayRows); }
            catch (Exception ex) { StingLog.Warn($"Sustain LCC grid push: {ex.Message}"); }

            // WS A5 — feed the measures into the BOQ Cost Manager's manual store as
            // real BOQLineItem rows, so they land in the DD Cost/Budget Estimate
            // (not just a side CSV). Idempotent: prior Sustainability rows replaced.
            int boqWritten = WriteToBoqManualStore(doc, boqRows);

            // Keep the CSV as a portable artefact (back-compat).
            string csv = WriteLccCsv(doc, rows);

            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — Life-Cycle Cost Benefit")
                .SetSubtitle($"{AnalysisYears}-year analysis · {boqWritten} measure(s) written to the BOQ Cost Manager");
            b.AddSection("Per-measure LCC")
             .Table(new[] { "Measure", "Gate", "Sizing basis", "Capex", "Annual saving", "Lifetime saving", "Net benefit" }, displayRows);
            var totals = b.AddSection("Totals")
             .Metric("Total measure capex", $"{totalCapex:N0}")
             .Metric($"Total {AnalysisYears}-yr operational saving", $"{totalLifetimeSaving:N0}")
             .Metric("Net lifetime benefit", $"{totalLifetimeSaving - totalCapex:N0}")
             .Metric("Rows in BOQ Cost Manager", $"{boqWritten}");
            if (proxySized > 0)
                totals.Metric("Proxy-sized measures", $"{proxySized} (no model quantity — sized by proxy)");
            // WS I6 — health caveat on the headline when its inputs are proxies.
            var health = SustainLccHealth.Evaluate(ready, measuresOnNotComputedGate, proxySized, noSavings);
            if (health.HasCaveat)
                b.AddSection("⚠ LCC health").Info(health.Caveat);
            if (noSavings)
                b.AddSection("⚠ Operational savings not computed")
                 .Info("Every measure shows 0 operational saving, so 'Net benefit' is just minus the capex " +
                       "— that's a cost figure, not a loss. The Dashboard hasn't produced any energy/water " +
                       "savings yet: enter floor area (GFA) + occupancy in Setup (or click 'From model'), run " +
                       "the Dashboard, then re-run LCC for a true payback. Until then the BOQ rows written are " +
                       "capex-only.");
            if (csv != null) b.AddSection("Output").Metric("LCC CSV", Path.GetFileName(csv), csv);
            b.Show();

            StingLog.Info($"Sustain_LccBenefit: {rows.Count} measures, capex {totalCapex:0}, " +
                          $"lifetime saving {totalLifetimeSaving:0}, {boqWritten} BOQ rows, {proxySized} proxy-sized.");
            return Result.Succeeded;
        }

        /// <summary>Gather the real model quantities each measure can be sized
        /// against (PV kWp, glazing m², plumbing-fixture count, floor area,
        /// occupancy). Cooling kW is left 0 ⇒ the helper uses its documented
        /// ~80 W/m² proxy until a model cooling capacity is wired.</summary>
        private static MeasureQuantityContext BuildQuantityContext(
            Document doc, SustainProjectSetup setup, SustainabilityRunResult res)
        {
            double floor = setup.TotalFloorAreaM2 > 0 ? setup.TotalFloorAreaM2 : (res?.Energy?.FloorAreaM2 ?? 0);
            return new MeasureQuantityContext
            {
                PvKwp = setup.Supply?.PvKwp ?? 0,
                FloorAreaM2 = floor,
                Occupancy = setup.TotalOccupancy,
                GlazingAreaM2 = SumGlazingAreaM2(doc),
                FixtureCount = CountPlumbingFixtures(doc),
                CoolingKw = 0
            };
        }

        private static double SumGlazingAreaM2(Document doc)
        {
            double total = 0;
            try
            {
                var cats = new[] { BuiltInCategory.OST_Windows, BuiltInCategory.OST_CurtainWallPanels };
                var filter = new ElementMulticategoryFilter(cats);
                foreach (var el in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType())
                {
                    var p = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)
                            ?? el.LookupParameter("Area");
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                        total += UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.SquareMeters);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain SumGlazingArea: {ex.Message}"); }
            return total;
        }

        private static int CountPlumbingFixtures(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType().GetElementCount();
            }
            catch (Exception ex) { StingLog.Warn($"Sustain CountFixtures: {ex.Message}"); return 0; }
        }

        /// <summary>Build a BOQ manual row for one measure. Quantity × RateUGX gives
        /// the capex (measure rates are in the project's BOQ currency). LifecycleCost
        /// is the net whole-life position (capex − operational saving; negative ⇒ a
        /// net benefit over the analysis period).</summary>
        private static BOQLineItem BuildBoqRow(GreenMeasure m, MeasureCapexResult sizing, double lifetimeSaving)
        {
            return new BOQLineItem
            {
                ItemName = m.Name,
                Category = "Sustainability",
                Discipline = GateDiscipline(m.Gate),
                Quantity = sizing.Quantity,
                Unit = string.IsNullOrWhiteSpace(m.Cost?.Unit) ? "item" : m.Cost.Unit,
                RateUGX = m.Cost?.DefaultRate ?? 0,
                RateUSD = 0,
                LifecycleCostUGX = Math.Round(sizing.Capex - lifetimeSaving, 0),
                Source = BOQRowSource.Manual,
                RateSource = SustainRateSource,
                RateConfidence = 50,
                RevitElementId = -1,
                BOQLineRef = $"SUS-{m.Id}",
                Note = ($"{m.Description} [STING sustainability measure · gate {m.Gate} · " +
                        $"sized by {sizing.BasisLabel} · {AnalysisYears}-yr op. saving {lifetimeSaving:0}]").Trim()
            };
        }

        /// <summary>Map a measure gate to the BOQ discipline code for grouping.</summary>
        private static string GateDiscipline(string gate)
        {
            switch ((gate ?? "").Trim().ToLowerInvariant())
            {
                case "energy":    return "M";   // mechanical / electrical services
                case "water":     return "P";   // public health / plumbing
                case "materials": return "A";   // architectural / structural fabric
                default:          return "M";
            }
        }

        /// <summary>Merge the sustainability measures into the BOQ manual store
        /// (BOQCostManager.SaveManualRows) so BuildBOQDocument picks them up. Replaces
        /// any prior Sustainability-tagged rows (idempotent re-run) while preserving
        /// the user's own manual / provisional rows and the project budget.</summary>
        private static int WriteToBoqManualStore(Document doc, List<BOQLineItem> sustainRows)
        {
            try
            {
                var store = BOQCostManager.LoadManualStore(doc);
                var kept = (store.ManualRows ?? new List<BOQLineItem>())
                    .Where(r => !string.Equals(r.RateSource, SustainRateSource, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                kept.AddRange(sustainRows);
                BOQCostManager.SaveManualRows(doc, kept, store.ProjectBudgetUGX);
                return sustainRows.Count;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Sustain LCC -> BOQ manual store: {ex.Message}");
                return 0;
            }
        }

        private static double EstimateAnnualSaving(GreenMeasure m, SustainProjectSetup setup, SustainabilityRunResult res)
        {
            var s = m.Savings;
            if (s == null) return 0;
            double tariffE = setup.Supply?.EnergyTariffPerKwh ?? 0.15;
            double tariffW = setup.Supply?.WaterTariffPerM3 ?? 1.5;
            double designKwh = res.Energy?.Design?.TotalKwh ?? 0;
            double designWaterM3 = (res.Water?.AnnualDemandL ?? 0) / 1000.0;

            switch ((s.Kind ?? "").ToLowerInvariant())
            {
                case "coolingreductionpct":
                    return (res.Energy?.Design?.CoolingKwh ?? 0) * s.Value / 100.0 * tariffE;
                case "lightingreductionpct":
                    return (res.Energy?.Design?.LightingKwh ?? 0) * s.Value / 100.0 * tariffE;
                case "waterreductionpct":
                    return designWaterM3 * s.Value / 100.0 * tariffW;
                case "embodiedcarbonreductionpct":
                    return 0; // capital/carbon benefit, not operational saving
                case "energyoffsetkwhyr":
                    return (res.Energy?.PvGenerationKwh ?? 0) * tariffE;
                case "wateroffsetm3yr":
                    return (res.Water?.RwhYieldL ?? 0) / 1000.0 * tariffW;
                default:
                    return 0;
            }
        }

        private static string WriteLccCsv(Document doc, List<string[]> rows)
        {
            try
            {
                var lines = new List<string> { "Measure,Gate,SizingBasis,Capex,AnnualSaving,LifetimeSaving,NetBenefit" };
                lines.AddRange(rows.Select(r => string.Join(",", r.Select(c => "\"" + (c ?? "").Replace("\"", "\"\"") + "\""))));
                string path = OutputLocationHelper.GetOutputPath(doc,
                    $"STING_Sustain_LCC_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
                File.WriteAllLines(path, lines);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain LCC csv: {ex.Message}"); return null; }
        }
    }
}
