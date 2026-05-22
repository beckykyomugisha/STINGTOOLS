// StingTools — TRACE / HAP block-load comparison (Phase 187g).
//
// Can't run TRACE / HAP from inside Revit (no license, no CLI surface
// even when installed). Best we can do: import their EXPORTED CSV
// (TRACE 3D Plus "Export → Load Report → CSV"; HAP "Reports → CSV
// Export") and compare per-zone against STING's current
// HVC_PEAK_SENS_W / HVC_OA_LS stamps.
//
// Accepted CSV shape (header row exact-match required):
//   ZoneId, SensibleKw, LatentKw, OutdoorAirLs
// Extra columns ignored. ZoneId joins against:
//   * STING Space's Number param (preferred)
//   * Space's Name fallback
//   * Space's ElementId as int (last resort)
//
// Output: result panel + CSV at <project>/_BIM_COORD/acoustic/
// trace_compare_<ts>.csv with per-zone deltas. Aggregate stats: mean
// abs delta, max delta, R² of the pair plot. Optional Project-Info
// param PRJ_TRACE_TOLERANCE_PCT (default 15%) sets the pass band.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacCompareLoadsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                string csvPath;
                using (var dlg = new OpenFileDialog
                {
                    Filter = "TRACE / HAP CSV export|*.csv|All files|*.*",
                    Title = "Pick TRACE 3D Plus or HAP CSV export"
                })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return Result.Cancelled;
                    csvPath = dlg.FileName;
                }
                if (!File.Exists(csvPath))
                {
                    TaskDialog.Show("STING HVAC", "Selected file does not exist.");
                    return Result.Cancelled;
                }

                var rows = ReadCsv(csvPath, out string parseError);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Compare Loads",
                        $"No rows parsed from {Path.GetFileName(csvPath)}.\n\n" +
                        $"Header row must contain: ZoneId, SensibleKw, LatentKw, OutdoorAirLs.\n\n" +
                        $"Parse error: {parseError ?? "(none — file empty?)"}");
                    return Result.Cancelled;
                }

                double tolPct = 15.0;
                try
                {
                    string s = doc.ProjectInformation?.LookupParameter("PRJ_TRACE_TOLERANCE_PCT")?.AsString();
                    if (!string.IsNullOrWhiteSpace(s) && double.TryParse(s,
                        NumberStyles.Any, CultureInfo.InvariantCulture, out double t)) tolPct = t;
                }
                catch { }

                // Build a join map: ZoneId (string) → STING Space + its stamps.
                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .Cast<Space>()
                    .ToList();
                var spaceByNumber = spaces
                    .Where(s => !string.IsNullOrEmpty(s.Number))
                    .GroupBy(s => s.Number.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var spaceByName = spaces
                    .Where(s => !string.IsNullOrEmpty(s.Name))
                    .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var spaceById = spaces.ToDictionary(s => s.Id.Value.ToString(),
                                                    s => s, StringComparer.OrdinalIgnoreCase);

                int matched = 0, missing = 0, withinBand = 0, outsideBand = 0;
                double sumAbsDelta = 0, maxDelta = 0;
                string worstZone = "";
                var compareRows = new List<CompareRow>();
                foreach (var r in rows)
                {
                    Space sp = null;
                    if (!spaceByNumber.TryGetValue(r.ZoneId, out sp) &&
                        !spaceByName.TryGetValue(r.ZoneId, out sp) &&
                        !spaceById.TryGetValue(r.ZoneId, out sp))
                    {
                        missing++;
                        compareRows.Add(new CompareRow {
                            ZoneId = r.ZoneId, RefSensKw = r.SensibleKw, RefOaLs = r.OutdoorAirLs,
                            StingSensKw = 0, StingOaLs = 0, DeltaPct = double.NaN, Matched = false
                        });
                        continue;
                    }
                    matched++;
                    double stingSensW = ReadDouble(sp, "HVC_PEAK_SENS_W");
                    double stingOaLs  = ReadDouble(sp, "HVC_OA_LS");
                    double stingKw    = stingSensW / 1000.0;
                    double delta      = r.SensibleKw > 0
                        ? 100.0 * (stingKw - r.SensibleKw) / r.SensibleKw : 0;
                    sumAbsDelta += Math.Abs(delta);
                    if (Math.Abs(delta) > maxDelta) { maxDelta = Math.Abs(delta); worstZone = r.ZoneId; }
                    bool within = Math.Abs(delta) <= tolPct;
                    if (within) withinBand++; else outsideBand++;
                    compareRows.Add(new CompareRow {
                        ZoneId = r.ZoneId, RefSensKw = r.SensibleKw, RefOaLs = r.OutdoorAirLs,
                        StingSensKw = stingKw, StingOaLs = stingOaLs,
                        DeltaPct = delta, Matched = true, WithinBand = within
                    });
                }

                double meanAbsDelta = matched > 0 ? sumAbsDelta / matched : 0;
                double rSquared = ComputeRsq(compareRows.Where(c => c.Matched));

                string outCsv = WriteCsv(doc, compareRows, tolPct);

                var panel = StingResultPanel.Create("HVAC — Load Compare (TRACE / HAP)");
                panel.SetSubtitle($"reference={Path.GetFileName(csvPath)} · " +
                                  $"tolerance ±{tolPct:F0}% · {rows.Count} rows imported");
                panel.AddSection("SUMMARY")
                     .Metric("Reference rows",        rows.Count.ToString())
                     .Metric("STING matches",         matched.ToString())
                     .Metric("Unmatched (no Space)",  missing.ToString())
                     .Metric($"Within ±{tolPct:F0} %", withinBand.ToString())
                     .Metric($"Outside ±{tolPct:F0} %", outsideBand.ToString())
                     .Metric("Mean |Δ|",              $"{meanAbsDelta:F1} %")
                     .Metric("Max |Δ|",               $"{maxDelta:F1} % @ {worstZone}")
                     .Metric("R²",                    $"{rSquared:F3}")
                     .Metric("Output CSV",            outCsv ?? "(not written)");

                panel.AddSection("OUTSIDE-BAND ZONES (worst 20)");
                foreach (var c in compareRows
                    .Where(x => x.Matched && !x.WithinBand)
                    .OrderByDescending(x => Math.Abs(x.DeltaPct))
                    .Take(20))
                {
                    panel.Text($"  {c.ZoneId}: STING {c.StingSensKw:F1} kW vs ref {c.RefSensKw:F1} kW · " +
                               $"Δ {c.DeltaPct,+6:+0.0;-0.0;0.0} %");
                }

                panel.Text("Imports a TRACE 3D Plus or HAP CSV export with columns " +
                           "(ZoneId, SensibleKw, LatentKw, OutdoorAirLs), joins on STING " +
                           "Space Number / Name / ElementId, and compares per-zone sensible " +
                           $"loads against HVC_PEAK_SENS_W stamps. Tolerance via " +
                           $"PRJ_TRACE_TOLERANCE_PCT (default {tolPct:F0} %). R² near 1.0 + " +
                           "mean |Δ| < 10 % is a typical pass for the BlockLoad engine; " +
                           "larger drifts usually indicate climate-site mismatch or RTS-class " +
                           "mis-set rather than engine error.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"TRACE/HAP compare ({matched} matched, mean |Δ| {meanAbsDelta:F0} %)",
                        outsideBand == 0 ? "⬤" : "⬡");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacCompareLoadsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Parsing ─────────────────────────────────────────────────

        private class RefRow
        {
            public string ZoneId       = "";
            public double SensibleKw;
            public double LatentKw;
            public double OutdoorAirLs;
        }

        private class CompareRow
        {
            public string ZoneId      = "";
            public double RefSensKw;
            public double RefOaLs;
            public double StingSensKw;
            public double StingOaLs;
            public double DeltaPct;
            public bool   Matched;
            public bool   WithinBand;
        }

        private static List<RefRow> ReadCsv(string path, out string parseError)
        {
            parseError = null;
            var rows = new List<RefRow>();
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return rows;
                var headers = SplitCsv(lines[0]).Select(h => h.Trim()).ToList();
                int idxZone = headers.FindIndex(h => h.Equals("ZoneId",       StringComparison.OrdinalIgnoreCase));
                int idxSens = headers.FindIndex(h => h.Equals("SensibleKw",   StringComparison.OrdinalIgnoreCase));
                int idxLat  = headers.FindIndex(h => h.Equals("LatentKw",     StringComparison.OrdinalIgnoreCase));
                int idxOa   = headers.FindIndex(h => h.Equals("OutdoorAirLs", StringComparison.OrdinalIgnoreCase));
                if (idxZone < 0 || idxSens < 0)
                {
                    parseError = "Header row missing 'ZoneId' or 'SensibleKw'.";
                    return rows;
                }
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cells = SplitCsv(lines[i]);
                    if (cells.Count <= Math.Max(idxZone, idxSens)) continue;
                    rows.Add(new RefRow
                    {
                        ZoneId       = cells[idxZone].Trim(),
                        SensibleKw   = ParseNum(cells, idxSens),
                        LatentKw     = ParseNum(cells, idxLat),
                        OutdoorAirLs = ParseNum(cells, idxOa)
                    });
                }
            }
            catch (Exception ex) { parseError = ex.Message; }
            return rows;
        }

        private static double ParseNum(List<string> cells, int idx)
        {
            if (idx < 0 || idx >= cells.Count) return 0;
            return double.TryParse(cells[idx], NumberStyles.Any, CultureInfo.InvariantCulture, out double v)
                ? v : 0;
        }

        private static List<string> SplitCsv(string line)
        {
            // Minimal CSV splitter — handles "quoted, commas" + escaped "".
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (ch == '"') inQuotes = false;
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '"') inQuotes = true;
                    else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
            }
            cells.Add(sb.ToString());
            return cells;
        }

        private static double ReadDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    return v;
            }
            catch { }
            return 0;
        }

        private static double ComputeRsq(IEnumerable<CompareRow> rows)
        {
            var xs = rows.Where(r => r.RefSensKw > 0).Select(r => r.RefSensKw).ToList();
            var ys = rows.Where(r => r.RefSensKw > 0).Select(r => r.StingSensKw).ToList();
            int n = xs.Count;
            if (n < 2) return 0;
            double mx = xs.Average(), my = ys.Average();
            double ssXy = 0, ssX = 0, ssY = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = xs[i] - mx, dy = ys[i] - my;
                ssXy += dx * dy;
                ssX  += dx * dx;
                ssY  += dy * dy;
            }
            if (ssX <= 0 || ssY <= 0) return 0;
            double r = ssXy / Math.Sqrt(ssX * ssY);
            return r * r;
        }

        private static string WriteCsv(Document doc, List<CompareRow> rows, double tolPct)
        {
            try
            {
                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir)) return null;
                string outDir = Path.Combine(projDir, "_BIM_COORD", "acoustic");
                Directory.CreateDirectory(outDir);
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string csv = Path.Combine(outDir, $"trace_compare_{ts}.csv");
                var sb = new StringBuilder();
                sb.AppendLine($"# tolerance_pct,{tolPct:F1}");
                sb.AppendLine("ZoneId,RefSensKw,StingSensKw,DeltaPct,RefOaLs,StingOaLs,Matched,WithinBand");
                foreach (var r in rows)
                    sb.AppendLine($"\"{r.ZoneId}\",{r.RefSensKw:F2},{r.StingSensKw:F2},{r.DeltaPct:F2}," +
                                  $"{r.RefOaLs:F1},{r.StingOaLs:F1},{r.Matched},{r.WithinBand}");
                File.WriteAllText(csv, sb.ToString());
                return csv;
            }
            catch (Exception ex) { StingLog.Warn($"WriteCsv: {ex.Message}"); return null; }
        }
    }
}
