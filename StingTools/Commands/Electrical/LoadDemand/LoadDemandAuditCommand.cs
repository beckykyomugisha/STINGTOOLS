using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Commands.Electrical.LoadDemand
{
    /// <summary>
    /// Walks every panel + power circuit, applies the diversity matrix,
    /// reports per-panel spare capacity, recommends neutral sizing for
    /// each panel based on its harmonic mix, and sizes a PFC capacitor
    /// bank for the total project demand. One Excel pack with five sheets.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadDemandAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // 1. Collect circuits per panel
            var circuitsByPanel = new Dictionary<string, List<(string load, double kw, double iA, double phaseCsa)>>();
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                .Where(s => { try { return s.SystemType == ElectricalSystemType.PowerCircuit; } catch { return true; } }))
            {
                try
                {
                    string panel = sys.PanelName ?? "(unassigned)";
                    double kw = (sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0) / 1000.0;
                    double iA = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0;
                    double csa = SafeDouble(sys, "ELC_FEEDER_CSA_MM2");
                    if (csa <= 0) csa = SafeDouble(sys, "ELC_CBL_SZ_MM");
                    string load = sys.LoadName ?? sys.Name ?? "";
                    if (!circuitsByPanel.TryGetValue(panel, out var list))
                        circuitsByPanel[panel] = list = new();
                    list.Add((load, kw, iA, csa));
                }
                catch (Exception ex) { StingLog.Warn($"LoadDemand circuit: {ex.Message}"); }
            }

            // 2. Per-panel rollup
            var panelRows = new List<PanelRow>();
            foreach (var (panelName, circuits) in circuitsByPanel)
            {
                var panel = FindPanelByName(doc, panelName);
                double busbarA = panel != null ? SafeDouble(panel, "ELC_BUSBAR_RATING_A") : 0;
                if (busbarA <= 0 && panel != null) busbarA = SafeDouble(panel, "ELC_PNL_RATING_A");
                if (busbarA <= 0) busbarA = 200; // sensible default for sub-DB
                double voltageV = panel != null ? SafeDouble(panel, "ELC_PNL_VOLTAGE") : 0;
                if (voltageV <= 0) voltageV = 400; // 400 V 3φ
                int phases = panel != null ? (int)SafeDouble(panel, "ELC_CKT_PHASE_COUNT_NR") : 3;
                if (phases == 0) phases = 3;
                string sector = panel != null ? (panel.LookupParameter("ELC_PNL_SECTOR")?.AsString() ?? "Commercial") : "Commercial";

                var diversity = LoadDemandEngine.ApplyDiversity(circuits.Select(c => (c.load, c.kw)));
                var spare = LoadDemandEngine.AssessSpareCapacity(diversity.TotalDemandKw, busbarA, voltageV, phases, sector);

                // Dominant load category for harmonic analysis
                var dominant = diversity.ByCategory.FirstOrDefault();
                string dominantCat = dominant?.Category ?? "General";
                double maxPhaseI = circuits.Max(c => c.iA);
                double maxPhaseCsa = circuits.Max(c => c.phaseCsa);
                var neutral = LoadDemandEngine.AssessNeutral(dominantCat, maxPhaseCsa, maxPhaseI);

                panelRows.Add(new PanelRow
                {
                    PanelName        = panelName,
                    Sector           = sector,
                    BusbarRatingA    = busbarA,
                    VoltageV         = voltageV,
                    Phases           = phases,
                    ConnectedKw      = diversity.TotalConnectedKw,
                    DemandKw         = diversity.TotalDemandKw,
                    BlendedFactor    = diversity.BlendedFactor,
                    SparePct         = spare.SparePct,
                    SpareVerdict     = spare.Verdict,
                    DominantCategory = dominantCat,
                    NeutralFactor    = neutral.NeutralFactor,
                    NeutralCsa       = neutral.RecommendedNeutralCsa,
                    Diversity        = diversity
                });
            }

            // 3. Project-wide PFC sizing
            double totalDemand = panelRows.Sum(p => p.DemandKw);
            var pfc = LoadDemandEngine.SizeCapacitorBank(totalDemand);

            // 4. Excel writer
            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"STING_LoadDemand_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
            WriteExcel(outPath, panelRows, pfc);

            // 5. Headline
            int red    = panelRows.Count(p => p.SpareVerdict == "RED");
            int amber  = panelRows.Count(p => p.SpareVerdict == "AMBER");
            int green  = panelRows.Count(p => p.SpareVerdict == "GREEN");
            int oversized = panelRows.Count(p => p.NeutralFactor > 1.05);

            var sb = new StringBuilder();
            sb.AppendLine($"Audited {panelRows.Count} panel(s) across {totalDemand:0.0} kW total demand.");
            sb.AppendLine();
            sb.AppendLine($"Spare capacity: ✅ {green}  ⚠ {amber}  ❌ {red}");
            sb.AppendLine($"Panels needing oversized neutral (triplens > 33%): {oversized}");
            sb.AppendLine();
            if (pfc.Required)
                sb.AppendLine($"PFC: install {pfc.CapacitorKvar:0} kVAR to lift PF {pfc.PresentPf:0.00} → {pfc.TargetPf:0.00} " +
                              $"(estimated annual saving £{pfc.AnnualSavingGbp:0})");
            else
                sb.AppendLine($"PFC: {pfc.Notes}");
            sb.AppendLine();
            sb.AppendLine($"Excel: {outPath}");
            TaskDialog.Show("STING Load + Demand Audit", sb.ToString());
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch { }
            return Result.Succeeded;
        }

        private static FamilyInstance FindPanelByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().OfType<FamilyInstance>()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static double SafeDouble(Element el, string name)
        {
            var p = el?.LookupParameter(name);
            if (p == null) return 0;
            try
            {
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out double v)) return v;
            }
            catch { }
            return 0;
        }

        private static void WriteExcel(string path, List<PanelRow> panels, PfcResult pfc)
        {
            using var wb = new XLWorkbook();

            // Sheet 1 — Per-panel summary
            var ws = wb.Worksheets.Add("Panels");
            ws.Cell(1, 1).Value = $"STING Load + Demand Audit  ·  {panels.Count} panels  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
            ws.Range(1, 1, 1, 12).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 12).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            string[] hdr = { "Panel", "Sector", "Busbar (A)", "Voltage", "Phases",
                             "Connected (kW)", "Demand (kW)", "Diversity", "Spare (%)", "Verdict",
                             "Neutral CSA mult", "Dominant load" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(2, i + 1).Value = hdr[i];
                ws.Cell(2, i + 1).Style.Font.Bold = true;
                ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 3;
            foreach (var p in panels.OrderBy(x => x.PanelName))
            {
                ws.Cell(row, 1).Value = p.PanelName;
                ws.Cell(row, 2).Value = p.Sector;
                ws.Cell(row, 3).Value = p.BusbarRatingA;
                ws.Cell(row, 4).Value = p.VoltageV;
                ws.Cell(row, 5).Value = p.Phases;
                ws.Cell(row, 6).Value = p.ConnectedKw;
                ws.Cell(row, 7).Value = p.DemandKw;
                ws.Cell(row, 8).Value = p.BlendedFactor;
                ws.Cell(row, 9).Value = p.SparePct;
                ws.Cell(row, 10).Value = p.SpareVerdict;
                ws.Cell(row, 11).Value = p.NeutralFactor;
                ws.Cell(row, 12).Value = p.DominantCategory;
                var fill = p.SpareVerdict == "GREEN" ? XLColor.LightGreen
                         : p.SpareVerdict == "AMBER" ? XLColor.LightYellow : XLColor.LightSalmon;
                ws.Range(row, 1, row, 12).Style.Fill.BackgroundColor = fill;
                row++;
            }
            ws.Columns().AdjustToContents();

            // Sheet 2 — Diversity by category (project-wide)
            var ws2 = wb.Worksheets.Add("Diversity Matrix");
            ws2.Cell(1, 1).Value = "Diversity factor application by load category";
            ws2.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ws2.Cell(2, 1).Value = "Category"; ws2.Cell(2, 2).Value = "Connected (kW)";
            ws2.Cell(2, 3).Value = "Demand (kW)"; ws2.Cell(2, 4).Value = "Factor";
            ws2.Range(2, 1, 2, 4).Style.Font.Bold = true;
            ws2.Range(2, 1, 2, 4).Style.Fill.BackgroundColor = XLColor.LightGray;
            int r2 = 3;
            var allCats = panels.SelectMany(p => p.Diversity.ByCategory)
                .GroupBy(c => c.Category)
                .Select(g => new
                {
                    Category   = g.Key,
                    Connected  = g.Sum(c => c.ConnectedKw),
                    Demand     = g.Sum(c => c.DemandKw),
                    Factor     = g.First().Factor
                })
                .OrderByDescending(c => c.Demand);
            foreach (var c in allCats)
            {
                ws2.Cell(r2, 1).Value = c.Category;
                ws2.Cell(r2, 2).Value = c.Connected;
                ws2.Cell(r2, 3).Value = c.Demand;
                ws2.Cell(r2, 4).Value = c.Factor;
                r2++;
            }
            ws2.Columns().AdjustToContents();

            // Sheet 3 — PFC sizing
            var ws3 = wb.Worksheets.Add("PFC Sizing");
            ws3.Cell(1, 1).Value = "Power Factor Correction Sizing";
            ws3.Range(1, 1, 1, 2).Merge().Style.Font.Bold = true;
            ws3.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            int r3 = 3;
            void Pf(string l, object v) { ws3.Cell(r3, 1).Value = l; ws3.Cell(r3, 1).Style.Font.Bold = true; ws3.Cell(r3, 2).Value = v?.ToString() ?? ""; r3++; }
            Pf("Required",          pfc.Required ? "Yes" : "No");
            Pf("Present PF",        pfc.PresentPf);
            Pf("Target PF",         pfc.TargetPf);
            Pf("Capacitor bank",    $"{pfc.CapacitorKvar:0} kVAR");
            Pf("Annual saving",     $"£{pfc.AnnualSavingGbp:0}");
            Pf("Notes",             pfc.Notes);
            ws3.Columns().AdjustToContents();
            ws3.Column(2).Width = 50;

            wb.SaveAs(path);
        }

        private class PanelRow
        {
            public string PanelName, Sector, SpareVerdict, DominantCategory;
            public double BusbarRatingA, VoltageV, ConnectedKw, DemandKw, BlendedFactor,
                          SparePct, NeutralFactor, NeutralCsa;
            public int Phases;
            public DiversitySummary Diversity;
        }
    }
}
