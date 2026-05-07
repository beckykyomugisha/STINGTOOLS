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

namespace StingTools.Commands.Electrical.Lighting
{
    /// <summary>
    /// Auto-derives lighting control zones per Part L 2021 / ASHRAE 90.1
    /// 9.4.1 / Title 24 §140.6 and writes a control schedule. One zone
    /// per room by default; daylight-responsive zones split off when a
    /// fixture sits within 6 m of a window/curtain wall (Part L 2021
    /// requires automatic daylight dimming in the perimeter band).
    /// Occupancy sensing required in offices/meeting rooms ≥ 10 m² and
    /// always in WCs/storage.
    ///
    /// Outputs an Excel control schedule grouping fixtures by zone with
    /// trigger type (occupancy / scene / daylight / switched), setpoint,
    /// and required regulation. Designed to feed straight into the
    /// commissioning data + Part L Section 6 evidence pack.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LightingControlZoneCommand : IExternalCommand
    {
        private const double PerimeterDaylightBandM = 6.0; // Part L 2021

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>()
                .Where(r => r.Area > 0).ToList();
            var fixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            var windows = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();

            if (rooms.Count == 0 || fixtures.Count == 0)
            {
                TaskDialog.Show("STING Lighting Zones", "Need rooms + fixtures placed.");
                return Result.Cancelled;
            }

            var zones = new List<ZoneRow>();
            int zoneSeq = 1;
            using (var tx = new Transaction(doc, "STING Lighting Control Zone"))
            {
                tx.Start();
                foreach (var room in rooms)
                {
                    var roomFixtures = fixtures.Where(f => InRoom(f, room)).ToList();
                    if (roomFixtures.Count == 0) continue;

                    string roomKey = (room.Name ?? "").ToLowerInvariant();
                    bool occupancyRequired = OccupancyRequired(roomKey, room.Area * 0.0929);
                    bool needsScene = NeedsSceneControl(roomKey);

                    // Split off perimeter daylight zone if fixtures within 6 m of windows
                    var perimeter = new List<FamilyInstance>();
                    var core      = new List<FamilyInstance>();
                    foreach (var f in roomFixtures)
                    {
                        var fp = (f.Location as LocationPoint)?.Point;
                        if (fp == null) { core.Add(f); continue; }
                        bool nearWindow = windows.Any(w =>
                        {
                            var wp = (w.Location as LocationPoint)?.Point;
                            return wp != null && fp.DistanceTo(wp) * 0.3048 <= PerimeterDaylightBandM;
                        });
                        (nearWindow ? perimeter : core).Add(f);
                    }

                    if (perimeter.Count > 0)
                    {
                        zones.Add(MakeZone(zoneSeq++, room, perimeter, "Daylight + Occupancy",
                            "Part L 2021 §6.3 — automatic daylight dimming required within 6 m of glazing", occupancyRequired));
                    }
                    if (core.Count > 0)
                    {
                        string trigger = occupancyRequired ? "Occupancy"
                                       : needsScene       ? "Scene"
                                       :                    "Switched";
                        string reg = occupancyRequired
                            ? "ASHRAE 90.1 §9.4.1.2 / Part L 2021 — automatic occupancy sensing"
                            : needsScene ? "DALI scene control for meeting/conference"
                            :              "Manual switched circuit";
                        zones.Add(MakeZone(zoneSeq++, room, core, trigger, reg, occupancyRequired));
                    }

                    // Stamp the zone tag onto each fixture if the param exists.
                    foreach (var z in zones.Where(z => z.RoomId == room.Id.Value))
                        foreach (var f in z.FixtureIds)
                            try { ParameterHelpers.SetString(doc.GetElement(new ElementId(f)),
                                "ELC_LITE_CONTROL_ZONE", $"Z{z.ZoneId:D3}", overwrite: true); } catch { }
                }
                tx.Commit();
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"STING_LightingControlZones_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
            WriteExcel(outPath, zones);

            TaskDialog.Show("STING Lighting Zones",
                $"Created {zones.Count} control zone(s).\n" +
                $"Wrote ELC_LITE_CONTROL_ZONE on each fixture (where the parameter exists).\n\n" +
                $"Excel: {outPath}");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir) { UseShellExecute = true }); } catch { }
            return Result.Succeeded;
        }

        private static bool InRoom(FamilyInstance fi, Room room)
        {
            try { if (fi.Room?.Id == room.Id) return true; } catch { }
            var pt = (fi.Location as LocationPoint)?.Point;
            return pt != null && room.IsPointInRoom(pt);
        }

        private static bool OccupancyRequired(string roomKey, double areaM2)
        {
            if (roomKey.Contains("toilet") || roomKey.Contains("wc") ||
                roomKey.Contains("storage") || roomKey.Contains("plant") ||
                roomKey.Contains("riser")  || roomKey.Contains("corridor")) return true;
            if ((roomKey.Contains("office") || roomKey.Contains("meeting") ||
                 roomKey.Contains("conference")) && areaM2 >= 10) return true;
            return false;
        }

        private static bool NeedsSceneControl(string roomKey) =>
            roomKey.Contains("conference") || roomKey.Contains("boardroom") ||
            roomKey.Contains("meeting")    || roomKey.Contains("training") ||
            roomKey.Contains("auditorium") || roomKey.Contains("classroom");

        private static ZoneRow MakeZone(int zoneId, Room room, List<FamilyInstance> fixtures,
            string trigger, string regulation, bool occupancyRequired)
            => new ZoneRow
            {
                ZoneId = zoneId,
                RoomId = room.Id.Value,
                RoomName = room.Name ?? "",
                FixtureIds = fixtures.Select(f => f.Id.Value).ToList(),
                FixtureCount = fixtures.Count,
                Trigger = trigger,
                Regulation = regulation,
                Setpoint = trigger.Contains("Daylight") ? "300-500 lx target" :
                           trigger.Contains("Occupancy") ? "ON/OFF + 15-min timeout" :
                           trigger.Contains("Scene")     ? "DALI scene 1-4 (meeting/presentation/AV/exit)"
                                                         : "Manual"
            };

        private static void WriteExcel(string path, List<ZoneRow> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Control Schedule");
            ws.Cell(1, 1).Value = $"STING Lighting Control Schedule  ·  {rows.Count} zones  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
            ws.Range(1, 1, 1, 6).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 6).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            string[] hdr = { "Zone", "Room", "Fixtures", "Trigger", "Setpoint", "Regulation" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(2, i + 1).Value = hdr[i];
                ws.Cell(2, i + 1).Style.Font.Bold = true;
                ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 3;
            foreach (var r in rows.OrderBy(z => z.ZoneId))
            {
                ws.Cell(row, 1).Value = $"Z{r.ZoneId:D3}";
                ws.Cell(row, 2).Value = r.RoomName;
                ws.Cell(row, 3).Value = r.FixtureCount;
                ws.Cell(row, 4).Value = r.Trigger;
                ws.Cell(row, 5).Value = r.Setpoint;
                ws.Cell(row, 6).Value = r.Regulation;
                if (r.Trigger.StartsWith("Daylight"))
                    ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.LightCyan;
                else if (r.Trigger.StartsWith("Occupancy"))
                    ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.LightYellow;
                row++;
            }
            ws.Columns().AdjustToContents();
            ws.Column(6).Width = 60;
            wb.SaveAs(path);
        }

        private class ZoneRow
        {
            public int ZoneId, FixtureCount;
            public long RoomId;
            public string RoomName, Trigger, Setpoint, Regulation;
            public List<long> FixtureIds = new();
        }
    }
}
