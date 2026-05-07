using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Commands.Electrical.Lighting;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Photometric
{
    /// <summary>
    /// The closing piece of the round-trip: take the DIALux-imported lux
    /// values, compare against BS EN 12464-1 / CIBSE LG7 / ASHRAE targets,
    /// and emit actionable design recommendations. Output is three things:
    ///   1. Graphic feedback in the active view (red / amber / green per
    ///      room, so the engineer sees what passed at a glance).
    ///   2. An Excel design-review report listing every below-target room
    ///      with a quantified "add N more fixtures" recommendation.
    ///   3. A TaskDialog summary with the worst offenders so the engineer
    ///      can decide what to change before the next DIALux run.
    /// This is what closes the loop — DIALux numbers DRIVE Revit changes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PhotometricDesignReviewCommand : IExternalCommand
    {
        private const double UnderTargetThresholdPct = 90.0; // < 90% of target is below
        private const double OverTargetThresholdPct  = 130.0;// > 130% is over-lit

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Use the LPD limits JSON as a source of room-type → target lux
            // (we map room-type patterns there to a default illuminance table).
            var limits = LightingPowerDensityCommand.LoadLpdLimits(
                StingElectricalCommandHandler_CurrentLpdStandard());
            var luxTargets = StingTools.Photometrics.LuxTargetTable.Load();

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>()
                .Where(r => r.Area > 0).ToList();
            if (rooms.Count == 0)
            {
                TaskDialog.Show("STING Design Review", "No placed rooms found.");
                return Result.Cancelled;
            }

            View view = doc.ActiveView;
            var ogsRed   = MakeOverride(244, 67, 54);
            var ogsAmber = MakeOverride(255, 152, 0);
            var ogsGreen = MakeOverride(76, 175, 80);

            var reviews = new List<DesignReviewRow>();
            using (var tx = new Transaction(doc, "STING Photometric Design Review"))
            {
                tx.Start();
                foreach (var room in rooms)
                {
                    string lastEngine = ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LAST_ENGINE);
                    double lux = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX));
                    if (lux <= 0)
                    {
                        // Try engine-specific values from Phase 181 import.
                        lux = TopOf(
                            ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX_DIALUX)),
                            ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX_ELUMTOOLS)),
                            ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX_RELUX)));
                    }
                    if (lux <= 0) continue;

                    string roomName = room.Name ?? "";
                    double targetLux = luxTargets.TargetFor(roomName);
                    if (targetLux <= 0) targetLux = 300; // BS EN 12464-1 default for general work

                    double pct = lux / targetLux * 100;
                    string verdict = pct < UnderTargetThresholdPct ? "BELOW"
                                   : pct > OverTargetThresholdPct  ? "OVER"
                                   : "PASS";

                    var review = new DesignReviewRow
                    {
                        RoomName = roomName,
                        AreaM2 = room.Area * 0.0929,
                        TargetLux = targetLux,
                        ActualLux = lux,
                        Pct = pct,
                        Verdict = verdict,
                        Engine = lastEngine,
                        UGR = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_UGR)),
                        Recommendation = BuildRecommendation(verdict, pct, room, doc)
                    };
                    reviews.Add(review);

                    if (view != null)
                    {
                        try
                        {
                            if (verdict == "BELOW")     view.SetElementOverrides(room.Id, ogsRed);
                            else if (verdict == "OVER") view.SetElementOverrides(room.Id, ogsAmber);
                            else                        view.SetElementOverrides(room.Id, ogsGreen);
                        }
                        catch { }
                    }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }

            // Excel report.
            string excelPath = WriteExcelReport(doc, reviews);

            int below = reviews.Count(r => r.Verdict == "BELOW");
            int over  = reviews.Count(r => r.Verdict == "OVER");
            int pass  = reviews.Count(r => r.Verdict == "PASS");
            var topThree = reviews.Where(r => r.Verdict == "BELOW")
                                  .OrderBy(r => r.Pct).Take(3).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Reviewed {reviews.Count} room(s) against BS EN 12464-1 / CIBSE / ASHRAE targets.");
            sb.AppendLine();
            sb.AppendLine($"✅ PASS  : {pass}  ({pass * 100.0 / Math.Max(reviews.Count, 1):0}%)");
            sb.AppendLine($"⚠ BELOW : {below} (< {UnderTargetThresholdPct:0}% of target)");
            sb.AppendLine($"⚠ OVER  : {over} (> {OverTargetThresholdPct:0}% of target — energy waste)");
            if (topThree.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("WORST OFFENDERS (action required):");
                foreach (var t in topThree)
                    sb.AppendLine($"  • {t.RoomName}: {t.ActualLux:0} lx vs {t.TargetLux:0} lx target ({t.Pct:0}%) → {t.Recommendation}");
            }
            sb.AppendLine();
            sb.AppendLine("Active view: rooms colour-coded red/amber/green by verdict.");
            if (!string.IsNullOrEmpty(excelPath))
                sb.AppendLine($"Excel report: {excelPath}");
            TaskDialog.Show("STING Photometric Design Review", sb.ToString());
            return Result.Succeeded;
        }

        // ── recommendation engine ───────────────────────────────────────

        /// <summary>
        /// Quantify what the engineer should change to bring this room to
        /// target lux. Honest about the limits — this is a linear estimate
        /// (lux scales with installed lumens at constant geometry), suitable
        /// for "add ~3 more 4000 lm panels" style guidance.
        /// </summary>
        private static string BuildRecommendation(string verdict, double pct, Room room, Document doc)
        {
            if (verdict == "PASS") return "no change required";
            try
            {
                int currentFixtures = CountFixturesInRoom(doc, room);
                if (verdict == "BELOW")
                {
                    if (currentFixtures == 0) return "no luminaires placed — add at least 2 in the working zone";
                    double scale = 100.0 / Math.Max(pct, 1);  // multiplier required
                    int needed = Math.Max(1, (int)Math.Ceiling(currentFixtures * (scale - 1)));
                    return $"add ~{needed} more fixture(s) of the same type (linear estimate); " +
                           $"or upgrade to higher-output luminaires (×{scale:0.0})";
                }
                if (verdict == "OVER")
                {
                    if (currentFixtures <= 1) return "swap to a lower-output luminaire";
                    double scale = pct / 100.0;
                    int reduce = Math.Max(1, (int)Math.Floor(currentFixtures - currentFixtures / scale));
                    return $"remove ~{reduce} fixture(s) or dim to {(100.0 / scale):0}% — saves energy";
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildRecommendation: {ex.Message}"); }
            return "review manually";
        }

        private static int CountFixturesInRoom(Document doc, Room room)
        {
            int n = 0;
            try
            {
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType().OfType<FamilyInstance>())
                {
                    try { if (fi.Room?.Id == room.Id) { n++; continue; } } catch { }
                    var pt = (fi.Location as LocationPoint)?.Point;
                    if (pt != null && room.IsPointInRoom(pt)) n++;
                }
            }
            catch { }
            return n;
        }

        // Lux target table moved to StingTools.Photometrics.LuxTargetTable
        // (loads from STING_LUX_TARGETS.json — single source of truth shared
        // with ElectricalSnapshotBuilder).

        // ── Excel writer ────────────────────────────────────────────────

        private static string WriteExcelReport(Document doc, List<DesignReviewRow> rows)
        {
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                outDir = Path.Combine(outDir ?? "", "electrical");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir,
                    $"STING_PhotometricDesignReview_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Design Review");
                ws.Cell(1, 1).Value = "Room";
                ws.Cell(1, 2).Value = "Area (m²)";
                ws.Cell(1, 3).Value = "Target lux";
                ws.Cell(1, 4).Value = "Actual lux";
                ws.Cell(1, 5).Value = "% of target";
                ws.Cell(1, 6).Value = "Verdict";
                ws.Cell(1, 7).Value = "UGR";
                ws.Cell(1, 8).Value = "Engine";
                ws.Cell(1, 9).Value = "Recommendation";
                ws.Range(1, 1, 1, 9).Style.Font.Bold = true;

                int row = 2;
                foreach (var r in rows.OrderBy(x => x.Pct))
                {
                    ws.Cell(row, 1).Value = r.RoomName;
                    ws.Cell(row, 2).Value = r.AreaM2;
                    ws.Cell(row, 3).Value = r.TargetLux;
                    ws.Cell(row, 4).Value = r.ActualLux;
                    ws.Cell(row, 5).Value = r.Pct;
                    ws.Cell(row, 6).Value = r.Verdict;
                    ws.Cell(row, 7).Value = r.UGR;
                    ws.Cell(row, 8).Value = r.Engine ?? "";
                    ws.Cell(row, 9).Value = r.Recommendation ?? "";
                    if (r.Verdict == "BELOW")
                        ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.LightSalmon;
                    else if (r.Verdict == "OVER")
                        ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.LightYellow;
                    else
                        ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.LightGreen;
                    row++;
                }
                ws.Columns().AdjustToContents();
                wb.SaveAs(outPath);
                return outPath;
            }
            catch (Exception ex)
            {
                StingLog.Error($"DesignReview Excel: {ex.Message}", ex);
                return "";
            }
        }

        private class DesignReviewRow
        {
            public string RoomName;
            public double AreaM2, TargetLux, ActualLux, Pct, UGR;
            public string Verdict;
            public string Engine;
            public string Recommendation;
        }

        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;
        private static double TopOf(params double[] xs) { double m = 0; foreach (var x in xs) if (x > m) m = x; return m; }

        private static OverrideGraphicSettings MakeOverride(int r, int g, int b)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color((byte)r, (byte)g, (byte)b));
            ogs.SetProjectionLineWeight(5);
            return ogs;
        }

        /// <summary>
        /// Wraps the static handler property so this command compiles
        /// without an explicit using on the UI namespace.
        /// </summary>
        private static string StingElectricalCommandHandler_CurrentLpdStandard()
            => StingTools.UI.StingElectricalCommandHandler.CurrentLpdStandard ?? "ASHRAE_90_1_2019";
    }
}
