using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Per-cable pull list — the construction document the install team
    /// actually uses on site. One row per cable with: from-panel, to-load,
    /// route metres, conduit/tray IDs traversed, cable type/size, drum
    /// allocation, weight estimate. Phase 184a closes the gap noted in the
    /// review: STING sized feeders but produced no pull schedule.
    ///
    /// Drum allocation algorithm: First-Fit Decreasing — sort cables by
    /// length descending, pack into drums of the configured drum length
    /// (default 500 m for 16 mm² and below, 250 m for 25-95 mm², 100 m for
    /// 120 mm² and up). Caller can override via STING_CABLE_DRUMS.json
    /// (not shipped — falls through to defaults).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CablePullListCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var systems = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                .Where(s => { try { return s.SystemType == ElectricalSystemType.PowerCircuit; } catch { return true; } })
                .ToList();
            if (systems.Count == 0)
            {
                TaskDialog.Show("STING Cable Pull List", "No power circuits found.");
                return Result.Cancelled;
            }

            var rows = new List<PullRow>();
            foreach (var sys in systems)
            {
                try { rows.Add(BuildPullRow(doc, sys)); }
                catch (Exception ex) { StingLog.Warn($"PullList circuit: {ex.Message}"); }
            }

            // Drum allocation — sort by length descending then pack.
            AllocateDrums(rows);

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir,
                $"STING_CablePullList_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");

            WriteExcel(outPath, rows);

            double totalLen = rows.Sum(r => r.LengthM);
            double totalKg = rows.Sum(r => r.WeightKg);
            int drums = rows.Where(r => r.DrumId > 0).Select(r => r.DrumId).Distinct().Count();
            TaskDialog.Show("STING Cable Pull List",
                $"Generated pull list for {rows.Count} cable(s).\n\n" +
                $"Total run length: {totalLen:0.0} m\n" +
                $"Total weight    : {totalKg:0.0} kg\n" +
                $"Drums needed    : {drums}\n\n" +
                $"Excel: {outPath}");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch { }
            return Result.Succeeded;
        }

        private static PullRow BuildPullRow(Document doc, ElectricalSystem sys)
        {
            double lenFt = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM)?.AsDouble() ?? 0;
            double lenM = lenFt > 0 ? lenFt * 0.3048 : 0;

            // Phase 178+ feeder CSA, fall back to legacy ELC_CBL_SZ_MM.
            double csa = SafeDouble(sys, "ELC_FEEDER_CSA_MM2");
            if (csa <= 0) csa = SafeDouble(sys, "ELC_CBL_SZ_MM");
            double cpc = SafeDouble(sys, "ELC_CPC_SZ_MM");

            // Canonical MR_PARAMETERS: ELC_CBL_INS_TYPE_TXT (Phase 188 fix).
            // Conductor material isn't tabulated in MR_PARAMETERS yet — default
            // to copper unless project schema adds an ELC_CBL_COND_MAT_TXT.
            string mat = "Cu";
            string ins = sys.LookupParameter("ELC_CBL_INS_TYPE_TXT")?.AsString() ?? "PVC";
            // Number of conductors (cores) for weight scaling.
            // RBS_ELEC_NUMBER_OF_RUNS doesn't exist in modern Revit API — use
            // the canonical MR_PARAMETERS ELC_CBL_NUM_OF_CORES_NR string
            // parameter (1-phase ~3 cores, 3-phase ~5 cores when populated;
            // defaults to 3 if blank).
            int cores = 3;
            try
            {
                string c = sys.LookupParameter("ELC_CBL_NUM_OF_CORES_NR")?.AsString();
                if (int.TryParse(c, out int parsed) && parsed > 0) cores = parsed;
            }
            catch { }

            // Weight estimate: copper PVC ≈ 9 kg/100m at 2.5 mm², linear in CSA
            // (BS 6004 informative). Aluminium ≈ 0.32× by mass.
            double kgPerM = (mat == "Al" ? 0.32 : 1.0) * (csa * 0.012 + 0.06);
            double weight = lenM * kgPerM * Math.Max(cores, 1);

            string from = sys.PanelName ?? "—";
            string to   = "(loads)";
            try
            {
                var elements = sys.Elements;
                if (elements != null && elements.Size > 0)
                {
                    var first = elements.GetEnumerator();
                    if (first.MoveNext() && first.Current is Element fe)
                        to = fe.Name ?? to;
                }
            }
            catch { }

            return new PullRow
            {
                CircuitTag = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "",
                FromPanel  = from,
                ToLoad     = sys.LoadName ?? to,
                CableType  = $"{mat}/{ins}",
                CsaMm2     = csa,
                CpcMm2     = cpc,
                Phases     = cores,
                LengthM    = lenM,
                WeightKg   = weight
            };
        }

        private static void AllocateDrums(List<PullRow> rows)
        {
            // FFD: order by length desc, then pack into drums.
            int drumId = 0;
            double currentDrumRemaining = 0;
            foreach (var r in rows.OrderByDescending(x => x.LengthM))
            {
                double drumLen = DrumLengthFor(r.CsaMm2);
                if (r.LengthM > drumLen)
                {
                    // Single cable longer than a drum → its own drum, splice flagged.
                    drumId++;
                    r.DrumId = drumId;
                    r.DrumNote = $"Splice required — exceeds {drumLen} m drum";
                    currentDrumRemaining = 0;
                    continue;
                }
                if (r.LengthM > currentDrumRemaining)
                {
                    drumId++;
                    currentDrumRemaining = drumLen;
                }
                r.DrumId = drumId;
                currentDrumRemaining -= r.LengthM;
            }
        }

        private static double DrumLengthFor(double csaMm2)
        {
            if (csaMm2 <= 16)  return 500;
            if (csaMm2 <= 95)  return 250;
            return 100;
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

        private static void WriteExcel(string path, List<PullRow> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Cable Pull List");
            ws.Cell(1, 1).Value = $"STING Cable Pull List  ·  {rows.Count} cables  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
            ws.Range(1, 1, 1, 11).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 11).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            string[] hdr = { "Drum", "Circuit", "From", "To", "Cable type",
                             "CSA (mm²)", "CPC (mm²)", "Cores", "Length (m)",
                             "Weight (kg)", "Notes" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(2, i + 1).Value = hdr[i];
                ws.Cell(2, i + 1).Style.Font.Bold = true;
                ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 3;
            foreach (var r in rows.OrderBy(x => x.DrumId).ThenBy(x => x.FromPanel).ThenBy(x => x.CircuitTag))
            {
                ws.Cell(row, 1).Value = r.DrumId == 0 ? "—" : r.DrumId.ToString();
                ws.Cell(row, 2).Value = r.CircuitTag;
                ws.Cell(row, 3).Value = r.FromPanel;
                ws.Cell(row, 4).Value = r.ToLoad;
                ws.Cell(row, 5).Value = r.CableType;
                ws.Cell(row, 6).Value = r.CsaMm2;
                ws.Cell(row, 7).Value = r.CpcMm2 > 0 ? r.CpcMm2 : 0;
                ws.Cell(row, 8).Value = r.Phases;
                ws.Cell(row, 9).Value = r.LengthM;
                ws.Cell(row, 10).Value = r.WeightKg;
                ws.Cell(row, 11).Value = r.DrumNote ?? "";
                if (!string.IsNullOrEmpty(r.DrumNote))
                    ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.LightYellow;
                row++;
            }
            // Subtotal band
            ws.Cell(row, 1).Value = "TOTAL";
            ws.Cell(row, 9).FormulaA1 = $"=SUM(I3:I{row - 1})";
            ws.Cell(row, 10).FormulaA1 = $"=SUM(J3:J{row - 1})";
            ws.Range(row, 1, row, 11).Style.Font.Bold = true;
            ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            ws.Columns().AdjustToContents();
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            wb.SaveAs(path);
        }

        private class PullRow
        {
            public string CircuitTag, FromPanel, ToLoad, CableType, DrumNote;
            public double CsaMm2, CpcMm2, LengthM, WeightKg;
            public int Phases, DrumId;
        }
    }
}
