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
            // Corporate thresholds + <project>/_BIM_COORD/bs7671_disconnection.json
            // (ROADMAP ELEC-14) — the override is how a non-UK supply declares its Ze.
            string overridePath = StingPaths.MetaFile(doc, "_BIM_COORD",
                BS7671ComplianceEngine.ProjectOverrideFileName);
            var thresholds = BS7671ComplianceEngine.Thresholds(overridePath);

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
                    inp.Thresholds = thresholds;
                    var r = BS7671ComplianceEngine.AuditCircuit(inp);
                    if (r != null) results.Add(r);
                }
                catch (Exception ex) { StingLog.Warn($"BS7671 circuit audit: {ex.Message}"); }
            }

            StingElectricalCommandHandler.LastBs7671Results = results;

            int pass = results.Count(r => r.Verdict == "PASS");
            int viaRcd = results.Count(r => r.Verdict == "PASS_VIA_RCD");
            int fail = results.Count(r => r.Verdict == "FAIL");
            int unverified = results.Count(r => r.Verdict == "UNVERIFIED");
            int withAssumptions = results.Count(r => r.Assumptions != null && r.Assumptions.Count > 0);

            string excel = WriteExcelReport(doc, results, earthing);

            var sb = new StringBuilder();
            sb.AppendLine($"Audited {results.Count} power circuit(s) on {earthing} earthing.");
            double zeShown = thresholds.Ze.TryGetValue(earthing, out double zeV) ? zeV : double.NaN;
            string zeFrom = thresholds.ZeSource.TryGetValue(earthing, out var zs) ? zs : "not declared";
            sb.AppendLine(double.IsNaN(zeShown)
                ? $"Ze: {earthing} not in the thresholds file — 0.8 Ω ASSUMED."
                : $"Ze = {zeShown:0.00} Ω ({zeFrom}{(zeFrom == "project" ? "" : " — UK DNO maximum; declare the local supply's Ze in _BIM_COORD/" + BS7671ComplianceEngine.ProjectOverrideFileName)}), " +
                  $"Cmin = {thresholds.Cmin:0.00}, U0 = {thresholds.NominalUo:0} V.");
            foreach (var w in thresholds.Warnings) sb.AppendLine($"⚠ {w}");
            sb.AppendLine();
            sb.AppendLine($"✅ PASS         : {pass}");
            sb.AppendLine($"⚠ PASS_VIA_RCD : {viaRcd}  (Zs fails ADS but RCD makes it compliant per §411.4.5)");
            sb.AppendLine($"❔ UNVERIFIED   : {unverified}  (Zs passes, but the adiabatic check needs a clearing time: no IEC 60898 band for this device, or the fault current is below its trip range)");
            if (withAssumptions > 0) sb.AppendLine($"ℹ {withAssumptions} circuit(s) used ASSUMED inputs (see the Assumed inputs column) — verdicts on those rest on the assumptions.");
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
            // Every input the model does not hold is defaulted AND recorded: the
            // verdict rests on it, so the report must say so.
            var assumed = new List<string>();

            double phaseCsa = SafeDouble(sys, "ELC_FEEDER_CSA_MM2");
            if (phaseCsa <= 0) phaseCsa = SafeDouble(sys, "ELC_CBL_SZ_MM");
            if (phaseCsa <= 0) { phaseCsa = 2.5; assumed.Add("phase CSA 2.5 mm²"); }
            double cpcCsa   = SafeDouble(sys, "ELC_CPC_SZ_MM");
            if (cpcCsa <= 0)
            {
                // BS 6004 twin & earth carries a reduced CPC; assuming CPC = phase
                // understated R2 and could turn a failing Zs into a pass.
                cpcCsa = ReducedCpcMm2(phaseCsa);
                assumed.Add($"CPC {cpcCsa:0.#} mm² (BS 6004 reduced CPC for {phaseCsa:0.#} mm²)");
            }

            double lenM = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM)?.AsDouble() ?? 0;
            // Revit length is in feet → m
            if (lenM > 0) lenM *= 0.3048;
            else { lenM = 30.0; assumed.Add("length 30 m (no circuit path drawn)"); }

            // Current and rating are Current-spec parameters: Revit stores amperes as-is.
            double iA = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0;
            double rating = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM)?.AsDouble() ?? 0;
            if (rating <= 0 && iA > 1) { rating = Math.Ceiling(iA); assumed.Add($"rating {rating:0} A (from design current)"); }
            if (rating <= 1) { rating = 16; assumed.Add("rating 16 A"); }

            // ELC_BREAKER_TYPE / ELC_CBL_MATERIAL aren't in MR_PARAMETERS yet
            // — use the defaults pending future schema additions. Insulation
            // is canonical: ELC_CBL_INS_TYPE_TXT (Phase 188 fix).
            string ocpd = "MCB_C";  // BS EN 60898 Type C is the safe default
            assumed.Add("OCPD Type C MCB");
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
                RatingA         = rating,
                LengthM         = lenM,
                PhaseCsaMm2     = phaseCsa,
                Assumptions     = assumed,
                CpcCsaMm2       = cpcCsa,
                Material        = mat,
                Insulation      = ins,
                Context         = context,
                WireTables      = wireTables
            };
        }

        /// <summary>
        /// CPC of BS 6004 flat twin &amp; earth for a given line conductor (1.0/1.5→1.0,
        /// 2.5→1.5, 4→1.5, 6→2.5, 10→4, 16→6); larger sizes assume a CPC equal to
        /// the line conductor, as for multicore cables with a full-size core.
        /// </summary>
        private static double ReducedCpcMm2(double phaseCsa)
        {
            if (phaseCsa <= 1.5) return 1.0;
            if (phaseCsa <= 2.5) return 1.5;
            if (phaseCsa <= 4)   return 1.5;
            if (phaseCsa <= 6)   return 2.5;
            if (phaseCsa <= 10)  return 4;
            if (phaseCsa <= 16)  return 6;
            return phaseCsa;
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
                    "RCD (mA)", "Verdict", "Assumed inputs"
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
                    ws.Cell(row, 4).Value = r.OcpdType ?? "";
                    ws.Cell(row, 5).Value = r.RatingA;
                    ws.Cell(row, 6).Value = r.ZsActualOhm;
                    ws.Cell(row, 7).Value = r.ZsMaxOhm;
                    ws.Cell(row, 8).Value = r.ZsMarginPct;
                    ws.Cell(row, 9).Value = r.ProspectivePscA / 1000.0;
                    if (double.IsNaN(r.ClearingTimeMs)) ws.Cell(row, 10).Value = "no band";
                    else ws.Cell(row, 10).Value = r.ClearingTimeMs;
                    ws.Cell(row, 11).Value = double.IsNaN(r.ClearingTimeMs) ? "NOT CHECKED"
                        : r.AdiabaticPasses ? "PASS" : $"FAIL — need ≥{r.AdiabaticMinCsa} mm²";
                    ws.Cell(row, 12).Value = r.RcdRequiredMA == 0 ? "—" : r.RcdRequiredMA.ToString();
                    ws.Cell(row, 13).Value = r.Verdict;
                    ws.Cell(row, 14).Value = r.Assumptions == null || r.Assumptions.Count == 0
                        ? "—" : string.Join("; ", r.Assumptions);

                    var fillColor = r.Verdict == "PASS"        ? XLColor.LightGreen
                                  : r.Verdict == "PASS_VIA_RCD"? XLColor.LightYellow
                                  : r.Verdict == "UNVERIFIED"  ? XLColor.LightGray
                                  :                              XLColor.LightSalmon;
                    ws.Range(row, 1, row, 14).Style.Fill.BackgroundColor = fillColor;
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
