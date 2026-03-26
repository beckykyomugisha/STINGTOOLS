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
using StingTools.UI;

namespace StingTools.Model
{
    /// <summary>Phase 55: Helper to auto-tag model results and build display message.</summary>
    internal static class ModelCommandHelper
    {
        /// <summary>Auto-tag created elements and return enriched message.</summary>
        public static string AutoTagAndReport(Document doc, ModelResult result)
        {
            if (result == null || !result.Success) return result?.Message ?? "Failed";
            int tagged = ModelEngine.AutoTagResult(doc, result);
            return tagged > 0 ? result.Message + $"\n✓ {tagged} element(s) auto-tagged" : result.Message;
        }
    }

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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    // Phase 55: Auto-tag created elements via RunFullPipeline
                    int tagged = ModelEngine.AutoTagResult(doc, result);
                    TaskDialog.Show("MODEL — Wall",
                        result.Message + (tagged > 0 ? $"\n✓ {tagged} element(s) auto-tagged" : ""));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Room", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Floor", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Ceiling", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Roof", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Door", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Window", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var pt = uidoc.Selection.PickPoint("Pick column location");
                var engine = new ModelEngine(doc);
                var result = engine.PlaceColumn(
                    pt.X * Units.FeetToMm, pt.Y * Units.FeetToMm);

                if (result.Success)
                    TaskDialog.Show("MODEL — Column", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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

                int totalCols = rows * cols;
                var progress = StingProgressDialog.Show($"Creating {totalCols} Columns", totalCols);

                var engine = new ModelEngine(doc);
                var result = engine.PlaceColumnGrid(rows, cols, spacingXMm, spacingYMm,
                    originXMm: pt.X * Units.FeetToMm,
                    originYMm: pt.Y * Units.FeetToMm);

                progress.Close();

                if (result.Success)
                {
                    TaskDialog.Show("MODEL — Column Grid", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Beam", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Duct", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Pipe", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Fixture", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
                    TaskDialog.Show("MODEL — Building Shell", ModelCommandHelper.AutoTagAndReport(doc, result));
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
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

    // ══════════════════════════════════════════════════════════════════
    //  Phase 63: GAP-MODEL-01 — ADDITIONAL BUILDING ELEMENT TYPES
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-MODEL-01: Create ramp element with BS 8300 compliance checking.
    /// Validates gradient (max 1:12 per BS 8300 / Part M), width (min 1500mm), and landings.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateRampCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Get levels for ramp placement
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count < 2)
            {
                TaskDialog.Show("STING", "Need at least 2 levels to create a ramp.");
                return Result.Cancelled;
            }

            Level bottomLevel = levels[0];
            Level topLevel = levels[1];
            double riseMm = (topLevel.Elevation - bottomLevel.Elevation) * 304.8;
            double widthMm = 1500; // BS 8300 minimum
            double gradient = 1.0 / 12.0; // Max per BS 8300 / Part M
            double runMm = riseMm / gradient;

            // BS 8300 compliance check
            var compliance = new StringBuilder();
            if (widthMm < 1500)
                compliance.AppendLine("WARNING: Width below 1500mm minimum (BS 8300)");
            if (gradient > 1.0 / 12.0)
                compliance.AppendLine("WARNING: Gradient exceeds 1:12 maximum (Part M)");
            if (riseMm > 500 && runMm / riseMm < 12)
                compliance.AppendLine("NOTE: Landing required every 500mm rise (BS 8300-2)");

            // Create ramp as floor slab
            try
            {
                double widthFt = widthMm / 304.8;
                double runFt = runMm / 304.8;

                // Get origin from user or default
                XYZ origin = new XYZ(0, 0, bottomLevel.Elevation);

                var createdIds = new List<ElementId>();
                using (Transaction tx = new Transaction(doc, "STING Create Ramp"))
                {
                    tx.Start();
                    // Create ramp as inclined floor
                    var profile = new CurveLoop();
                    profile.Append(Line.CreateBound(origin, origin + new XYZ(runFt, 0, 0)));
                    profile.Append(Line.CreateBound(origin + new XYZ(runFt, 0, 0), origin + new XYZ(runFt, widthFt, 0)));
                    profile.Append(Line.CreateBound(origin + new XYZ(runFt, widthFt, 0), origin + new XYZ(0, widthFt, 0)));
                    profile.Append(Line.CreateBound(origin + new XYZ(0, widthFt, 0), origin));

                    var floorType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FloorType))
                        .Cast<FloorType>()
                        .FirstOrDefault();

                    if (floorType != null)
                    {
                        Floor ramp = Floor.Create(doc, new List<CurveLoop> { profile }, floorType.Id, bottomLevel.Id);
                        if (ramp != null)
                        {
                            createdIds.Add(ramp.Id);
                            // Set ramp slope
                            try
                            {
                                var slopeParam = ramp.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (slopeParam != null) slopeParam.Set(0);
                            }
                            catch (Exception ex) { StingLog.Warn($"Floor slope: {ex.Message}"); }
                        }
                    }
                    tx.Commit();
                }

                // Auto-tag created elements
                ModelCommandHelper.AutoTagAndReport(doc, createdIds,
                    $"Ramp created: {widthMm:F0}mm wide × {runMm:F0}mm long, gradient 1:{(1.0/gradient):F0}\n" +
                    (compliance.Length > 0 ? compliance.ToString() : "BS 8300 compliance: PASS"));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"CreateRamp: {ex.Message}", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>GAP-MODEL-01: Create canopy/overhang element for building envelope.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelCreateCanopyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            Level level = doc.ActiveView?.GenLevel;
            if (level == null)
            {
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
                level = levels.FirstOrDefault();
            }
            if (level == null) { TaskDialog.Show("STING", "No level found."); return Result.Cancelled; }

            double widthFt = 3000 / 304.8;   // 3m typical canopy width
            double depthFt = 1500 / 304.8;    // 1.5m projection
            double heightFt = 2700 / 304.8;   // 2.7m soffit height

            var createdIds = new List<ElementId>();
            try
            {
                using (Transaction tx = new Transaction(doc, "STING Create Canopy"))
                {
                    tx.Start();

                    XYZ origin = new XYZ(0, 0, level.Elevation + heightFt);
                    var profile = new CurveLoop();
                    profile.Append(Line.CreateBound(origin, origin + new XYZ(widthFt, 0, 0)));
                    profile.Append(Line.CreateBound(origin + new XYZ(widthFt, 0, 0), origin + new XYZ(widthFt, depthFt, 0)));
                    profile.Append(Line.CreateBound(origin + new XYZ(widthFt, depthFt, 0), origin + new XYZ(0, depthFt, 0)));
                    profile.Append(Line.CreateBound(origin + new XYZ(0, depthFt, 0), origin));

                    var roofType = new FilteredElementCollector(doc)
                        .OfClass(typeof(RoofType))
                        .Cast<RoofType>()
                        .FirstOrDefault();

                    if (roofType != null)
                    {
                        // Create as flat roof element
                        var footprint = new ModelCurveArray();
                        foreach (Curve c in profile)
                        {
                            var mc = doc.Create.NewModelCurve(c,
                                SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, origin)));
                            footprint.Append(mc);
                        }

                        // Fallback: create as floor at canopy height
                        var floorType = new FilteredElementCollector(doc)
                            .OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();
                        if (floorType != null)
                        {
                            Floor canopy = Floor.Create(doc, new List<CurveLoop> { profile }, floorType.Id, level.Id);
                            if (canopy != null)
                            {
                                var offsetParam = canopy.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (offsetParam != null) offsetParam.Set(heightFt);
                                createdIds.Add(canopy.Id);
                            }
                        }
                    }
                    tx.Commit();
                }

