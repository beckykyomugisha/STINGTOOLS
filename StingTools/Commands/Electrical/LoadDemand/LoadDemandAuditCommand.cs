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
    /// bank for the total project demand. One Excel pack with three sheets.
    ///
    /// Units: Revit's circuit load is APPARENT power (RBS_ELEC_APPARENT_LOAD,
    /// VA), so every load figure here is kVA - it was labelled kW until
    /// 2026-09 (ROADMAP ELEC-20). The busbar capacity it is compared with is
    /// √3·V·I, also kVA. PFC converts to active power at the present PF.
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
            var circuitsByPanel = new Dictionary<string, List<(string load, double kva, double iA, double phaseCsa)>>();
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                .Where(s => { try { return s.SystemType == ElectricalSystemType.PowerCircuit; } catch { return true; } }))
            {
                try
                {
                    string panel = sys.PanelName ?? "(unassigned)";
                    double kva = StingTools.Core.Electrical.ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_APPARENT_LOAD) / 1000.0;
                    double iA = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0;
                    double csa = SafeDouble(sys, "ELC_FEEDER_CSA_MM2");
                    if (csa <= 0) csa = SafeDouble(sys, "ELC_CBL_SZ_MM");
                    string load = sys.LoadName ?? sys.Name ?? "";
                    if (!circuitsByPanel.TryGetValue(panel, out var list))
                        circuitsByPanel[panel] = list = new();
                    list.Add((load, kva, iA, csa));
                }
                catch (Exception ex) { StingLog.Warn($"LoadDemand circuit: {ex.Message}"); }
            }

            // 2. Per-panel rollup
            var panelRows = new List<PanelRow>();
            foreach (var (panelName, circuits) in circuitsByPanel)
            {
                var panel = FindPanelByName(doc, panelName);
                // Canonical MR_PARAMETERS names (Phase 188 fix):
                //   ELC_BUSBAR_RATING_A     (Phase 179, busbar trunking) — ✓
                //   ELC_PNL_MAIN_BRK_A      (panel rated breaker, replaces made-up ELC_PNL_RATING_A)
                //   ELC_PNL_VLT_V           (replaces ELC_PNL_VOLTAGE)
                //   ELC_CKT_PHASE_COUNT_NR  (existing, ✓)
                // ELC_PNL_SECTOR doesn't exist in MR_PARAMETERS — pull project sector
                // from ProjectInformation.OrganizationDescription as a heuristic fallback,
                // default to "Commercial".
                var assumed = new List<string>();
                double busbarA = panel != null ? SafeDouble(panel, "ELC_BUSBAR_RATING_A") : 0;
                if (busbarA <= 0 && panel != null) busbarA = SafeDouble(panel, "ELC_PNL_MAIN_BRK_A");
                if (busbarA <= 0) { busbarA = 200; assumed.Add("busbar 200 A ASSUMED"); }
                double voltageV = panel != null ? SafeDouble(panel, "ELC_PNL_VLT_V") : 0;
                if (voltageV <= 0) { voltageV = 400; assumed.Add("400 V ASSUMED"); }
                int phases = panel != null ? (int)SafeDouble(panel, "ELC_CKT_PHASE_COUNT_NR") : 0;
                if (phases == 0) { phases = 3; assumed.Add("3-phase ASSUMED"); }
                string sector = ResolveSector(doc);

                // ApplyDiversity is unit-agnostic; kVA in, kVA out (its *Kw field names predate this).
                var diversity = LoadDemandEngine.ApplyDiversity(circuits.Select(c => (c.load, c.kva)));
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
                    Diversity        = diversity,
                    Assumed          = string.Join("; ", assumed)
                });
            }

            // 3. Project-wide PFC sizing
            double totalDemand = panelRows.Sum(p => p.DemandKw);   // kVA
            var pfc = LoadDemandEngine.SizeCapacitorBankFromKva(totalDemand);

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
            sb.AppendLine($"Audited {panelRows.Count} panel(s) across {totalDemand:0.0} kVA total demand (after diversity).");
            int withAssumptions = panelRows.Count(p => !string.IsNullOrEmpty(p.Assumed));
            if (withAssumptions > 0)
                sb.AppendLine($"{withAssumptions} panel(s) used an assumed busbar rating / voltage / phase count — see the 'Assumed' column.");
            sb.AppendLine();
            sb.AppendLine($"Spare capacity: ✅ {green}  ⚠ {amber}  ❌ {red}");
            sb.AppendLine($"Panels needing oversized neutral (triplens > 33%): {oversized}");
            sb.AppendLine();
            if (pfc.Required)
            {
                sb.AppendLine($"PFC: install {pfc.CapacitorKvar:0} kVAR to lift PF {pfc.PresentPf:0.00} → {pfc.TargetPf:0.00} " +
                              $"on {pfc.ActiveKw:0.0} kW active (= {totalDemand:0.0} kVA × {pfc.PresentPf:0.00}).");
                if (!string.IsNullOrEmpty(pfc.Assumptions)) sb.AppendLine($"     {pfc.Assumptions}");
            }
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

        // ELC_PNL_SECTOR isn't in MR_PARAMETERS. Heuristic: read
        // ProjectInformation.OrganizationDescription / BuildingName /
        // ProjectName for sector hints (hospital→Healthcare, school→Education,
        // etc.); fall back to "Commercial". Engineer can override at panel
        // level once a formal sector parameter is added to MR_PARAMETERS.
        // Delegates. This used to be a second, private copy of the sector
        // rules - and it did NOT read PRJ_BUILDING_USE_TXT, so a project
        // that had declared its use was still guessed at from its name.
        // Two copies of a judgement are two answers waiting to differ.
        private static string ResolveSector(Document doc)
            => Core.Electrical.ProjectSector.Resolve(doc);

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
                if (p.StorageType == StorageType.Double)  return StingTools.Core.Electrical.ElecUnits.ToSi(p);
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
            ws.Range(1, 1, 1, 13).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 13).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            string[] hdr = { "Panel", "Sector", "Busbar (A)", "Voltage", "Phases",
                             "Connected (kVA)", "Demand (kVA)", "Diversity", "Spare (%)", "Verdict",
                             "Neutral CSA mult", "Dominant load", "Assumed" };
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
                ws.Cell(row, 13).Value = p.Assumed ?? "";
                var fill = p.SpareVerdict == "GREEN" ? XLColor.LightGreen
                         : p.SpareVerdict == "AMBER" ? XLColor.LightYellow : XLColor.LightSalmon;
                ws.Range(row, 1, row, 13).Style.Fill.BackgroundColor = fill;
                row++;
            }
            ws.Columns().AdjustToContents();

            // Sheet 2 — Diversity by category (project-wide)
            var ws2 = wb.Worksheets.Add("Diversity Matrix");
            ws2.Cell(1, 1).Value = "Diversity factor application by load category";
            ws2.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ws2.Cell(2, 1).Value = "Category"; ws2.Cell(2, 2).Value = "Connected (kVA)";
            ws2.Cell(2, 3).Value = "Demand (kVA)"; ws2.Cell(2, 4).Value = "Factor";
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
            Pf("Active power",      $"{pfc.ActiveKw:0.0} kW");
            Pf("Capacitor bank",    $"{pfc.CapacitorKvar:0} kVAR");
            Pf("Method",            "Q = P·(tan φ1 − tan φ2), φ = acos(PF)");
            Pf("Assumptions",       pfc.Assumptions);
            Pf("Annual saving",     $"£{pfc.AnnualSavingGbp:0} (indicative — placeholder kVArh rate, not a utility tariff)");
            Pf("Notes",             pfc.Notes);
            ws3.Columns().AdjustToContents();
            ws3.Column(2).Width = 50;

            wb.SaveAs(path);
        }

        private class PanelRow
        {
            public string PanelName, Sector, SpareVerdict, DominantCategory, Assumed;
            public double BusbarRatingA, VoltageV, ConnectedKw, DemandKw, BlendedFactor,
                          SparePct, NeutralFactor, NeutralCsa;
            public int Phases;
            public DiversitySummary Diversity;
        }
    }
}
