// ============================================================================
// ModelCommands.cs — IExternalCommand classes for the MODEL auto-modeling engine.
// Each command wraps a ModelEngine or CADToModelEngine method, collecting user
// input via TaskDialog prompts and delegating to the engine.
//
// Pattern: [Transaction(TransactionMode.Manual)] + [Regeneration(RegenerationOption.Manual)]
// All commands dispatched from StingCommandHandler via RunCommand<T>(app).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Model
{
    // ══════════════════════════════════════════════════════════════════
    // WALLS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a single straight wall between two points.
    /// User picks two points or enters coordinates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateWallCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                // Pick two points in the model
                var p1 = uidoc.Selection.PickPoint("Pick wall start point");
                var p2 = uidoc.Selection.PickPoint("Pick wall end point");

                var engine = new ModelEngine(doc);
                var result = engine.CreateWall(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm);

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Wall", result.Message);
                    if (result.CreatedElementIds.Count > 0)
                        uidoc.Selection.SetElementIds(result.CreatedElementIds);
                }
                else
                {
                    TaskDialog.Show("MODEL — Wall", $"Failed: {result.Message}");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreateWall failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ROOMS (rectangular enclosure)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a rectangular room enclosure (4 walls + floor + room element).
    /// User enters width, depth and optional room name.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                // Simple input dialog via TaskDialog
                var input = new TaskDialog("MODEL — Create Room");
                input.MainContent = "Create a rectangular room enclosure.\n\n" +
                    "Enter dimensions in the format: width,depth (mm)\n" +
                    "Example: 4000,3000 for a 4m×3m room\n\n" +
                    "Default: 4000,3000 (4m × 3m)";
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "4000 × 3000 mm (Small Room)");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "5000 × 4000 mm (Medium Room)");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "6000 × 5000 mm (Large Room)");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Pick origin point in model");
                input.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = input.Show();

                double widthMm, depthMm;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: widthMm = 4000; depthMm = 3000; break;
                    case TaskDialogResult.CommandLink2: widthMm = 5000; depthMm = 4000; break;
                    case TaskDialogResult.CommandLink3: widthMm = 6000; depthMm = 5000; break;
                    case TaskDialogResult.CommandLink4: widthMm = 4000; depthMm = 3000; break;
                    default: return Result.Cancelled;
                }

                double originXMm = 0, originYMm = 0;
                if (choice == TaskDialogResult.CommandLink4)
                {
                    var pt = uidoc.Selection.PickPoint("Pick room origin (bottom-left corner)");
                    originXMm = pt.X * Units.FeetToMm;
                    originYMm = pt.Y * Units.FeetToMm;
                }

                var engine = new ModelEngine(doc);
                var result = engine.CreateRectangularRoom(
                    widthMm, depthMm, "Room",
                    originXMm: originXMm, originYMm: originYMm);

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Room", result.Message);
                    if (result.CreatedElementIds.Count > 0)
                        uidoc.Selection.SetElementIds(result.CreatedElementIds);
                }
                else
                {
                    TaskDialog.Show("MODEL — Room", $"Failed: {result.Message}");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreateRoom failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // FLOOR
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a rectangular floor slab. User picks a point or uses default origin.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateFloorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var input = new TaskDialog("MODEL — Create Floor");
                input.MainContent = "Create a rectangular floor slab.\n\n" +
                    "Select floor size:";
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "5000 × 4000 mm");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "8000 × 6000 mm");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "10000 × 8000 mm");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Floor in selected room");
                input.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = input.Show();

                var engine = new ModelEngine(doc);
                ModelResult result;

                if (choice == TaskDialogResult.CommandLink4)
                {
                    // Floor from selected room
                    var selIds = uidoc.Selection.GetElementIds();
                    Room room = null;
                    foreach (var id in selIds)
                    {
                        room = doc.GetElement(id) as Room;
                        if (room != null) break;
                    }
                    if (room == null)
                    {
                        TaskDialog.Show("MODEL — Floor", "Please select a Room element first.");
                        return Result.Cancelled;
                    }
                    result = engine.CreateFloorInRoom(room);
                }
                else
                {
                    double widthMm, depthMm;
                    switch (choice)
                    {
                        case TaskDialogResult.CommandLink1: widthMm = 5000; depthMm = 4000; break;
                        case TaskDialogResult.CommandLink2: widthMm = 8000; depthMm = 6000; break;
                        case TaskDialogResult.CommandLink3: widthMm = 10000; depthMm = 8000; break;
                        default: return Result.Cancelled;
                    }

                    var pt = uidoc.Selection.PickPoint("Pick floor origin (bottom-left corner)");
                    result = engine.CreateFloor(widthMm, depthMm,
                        originXMm: pt.X * Units.FeetToMm,
                        originYMm: pt.Y * Units.FeetToMm);
                }

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Floor", result.Message);
                    if (result.CreatedElementIds.Count > 0)
                        uidoc.Selection.SetElementIds(result.CreatedElementIds);
                }
                else
                {
                    TaskDialog.Show("MODEL — Floor", $"Failed: {result.Message}");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreateFloor failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // CEILING
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a rectangular ceiling. User picks origin and size.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateCeilingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var pt = uidoc.Selection.PickPoint("Pick ceiling origin (bottom-left corner)");
                var engine = new ModelEngine(doc);
                var result = engine.CreateCeiling(5000, 4000,
                    originXMm: pt.X * Units.FeetToMm,
                    originYMm: pt.Y * Units.FeetToMm);

                if (result.Success)
                    TaskDialog.Show("MODEL — Ceiling", result.Message);
                else
                    TaskDialog.Show("MODEL — Ceiling", $"Failed: {result.Message}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreateCeiling failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ROOF
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a footprint roof over a rectangular area.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateRoofCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var pt = uidoc.Selection.PickPoint("Pick roof origin (bottom-left corner)");
                var engine = new ModelEngine(doc);
                var result = engine.CreateRoof(8000, 6000,
                    originXMm: pt.X * Units.FeetToMm,
                    originYMm: pt.Y * Units.FeetToMm);

                if (result.Success)
                    TaskDialog.Show("MODEL — Roof", result.Message);
                else
                    TaskDialog.Show("MODEL — Roof", $"Failed: {result.Message}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreateRoof failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // DOOR
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Places a door in a selected wall. User selects a wall, then picks a point on it.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelPlaceDoorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                // Pick wall host
                var wallRef = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    "Select a wall to place the door in");
                var wall = doc.GetElement(wallRef.ElementId) as Wall;
                if (wall == null)
                {
                    TaskDialog.Show("MODEL — Door", "Selected element is not a wall.");
                    return Result.Failed;
                }

                var engine = new ModelEngine(doc);
                var result = engine.PlaceDoor(wall);

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Door", result.Message);
                    if (result.CreatedElementIds.Count > 0)
                        uidoc.Selection.SetElementIds(result.CreatedElementIds);
                }
                else
                {
                    TaskDialog.Show("MODEL — Door", $"Failed: {result.Message}");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelPlaceDoor failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // WINDOW
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Places a window in a selected wall.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelPlaceWindowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var wallRef = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    "Select a wall to place the window in");
                var wall = doc.GetElement(wallRef.ElementId) as Wall;
                if (wall == null)
                {
                    TaskDialog.Show("MODEL — Window", "Selected element is not a wall.");
                    return Result.Failed;
                }

                var engine = new ModelEngine(doc);
                var result = engine.PlaceWindow(wall, sillHeightMm: 900);

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Window", result.Message);
                    if (result.CreatedElementIds.Count > 0)
                        uidoc.Selection.SetElementIds(result.CreatedElementIds);
                }
                else
                {
                    TaskDialog.Show("MODEL — Window", $"Failed: {result.Message}");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelPlaceWindow failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // COLUMN
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Places a structural column at a picked point.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelPlaceColumnCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var pt = uidoc.Selection.PickPoint("Pick column location");
                var engine = new ModelEngine(doc);
                var result = engine.PlaceColumn(
                    pt.X * Units.FeetToMm, pt.Y * Units.FeetToMm);

                if (result.Success)
                    TaskDialog.Show("MODEL — Column", result.Message);
                else
                    TaskDialog.Show("MODEL — Column", $"Failed: {result.Message}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelPlaceColumn failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // COLUMN GRID
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a rectangular grid of structural columns.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelColumnGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var input = new TaskDialog("MODEL — Column Grid");
                input.MainContent = "Create a rectangular column grid.\nSelect grid configuration:";
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "3×3 at 5m spacing");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "4×4 at 6m spacing");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "5×3 at 7m × 5m spacing");
                input.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = input.Show();

                int rows, cols;
                double spacingXMm, spacingYMm;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: rows = 3; cols = 3; spacingXMm = 5000; spacingYMm = 5000; break;
                    case TaskDialogResult.CommandLink2: rows = 4; cols = 4; spacingXMm = 6000; spacingYMm = 6000; break;
                    case TaskDialogResult.CommandLink3: rows = 5; cols = 3; spacingXMm = 7000; spacingYMm = 5000; break;
                    default: return Result.Cancelled;
                }

                var pt = uidoc.Selection.PickPoint("Pick grid origin");
                var engine = new ModelEngine(doc);
                var result = engine.PlaceColumnGrid(rows, cols, spacingXMm, spacingYMm,
                    originXMm: pt.X * Units.FeetToMm,
                    originYMm: pt.Y * Units.FeetToMm);

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Column Grid", result.Message);
                    if (result.CreatedElementIds.Count > 0)
                        uidoc.Selection.SetElementIds(result.CreatedElementIds);
                }
                else
                {
                    TaskDialog.Show("MODEL — Column Grid", $"Failed: {result.Message}");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelColumnGrid failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // BEAM
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a structural beam between two picked points.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var p1 = uidoc.Selection.PickPoint("Pick beam start point");
                var p2 = uidoc.Selection.PickPoint("Pick beam end point");

                var engine = new ModelEngine(doc);
                var result = engine.CreateBeam(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm, p1.Z * Units.FeetToMm,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm, p2.Z * Units.FeetToMm);

                if (result.Success)
                    TaskDialog.Show("MODEL — Beam", result.Message);
                else
                    TaskDialog.Show("MODEL — Beam", $"Failed: {result.Message}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreateBeam failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // DUCT
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a duct run between two picked points.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateDuctCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var p1 = uidoc.Selection.PickPoint("Pick duct start point");
                var p2 = uidoc.Selection.PickPoint("Pick duct end point");

                var engine = new ModelEngine(doc);
                var result = engine.CreateDuct(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm, 2700,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm, 2700);

                if (result.Success)
                    TaskDialog.Show("MODEL — Duct", result.Message);
                else
                    TaskDialog.Show("MODEL — Duct", $"Failed: {result.Message}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreateDuct failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // PIPE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a pipe run between two picked points.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreatePipeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var p1 = uidoc.Selection.PickPoint("Pick pipe start point");
                var p2 = uidoc.Selection.PickPoint("Pick pipe end point");

                var engine = new ModelEngine(doc);
                var result = engine.CreatePipe(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm, 0,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm, 0);

                if (result.Success)
                    TaskDialog.Show("MODEL — Pipe", result.Message);
                else
                    TaskDialog.Show("MODEL — Pipe", $"Failed: {result.Message}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCreatePipe failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // MEP FIXTURE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Places an MEP fixture (lighting, plumbing, electrical) at a picked point.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelPlaceFixtureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var input = new TaskDialog("MODEL — Place Fixture");
                input.MainContent = "Select fixture type to place:";
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Lighting Fixture");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Plumbing Fixture (Sink/WC)");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Electrical Outlet / Switch");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Mechanical Equipment");
                input.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = input.Show();
                string hint;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: hint = "light"; break;
                    case TaskDialogResult.CommandLink2: hint = "plumbing"; break;
                    case TaskDialogResult.CommandLink3: hint = "electrical"; break;
                    case TaskDialogResult.CommandLink4: hint = "mechanical"; break;
                    default: return Result.Cancelled;
                }

                var pt = uidoc.Selection.PickPoint("Pick fixture location");
                var engine = new ModelEngine(doc);
                var result = engine.PlaceMEPFixture(
                    pt.X * Units.FeetToMm, pt.Y * Units.FeetToMm, 0, hint);

                if (result.Success)
                    TaskDialog.Show("MODEL — Fixture", result.Message);
                else
                    TaskDialog.Show("MODEL — Fixture", $"Failed: {result.Message}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelPlaceFixture failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // BUILDING SHELL
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a complete building shell: 4 walls + floor + roof in one operation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelBuildingShellCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var input = new TaskDialog("MODEL — Building Shell");
                input.MainContent = "Create a complete building shell (walls + floor + roof).\n\nSelect building size:";
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Small: 6m × 5m × 3m");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Medium: 10m × 8m × 3.5m");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Large: 15m × 10m × 4m");
                input.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Pick origin and use Medium");
                input.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = input.Show();

                double widthMm, depthMm, heightMm;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: widthMm = 6000; depthMm = 5000; heightMm = 3000; break;
                    case TaskDialogResult.CommandLink2: widthMm = 10000; depthMm = 8000; heightMm = 3500; break;
                    case TaskDialogResult.CommandLink3: widthMm = 15000; depthMm = 10000; heightMm = 4000; break;
                    case TaskDialogResult.CommandLink4: widthMm = 10000; depthMm = 8000; heightMm = 3500; break;
                    default: return Result.Cancelled;
                }

                double originXMm = 0, originYMm = 0;
                if (choice == TaskDialogResult.CommandLink4)
                {
                    var pt = uidoc.Selection.PickPoint("Pick building origin (bottom-left corner)");
                    originXMm = pt.X * Units.FeetToMm;
                    originYMm = pt.Y * Units.FeetToMm;
                }

                var engine = new ModelEngine(doc);
                var result = engine.CreateBuildingShell(widthMm, depthMm,
                    wallHeightMm: heightMm, originXMm: originXMm, originYMm: originYMm);

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Building Shell", result.Message);
                    if (result.CreatedElementIds.Count > 0)
                        uidoc.Selection.SetElementIds(result.CreatedElementIds);
                }
                else
                {
                    TaskDialog.Show("MODEL — Building Shell", $"Failed: {result.Message}");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelBuildingShell failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // DWG TO MODEL (CAD Import)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converts a linked/imported DWG file to BIM elements.
    /// Detects walls from parallel lines, floors from closed loops,
    /// and auto-places rooms in enclosed areas.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelDWGToModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                // Find DWG imports
                var imports = CADToModelEngine.FindImportInstances(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("MODEL — DWG to Model",
                        "No DWG imports found in the project.\n\n" +
                        "Import or link a DWG file first:\n" +
                        "  Insert → Link CAD / Import CAD");
                    return Result.Cancelled;
                }

                // Let user choose if multiple
                ImportInstance target;
                if (imports.Count == 1)
                {
                    target = imports[0];
                }
                else
                {
                    var dlg = new TaskDialog("MODEL — DWG to Model");
                    dlg.MainContent = $"Found {imports.Count} DWG imports. Select the one to convert, or pick from the model.";
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Use first import: {GetImportName(doc, imports[0])}");
                    if (imports.Count > 1)
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Use: {GetImportName(doc, imports[1])}");
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Pick from model");
                    dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var choice = dlg.Show();
                    switch (choice)
                    {
                        case TaskDialogResult.CommandLink1: target = imports[0]; break;
                        case TaskDialogResult.CommandLink2: target = imports.Count > 1 ? imports[1] : imports[0]; break;
                        case TaskDialogResult.CommandLink3:
                            var objRef = uidoc.Selection.PickObject(
                                Autodesk.Revit.UI.Selection.ObjectType.Element,
                                "Select a DWG import instance");
                            target = doc.GetElement(objRef.ElementId) as ImportInstance;
                            if (target == null)
                            {
                                TaskDialog.Show("MODEL — DWG to Model", "Selected element is not a DWG import.");
                                return Result.Failed;
                            }
                            break;
                        default: return Result.Cancelled;
                    }
                }

                // Conversion options
                var optDlg = new TaskDialog("MODEL — Conversion Options");
                optDlg.MainContent = "What should be created from the DWG?";
                optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Walls + Rooms + Floors (Full conversion)");
                optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Walls only");
                optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Preview only (no creation — audit layers)");
                optDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var optChoice = optDlg.Show();

                var cadEngine = new CADToModelEngine(doc);

                if (optChoice == TaskDialogResult.CommandLink3)
                {
                    // Preview mode
                    var preview = cadEngine.PreviewImport(target);
                    var sb = new StringBuilder();
                    sb.AppendLine($"DWG Preview: {preview.TotalEntities} entities");
                    sb.AppendLine($"Layers: {preview.LayerCounts.Count}");
                    sb.AppendLine($"Lines extracted: {preview.Lines.Count}");
                    sb.AppendLine();
                    sb.AppendLine("Layer breakdown:");
                    foreach (var kv in preview.LayerCounts.OrderByDescending(x => x.Value).Take(20))
                    {
                        var cat = LayerMapper.InferCategory(kv.Key);
                        sb.AppendLine($"  {kv.Key}: {kv.Value} entities" +
                            (cat != null ? $" → {cat}" : ""));
                    }
                    if (preview.Walls.Count > 0)
                        sb.AppendLine($"\nDetected wall candidates: {preview.Walls.Count}");
                    if (preview.Loops.Count > 0)
                        sb.AppendLine($"Detected floor boundaries: {preview.Loops.Count}");

                    TaskDialog.Show("MODEL — DWG Preview", sb.ToString());
                    return Result.Succeeded;
                }

                bool createWalls = true;
                bool createFloors = optChoice == TaskDialogResult.CommandLink1;
                bool createRooms = optChoice == TaskDialogResult.CommandLink1;

                var convResult = cadEngine.ConvertImportToElements(target,
                    createWalls: createWalls,
                    createFloors: createFloors,
                    createRooms: createRooms);

                var report = new StringBuilder();
                report.AppendLine(convResult.Summary);
                if (convResult.Warnings.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine($"Warnings ({convResult.Warnings.Count}):");
                    foreach (var w in convResult.Warnings.Take(10))
                        report.AppendLine($"  • {w}");
                    if (convResult.Warnings.Count > 10)
                        report.AppendLine($"  ... and {convResult.Warnings.Count - 10} more");
                }

                TaskDialog.Show("MODEL — DWG to Model", report.ToString());

                if (convResult.CreatedElementIds.Count > 0)
                    uidoc.Selection.SetElementIds(convResult.CreatedElementIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelDWGToModel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string GetImportName(Document doc, ImportInstance import)
        {
            try
            {
                var typeId = import.GetTypeId();
                var type = doc.GetElement(typeId);
                return type?.Name ?? $"Import #{import.Id.Value}";
            }
            catch
            {
                return $"Import #{import.Id.Value}";
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // DWG PREVIEW (audit only)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Previews a DWG import: shows layer counts and detected elements
    /// without creating anything. Pre-conversion audit.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelDWGPreviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var imports = CADToModelEngine.FindImportInstances(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("MODEL — DWG Preview", "No DWG imports found.");
                    return Result.Cancelled;
                }

                var target = imports[0];
                if (imports.Count > 1)
                {
                    var objRef = uidoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        "Select a DWG import to preview");
                    target = doc.GetElement(objRef.ElementId) as ImportInstance ?? imports[0];
                }

                var cadEngine = new CADToModelEngine(doc);
                var preview = cadEngine.PreviewImport(target);

                var sb = new StringBuilder();
                sb.AppendLine($"DWG Import Analysis");
                sb.AppendLine(new string('─', 40));
                sb.AppendLine($"Total entities: {preview.TotalEntities}");
                sb.AppendLine($"Total layers: {preview.LayerCounts.Count}");
                sb.AppendLine($"Lines extracted: {preview.Lines.Count}");
                sb.AppendLine();

                // Categorize lines
                var byCat = preview.Lines.GroupBy(l => l.Category ?? "(unknown)")
                    .OrderByDescending(g => g.Count());
                sb.AppendLine("By detected category:");
                foreach (var g in byCat)
                    sb.AppendLine($"  {g.Key}: {g.Count()} lines");

                sb.AppendLine();
                sb.AppendLine("Top layers:");
                foreach (var kv in preview.LayerCounts.OrderByDescending(x => x.Value).Take(15))
                {
                    var cat = LayerMapper.InferCategory(kv.Key);
                    sb.AppendLine($"  {kv.Key}: {kv.Value}" + (cat != null ? $" → [{cat}]" : ""));
                }

                TaskDialog.Show("MODEL — DWG Preview", sb.ToString());
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelDWGPreview failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