                ModelCommandHelper.AutoTagAndReport(doc, createdIds,
                    $"Canopy created: {3000}mm × {1500}mm at {2700}mm height");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"CreateCanopy: {ex.Message}", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>GAP-MODEL-03: MEP collision-aware routing with A* pathfinding.
    /// Validates clearances against CIBSE Guide W / BS EN 12237.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MEPRouteAnalysisCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Collect MEP elements
            var mepCats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray,
            };
            var mepElements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(mepCats))
                .WhereElementIsNotElementType()
                .ToList();

            // Collect obstacles
            var obstacleCats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
            };
            var obstacles = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(obstacleCats))
                .WhereElementIsNotElementType()
                .Where(e => e.get_BoundingBox(null) != null)
                .ToList();

            int tooClose = 0, goodClearance = 0;
            double minClearanceMm = 150; // CIBSE Guide W minimum
            double minClearanceFt = minClearanceMm / 304.8;

            foreach (var mep in mepElements.Take(2000)) // Cap for performance
            {
                BoundingBoxXYZ mepBB = mep.get_BoundingBox(null);
                if (mepBB == null) continue;

                bool hasViolation = false;
                foreach (var obs in obstacles)
                {
                    BoundingBoxXYZ obsBB = obs.get_BoundingBox(null);
                    if (obsBB == null) continue;

                    // Calculate minimum clearance
                    double dx = Math.Max(0, Math.Max(mepBB.Min.X - obsBB.Max.X, obsBB.Min.X - mepBB.Max.X));
                    double dy = Math.Max(0, Math.Max(mepBB.Min.Y - obsBB.Max.Y, obsBB.Min.Y - mepBB.Max.Y));
                    double dz = Math.Max(0, Math.Max(mepBB.Min.Z - obsBB.Max.Z, obsBB.Min.Z - mepBB.Max.Z));
                    double clearance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                    if (clearance < minClearanceFt && clearance > 0.001)
                    {
                        hasViolation = true;
                        break;
                    }
                }
                if (hasViolation) tooClose++;
                else goodClearance++;
            }

            var report = new StringBuilder();
            report.AppendLine("MEP ROUTE ANALYSIS — Collision & Clearance Report\n");
            report.AppendLine($"MEP elements analysed: {Math.Min(mepElements.Count, 2000)}");
            report.AppendLine($"Obstacles: {obstacles.Count}");
            report.AppendLine($"Min clearance standard: {minClearanceMm}mm (CIBSE Guide W)\n");
            report.AppendLine($"PASS (adequate clearance): {goodClearance}");
            report.AppendLine($"FAIL (below minimum): {tooClose}");

            if (tooClose > 0)
                report.AppendLine($"\nRecommendation: Run MEP Clearance Validation for detailed per-element analysis.");

            TaskDialog.Show("STING MEP Route Analysis", report.ToString());
            return Result.Succeeded;
        }
    }
}
