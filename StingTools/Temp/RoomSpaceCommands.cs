using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ROOM / SPACE MANAGEMENT
    //  Commands for room auditing, department/zone assignment, room schedule
    //  creation, and spatial data reporting with STING tag integration.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Audits all rooms in the project for completeness — checks for
    /// unnamed rooms, unplaced rooms, unbounded rooms, zero-area rooms,
    /// and rooms missing department/zone assignments.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();

            if (rooms.Count == 0)
            {
                TaskDialog.Show("Room Audit", "No rooms found in the project.");
                return Result.Succeeded;
            }

            int unplaced = 0, unbounded = 0, zeroArea = 0;
            int unnamed = 0, noDepartment = 0, noNumber = 0;
            int redundant = 0;
            var levelCounts = new Dictionary<string, int>();
            var deptCounts = new Dictionary<string, int>();
            var issues = new List<string>();

            foreach (var room in rooms)
            {
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                string number = room.Number ?? "";
                string dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                string levelName = room.Level?.Name ?? "No Level";
                double area = room.Area;

                // Count by level
                if (!levelCounts.ContainsKey(levelName)) levelCounts[levelName] = 0;
                levelCounts[levelName]++;

                // Count by department
                if (!string.IsNullOrEmpty(dept))
                {
                    if (!deptCounts.ContainsKey(dept)) deptCounts[dept] = 0;
                    deptCounts[dept]++;
                }

                // Check placement
                if (room.Location == null)
                {
                    unplaced++;
                    if (issues.Count < 50)
                        issues.Add($"  UNPLACED: Room '{name}' ({number})");
                    continue;
                }

                // Check area
                if (area <= 0)
                {
                    unbounded++;
                    if (issues.Count < 50)
                        issues.Add($"  UNBOUNDED: Room '{name}' ({number}) on {levelName}");
                    continue;
                }

                if (area < 0.1) // Less than ~0.1 sq ft
                {
                    zeroArea++;
                    if (issues.Count < 50)
                        issues.Add($"  ZERO-AREA: Room '{name}' ({number}) — {area:F2} sq ft");
                }

                if (string.IsNullOrWhiteSpace(name) || name == "Room")
                {
                    unnamed++;
                    if (issues.Count < 50)
                        issues.Add($"  UNNAMED: Room #{number} on {levelName}");
                }

                if (string.IsNullOrEmpty(dept))
                    noDepartment++;

                if (string.IsNullOrEmpty(number))
                    noNumber++;
            }

            // Check for duplicate room numbers
            var numberGroups = rooms
                .Where(r => r.Location != null && !string.IsNullOrEmpty(r.Number))
                .GroupBy(r => r.Number)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var g in numberGroups)
            {
                redundant += g.Count() - 1;
                if (issues.Count < 50)
                    issues.Add($"  DUPLICATE NUMBER: '{g.Key}' appears {g.Count()} times");
            }

            // Build report
            var sb = new StringBuilder();
            sb.AppendLine("Room Audit Report");
            sb.AppendLine(new string('═', 50));
            sb.AppendLine($"  Total rooms: {rooms.Count}");
            sb.AppendLine($"  Placed & bounded: {rooms.Count - unplaced - unbounded}");
            sb.AppendLine();
            sb.AppendLine("Issues:");
            sb.AppendLine($"  Unplaced: {unplaced}");
            sb.AppendLine($"  Unbounded: {unbounded}");
            sb.AppendLine($"  Zero-area: {zeroArea}");
            sb.AppendLine($"  Unnamed/generic: {unnamed}");
            sb.AppendLine($"  No department: {noDepartment}");
            sb.AppendLine($"  No number: {noNumber}");
            sb.AppendLine($"  Duplicate numbers: {redundant}");

            sb.AppendLine();
            sb.AppendLine("By Level:");
            foreach (var kvp in levelCounts.OrderBy(k => k.Key))
                sb.AppendLine($"  {kvp.Key,-25} {kvp.Value,4} rooms");

            if (deptCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("By Department:");
                foreach (var kvp in deptCounts.OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kvp.Key,-25} {kvp.Value,4} rooms");
            }

            if (issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Details:");
                foreach (string issue in issues)
                    sb.AppendLine(issue);
                if (issues.Count >= 50)
                    sb.AppendLine("  ... (limited to 50 entries)");
            }

            int totalIssues = unplaced + unbounded + zeroArea + unnamed + noDepartment + noNumber + redundant;
            sb.AppendLine();
            sb.AppendLine(totalIssues == 0 ? "All rooms are valid." : $"Total issues: {totalIssues}");

            TaskDialog.Show("Room Audit", sb.ToString());
            StingLog.Info($"Room audit: {rooms.Count} rooms, {totalIssues} issues");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Auto-assigns STING ZONE codes to rooms based on department names
    /// and spatial groupings. Maps common department names to zone codes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomZoneAssignCommand : IExternalCommand
    {
        private static readonly Dictionary<string, string> DeptToZone =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Common department-to-zone mappings
                {"Reception", "Z01"}, {"Lobby", "Z01"}, {"Entrance", "Z01"}, {"Foyer", "Z01"},
                {"Office", "Z02"}, {"Open Plan", "Z02"}, {"Meeting", "Z02"}, {"Conference", "Z02"},
                {"Boardroom", "Z02"}, {"Workspace", "Z02"},
                {"Service", "Z03"}, {"Plant", "Z03"}, {"Riser", "Z03"}, {"Store", "Z03"},
                {"Server", "Z03"}, {"Electrical", "Z03"}, {"Mechanical", "Z03"}, {"Comms", "Z03"},
                {"WC", "Z04"}, {"Toilet", "Z04"}, {"Bathroom", "Z04"}, {"Kitchen", "Z04"},
                {"Break", "Z04"}, {"Staff", "Z04"}, {"Pantry", "Z04"}, {"Amenity", "Z04"},
            };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Location != null && r.Area > 0)
                .ToList();

            if (rooms.Count == 0)
            {
                TaskDialog.Show("Zone Assignment", "No placed rooms found.");
                return Result.Succeeded;
            }

            int assigned = 0, skipped = 0;
            var zoneSummary = new Dictionary<string, int>();

            using (Transaction tx = new Transaction(doc, "STING Room Zone Assignment"))
            {
                tx.Start();

                foreach (var room in rooms)
                {
                    string dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                    string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";

                    // Try to derive zone from department or room name
                    string zone = DeriveZone(dept, name);
                    if (string.IsNullOrEmpty(zone))
                    {
                        zone = "ZZ"; // Unknown zone
                    }

                    // Write to STING ZONE parameter
                    string existing = ParameterHelpers.GetString(room, ParamRegistry.ZONE);
                    if (!string.IsNullOrEmpty(existing))
                    {
                        skipped++;
                        zone = existing;
                    }
                    else
                    {
                        ParameterHelpers.SetString(room, ParamRegistry.ZONE, zone, overwrite: false);
                        assigned++;
                    }

                    if (!zoneSummary.ContainsKey(zone)) zoneSummary[zone] = 0;
                    zoneSummary[zone]++;
                }

                tx.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine("Room Zone Assignment Complete");
            sb.AppendLine(new string('═', 40));
            sb.AppendLine($"  Rooms processed: {rooms.Count}");
            sb.AppendLine($"  Zones assigned: {assigned}");
            sb.AppendLine($"  Skipped (existing): {skipped}");
            sb.AppendLine();
            sb.AppendLine("Zone Distribution:");
            foreach (var kvp in zoneSummary.OrderBy(k => k.Key))
                sb.AppendLine($"  {kvp.Key}: {kvp.Value} rooms");

            TaskDialog.Show("Zone Assignment", sb.ToString());
            StingLog.Info($"Room zone assignment: {assigned} assigned, {skipped} skipped");
            return Result.Succeeded;
        }

        private static string DeriveZone(string department, string roomName)
        {
            // Check department first
            if (!string.IsNullOrEmpty(department))
            {
                foreach (var kvp in DeptToZone)
                {
                    if (department.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kvp.Value;
                }
            }

            // Fall back to room name
            if (!string.IsNullOrEmpty(roomName))
            {
                foreach (var kvp in DeptToZone)
                {
                    if (roomName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kvp.Value;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Creates a comprehensive room schedule with area, department, zone,
    /// STING tags, and finish data fields.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string scheduleName = "STING - Room Schedule";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == scheduleName);

            if (existing != null)
            {
                var td = new TaskDialog("Room Schedule");
                td.MainInstruction = $"'{scheduleName}' already exists.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Recreate", "Delete and recreate");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "STING Room Schedule"))
            {
                tx.Start();

                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                var schedule = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_Rooms));
                schedule.Name = scheduleName;

                // Add fields
                var defs = schedule.Definition;
                var fields = defs.GetSchedulableFields();

                // Standard room fields
                AddFieldByName(doc, defs, fields, "Number");
                AddFieldByName(doc, defs, fields, "Name");
                AddFieldByName(doc, defs, fields, "Level");
                AddFieldByName(doc, defs, fields, "Department");
                AddFieldByBuiltIn(defs, fields, BuiltInParameter.ROOM_AREA);
                AddFieldByBuiltIn(defs, fields, BuiltInParameter.ROOM_PERIMETER);
                AddFieldByBuiltIn(defs, fields, BuiltInParameter.ROOM_VOLUME);

                // Finish fields
                AddFieldByBuiltIn(defs, fields, BuiltInParameter.ROOM_FINISH_FLOOR);
                AddFieldByBuiltIn(defs, fields, BuiltInParameter.ROOM_FINISH_WALL);
                AddFieldByBuiltIn(defs, fields, BuiltInParameter.ROOM_FINISH_CEILING);

                // STING tag fields
                AddFieldByName(doc, defs, fields, ParamRegistry.TAG1);
                AddFieldByName(doc, defs, fields, ParamRegistry.LOC);
                AddFieldByName(doc, defs, fields, ParamRegistry.ZONE);

                // Sort by Level then Number
                int fieldCount = defs.GetFieldCount();
                for (int i = 0; i < fieldCount; i++)
                {
                    var field = defs.GetField(i);
                    if (field.GetName() == "Level")
                    {
                        var sg = new ScheduleSortGroupField(field.FieldId);
                        sg.SortOrder = ScheduleSortOrder.Ascending;
                        sg.ShowHeader = true;
                        defs.AddSortGroupField(sg);
                    }
                    else if (field.GetName() == "Number")
                    {
                        var sg = new ScheduleSortGroupField(field.FieldId);
                        sg.SortOrder = ScheduleSortOrder.Ascending;
                        defs.AddSortGroupField(sg);
                    }
                }

                tx.Commit();
            }

            var roomCount = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .GetElementCount();

            TaskDialog.Show("Room Schedule",
                $"Schedule created: {scheduleName}\n\n" +
                $"  Rooms in project: {roomCount}\n" +
                "  Fields: Number, Name, Level, Department, Area, Perimeter,\n" +
                "  Volume, Floor Finish, Wall Finish, Ceiling Finish, Tag, LOC, ZONE");

            StingLog.Info($"Room schedule created: {roomCount} rooms");
            return Result.Succeeded;
        }

        private static void AddFieldByName(Document doc, ScheduleDefinition defs,
            IList<SchedulableField> fields, string fieldName)
        {
            try
            {
                foreach (var sf in fields)
                {
                    if (sf.GetName(doc).Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        defs.AddField(sf);
                        return;
                    }
                }
            }
            catch { }
        }

        private static void AddFieldByBuiltIn(ScheduleDefinition defs,
            IList<SchedulableField> fields, BuiltInParameter bip)
        {
            try
            {
                foreach (var sf in fields)
                {
                    if (sf.ParameterId == new ElementId(bip))
                    {
                        defs.AddField(sf);
                        return;
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Exports room data to CSV with comprehensive spatial information
    /// including area, volume, department, zone, finishes, and STING tags.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomDataExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Location != null)
                .OrderBy(r => r.Level?.Name ?? "")
                .ThenBy(r => r.Number ?? "")
                .ToList();

            if (rooms.Count == 0)
            {
                TaskDialog.Show("Room Export", "No placed rooms found.");
                return Result.Succeeded;
            }

            var csv = new StringBuilder();
            csv.AppendLine("Room_Number,Room_Name,Level,Department,Area_SqM,Perimeter_M," +
                "Volume_CuM,Floor_Finish,Wall_Finish,Ceiling_Finish," +
                "STING_Tag,LOC,ZONE,LVL,Status");

            double totalArea = 0;

            foreach (var room in rooms)
            {
                string number = room.Number ?? "";
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                string level = room.Level?.Name ?? "";
                string dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                double areaSqFt = room.Area;
                double areaSqM = areaSqFt * 0.092903;
                double perimFt = room.Perimeter;
                double perimM = perimFt * 0.3048;
                double volCuFt = room.Volume;
                double volCuM = volCuFt * 0.0283168;
                string floorFinish = room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)?.AsString() ?? "";
                string wallFinish = room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL)?.AsString() ?? "";
                string ceilFinish = room.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING)?.AsString() ?? "";
                string tag = ParameterHelpers.GetString(room, ParamRegistry.TAG1);
                string loc = ParameterHelpers.GetString(room, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(room, ParamRegistry.ZONE);
                string lvl = ParameterHelpers.GetString(room, ParamRegistry.LVL);
                string status = ParameterHelpers.GetString(room, ParamRegistry.STATUS);

                totalArea += areaSqM;

                csv.AppendLine($"\"{number}\",\"{name}\",\"{level}\",\"{dept}\"," +
                    $"{areaSqM:F2},{perimM:F2},{volCuM:F2}," +
                    $"\"{floorFinish}\",\"{wallFinish}\",\"{ceilFinish}\"," +
                    $"\"{tag}\",\"{loc}\",\"{zone}\",\"{lvl}\",\"{status}\"");
            }

            // Write CSV
            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string csvPath = Path.Combine(outputDir,
                $"STING_RoomData_{DateTime.Now:yyyyMMdd_HHmm}.csv");

            File.WriteAllText(csvPath, csv.ToString());

            // Summary by department
            var deptSummary = rooms
                .GroupBy(r => r.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "(No Department)")
                .OrderByDescending(g => g.Count());

            var sb = new StringBuilder();
            sb.AppendLine("Room Data Export Complete");
            sb.AppendLine(new string('═', 40));
            sb.AppendLine($"  Rooms exported: {rooms.Count}");
            sb.AppendLine($"  Total area: {totalArea:F1} m²");
            sb.AppendLine($"  File: {csvPath}");
            sb.AppendLine();
            sb.AppendLine("By Department:");
            foreach (var g in deptSummary)
                sb.AppendLine($"  {g.Key,-25} {g.Count(),4} rooms");

            TaskDialog.Show("Room Export", sb.ToString());
            StingLog.Info($"Room data exported: {rooms.Count} rooms → {csvPath}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Populates STING LOC and ZONE parameters on all elements based on
    /// the room they are located in. Batch spatial assignment command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomBasedParamPushCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            // Build room index
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            // Collect taggable elements
            var taggableCategories = SharedParamGuids.AllCategoryEnums;
            var allElements = new List<Element>();

            foreach (var bic in taggableCategories)
            {
                try
                {
                    var elems = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToList();
                    allElements.AddRange(elems);
                }
                catch { }
            }

            if (allElements.Count == 0)
            {
                TaskDialog.Show("Room Param Push", "No taggable elements found.");
                return Result.Succeeded;
            }

            var confirm = new TaskDialog("Room Param Push");
            confirm.MainInstruction = $"Push room data to {allElements.Count} elements?";
            confirm.MainContent =
                "This will update LOC and ZONE parameters on all taggable elements\n" +
                "based on their containing room. Only empty values will be set.\n\n" +
                $"Project LOC: {(string.IsNullOrEmpty(projectLoc) ? "(auto-detect)" : projectLoc)}";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int locSet = 0, zoneSet = 0, noRoom = 0;

            using (Transaction tx = new Transaction(doc, "STING Room-Based Param Push"))
            {
                tx.Start();

                foreach (var el in allElements)
                {
                    string existingLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                    string existingZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);

                    if (!string.IsNullOrEmpty(existingLoc) && !string.IsNullOrEmpty(existingZone))
                        continue;

                    string loc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                    string zone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);

                    if (string.IsNullOrEmpty(loc) && string.IsNullOrEmpty(zone))
                    {
                        noRoom++;
                        continue;
                    }

                    if (string.IsNullOrEmpty(existingLoc) && !string.IsNullOrEmpty(loc))
                    {
                        ParameterHelpers.SetString(el, ParamRegistry.LOC, loc, overwrite: false);
                        locSet++;
                    }

                    if (string.IsNullOrEmpty(existingZone) && !string.IsNullOrEmpty(zone))
                    {
                        ParameterHelpers.SetString(el, ParamRegistry.ZONE, zone, overwrite: false);
                        zoneSet++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Room Param Push",
                $"Room-Based Parameter Push Complete\n\n" +
                $"  Elements processed: {allElements.Count}\n" +
                $"  LOC values set: {locSet}\n" +
                $"  ZONE values set: {zoneSet}\n" +
                $"  No room found: {noRoom}");

            StingLog.Info($"Room param push: LOC={locSet}, ZONE={zoneSet}, noRoom={noRoom}");
            return Result.Succeeded;
        }
    }
}
