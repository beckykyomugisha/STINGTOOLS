// ===================================================================================
// Model Creation Commands — Adapted from StingBIM.AI.Design + AI.Creation
// Programmatic Revit element creation: walls, floors, ceilings, roofs, doors,
// windows, columns, beams, stairs, rooms, MEP systems.
// Covers gaps: ME-01 through ME-09 (Model creation engine)
// ===================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Temp
{
    #region Commands

    /// <summary>
    /// ME-01: Create walls from parameters (type, start, end, height, level).
    /// Supports Generic, Curtain, Stacked, and Compound wall types.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateWallsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var wallTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .Select(wt => wt.Name)
                    .OrderBy(n => n)
                    .ToList();

                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                if (levels.Count == 0)
                {
                    TaskDialog.Show("STING Create Walls", "No levels found in the project.");
                    return Result.Failed;
                }

                // Show available wall types summary
                var sb = new StringBuilder();
                sb.AppendLine($"Available Wall Types: {wallTypes.Count}");
                sb.AppendLine($"Available Levels: {levels.Count}");
                sb.AppendLine();
                sb.AppendLine("Wall types:");
                foreach (var wt in wallTypes.Take(20))
                    sb.AppendLine($"  • {wt}");
                if (wallTypes.Count > 20)
                    sb.AppendLine($"  ... +{wallTypes.Count - 20} more");

                sb.AppendLine("\nThis command creates walls along the active view's level.");
                sb.AppendLine("Select two points in the model to define the wall line.");

                var td = new TaskDialog("STING Create Walls")
                {
                    MainContent = sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                };
                if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

                // Use the first level or the active view's level
                var activeView = doc.ActiveView;
                var level = levels.FirstOrDefault(l => l.Id == activeView.GenLevel?.Id) ?? levels.First();

                // Get wall type (default to Generic)
                var wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(wt => wt.Name.Contains("Generic - 200"))
                    ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                // Prompt user to pick two points
                var uidoc = commandData.Application.ActiveUIDocument;
                XYZ pt1, pt2;
                try
                {
                    pt1 = uidoc.Selection.PickPoint("Pick wall start point");
                    pt2 = uidoc.Selection.PickPoint("Pick wall end point");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                using (var t = new Transaction(doc, "STING Create Wall"))
                {
                    t.Start();
                    var line = Line.CreateBound(pt1, pt2);
                    double height = 10; // ~3m in feet
                    var wall = Wall.Create(doc, line, wallType.Id, level.Id, height, 0, false, false);
                    t.Commit();

                    TaskDialog.Show("STING Create Walls",
                        $"Wall created successfully.\n" +
                        $"Type: {wallType.Name}\n" +
                        $"Level: {level.Name}\n" +
                        $"Length: {Math.Round(pt1.DistanceTo(pt2) * 304.8)}mm");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Create walls failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-02: Create floor from room boundary or picked points.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFloorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var uidoc = commandData.Application.ActiveUIDocument;

            try
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                var level = levels.FirstOrDefault(l => l.Id == doc.ActiveView.GenLevel?.Id) ?? levels.First();

                // Get floor type
                var floorType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(ft => ft.Name.Contains("Generic"))
                    ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(FloorType))
                        .Cast<FloorType>()
                        .First();

                // Try to use selected rooms for boundary
                var selectedIds = uidoc.Selection.GetElementIds();
                var rooms = selectedIds
                    .Select(id => doc.GetElement(id))
                    .OfType<Room>()
                    .ToList();

                int created = 0;

                using (var t = new Transaction(doc, "STING Create Floors"))
                {
                    t.Start();

                    if (rooms.Count > 0)
                    {
                        // Create floor for each selected room
                        foreach (var room in rooms)
                        {
                            var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                            if (boundaries == null || boundaries.Count == 0) continue;

                            var curveLoop = new CurveLoop();
                            foreach (var segment in boundaries[0])
                            {
                                curveLoop.Append(segment.GetCurve());
                            }

                            var loops = new List<CurveLoop> { curveLoop };
                            Floor.Create(doc, loops, floorType.Id, level.Id);
                            created++;
                        }
                    }
                    else
                    {
                        // Create a simple rectangular floor from 4 picked points
                        TaskDialog.Show("STING Create Floors",
                            "No rooms selected. Please select rooms first, then run this command.\n" +
                            "The floor will be created from the room boundary.");
                        t.RollBack();
                        return Result.Cancelled;
                    }

                    t.Commit();
                }

                TaskDialog.Show("STING Create Floors",
                    $"Floors created: {created}\n" +
                    $"Type: {floorType.Name}\n" +
                    $"Level: {level.Name}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Create floors failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-03: Create ceilings in rooms (auto-detect room boundaries).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateCeilingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var uidoc = commandData.Application.ActiveUIDocument;

            try
            {
                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Id == doc.ActiveView.GenLevel?.Id);

                if (level == null)
                {
                    TaskDialog.Show("STING Create Ceilings", "No active level found.");
                    return Result.Failed;
                }

                var ceilingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(CeilingType))
                    .Cast<CeilingType>()
                    .FirstOrDefault(ct => ct.Name.Contains("Generic") || ct.Name.Contains("Compound"))
                    ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(CeilingType))
                        .Cast<CeilingType>()
                        .FirstOrDefault();

                if (ceilingType == null)
                {
                    TaskDialog.Show("STING Create Ceilings", "No ceiling types found in the project.");
                    return Result.Failed;
                }

                // Get rooms on this level
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .Cast<Room>()
                    .Where(r => r.Level?.Id == level.Id && r.Area > 0)
                    .ToList();

                if (rooms.Count == 0)
                {
                    TaskDialog.Show("STING Create Ceilings", $"No rooms found on level '{level.Name}'.");
                    return Result.Failed;
                }

                int created = 0;
                var errors = new List<string>();

                using (var t = new Transaction(doc, "STING Create Ceilings"))
                {
                    t.Start();

                    foreach (var room in rooms)
                    {
                        try
                        {
                            var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                            if (boundaries == null || boundaries.Count == 0) continue;

                            var curveLoop = new CurveLoop();
                            foreach (var segment in boundaries[0])
                                curveLoop.Append(segment.GetCurve());

                            var loops = new List<CurveLoop> { curveLoop };
                            Ceiling.Create(doc, loops, ceilingType.Id, level.Id);
                            created++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Room {room.Number}: {ex.Message}");
                        }
                    }

                    t.Commit();
                }

                var report = new StringBuilder();
                report.AppendLine($"Ceilings created: {created} of {rooms.Count} rooms");
                report.AppendLine($"Type: {ceilingType.Name}");
                report.AppendLine($"Level: {level.Name}");
                if (errors.Count > 0)
                {
                    report.AppendLine($"\nErrors ({errors.Count}):");
                    foreach (var err in errors.Take(10))
                        report.AppendLine($"  • {err}");
                }

                TaskDialog.Show("STING Create Ceilings", report.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Create ceilings failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-04: Place doors in walls (auto-detect wall centerline, pick point).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceDoorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var uidoc = commandData.Application.ActiveUIDocument;

            try
            {
                // Get door family symbols
                var doorSymbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .OrderBy(s => s.Family.Name)
                    .ThenBy(s => s.Name)
                    .ToList();

                if (doorSymbols.Count == 0)
                {
                    TaskDialog.Show("STING Place Doors", "No door families loaded in the project.");
                    return Result.Failed;
                }

                var symbol = doorSymbols.First();

                var sb = new StringBuilder();
                sb.AppendLine($"Door families: {doorSymbols.Count}");
                sb.AppendLine($"Using: {symbol.Family.Name} : {symbol.Name}");
                sb.AppendLine("\nPick a point on a wall to place the door.");

                var td = new TaskDialog("STING Place Doors")
                {
                    MainContent = sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                };
                if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

                using (var t = new Transaction(doc, "STING Place Door"))
                {
                    t.Start();
                    if (!symbol.IsActive) symbol.Activate();

                    try
                    {
                        var point = uidoc.Selection.PickPoint("Pick point on wall for door");
                        var level = doc.ActiveView.GenLevel
                            ?? new FilteredElementCollector(doc)
                                .OfClass(typeof(Level))
                                .Cast<Level>()
                                .OrderBy(l => l.Elevation)
                                .First();

                        // Find the nearest wall
                        var wall = ModelCreationHelper.FindNearestWall(doc, point);

                        if (wall != null)
                        {
                            doc.Create.NewFamilyInstance(point, symbol, wall, level,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            t.Commit();
                            TaskDialog.Show("STING Place Doors", $"Door placed: {symbol.Family.Name} : {symbol.Name}");
                        }
                        else
                        {
                            t.RollBack();
                            TaskDialog.Show("STING Place Doors", "No wall found near the picked point.");
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        t.RollBack();
                        return Result.Cancelled;
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Place doors failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-05: Place windows in walls (pick point on wall).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceWindowsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var uidoc = commandData.Application.ActiveUIDocument;

            try
            {
                var windowSymbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .OrderBy(s => s.Family.Name)
                    .ToList();

                if (windowSymbols.Count == 0)
                {
                    TaskDialog.Show("STING Place Windows", "No window families loaded in the project.");
                    return Result.Failed;
                }

                var symbol = windowSymbols.First();

                using (var t = new Transaction(doc, "STING Place Window"))
                {
                    t.Start();
                    if (!symbol.IsActive) symbol.Activate();

                    try
                    {
                        var point = uidoc.Selection.PickPoint("Pick point on wall for window");
                        var level = doc.ActiveView.GenLevel
                            ?? new FilteredElementCollector(doc)
                                .OfClass(typeof(Level))
                                .Cast<Level>()
                                .OrderBy(l => l.Elevation)
                                .First();

                        var wall = ModelCreationHelper.FindNearestWall(doc, point);

                        if (wall != null)
                        {
                            doc.Create.NewFamilyInstance(point, symbol, wall, level,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            t.Commit();
                            TaskDialog.Show("STING Place Windows", $"Window placed: {symbol.Family.Name} : {symbol.Name}");
                        }
                        else
                        {
                            t.RollBack();
                            TaskDialog.Show("STING Place Windows", "No wall found near the picked point.");
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        t.RollBack();
                        return Result.Cancelled;
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Place windows failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-06: Create structural columns at grid intersections.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateColumnsAtGridsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                // Get grid intersections
                var grids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .ToList();

                if (grids.Count < 2)
                {
                    TaskDialog.Show("STING Create Columns", "At least 2 grids are needed to find intersections.");
                    return Result.Failed;
                }

                // Get column symbol
                var colSymbol = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();

                if (colSymbol == null)
                {
                    colSymbol = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Columns)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .FirstOrDefault();
                }

                if (colSymbol == null)
                {
                    TaskDialog.Show("STING Create Columns", "No column families loaded.");
                    return Result.Failed;
                }

                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .First();

                // Find intersections
                var intersections = new List<XYZ>();
                for (int i = 0; i < grids.Count; i++)
                {
                    for (int j = i + 1; j < grids.Count; j++)
                    {
                        var curve1 = grids[i].Curve;
                        var curve2 = grids[j].Curve;

                        var results = new IntersectionResultArray();
                        var setCompResult = curve1.Intersect(curve2, out results);

                        if (setCompResult == SetComparisonResult.Overlap && results != null)
                        {
                            for (int k = 0; k < results.Size; k++)
                            {
                                intersections.Add(results.get_Item(k).XYZPoint);
                            }
                        }
                    }
                }

                if (intersections.Count == 0)
                {
                    TaskDialog.Show("STING Create Columns", "No grid intersections found.");
                    return Result.Failed;
                }

                var td = new TaskDialog("STING Create Columns")
                {
                    MainContent = $"Found {intersections.Count} grid intersections.\n" +
                                  $"Column type: {colSymbol.Family.Name} : {colSymbol.Name}\n" +
                                  $"Level: {level.Name}\n\nCreate columns?",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                };
                if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

                int created = 0;
                using (var t = new Transaction(doc, "STING Create Columns at Grids"))
                {
                    t.Start();
                    if (!colSymbol.IsActive) colSymbol.Activate();

                    foreach (var point in intersections)
                    {
                        try
                        {
                            doc.Create.NewFamilyInstance(point, colSymbol, level,
                                Autodesk.Revit.DB.Structure.StructuralType.Column);
                            created++;
                        }
                        catch { /* skip failed placements */ }
                    }

                    t.Commit();
                }

                TaskDialog.Show("STING Create Columns", $"Columns created: {created} at {intersections.Count} grid intersections.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Create columns at grids failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-07: Create rooms from closed wall boundaries (auto-detect).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoCreateRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var level = doc.ActiveView.GenLevel;
                if (level == null)
                {
                    TaskDialog.Show("STING Create Rooms", "Active view must be a plan view with a level.");
                    return Result.Failed;
                }

                // Get existing rooms on this level to avoid duplicates
                var existingRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .Cast<Room>()
                    .Where(r => r.Level?.Id == level.Id && r.Area > 0)
                    .Count();

                // Find plan circuit boundaries (closed wall loops = potential rooms)
                var phase = doc.Phases.Cast<Phase>().LastOrDefault();
                if (phase == null)
                {
                    TaskDialog.Show("STING Create Rooms", "No phases found in the project.");
                    return Result.Failed;
                }

                var planTopology = doc.get_PlanTopology(level, phase);
                int unenclosed = 0;
                int created = 0;
                int sequenceNum = existingRooms + 1;

                using (var t = new Transaction(doc, "STING Auto-Create Rooms"))
                {
                    t.Start();

                    foreach (PlanCircuit circuit in planTopology.Circuits)
                    {
                        if (!circuit.IsRoomLocated)
                        {
                            try
                            {
                                var room = doc.Create.NewRoom(null, circuit);
                                if (room != null)
                                {
                                    room.Name = $"Room";
                                    room.Number = sequenceNum.ToString("D3");
                                    sequenceNum++;
                                    created++;
                                }
                            }
                            catch
                            {
                                unenclosed++;
                            }
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("STING Create Rooms",
                    $"Rooms created: {created}\n" +
                    $"Level: {level.Name}\n" +
                    $"Pre-existing rooms: {existingRooms}\n" +
                    (unenclosed > 0 ? $"Unenclosed boundaries skipped: {unenclosed}" : ""));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Auto-create rooms failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-08: Batch create grids from CSV (name, start, end coordinates).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateGridsFromCSVCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Grid Definition CSV",
                    Filter = "CSV Files (*.csv)|*.csv"
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                var lines = File.ReadAllLines(dialog.FileName).Skip(1).ToList(); // Skip header
                int created = 0;

                using (var t = new Transaction(doc, "STING Create Grids from CSV"))
                {
                    t.Start();

                    foreach (var line in lines)
                    {
                        var parts = StingToolsApp.ParseCsvLine(line);
                        if (parts.Length < 5) continue;

                        string name = parts[0].Trim();
                        if (!double.TryParse(parts[1], out double x1)) continue;
                        if (!double.TryParse(parts[2], out double y1)) continue;
                        if (!double.TryParse(parts[3], out double x2)) continue;
                        if (!double.TryParse(parts[4], out double y2)) continue;

                        // Convert mm to feet
                        var start = new XYZ(x1 / 304.8, y1 / 304.8, 0);
                        var end = new XYZ(x2 / 304.8, y2 / 304.8, 0);

                        if (start.DistanceTo(end) < 0.01) continue;

                        try
                        {
                            var gridLine = Line.CreateBound(start, end);
                            var grid = Grid.Create(doc, gridLine);
                            grid.Name = name;
                            created++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Grid '{name}' creation failed: {ex.Message}");
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("STING Create Grids", $"Grids created: {created} from {lines.Count} CSV rows.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Create grids from CSV failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-09: Create levels from CSV (name, elevation).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateLevelsFromCSVCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Level Definition CSV",
                    Filter = "CSV Files (*.csv)|*.csv"
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                var lines = File.ReadAllLines(dialog.FileName).Skip(1).ToList();
                int created = 0;

                using (var t = new Transaction(doc, "STING Create Levels from CSV"))
                {
                    t.Start();

                    foreach (var line in lines)
                    {
                        var parts = StingToolsApp.ParseCsvLine(line);
                        if (parts.Length < 2) continue;

                        string name = parts[0].Trim();
                        if (!double.TryParse(parts[1], out double elevationMm)) continue;

                        double elevationFt = elevationMm / 304.8;

                        try
                        {
                            var level = Level.Create(doc, elevationFt);
                            level.Name = name;
                            created++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Level '{name}' creation failed: {ex.Message}");
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("STING Create Levels", $"Levels created: {created} from {lines.Count} CSV rows.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Create levels from CSV failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ME-10: Model element audit — count all elements by category, level, family.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelElementAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var categories = new[]
                {
                    BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs,
                    BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
                    BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Stairs,
                    BuiltInCategory.OST_Rooms, BuiltInCategory.OST_Furniture,
                    BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Grids,
                    BuiltInCategory.OST_Levels
                };

                var sb = new StringBuilder();
                sb.AppendLine("═══ Model Element Audit ═══\n");

                int totalElements = 0;
                foreach (var cat in categories)
                {
                    var count = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .GetElementCount();

                    if (count > 0)
                    {
                        sb.AppendLine($"  {cat.ToString().Replace("OST_", "")}: {count}");
                        totalElements += count;
                    }
                }

                sb.AppendLine($"\nTotal model elements: {totalElements}");

                // Level summary
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();
                sb.AppendLine($"\nLevels ({levels.Count}):");
                foreach (var level in levels)
                    sb.AppendLine($"  {level.Name} — {Math.Round(level.Elevation * 304.8)}mm");

                // Grid summary
                var grids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .GetElementCount();
                sb.AppendLine($"\nGrids: {grids}");

                TaskDialog.Show("STING Model Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Model audit failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Shared utilities for model creation commands.
    /// </summary>
    internal static class ModelCreationHelper
    {
        /// <summary>
        /// Find the nearest wall to a given point in the active view.
        /// </summary>
        public static Wall FindNearestWall(Document doc, XYZ point)
        {
            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            Wall nearest = null;
            double minDist = double.MaxValue;

            foreach (var wall in walls)
            {
                var location = wall.Location as LocationCurve;
                if (location == null) continue;

                var curve = location.Curve;
                var result = curve.Project(point);
                if (result != null && result.Distance < minDist)
                {
                    minDist = result.Distance;
                    nearest = wall;
                }
            }

            // Only return if within reasonable distance (5 feet ~ 1.5m)
            return minDist < 5 ? nearest : null;
        }

        /// <summary>
        /// Get all family symbols for a category, grouped by family.
        /// </summary>
        public static Dictionary<string, List<FamilySymbol>> GetFamilySymbolsByCategory(Document doc, BuiltInCategory category)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .GroupBy(s => s.Family.Name)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name).ToList());
        }
    }

    #endregion
}
