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
    /// Per-room lighting calculation sheet — one A4-sized worksheet per
    /// room showing the full CIBSE LG10 Lumen Method derivation:
    ///
    ///   E = (n × Φ × UF × MF) / A
    ///
    /// where E is maintained illuminance (lx), n the fixture count, Φ the
    /// luminous flux per fixture (lm), UF the utilisation factor from
    /// Table 6 by room index k, MF the maintenance factor (0.8 default),
    /// and A the working-plane area (m²). The sheet shows every input,
    /// the room geometry estimate, the UF/MF derivation, the result vs
    /// target lux from BS EN 12464-1 / CIBSE LG7, and a designer/checker
    /// signature block — same shape as the Phase 183 BS 7671 loop calc
    /// sheet. Designed to go into the design pack.
    ///
    /// Pairs with <see cref="QuickLuxEstimateCommand"/> (the headline
    /// summary) — this command is the supporting working that engineers
    /// review and stamp.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LightingCalcSheetCommand : IExternalCommand
    {
        private const double DefaultMf = 0.80;
        private const double DefaultMountingHeightM = 2.7;
        private const double DefaultCeilingReflectance = 0.70;
        private const double DefaultWallReflectance    = 0.50;
        private const double DefaultFloorReflectance   = 0.20;

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
                TaskDialog.Show("STING Lighting Calc Sheet", "No placed rooms found.");
                return Result.Cancelled;
            }

            var sheets = new List<RoomSheet>();
            foreach (var room in rooms)
            {
                try
                {
                    var s = BuildSheet(doc, room, luxTargets);
                    if (s != null) sheets.Add(s);
                }
                catch (Exception ex) { StingLog.Warn($"LightingCalcSheet room {room.Name}: {ex.Message}"); }
            }
            if (sheets.Count == 0)
            {
                TaskDialog.Show("STING Lighting Calc Sheet",
                    "No rooms had luminaires placed in them — calc sheet is empty.");
                return Result.Cancelled;
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir,
                $"STING_LightingCalcSheets_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");

            using var wb = new XLWorkbook();

            // Index sheet
            var ix = wb.Worksheets.Add("Index");
            ix.Cell(1, 1).Value = $"Lighting Calculation Sheets  ·  {sheets.Count} rooms  ·  CIBSE LG10 Lumen Method  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
            ix.Range(1, 1, 1, 6).Merge().Style.Font.Bold = true;
            ix.Range(1, 1, 1, 6).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            string[] hdr = { "Room", "Area (m²)", "Fixtures", "Est lux", "Target lux", "Verdict" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ix.Cell(2, i + 1).Value = hdr[i];
                ix.Cell(2, i + 1).Style.Font.Bold = true;
                ix.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int ixRow = 3, sheetSeq = 1;
            foreach (var s in sheets.OrderBy(x => x.RoomName))
            {
                string sheetName = SafeSheetName($"{sheetSeq:D3}_{s.RoomName}");
                WriteRoomSheet(wb.Worksheets.Add(sheetName), s);
                ix.Cell(ixRow, 1).Value = s.RoomName;
                ix.Cell(ixRow, 2).Value = s.AreaM2;
                ix.Cell(ixRow, 3).Value = s.Fixtures;
                ix.Cell(ixRow, 4).Value = s.EstLux;
                ix.Cell(ixRow, 5).Value = s.TargetLux;
                ix.Cell(ixRow, 6).Value = s.Verdict;
                var fill = s.Verdict == "PASS" ? XLColor.LightGreen
                         : s.Verdict == "OVER" ? XLColor.LightYellow : XLColor.LightSalmon;
                ix.Range(ixRow, 1, ixRow, 6).Style.Fill.BackgroundColor = fill;
                ixRow++;
                sheetSeq++;
            }
            ix.Columns().AdjustToContents();
            ix.PageSetup.PaperSize = XLPaperSize.A4Paper;

            try { wb.SaveAs(outPath); }
            catch (Exception ex) { StingLog.Error($"LightingCalcSheet save: {ex.Message}", ex); msg = ex.Message; return Result.Failed; }

            int below = sheets.Count(s => s.Verdict == "BELOW");
            int over  = sheets.Count(s => s.Verdict == "OVER");
            int pass  = sheets.Count(s => s.Verdict == "PASS");
            TaskDialog.Show("STING Lighting Calc Sheet",
                $"Wrote {sheets.Count} per-room calc sheet(s) to:\n{outPath}\n\n" +
                $"✅ PASS {pass}   ⚠ BELOW {below}   ⚠ OVER {over}\n\n" +
                "Each sheet shows the full Lumen Method derivation (n × Φ × UF × MF / A = E) " +
                "with a designer/checker signature block. Designed for the design pack.");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch { }
            return Result.Succeeded;
        }

        private static RoomSheet BuildSheet(Document doc, Room room, LuxTargetTable luxTargets)
        {
            int fixtures = 0;
            double totalLumens = 0;
            double totalWatts  = 0;
            string fixtureType = "";
            string iesFile     = "";

            foreach (var fi in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                bool inRoom = false;
                try { if (fi.Room?.Id == room.Id) inRoom = true; } catch { }
                if (!inRoom)
                {
                    var pt = (fi.Location as LocationPoint)?.Point;
                    if (pt != null && room.IsPointInRoom(pt)) inRoom = true;
                }
                if (!inRoom) continue;

                fixtures++;
                double lm = SafeDouble(fi, "ELC_LITE_LUMENS", "Initial Intensity", "Luminous Flux");
                if (lm <= 0) lm = 4000;
                totalLumens += lm;
                double w = SafeDouble(fi, "ELC_LITE_WATTAGE", "Wattage");
                if (w <= 0) w = 36;
                totalWatts += w;
                if (string.IsNullOrEmpty(fixtureType))
                {
                    fixtureType = $"{fi.Symbol?.FamilyName} : {fi.Symbol?.Name}";
                    iesFile = SafeString(fi, "ELC_LITE_IES_FILE", "Photometric File", "ELC_PHOTOMETRIC_FILE");
                }
            }
            if (fixtures == 0 && totalLumens == 0) return null;

            double areaM2 = room.Area * 0.0929;
            double perimeterM = (room.Perimeter > 0 ? room.Perimeter : 0) * 0.3048;
            double l, w2;
            EstimateLW(areaM2, perimeterM, out l, out w2);
            double hm = DefaultMountingHeightM;
            double k = (l * w2) / Math.Max(hm * (l + w2), 0.01);
            double uf = UtilisationFactor(k);
            double mf = DefaultMf;
            double estLux = areaM2 > 0 ? totalLumens * uf * mf / areaM2 : 0;

            string roomName = room.Name ?? "";
            var (target, uniformity) = luxTargets.TargetAndUniformityFor(roomName);
            if (target <= 0) target = 300;

            double pct = target > 0 ? estLux / target * 100.0 : 0;
            string verdict = pct < 90 ? "BELOW" : pct > 130 ? "OVER" : "PASS";

            int delta = 0;
            if (verdict == "BELOW" && fixtures > 0)
                delta = (int)Math.Ceiling(fixtures * (target / Math.Max(estLux, 1) - 1));
            else if (verdict == "OVER" && fixtures > 1)
                delta = -(int)Math.Floor(fixtures - fixtures * target / estLux);

            // LPD computation for the same room
            double lpd = areaM2 > 0 ? totalWatts / areaM2 : 0;

            return new RoomSheet
            {
                RoomName = roomName,
                RoomId = room.Id.Value,
                AreaM2 = areaM2,
                LengthM = l,
                WidthM  = w2,
                MountingHeightM = hm,
                Fixtures = fixtures,
                FixtureType = fixtureType,
                IesFile = iesFile,
                LumensPerFixture = fixtures > 0 ? totalLumens / fixtures : 0,
                TotalLumens = totalLumens,
                TotalWatts  = totalWatts,
                LpdWperM2 = lpd,
                RoomIndexK = k,
                UF = uf,
                MF = mf,
                CeilingReflectance = DefaultCeilingReflectance,
                WallReflectance    = DefaultWallReflectance,
                FloorReflectance   = DefaultFloorReflectance,
                EstLux = estLux,
                TargetLux = target,
                UniformityTarget = uniformity,
                Pct = pct,
                Verdict = verdict,
                FixturesDelta = delta
            };
        }

        private static void WriteRoomSheet(IXLWorksheet ws, RoomSheet s)
        {
            // ── Header band ──────────────────────────────────────────────
            ws.Cell(1, 1).Value = "LIGHTING CALCULATION SHEET — CIBSE LG10 Lumen Method / BS EN 12464-1";
            ws.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            ws.Range(1, 1, 1, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 3;
            ws.Cell(row, 1).Value = "Room";          ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = s.RoomName;
            row++;
            ws.Cell(row, 1).Value = "Date";          ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = DateTime.Now.ToString("yyyy-MM-dd");
            row += 2;

            // ── Room geometry ────────────────────────────────────────────
            ws.Cell(row, 1).Value = "ROOM GEOMETRY";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;

            void Field(string label, object value, string unit = "")
            {
                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 2).Value = value?.ToString() ?? "";
                ws.Cell(row, 3).Value = unit;
                ws.Range(row, 1, row, 4).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                row++;
            }
            Field("Floor area A",          $"{s.AreaM2:0.0}",    "m²");
            Field("Length L (estimated)",  $"{s.LengthM:0.00}",  "m");
            Field("Width W (estimated)",   $"{s.WidthM:0.00}",   "m");
            Field("Mounting height Hm",    $"{s.MountingHeightM:0.0}", "m");
            row++;

            // ── Reflectances ─────────────────────────────────────────────
            ws.Cell(row, 1).Value = "SURFACE REFLECTANCES (LG10 standard)";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            Field("Ceiling ρc",  $"{s.CeilingReflectance:0.00}");
            Field("Walls ρw",    $"{s.WallReflectance:0.00}");
            Field("Floor ρf",    $"{s.FloorReflectance:0.00}");
            row++;

            // ── Luminaire ────────────────────────────────────────────────
            ws.Cell(row, 1).Value = "LUMINAIRE";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            Field("Family / Type",     s.FixtureType);
            Field("Photometric file",  string.IsNullOrEmpty(s.IesFile) ? "(unassigned — using fallback 4000 lm)" : s.IesFile);
            Field("Number of luminaires n", s.Fixtures);
            Field("Lumens per luminaire Φ", $"{s.LumensPerFixture:0}",     "lm");
            Field("Total installed lumens", $"{s.TotalLumens:0}",          "lm");
            Field("Total installed wattage",$"{s.TotalWatts:0.0}",         "W");
            Field("Lighting power density (LPD)", $"{s.LpdWperM2:0.00}",   "W/m²");
            row++;

            // ── Calculation ─────────────────────────────────────────────
            ws.Cell(row, 1).Value = "CALCULATION  E = (n × Φ × UF × MF) / A";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            Field("Room index k = (L·W) / (Hm·(L+W))", $"{s.RoomIndexK:0.00}");
            Field("Utilisation factor UF (LG10 Table 6)", $"{s.UF:0.00}");
            Field("Maintenance factor MF (CIBSE)", $"{s.MF:0.00}");
            Field("Estimated illuminance E", $"{s.EstLux:0}", "lx");
            row++;

            // ── Target ──────────────────────────────────────────────────
            ws.Cell(row, 1).Value = "TARGET (BS EN 12464-1 / CIBSE LG7)";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            Field("Target maintained illuminance Em", $"{s.TargetLux:0}", "lx");
            Field("Target uniformity Uo (min/avg)", $"{s.UniformityTarget:0.00}");
            Field("Achieved % of target", $"{s.Pct:0}", "%");
            row++;

            // ── Verdict ─────────────────────────────────────────────────
            ws.Cell(row, 1).Value = "RESULT";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor =
                s.Verdict == "PASS" ? XLColor.LightGreen :
                s.Verdict == "OVER" ? XLColor.LightYellow : XLColor.LightSalmon;
            row++;
            ws.Cell(row, 1).Value = s.Verdict;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 14;
            if (s.FixturesDelta != 0)
            {
                ws.Cell(row, 2).Value = s.FixturesDelta > 0
                    ? $"add {s.FixturesDelta} more fixture(s)"
                    : $"remove {-s.FixturesDelta} fixture(s) or dim";
                ws.Cell(row, 2).Style.Font.Italic = true;
            }
            row += 2;

            // ── Notes ────────────────────────────────────────────────────
            ws.Cell(row, 1).Value = "NOTES";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            ws.Cell(row, 1).Value =
                "This is a coarse first-pass estimate (LG10 Lumen Method). UF interpolated from " +
                "Table 6 at standard reflectance combo (70/50/20). MF=0.8 fixed. Room L×W estimated " +
                "from area + perimeter (quadratic). For final compliance, run a DIALux / ElumTools / " +
                "Relux photometric calculation and import the IFC results via STING → PHOTO → IFC Import.";
            ws.Range(row, 1, row + 3, 4).Merge().Style.Alignment.WrapText = true;
            row += 4;
            row++;

            // ── Sign-off ────────────────────────────────────────────────
            ws.Cell(row, 1).Value = "SIGN-OFF";
            ws.Range(row, 1, row, 4).Merge().Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray; row++;
            ws.Cell(row, 1).Value = "Designer";   ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 3).Value = "Date";       ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Border.BottomBorder = XLBorderStyleValues.Thin; row++;
            row++;
            ws.Cell(row, 1).Value = "Checker";    ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 3).Value = "Date";       ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            // Page setup
            ws.Columns().AdjustToContents();
            ws.Column(1).Width = Math.Min(ws.Column(1).Width, 38);
            ws.Column(2).Width = Math.Max(ws.Column(2).Width, 24);
            ws.Column(3).Width = Math.Max(ws.Column(3).Width, 8);
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 1);
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        }

        // CIBSE LG10 Table 6 — UF for typical commercial reflectance (70/50/20). Linear interp.
        private static double UtilisationFactor(double k)
        {
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
            double s = perimeterM / 2.0;
            double disc = s * s - 4 * areaM2;
            if (disc < 0 || perimeterM <= 0) { l = w = Math.Sqrt(Math.Max(areaM2, 1)); return; }
            double sqrt = Math.Sqrt(disc);
            l = (s + sqrt) / 2.0;
            w = (s - sqrt) / 2.0;
            if (w <= 0) { l = w = Math.Sqrt(Math.Max(areaM2, 1)); }
        }

        private static double SafeDouble(Element el, params string[] names)
        {
            foreach (var n in names)
            {
                var p = el?.LookupParameter(n);
                if (p == null) continue;
                try
                {
                    if (p.StorageType == StorageType.Double) { var v = p.AsDouble(); if (v > 0) return v; }
                    if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out double v2) && v2 > 0) return v2;
                    if (p.StorageType == StorageType.Integer) { var v = p.AsInteger(); if (v > 0) return v; }
                }
                catch { }
            }
            return 0;
        }

        private static string SafeString(Element el, params string[] names)
        {
            foreach (var n in names)
            {
                var p = el?.LookupParameter(n);
                if (p == null) continue;
                try
                {
                    if (p.StorageType == StorageType.String)
                    {
                        var s = p.AsString();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
                catch { }
            }
            return "";
        }

        private static string SafeSheetName(string s)
        {
            if (string.IsNullOrEmpty(s)) s = "Room";
            foreach (char c in new[] { '\\', '/', '*', '?', ':', '[', ']' }) s = s.Replace(c, '_');
            return s.Length > 31 ? s.Substring(0, 31) : s;
        }

        private class RoomSheet
        {
            public string RoomName, FixtureType, IesFile, Verdict;
            public long RoomId;
            public int Fixtures, FixturesDelta;
            public double AreaM2, LengthM, WidthM, MountingHeightM,
                          LumensPerFixture, TotalLumens, TotalWatts, LpdWperM2,
                          CeilingReflectance, WallReflectance, FloorReflectance,
                          RoomIndexK, UF, MF, EstLux, TargetLux, UniformityTarget, Pct;
        }
    }
}
