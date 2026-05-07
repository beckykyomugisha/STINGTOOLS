using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.Photometrics;

namespace StingTools.Commands.Electrical.Lighting
{
    /// <summary>
    /// Coarse first-pass illuminance estimate per CIBSE LG10 / IESNA Lumen
    /// Method, before round-tripping to DIALux. Answers the practical
    /// question "do we even need 12 fixtures or 8?" while the design is
    /// still on the move.
    ///
    /// E = (n × Φ × UF × MF) / A
    ///   E  = average maintained illuminance (lux)
    ///   n  = number of luminaires in the room
    ///   Φ  = lumens per luminaire (read from PhotometricLibrary if assigned)
    ///   UF = utilisation factor — looked up in CIBSE LG10 Table 6 by
    ///        room index k = (L·W)/(Hm·(L+W)) and reflectance combo
    ///   MF = maintenance factor (0.8 default per CIBSE LG10)
    ///   A  = working-plane area (m²)
    ///
    /// Outputs an Excel pack alongside the BS EN 12464-1 lux targets
    /// (single source of truth, Phase 182 LuxTargetTable). Per-room result:
    /// estimated lux vs target lux vs % of target, plus the count of
    /// fixtures the room would need to add/remove to land on target.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class QuickLuxEstimateCommand : IExternalCommand
    {
        private const double DefaultMf = 0.80;       // Maintenance factor
        private const double DefaultMountingHeightM = 2.7;

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var luxTargets = LuxTargetTable.Load();

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>()
                .Where(r => r.Area > 0).ToList();
            if (rooms.Count == 0)
            {
                TaskDialog.Show("STING Quick Lux", "No placed rooms found.");
                return Result.Cancelled;
            }

            var rows = new List<RoomRow>();
            foreach (var room in rooms)
            {
                try
                {
                    var fixtures = CountFixturesInRoom(doc, room, out double totalLumens);
                    if (fixtures == 0 && totalLumens == 0) continue;

                    double areaM2 = room.Area * 0.0929;
                    double perimeterM = (room.Perimeter > 0 ? room.Perimeter : 0) * 0.3048;
                    // Approximate L × W from area + perimeter: solve quadratic.
                    double l, w;
                    EstimateLW(areaM2, perimeterM, out l, out w);
                    double hm = DefaultMountingHeightM;
                    double k  = (l * w) / Math.Max(hm * (l + w), 0.01);
                    double uf = UtilisationFactor(k);
                    double mf = DefaultMf;
                    double estLux = areaM2 > 0 ? totalLumens * uf * mf / areaM2 : 0;

                    string roomName = room.Name ?? "";
                    var (target, _) = luxTargets.TargetAndUniformityFor(roomName);
                    double pct = target > 0 ? estLux / target * 100.0 : 0;
                    string verdict = pct < 90 ? "BELOW" : pct > 130 ? "OVER" : "PASS";

                    int fixturesNeeded = 0;
                    if (verdict == "BELOW" && fixtures > 0)
                        fixturesNeeded = (int)Math.Ceiling(fixtures * (target / Math.Max(estLux, 1) - 1));
                    else if (verdict == "OVER" && fixtures > 1)
                        fixturesNeeded = -(int)Math.Floor(fixtures - fixtures * target / estLux);

                    rows.Add(new RoomRow
                    {
                        Name = roomName, AreaM2 = areaM2,
                        Fixtures = fixtures, TotalLumens = totalLumens,
                        RoomIndexK = k, UF = uf, MF = mf,
                        EstLux = estLux, TargetLux = target,
                        Pct = pct, Verdict = verdict,
                        FixturesDelta = fixturesNeeded
                    });
                }
                catch (Exception ex) { StingLog.Warn($"QuickLux room {room.Name}: {ex.Message}"); }
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"STING_QuickLux_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
            WriteExcel(outPath, rows);

            int below = rows.Count(r => r.Verdict == "BELOW");
            int over  = rows.Count(r => r.Verdict == "OVER");
            int pass  = rows.Count(r => r.Verdict == "PASS");
            TaskDialog.Show("STING Quick Lux Estimate",
                $"CIBSE LG10 Lumen Method — first-pass lux estimate.\n" +
                $"This is a coarse estimate (UF from room index, MF=0.8 fixed).\n" +
                $"For final compliance use Photo_DesignReview after a DIALux round-trip.\n\n" +
                $"✅ PASS {pass}   ⚠ BELOW {below}   ⚠ OVER {over}\n\n" +
                $"Excel: {outPath}");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir) { UseShellExecute = true }); } catch { }
            return Result.Succeeded;
        }

        private static int CountFixturesInRoom(Document doc, Room room, out double totalLumens)
        {
            totalLumens = 0;
            int count = 0;
            foreach (var fi in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                bool inRoom = false;
                try { inRoom = fi.Room?.Id == room.Id; } catch { }
                if (!inRoom)
                {
                    var pt = (fi.Location as LocationPoint)?.Point;
                    if (pt != null && room.IsPointInRoom(pt)) inRoom = true;
                }
                if (!inRoom) continue;
                count++;
                // Pull lumens from the type / instance; fall back to default.
                double lm = SafeLumens(fi);
                if (lm <= 0) lm = 4000; // fallback assumption: 4000 lm fixture
                totalLumens += lm;
            }
            return count;
        }

        private static double SafeLumens(FamilyInstance fi)
        {
            try
            {
                var p = fi.LookupParameter("ELC_LITE_LUMENS")
                        ?? fi.LookupParameter("Initial Intensity")
                        ?? fi.LookupParameter("Luminous Flux");
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out double v)) return v;
            }
            catch { }
            return 0;
        }

        // CIBSE LG10 Table 6 — UF for typical commercial reflectance
        // (ceiling 70%, walls 50%, working plane 20%). Linear interp.
        private static double UtilisationFactor(double k)
        {
            // (k → UF) anchor points from CIBSE LG10 Table 6
            (double K, double UF)[] anchors =
            {
                (0.6, 0.36), (0.8, 0.45), (1.0, 0.51), (1.25, 0.57),
                (1.5, 0.61), (2.0, 0.67), (2.5, 0.71), (3.0, 0.74),
                (4.0, 0.78), (5.0, 0.81)
            };
            if (k <= anchors[0].K) return anchors[0].UF;
            if (k >= anchors[^1].K) return anchors[^1].UF;
            for (int i = 0; i < anchors.Length - 1; i++)
            {
                if (k < anchors[i].K || k > anchors[i + 1].K) continue;
                double f = (k - anchors[i].K) / (anchors[i + 1].K - anchors[i].K);
                return anchors[i].UF + f * (anchors[i + 1].UF - anchors[i].UF);
            }
            return 0.6;
        }

        private static void EstimateLW(double areaM2, double perimeterM, out double l, out double w)
        {
            // Solve l + w = P/2, l × w = A → quadratic l² - (P/2)l + A = 0.
            double s = perimeterM / 2.0;
            double disc = s * s - 4 * areaM2;
            if (disc < 0 || perimeterM <= 0) { l = w = Math.Sqrt(Math.Max(areaM2, 1)); return; }
            double sqrt = Math.Sqrt(disc);
            l = (s + sqrt) / 2.0;
            w = (s - sqrt) / 2.0;
            if (w <= 0) { l = w = Math.Sqrt(Math.Max(areaM2, 1)); }
        }

        private static void WriteExcel(string path, List<RoomRow> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Quick Lux");
            ws.Cell(1, 1).Value = $"STING Quick Lux Estimate (CIBSE LG10 Lumen Method)  ·  {rows.Count} rooms  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
            ws.Range(1, 1, 1, 11).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 11).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            string[] hdr = { "Room", "Area (m²)", "Fixtures", "Lumens",
                             "Room idx k", "UF", "MF", "Est lux", "Target lux",
                             "% of target", "Δ fixtures" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(2, i + 1).Value = hdr[i];
                ws.Cell(2, i + 1).Style.Font.Bold = true;
                ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 3;
            foreach (var r in rows.OrderBy(x => x.Pct))
            {
                ws.Cell(row, 1).Value = r.Name;
                ws.Cell(row, 2).Value = r.AreaM2;
                ws.Cell(row, 3).Value = r.Fixtures;
                ws.Cell(row, 4).Value = r.TotalLumens;
                ws.Cell(row, 5).Value = r.RoomIndexK;
                ws.Cell(row, 6).Value = r.UF;
                ws.Cell(row, 7).Value = r.MF;
                ws.Cell(row, 8).Value = r.EstLux;
                ws.Cell(row, 9).Value = r.TargetLux;
                ws.Cell(row, 10).Value = r.Pct;
                ws.Cell(row, 11).Value = r.FixturesDelta == 0 ? "—" :
                    (r.FixturesDelta > 0 ? $"+{r.FixturesDelta}" : r.FixturesDelta.ToString());
                var fill = r.Verdict == "PASS"  ? XLColor.LightGreen
                         : r.Verdict == "OVER"  ? XLColor.LightYellow : XLColor.LightSalmon;
                ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = fill;
                row++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
        }

        private class RoomRow
        {
            public string Name, Verdict;
            public double AreaM2, TotalLumens, RoomIndexK, UF, MF, EstLux, TargetLux, Pct;
            public int Fixtures, FixturesDelta;
        }
    }
}
