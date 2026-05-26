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
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Compliance
{
    /// <summary>
    /// One-shot BS 7671 compliance audit across every power circuit in the
    /// project. For each circuit computes:
    ///
    /// <list type="bullet">
    /// <item>Earth fault loop impedance Zs and the Table 41.1 disconnection-time check</item>
    /// <item>Adiabatic conductor verification (k·S)² ≥ I²·t per §434.5.2</item>
    /// <item>RCD/RCBO sensitivity recommendation per §411.3.3 / 411.3.4 / 522.6.202</item>
    /// </list>
    ///
    /// Output: red/amber/green Excel pack at
    /// <c>&lt;output&gt;/electrical/STING_BS7671_Compliance_YYYYMMDD-HHmm.xlsx</c>
    /// + cached results on
    /// <see cref="StingElectricalCommandHandler.LastBs7671Results"/> for the
    /// dock-panel grid.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BS7671AuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string earthing = StingElectricalCommandHandler.CurrentEarthingSystem ?? "TN-C-S";
            var wireTables = WireTableSet.Load(null);

            var systems = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                .Where(s => { try { return s.SystemType == ElectricalSystemType.PowerCircuit; } catch { return true; } })
                .ToList();

            if (systems.Count == 0)
            {
                TaskDialog.Show("STING BS 7671 Audit", "No power circuits found.");
                return Result.Cancelled;
            }

            var results = new List<CircuitAuditResult>();
            foreach (var sys in systems)
            {
                try
                {
                    var inp = BuildInput(doc, sys, earthing, wireTables);
                    if (inp == null) continue;
                    var r = BS7671ComplianceEngine.AuditCircuit(inp);
                    if (r != null) results.Add(r);
                }
                catch (Exception ex) { StingLog.Warn($"BS7671 circuit audit: {ex.Message}"); }
            }

            StingElectricalCommandHandler.LastBs7671Results = results;

            int pass = results.Count(r => r.Verdict == "PASS");
            int viaRcd = results.Count(r => r.Verdict == "PASS_VIA_RCD");
            int fail = results.Count(r => r.Verdict == "FAIL");

            string excel = WriteExcelReport(doc, results, earthing);

            var sb = new StringBuilder();
            sb.AppendLine($"Audited {results.Count} power circuit(s) on {earthing} earthing.");
            sb.AppendLine();
            sb.AppendLine($"✅ PASS         : {pass}");
            sb.AppendLine($"⚠ PASS_VIA_RCD : {viaRcd}  (Zs fails ADS but RCD makes it compliant per §411.4.5)");
            sb.AppendLine($"❌ FAIL         : {fail}  (review CPC sizing, OCPD type, or apply RCD)");

            var topFails = results.Where(r => r.Verdict == "FAIL").Take(3).ToList();
            if (topFails.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("WORST OFFENDERS:");
                foreach (var r in topFails)
                    sb.AppendLine($"  • {r.PanelName}/{r.CircuitTag}: Zs={r.ZsActualOhm:0.000} Ω vs Zs_max={r.ZsMaxOhm:0.000} Ω, " +
                                  $"min CSA={r.AdiabaticMinCsa:0.0} mm²");
            }
            if (!string.IsNullOrEmpty(excel))
                sb.AppendLine($"\nExcel report: {excel}");
            TaskDialog.Show("STING BS 7671 Compliance", sb.ToString());

            return Result.Succeeded;
        }

        private static CircuitAuditInput BuildInput(Document doc, ElectricalSystem sys,
            string earthing, WireTableSet wireTables)
        {
            double phaseCsa = SafeDouble(sys, "ELC_FEEDER_CSA_MM2");
            if (phaseCsa <= 0) phaseCsa = SafeDouble(sys, "ELC_CBL_SZ_MM");
            double cpcCsa   = SafeDouble(sys, "ELC_CPC_SZ_MM");
            if (cpcCsa <= 0) cpcCsa = phaseCsa;

            double lenM = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM)?.AsDouble() ?? 0;
            // Revit length is in feet → m
            lenM = lenM > 0 ? lenM * 0.3048 : 30.0;  // 30 m fallback

            double iA = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0;
            double rating = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM)?.AsDouble() ?? 0;
            // Revit stores ratings in internal Amps already
            if (rating <= 0) rating = Math.Ceiling(iA);

            // ELC_BREAKER_TYPE / ELC_CBL_MATERIAL aren't in MR_PARAMETERS yet
            // — use the defaults pending future schema additions. Insulation
            // is canonical: ELC_CBL_INS_TYPE_TXT (Phase 188 fix).
            string ocpd = "MCB_C";  // BS EN 60898 Type C is the safe default
            string mat = "Cu";       // copper unless project specifies aluminium
            string ins = sys.LookupParameter("ELC_CBL_INS_TYPE_TXT")?.AsString() ?? "PVC";

            string load = (sys.LoadName ?? sys.Name ?? "").ToLowerInvariant();
            string context = load;  // future: pull room category, plus circuit description

            return new CircuitAuditInput
            {
                CircuitTag      = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? sys.Id.ToString(),
                PanelName       = sys.PanelName ?? "",
                LoadName        = sys.LoadName ?? sys.Name ?? "",
                EarthingSystem  = earthing,
                OcpdType        = ocpd,
                RatingA         = rating > 1 ? rating : 16,
                LengthM         = lenM,
                PhaseCsaMm2     = phaseCsa > 0 ? phaseCsa : 2.5,
                CpcCsaMm2       = cpcCsa,
                Material        = mat,
                Insulation      = ins,
                Context         = context,
                WireTables      = wireTables
            };
        }

        private static double SafeDouble(Element el, string name)
        {
            var p = el?.LookupParameter(name);
            if (p == null) return 0;
            try
            {
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out double v)) return v;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch { }
            return 0;
        }

        private static string WriteExcelReport(Document doc, List<CircuitAuditResult> rows, string earthing)
        {
            try
            {
                string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir,
                    $"STING_BS7671_Compliance_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("BS 7671 Audit");

                ws.Cell(1, 1).Value = $"BS 7671:2018 + A2:2022 Compliance Audit  ·  Earthing: {earthing}  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
                ws.Range(1, 1, 1, 13).Merge().Style.Font.Bold = true;
                ws.Range(1, 1, 1, 13).Style.Fill.BackgroundColor = XLColor.LightGray;

                string[] headers = {
                    "Panel", "Circuit", "Load", "OCPD", "Rating (A)",
                    "Zs actual (Ω)", "Zs max (Ω)", "Margin (%)",
                    "PSC (kA)", "Clearing (ms)", "k·S (Adiabatic)",
                    "RCD (mA)", "Verdict"
                };
                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cell(2, i + 1).Value = headers[i];
                    ws.Cell(2, i + 1).Style.Font.Bold = true;
                    ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                }

                int row = 3;
                foreach (var r in rows.OrderByDescending(x => x.Verdict))
                {
                    ws.Cell(row, 1).Value = r.PanelName;
                    ws.Cell(row, 2).Value = r.CircuitTag;
                    ws.Cell(row, 3).Value = r.LoadName;
                    ws.Cell(row, 4).Value = "—";  // OCPD type not on result; future
                    ws.Cell(row, 5).Value = "—";
                    ws.Cell(row, 6).Value = r.ZsActualOhm;
                    ws.Cell(row, 7).Value = r.ZsMaxOhm;
                    ws.Cell(row, 8).Value = r.ZsMarginPct;
                    ws.Cell(row, 9).Value = r.ProspectivePscA / 1000.0;
                    ws.Cell(row, 10).Value = r.ClearingTimeMs;
                    ws.Cell(row, 11).Value = r.AdiabaticPasses ? "PASS" : $"FAIL — need ≥{r.AdiabaticMinCsa} mm²";
                    ws.Cell(row, 12).Value = r.RcdRequiredMA == 0 ? "—" : r.RcdRequiredMA.ToString();
                    ws.Cell(row, 13).Value = r.Verdict;

                    var fillColor = r.Verdict == "PASS"        ? XLColor.LightGreen
                                  : r.Verdict == "PASS_VIA_RCD"? XLColor.LightYellow
                                  :                              XLColor.LightSalmon;
                    ws.Range(row, 1, row, 13).Style.Fill.BackgroundColor = fillColor;
                    row++;
                }
                ws.Columns().AdjustToContents();
                wb.SaveAs(outPath);
                return outPath;
            }
            catch (Exception ex)
            {
                StingLog.Error($"BS7671 Excel: {ex.Message}", ex);
                return "";
            }
        }
    }
}
