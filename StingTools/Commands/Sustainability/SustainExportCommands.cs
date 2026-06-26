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
                    BuildProjectSheet(wb, setup, res);
                    BuildEnergySheet(wb, res);
                    BuildWaterSheet(wb, setup, res);
                    BuildMaterialsSheet(wb, doc, res);

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

        private static void BuildProjectSheet(XLWorkbook wb, SustainProjectSetup setup, SustainabilityRunResult res)
        {
            var ws = wb.Worksheets.Add("Project");
            int r = 1;
            ws.Cell(r, 1).Value = "STING EDGE Export — indicative inputs for the EDGE app"; ws.Cell(r, 1).Style.Font.Bold = true; r += 2;
            void Row(string k, string v) { ws.Cell(r, 1).Value = k; ws.Cell(r, 2).Value = v; r++; }
            Row("Country", setup.Country);
            Row("Climate zone", setup.ClimateZone);
            Row("Dominant building use", setup.DominantBuildingUse);
            Row("Floor area (m²)", setup.TotalFloorAreaM2.ToString("0"));
            Row("Occupancy", setup.TotalOccupancy.ToString());
            Row("Supply mode", setup.Supply?.Mode ?? "");
            Row("PV (kWp)", (setup.Supply?.PvKwp ?? 0).ToString("0"));
            Row("Baseline provenance", res.Baseline?.Provenance ?? "");
            Row("Baseline resolution", res.Baseline?.Summary ?? "");
            ws.Columns().AdjustToContents();
        }

        private static void BuildEnergySheet(XLWorkbook wb, SustainabilityRunResult res)
        {
            var ws = wb.Worksheets.Add("Energy");
            ws.Cell(1, 1).Value = "End use"; ws.Cell(1, 2).Value = "Annual kWh";
            ws.Row(1).Style.Font.Bold = true;
            var e = res.Energy?.Design;
            int r = 2;
            void Row(string k, double v) { ws.Cell(r, 1).Value = k; ws.Cell(r, 2).Value = v; r++; }
            if (e != null)
            {
                Row("Cooling", e.CoolingKwh);
                Row("Heating", e.HeatingKwh);
                Row("Fans/pumps", e.FansKwh);
                Row("Lighting", e.LightingKwh);
                Row("Equipment", e.EquipmentKwh);
                Row("DHW", e.DhwKwh);
                Row("TOTAL", e.TotalKwh);
            }
            r++;
            Row("Design EUI (kWh/m²·yr) — indicative", res.Energy?.DesignEuiKwhM2Yr ?? 0);
            Row("Baseline EUI (kWh/m²·yr)", res.Energy?.BaselineEuiKwhM2Yr ?? 0);
            Row("Energy savings % — indicative", res.Energy?.EnergySavingsPct ?? 0);
            Row("PV generation (kWh/yr)", res.Energy?.PvGenerationKwh ?? 0);
            Row("Net import (kWh/yr)", res.Energy?.NetImportKwh ?? 0);
            ws.Columns().AdjustToContents();
        }

        private static void BuildWaterSheet(XLWorkbook wb, SustainProjectSetup setup, SustainabilityRunResult res)
        {
            var ws = wb.Worksheets.Add("Water");
            int r = 1;
            void Row(string k, string v) { ws.Cell(r, 1).Value = k; ws.Cell(r, 2).Value = v; r++; }
            Row("Design L/person·day — indicative", (res.Water?.DesignLPersonDay ?? 0).ToString("0.0"));
            Row("Baseline L/person·day", (res.Water?.BaselineLPersonDay ?? 0).ToString("0.0"));
            Row("Water savings % — indicative", (res.Water?.WaterSavingsPct ?? 0).ToString("0.0"));
            Row("Annual demand (L)", (res.Water?.AnnualDemandL ?? 0).ToString("0"));
            Row("RWH yield (L/yr)", (res.Water?.RwhYieldL ?? 0).ToString("0"));
            Row("Net demand (L)", (res.Water?.NetDemandL ?? 0).ToString("0"));
            ws.Columns().AdjustToContents();
        }

        private static void BuildMaterialsSheet(XLWorkbook wb, Document doc, SustainabilityRunResult res)
        {
            var ws = wb.Worksheets.Add("Materials");
            ws.Cell(1, 1).Value = "Material";
            ws.Cell(1, 2).Value = "kgCO2e/m² (indicative)";
            ws.Cell(1, 3).Value = "MJ/m² (indicative)";
            ws.Row(1).Style.Font.Bold = true;
            int r = 2;
            ws.Cell(r, 1).Value = "BUILDING TOTAL";
            ws.Cell(r, 2).Value = res.Materials?.CarbonIntensityKgM2 ?? 0;
            ws.Cell(r, 3).Value = res.Materials?.EnergyIntensityMjM2 ?? 0;
            r += 2;
            ws.Cell(r, 1).Value = "Carbon hotspots (A1-A3 GWP)"; ws.Cell(r, 1).Style.Font.Bold = true; r++;
            if (res.Materials?.Hotspots != null)
                foreach (var h in res.Materials.Hotspots)
                {
                    ws.Cell(r, 1).Value = h.Material;
                    ws.Cell(r, 2).Value = h.CarbonKg;
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

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            var res = SustainabilityEngine.Run(doc, setup);
            var measures = SustainabilityRegistries.Measures(doc);

            var rows = new List<string[]>();
            double totalCapex = 0, totalLifetimeSaving = 0;

            foreach (var m in measures.All)
            {
                double capex = EstimateCapex(m, setup);
                double annualSaving = EstimateAnnualSaving(m, setup, res);
                double lifetimeSaving = annualSaving * AnalysisYears;
                double netBenefit = lifetimeSaving - capex;
                totalCapex += capex;
                totalLifetimeSaving += lifetimeSaving;
                rows.Add(new[]
                {
                    m.Name, m.Gate, m.Cost.Key,
                    $"{capex:0}", $"{annualSaving:0}/yr",
                    $"{lifetimeSaving:0}", $"{netBenefit:0}"
                });
            }

            // Push the rows into the COST tab grid (not just the popup).
            try { StingTools.UI.Sustainability.StingSustainabilityPanel.Instance?.ApplyLcc(rows); }
            catch (Exception ex) { StingLog.Warn($"Sustain LCC grid push: {ex.Message}"); }

            // Persist a CSV the BOQ Cost Manager picks up alongside the model so
            // the measure costs land in the DD Cost/Budget Estimate.
            string csv = WriteLccCsv(doc, rows);

            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — Life-Cycle Cost Benefit")
                .SetSubtitle($"{AnalysisYears}-year analysis · feeds the BOQ Cost Manager / DD Cost Estimate");
            b.AddSection("Per-measure LCC")
             .Table(new[] { "Measure", "Gate", "Cost key", "Capex", "Annual saving", "Lifetime saving", "Net benefit" }, rows);
            b.AddSection("Totals")
             .Metric("Total measure capex", $"{totalCapex:0}")
             .Metric($"Total {AnalysisYears}-yr operational saving", $"{totalLifetimeSaving:0}")
             .Metric("Net lifetime benefit", $"{totalLifetimeSaving - totalCapex:0}");
            if (csv != null) b.AddSection("Output").Metric("LCC CSV", Path.GetFileName(csv), csv);
            b.Show();

            StingLog.Info($"Sustain_LccBenefit: {rows.Count} measures, capex {totalCapex:0}, lifetime saving {totalLifetimeSaving:0}.");
            return Result.Succeeded;
        }

        private static double EstimateCapex(GreenMeasure m, SustainProjectSetup setup)
        {
            // capex = defaultRate x a sizing quantity derived from the measure unit.
            double rate = m.Cost?.DefaultRate ?? 0;
            switch ((m.Cost?.Unit ?? "").ToLowerInvariant())
            {
                case "kwp":  return rate * (setup.Supply?.PvKwp ?? 0);
                case "m2":   return rate * setup.TotalFloorAreaM2;
                case "m3":   return rate * Math.Max(1, setup.TotalFloorAreaM2 / 100.0); // crude m3 proxy
                case "nr":   return rate * Math.Max(1, setup.TotalOccupancy / 4.0);     // ~1 fixture / 4 ppl
                case "kw":   return rate * Math.Max(1, setup.TotalFloorAreaM2 * 0.08);  // ~80 W/m² cooling
                default:     return rate;
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
                var lines = new List<string> { "Measure,Gate,CostKey,Capex,AnnualSaving,LifetimeSaving,NetBenefit" };
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
