// ============================================================================
// StructuralModelingCommands.cs — IExternalCommand classes for structural modeling.
// Each command wraps StructuralModelingEngine methods, collecting user input
// via TaskDialog/StingModePicker and delegating to the engine.
//
// Commands (14):
//   1. StrCreatePadFooting         — Isolated pad footing at point
//   2. StrCreateStripFooting       — Strip/continuous footing along line
//   3. StrCreateStructuralSlab     — Structural slab with openings
//   4. StrCreateStructuralWall     — Shear/core/retaining wall
//   5. StrCreateBeamSystem         — Beam system for a bay
//   6. StrCreateBracing            — Bracing between columns
//   7. StrCreateTruss              — Parametric truss generation
//   8. StrCreateFullBayFrame       — Complete bay (cols + beams + braces + slab)
//   9. StrCreateGridFrame          — Multi-bay grid frame
//  10. StrAnalyzeLoadPaths         — Load path connectivity analysis
//  11. StrDetectBays               — Bay detection from existing columns
//  12. StrCADToStructural          — DWG structural layer → BIM conversion
//  13. StrCADPreview               — Preview structural layers from DWG
//  14. StrRecommendGrid            — Optimal grid spacing recommendation
//
// Pattern: [Transaction(TransactionMode.Manual)] + [Regeneration(RegenerationOption.Manual)]
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Model
{
    // ══════════════════════════════════════════════════════════════════
    // PAD FOOTING
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates an isolated (pad) footing at a picked point or under selected column.
    /// Supports auto-sizing from column load and soil capacity.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreatePadFootingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Pad Footing");
                dlg.MainContent = "Create an isolated (pad) footing.\nSelect size or auto-calculate from load:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "1200 × 1200 × 400 mm (Standard)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "1500 × 1500 × 500 mm (Medium)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "2000 × 2000 × 600 mm (Heavy)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Auto-size from load (500kN, 150kPa soil)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                double widthMm, depthMm, thickMm;
                double loadKN = 0;

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: widthMm = 1200; depthMm = 1200; thickMm = 400; break;
                    case TaskDialogResult.CommandLink2: widthMm = 1500; depthMm = 1500; thickMm = 500; break;
                    case TaskDialogResult.CommandLink3: widthMm = 2000; depthMm = 2000; thickMm = 600; break;
                    case TaskDialogResult.CommandLink4: widthMm = 1000; depthMm = 1000; thickMm = 400; loadKN = 500; break;
                    default: return Result.Cancelled;
                }

                var pt = uidoc.Selection.PickPoint("Pick footing center point");
                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreatePadFooting(
                    pt.X * Units.FeetToMm, pt.Y * Units.FeetToMm,
                    widthMm, depthMm, thickMm,
                    columnLoadKN: loadKN);

                TaskDialog.Show("STRUCT — Pad Footing",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreatePadFooting failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // STRIP FOOTING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateStripFootingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Strip Footing");
                dlg.MainContent = "Create a strip (continuous) footing along a wall line.\nSelect width:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "600mm wide × 300mm deep (Standard)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "900mm wide × 450mm deep (Medium)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "1200mm wide × 600mm deep (Heavy)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                double widthMm, depthMm;

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: widthMm = 600; depthMm = 300; break;
                    case TaskDialogResult.CommandLink2: widthMm = 900; depthMm = 450; break;
                    case TaskDialogResult.CommandLink3: widthMm = 1200; depthMm = 600; break;
                    default: return Result.Cancelled;
                }

                var p1 = uidoc.Selection.PickPoint("Pick strip footing start point");
                var p2 = uidoc.Selection.PickPoint("Pick strip footing end point");

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateStripFooting(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm,
                    widthMm, depthMm);

                TaskDialog.Show("STRUCT — Strip Footing",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateStripFooting failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // STRUCTURAL SLAB
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateStructuralSlabCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Structural Slab");
                dlg.MainContent = "Create a structural floor slab.\nSelect size:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "6000 × 6000 mm (Single bay)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "9000 × 7500 mm (Office bay)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "12000 × 9000 mm (Large bay)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Custom — pick 2 corners");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();

                double widthMm, depthMm, originXMm, originYMm;
                if (choice == TaskDialogResult.CommandLink4)
                {
                    var p1 = uidoc.Selection.PickPoint("Pick slab corner 1");
                    var p2 = uidoc.Selection.PickPoint("Pick slab corner 2");
                    originXMm = Math.Min(p1.X, p2.X) * Units.FeetToMm;
                    originYMm = Math.Min(p1.Y, p2.Y) * Units.FeetToMm;
                    widthMm = Math.Abs(p2.X - p1.X) * Units.FeetToMm;
                    depthMm = Math.Abs(p2.Y - p1.Y) * Units.FeetToMm;
                }
                else
                {
                    switch (choice)
                    {
                        case TaskDialogResult.CommandLink1: widthMm = 6000; depthMm = 6000; break;
                        case TaskDialogResult.CommandLink2: widthMm = 9000; depthMm = 7500; break;
                        case TaskDialogResult.CommandLink3: widthMm = 12000; depthMm = 9000; break;
                        default: return Result.Cancelled;
                    }
                    var pt = uidoc.Selection.PickPoint("Pick slab origin (bottom-left corner)");
                    originXMm = pt.X * Units.FeetToMm;
                    originYMm = pt.Y * Units.FeetToMm;
                }

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateStructuralSlab(widthMm, depthMm,
                    originXMm: originXMm, originYMm: originYMm);

                TaskDialog.Show("STRUCT — Structural Slab",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateStructuralSlab failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // STRUCTURAL WALL
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateStructuralWallCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Structural Wall");
                dlg.MainContent = "Create a structural wall.\nSelect wall type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Shear Wall (200mm RC)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Core Wall (300mm RC)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Retaining Wall (250mm RC)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                StructuralElementType wallType;
                double thickMm;

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: wallType = StructuralElementType.ShearWall; thickMm = 200; break;
                    case TaskDialogResult.CommandLink2: wallType = StructuralElementType.CoreWall; thickMm = 300; break;
                    case TaskDialogResult.CommandLink3: wallType = StructuralElementType.RetainingWall; thickMm = 250; break;
                    default: return Result.Cancelled;
                }

                var p1 = uidoc.Selection.PickPoint("Pick wall start point");
                var p2 = uidoc.Selection.PickPoint("Pick wall end point");

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateStructuralWall(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm,
                    thicknessMm: thickMm, wallType: wallType);

                TaskDialog.Show("STRUCT — Structural Wall",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateStructuralWall failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // BEAM SYSTEM
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateBeamSystemCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Beam System");
                dlg.MainContent = "Create a beam system for a structural bay.\n" +
                    "Includes edge beams + auto-calculated secondary beams.\nSelect bay size:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "6000 × 6000 mm (Small bay)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "9000 × 7500 mm (Office bay)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "12000 × 9000 mm (Large bay)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Custom — pick 2 corners");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                double bayX, bayY, originX, originY;

                if (choice == TaskDialogResult.CommandLink4)
                {
                    var p1 = uidoc.Selection.PickPoint("Pick bay corner 1");
                    var p2 = uidoc.Selection.PickPoint("Pick bay corner 2");
                    originX = Math.Min(p1.X, p2.X) * Units.FeetToMm;
                    originY = Math.Min(p1.Y, p2.Y) * Units.FeetToMm;
                    bayX = Math.Abs(p2.X - p1.X) * Units.FeetToMm;
                    bayY = Math.Abs(p2.Y - p1.Y) * Units.FeetToMm;
                }
                else
                {
                    switch (choice)
                    {
                        case TaskDialogResult.CommandLink1: bayX = 6000; bayY = 6000; break;
                        case TaskDialogResult.CommandLink2: bayX = 9000; bayY = 7500; break;
                        case TaskDialogResult.CommandLink3: bayX = 12000; bayY = 9000; break;
                        default: return Result.Cancelled;
                    }
                    var pt = uidoc.Selection.PickPoint("Pick bay origin (bottom-left corner)");
                    originX = pt.X * Units.FeetToMm;
                    originY = pt.Y * Units.FeetToMm;
                }

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateBeamSystem(bayX, bayY, originX, originY);

                TaskDialog.Show("STRUCT — Beam System",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateBeamSystem failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // BRACING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateBracingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Bracing");
                dlg.MainContent = "Create a bracing system between two columns.\nSelect pattern:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "X-Brace (maximum stiffness)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Chevron / Inverted-V (allows openings)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "V-Brace");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "K-Brace (mid-column split)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                BracingPattern pattern;

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: pattern = BracingPattern.XBrace; break;
                    case TaskDialogResult.CommandLink2: pattern = BracingPattern.Chevron; break;
                    case TaskDialogResult.CommandLink3: pattern = BracingPattern.VBrace; break;
                    case TaskDialogResult.CommandLink4: pattern = BracingPattern.KBrace; break;
                    default: return Result.Cancelled;
                }

                var p1 = uidoc.Selection.PickPoint("Pick column 1 position");
                var p2 = uidoc.Selection.PickPoint("Pick column 2 position");

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateBracing(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm,
                    storeyCount: 1, pattern: pattern);

                TaskDialog.Show("STRUCT — Bracing",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateBracing failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // TRUSS
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateTrussCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Truss");
                dlg.MainContent = "Create a parametric truss.\nSelect truss type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Pratt Truss (standard, diagonals toward center)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Warren Truss (no verticals, alternating diagonals)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Howe Truss (diagonals away from center)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Vierendeel (rigid frame, no diagonals)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                TrussType type;

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: type = TrussType.Pratt; break;
                    case TaskDialogResult.CommandLink2: type = TrussType.Warren; break;
                    case TaskDialogResult.CommandLink3: type = TrussType.Howe; break;
                    case TaskDialogResult.CommandLink4: type = TrussType.Vierendeel; break;
                    default: return Result.Cancelled;
                }

                var p1 = uidoc.Selection.PickPoint("Pick truss left support point");
                var p2 = uidoc.Selection.PickPoint("Pick truss right support point");

                double zMm = 3600; // Default height
                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateTruss(
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm, zMm,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm, zMm,
                    type);

                TaskDialog.Show("STRUCT — Truss",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateTruss failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // FULL BAY FRAME
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateFullBayFrameCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Full Bay Frame");
                dlg.MainContent = "Create a complete structural bay frame:\n" +
                    "4 columns + edge beams + secondary beams + optional bracing + slab.\nSelect configuration:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "6m × 6m bay — columns + beams + slab");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "9m × 7.5m bay — with X-bracing on one face");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "12m × 9m bay — with chevron bracing + slab");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Custom — pick 2 corners");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                double bayX, bayY, originX, originY;
                bool addBracing = false;
                var pattern = BracingPattern.XBrace;

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: bayX = 6000; bayY = 6000; break;
                    case TaskDialogResult.CommandLink2: bayX = 9000; bayY = 7500; addBracing = true; break;
                    case TaskDialogResult.CommandLink3: bayX = 12000; bayY = 9000; addBracing = true; pattern = BracingPattern.Chevron; break;
                    case TaskDialogResult.CommandLink4: bayX = 8000; bayY = 8000; break;
                    default: return Result.Cancelled;
                }

                if (choice == TaskDialogResult.CommandLink4)
                {
                    var p1 = uidoc.Selection.PickPoint("Pick bay corner 1");
                    var p2 = uidoc.Selection.PickPoint("Pick bay corner 2");
                    originX = Math.Min(p1.X, p2.X) * Units.FeetToMm;
                    originY = Math.Min(p1.Y, p2.Y) * Units.FeetToMm;
                    bayX = Math.Abs(p2.X - p1.X) * Units.FeetToMm;
                    bayY = Math.Abs(p2.Y - p1.Y) * Units.FeetToMm;
                }
                else
                {
                    var pt = uidoc.Selection.PickPoint("Pick bay origin (bottom-left corner)");
                    originX = pt.X * Units.FeetToMm;
                    originY = pt.Y * Units.FeetToMm;
                }

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateFullBayFrame(bayX, bayY,
                    originXMm: originX, originYMm: originY,
                    addBracing: addBracing, bracingPattern: pattern);

                TaskDialog.Show("STRUCT — Full Bay Frame",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateFullBayFrame failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // GRID FRAME
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCreateGridFrameCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Grid Frame");
                dlg.MainContent = "Create a multi-bay structural grid frame.\n" +
                    "Columns at all intersections + beams along all grid lines.\nSelect configuration:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "3×3 bays @ 7.5m (Office building)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "4×2 bays @ 9m × 12m (Warehouse)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "5×4 bays @ 6m (Multi-storey, 3 floors)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "2×2 bays with perimeter bracing");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                int baysX, baysY, storeys;
                double spacingX, spacingY;
                bool bracing;

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: baysX = 3; baysY = 3; spacingX = 7500; spacingY = 7500; storeys = 1; bracing = false; break;
                    case TaskDialogResult.CommandLink2: baysX = 4; baysY = 2; spacingX = 9000; spacingY = 12000; storeys = 1; bracing = false; break;
                    case TaskDialogResult.CommandLink3: baysX = 5; baysY = 4; spacingX = 6000; spacingY = 6000; storeys = 3; bracing = false; break;
                    case TaskDialogResult.CommandLink4: baysX = 2; baysY = 2; spacingX = 8000; spacingY = 8000; storeys = 1; bracing = true; break;
                    default: return Result.Cancelled;
                }

                var pt = uidoc.Selection.PickPoint("Pick grid frame origin (bottom-left corner)");

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateGridFrame(baysX, baysY, spacingX, spacingY,
                    storeyCount: storeys,
                    originXMm: pt.X * Units.FeetToMm, originYMm: pt.Y * Units.FeetToMm,
                    perimeterBracing: bracing);

                TaskDialog.Show("STRUCT — Grid Frame",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCreateGridFrame failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // LOAD PATH ANALYSIS
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAnalyzeLoadPathsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(doc);
                var result = engine.AnalyzeLoadPaths();

                var msg = result.Summary;
                if (result.Warnings.Count > 0)
                    msg += "\n\nWarnings:\n" + string.Join("\n", result.Warnings.Select(w => $"• {w}"));

                TaskDialog.Show("STRUCT — Load Path Analysis", msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrAnalyzeLoadPaths failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // BAY DETECTION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrDetectBaysCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(doc);
                var (bays, summary) = engine.DetectExistingBays();

                var msg = summary;
                if (bays.Count > 0)
                {
                    msg += "\n\nBay Details:";
                    for (int i = 0; i < Math.Min(bays.Count, 10); i++)
                    {
                        var b = bays[i];
                        msg += $"\n  Bay {i + 1}: {b.SpanXFt * Units.FeetToMm / 1000:F1}m × " +
                            $"{b.SpanYFt * Units.FeetToMm / 1000:F1}m" +
                            $"{(b.NeedsSecondaryBeams ? $" → needs {b.RecommendedSecondaryCount} secondary beams" : "")}";
                    }
                    if (bays.Count > 10)
                        msg += $"\n  ... and {bays.Count - 10} more bays";
                }

                TaskDialog.Show("STRUCT — Bay Detection", msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrDetectBays failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // CAD TO STRUCTURAL
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCADToStructuralCommand : IExternalCommand
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
                    TaskDialog.Show("STRUCT — CAD to Structural",
                        "No imported/linked DWG files found.\nLink a structural DWG first.");
                    return Result.Succeeded;
                }

                // Use first import or let user pick
                ImportInstance import = imports[0];
                if (imports.Count > 1)
                {
                    var dlg = new TaskDialog("STRUCT — CAD to Structural");
                    dlg.MainContent = $"Found {imports.Count} DWG imports. Using the first one.\n" +
                        "Structural layers will be auto-detected and converted.";
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Proceed with structural conversion");
                    dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    if (dlg.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;
                }

                // Use the full structural pipeline (not base CADToModelEngine)
                var pipeline = new StructuralCADPipeline(doc);
                var result = pipeline.RunFullPipeline(import);

                var msg = result.Summary;
                if (result.Warnings.Count > 0)
                    msg += $"\n\nWarnings ({result.Warnings.Count}):\n" +
                        string.Join("\n", result.Warnings.Take(10).Select(w => $"• {w}"));

                TaskDialog.Show("STRUCT — CAD to Structural", msg);

                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCADToStructural failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // CAD STRUCTURAL PREVIEW
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCADPreviewCommand : IExternalCommand
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
                    TaskDialog.Show("STRUCT — CAD Preview", "No imported/linked DWG files found.");
                    return Result.Succeeded;
                }

                var cadEngine = new CADToModelEngine(doc);
                var extraction = cadEngine.PreviewImport(imports[0]);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"DWG Analysis: {extraction.TotalEntities} entities, {extraction.LayerCounts.Count} layers");
                sb.AppendLine();
                sb.AppendLine("STRUCTURAL LAYERS:");
                sb.AppendLine("─────────────────────────────────────");

                int structTotal = 0;
                foreach (var kvp in extraction.LayerCounts.OrderByDescending(kvp => kvp.Value))
                {
                    var cls = StructuralLayerClassifier.Classify(kvp.Key);
                    if (cls.HasValue)
                    {
                        sb.AppendLine($"  ✓ {kvp.Key}: {kvp.Value} entities → {cls.Value.Type} ({cls.Value.Confidence * 100:F0}%)");
                        structTotal += kvp.Value;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("NON-STRUCTURAL LAYERS:");
                sb.AppendLine("─────────────────────────────────────");
                foreach (var kvp in extraction.LayerCounts.OrderByDescending(kvp => kvp.Value))
                {
                    var cls = StructuralLayerClassifier.Classify(kvp.Key);
                    if (!cls.HasValue)
                        sb.AppendLine($"  - {kvp.Key}: {kvp.Value} entities");
                }

                sb.AppendLine();
                sb.AppendLine($"Summary: {structTotal}/{extraction.TotalEntities} entities are structural ({(extraction.TotalEntities > 0 ? structTotal * 100.0 / extraction.TotalEntities : 0):F0}%)");

                TaskDialog.Show("STRUCT — CAD Preview", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrCADPreview failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // RECOMMEND GRID
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrRecommendGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("OPTIMAL STRUCTURAL GRID SPACING RECOMMENDATIONS");
                sb.AppendLine("═════════════════════════════════════════════════");
                sb.AppendLine();
                sb.AppendLine("Building Type        │  Steel (X × Y)  │  RC (X × Y)");
                sb.AppendLine("─────────────────────┼─────────────────┼─────────────────");

                var types = new[] { "office", "residential", "warehouse", "retail",
                    "hospital", "school", "car_park", "industrial" };

                foreach (var t in types)
                {
                    var (sx, sy) = StructuralGridOptimizer.RecommendGridSpacing(t, true);
                    var (rx, ry) = StructuralGridOptimizer.RecommendGridSpacing(t, false);
                    sb.AppendLine($"  {t,-18} │  {sx / 1000:F1}m × {sy / 1000:F1}m  │  {rx / 1000:F1}m × {ry / 1000:F1}m");
                }

                sb.AppendLine();
                sb.AppendLine("Notes:");
                sb.AppendLine("• Steel spans can be 30-50% longer than RC for the same cost");
                sb.AppendLine("• Warehouse/industrial prioritise clear spans in one direction");
                sb.AppendLine("• Car parks use 7.5m for 2 parking bays + aisle + 2 bays");
                sb.AppendLine("• Office: 7.5-9m suits open-plan layouts with raised floors");

                TaskDialog.Show("STRUCT — Grid Recommendations", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrRecommendGrid failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // STRUCTURAL CAD WIZARD (Full Automation)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Launches the multi-page WPF wizard for structural CAD-to-BIM automation.
    /// Guides user through prerequisites → DWG selection → configuration → execution.
    /// Uses StructuralTypeFactory for intelligent family search + type creation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCADWizardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var wizard = StructuralCADWizard.Show(doc);
                if (!wizard.Confirmed) return Result.Cancelled;

                if (wizard.SelectedImport == null)
                {
                    TaskDialog.Show("STRUCT — CAD Wizard", "No DWG import selected.");
                    return Result.Cancelled;
                }

                // Run the full pipeline with wizard settings + selected layers + tolerances
                var pipeline = new StructuralCADPipeline(doc);
                pipeline.SelectedLayers = wizard.GetSelectedLayers();
                pipeline.EndpointToleranceFt = wizard.EndpointToleranceMm * Units.MmToFeet;
                var result = pipeline.RunFullPipeline(
                    wizard.SelectedImport,
                    wizard.SelectedLevel,
                    wizard.CreateColumns,
                    wizard.CreateBeams,
                    wizard.CreateSlabs,
                    wizard.CreateGrids,
                    wizard.DefaultBeamDepthMm,
                    wizard.DefaultSlabThickMm,
                    wizard.DefaultStoreyHeightMm);

                var msg = result.Summary;
                if (result.Warnings.Count > 0)
                    msg += $"\n\nWarnings ({result.Warnings.Count}):\n" +
                        string.Join("\n", result.Warnings.Take(15).Select(w => $"• {w}"));

                TaskDialog.Show("STRUCT — CAD Wizard", msg);

                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrCADWizard failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // PREREQUISITES CHECK
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks project prerequisites for structural automation without running conversion.
    /// Shows loaded families, levels, DWG imports, and type catalog.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCheckPrerequisitesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var pipeline = new StructuralCADPipeline(doc);
                var prereq = pipeline.CheckPrerequisites();

                pipeline.TypeFactory.BuildCatalog();
                var catalog = pipeline.TypeFactory.GetCatalogSummary();

                TaskDialog.Show("STRUCT — Prerequisites",
                    prereq.GetStatusText() + "\n\n" + catalog);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrCheckPrerequisites failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // TYPE CATALOG BROWSER
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shows all loaded structural family types with extracted dimensions.
    /// Helps user understand what families are available for type matching.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrBrowseTypeCatalogCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var factory = new StructuralTypeFactory(doc);
                factory.BuildCatalog();
                TaskDialog.Show("STRUCT — Type Catalog", factory.GetCatalogSummary());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrBrowseTypeCatalog failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // AUTO-SIZED FOUNDATIONS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates pad footings under all columns, auto-sized from tributary load analysis.
    /// Pipeline: column loads → bearing capacity check → pad sizing → type creation → placement.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAutoFoundationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            var doc = uidoc.Document;

            try
            {
                var dlg = new TaskDialog("STRUCT — Auto Foundations");
                dlg.MainContent = "Create auto-sized pad footings under all columns.\n" +
                    "Sizes calculated from tributary slab loads + soil bearing capacity.\n\nSelect soil type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Soft clay (75 kPa)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Medium clay / dense sand (150 kPa)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Hard clay / gravel (300 kPa)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Rock (600 kPa)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                double soilKPa;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: soilKPa = 75; break;
                    case TaskDialogResult.CommandLink2: soilKPa = 150; break;
                    case TaskDialogResult.CommandLink3: soilKPa = 300; break;
                    case TaskDialogResult.CommandLink4: soilKPa = 600; break;
                    default: return Result.Cancelled;
                }

                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateAutoSizedFootings(soilKPa);

                TaskDialog.Show("STRUCT — Auto Foundations",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrAutoFoundations failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // COLUMN LOAD TAKEDOWN
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrColumnLoadTakedownCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            var doc = uidoc.Document;

            try
            {
                var engine = new StructuralModelingEngine(doc);
                var loads = engine.CalculateColumnLoads();

                if (loads.Count == 0)
                {
                    TaskDialog.Show("STRUCT — Load Takedown", "No columns found.");
                    return Result.Succeeded;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"COLUMN LOAD TAKEDOWN — {loads.Count} columns");
                sb.AppendLine("═══════════════════════════════════════════");
                sb.AppendLine();

                var sorted = loads.OrderByDescending(kvp => kvp.Value);
                int rank = 1;
                foreach (var kvp in sorted.Take(20))
                {
                    var col = doc.GetElement(kvp.Key);
                    string name = col?.Name ?? $"#{kvp.Key.Value}";
                    double padMm = FoundationAnalyzer.CalculatePadSize(kvp.Value);
                    sb.AppendLine($"  {rank++}. {name}: {kvp.Value:F0} kN → pad {padMm:F0}×{padMm:F0}mm");
                }

                if (loads.Count > 20)
                    sb.AppendLine($"\n  ... and {loads.Count - 20} more columns");

                sb.AppendLine($"\n  Total load: {loads.Values.Sum():F0} kN");
                sb.AppendLine($"  Max column: {loads.Values.Max():F0} kN");
                sb.AppendLine($"  Avg column: {loads.Values.Average():F0} kN");

                TaskDialog.Show("STRUCT — Load Takedown", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrColumnLoadTakedown failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // SLAB EDGE BEAMS
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSlabEdgeBeamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            var doc = uidoc.Document;

            try
            {
                var engine = new StructuralModelingEngine(doc);
                var result = engine.CreateSlabEdgeBeams();

                TaskDialog.Show("STRUCT — Slab Edge Beams",
                    result.Success ? result.Summary : $"Failed: {result.Summary}");
                if (result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrSlabEdgeBeams failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // STRUCTURAL SYSTEM CLASSIFIER
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-classifies the structural system type (frame, braced, shear wall, dual, flat slab).
    /// Analyzes element counts, ratios, regularity, and connectivity.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrClassifySystemCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(uidoc.Document);
                var result = engine.ClassifyStructuralSystem();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Structural System Type: {result.SystemType}");
                sb.AppendLine($"Material: {result.MaterialType}");
                sb.AppendLine();
                sb.AppendLine("Element Counts:");
                sb.AppendLine($"  Columns: {result.TotalColumns}");
                sb.AppendLine($"  Beams: {result.TotalBeams}");
                sb.AppendLine($"  Walls: {result.TotalWalls}");
                sb.AppendLine($"  Bracing: {result.TotalBraces}");
                sb.AppendLine($"  Foundations: {result.TotalFoundations}");
                sb.AppendLine();
                sb.AppendLine($"Wall-to-Column Ratio: {result.WallToColumnRatio:F2}");
                sb.AppendLine($"Has Bracing: {result.HasBracing}");
                sb.AppendLine($"Has Transfer Elements: {result.HasTransferElements}");
                sb.AppendLine($"Regular in Plan: {result.IsRegularInPlan}");
                sb.AppendLine($"Regular in Elevation: {result.IsRegularInElevation}");

                TaskDialog.Show("STRUCT — System Classification", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrClassifySystem failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // DEFLECTION CHECK — ALL BEAMS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs serviceability deflection checks on all beams in the model.
    /// Reports pass/fail with utilisation ratios per EC2/EC3.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrDeflectionCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Deflection Check");
                dlg.MainContent = "Check beam deflections against serviceability limits.";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Steel beams (EC3, E=210GPa)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "RC beams (EC2, span/depth ratios)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = dlg.Show();

                bool isSteel = choice == TaskDialogResult.CommandLink1;
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                var engine = new StructuralModelingEngine(uidoc.Document);
                var results = engine.CheckAllBeamDeflections(isSteel: isSteel);

                int passing = results.Count(r => r.Result.Pass);
                int failing = results.Count(r => !r.Result.Pass);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Deflection Check: {results.Count} beams");
                sb.AppendLine($"  ✓ Passing: {passing}");
                sb.AppendLine($"  ✗ Failing: {failing}");
                sb.AppendLine();

                if (failing > 0)
                {
                    sb.AppendLine("Failing beams (worst first):");
                    foreach (var (id, r) in results.Where(r => !r.Result.Pass)
                        .OrderByDescending(r => r.Result.CalculatedMm)
                        .Take(10))
                    {
                        sb.AppendLine($"  Beam {id.Value}: {r.Summary}");
                    }

                    // Select failing beams
                    var failIds = results.Where(r => !r.Result.Pass)
                        .Select(r => r.BeamId).ToList();
                    uidoc.Selection.SetElementIds(failIds);
                }

                TaskDialog.Show("STRUCT — Deflection Results", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrDeflectionCheck failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // PUNCHING SHEAR CHECK
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks punching shear at all slab-column interfaces per EC2 Section 6.4.
    /// Identifies columns needing shear reinforcement.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrPunchingShearCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(uidoc.Document);
                var results = engine.CheckAllPunchingShear();

                int passing = results.Count(r => r.Result.Pass);
                int failing = results.Count(r => !r.Result.Pass);
                int needsReinf = results.Count(r => r.Result.NeedsShearReinforcement);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Punching Shear Check (EC2 §6.4): {results.Count} interfaces");
                sb.AppendLine($"  ✓ Passing: {passing}");
                sb.AppendLine($"  ✗ Failing: {failing}");
                sb.AppendLine($"  ⚠ Needs shear reinforcement: {needsReinf}");

                if (failing > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Critical columns:");
                    foreach (var (id, r) in results.Where(r => !r.Result.Pass)
                        .OrderByDescending(r => r.Result.UtilisationRatio).Take(10))
                    {
                        sb.AppendLine($"  Column {id.Value}: util={r.UtilisationRatio:P0}, {r.Summary}");
                    }

                    var failIds = results.Where(r => !r.Result.Pass)
                        .Select(r => r.ColumnId).ToList();
                    uidoc.Selection.SetElementIds(failIds);
                }

                TaskDialog.Show("STRUCT — Punching Shear", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrPunchingShearCheck failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // WIND LOAD ANALYSIS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculates wind loads on the building per EC1-1-4 and distributes to storeys.
    /// Auto-detects building dimensions from model geometry.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrWindLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(uidoc.Document);
                var result = engine.CalculateWindLoads();
                TaskDialog.Show("STRUCT — Wind Load Analysis", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrWindLoad failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // CONSTRUCTION SEQUENCE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-generates a construction phase sequence from the structural model.
    /// Orders: foundations → columns → beams → slabs per level.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrConstructionSequenceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(uidoc.Document);
                var sequence = engine.GenerateConstructionSequence();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Construction Sequence: {sequence.Phases.Count} phases");
                sb.AppendLine($"Estimated duration: {sequence.TotalEstimatedDays} working days");
                sb.AppendLine();

                foreach (var phase in sequence.Phases)
                {
                    sb.AppendLine($"Phase {phase.PhaseNumber}: {phase.PhaseName}");
                    sb.AppendLine($"  Level: {phase.LevelName}");
                    sb.AppendLine($"  Elements: {phase.ElementIds.Count}");
                    sb.AppendLine($"  Duration: ~{phase.EstimatedDays} days");
                    sb.AppendLine($"  {phase.Description}");
                    sb.AppendLine();
                }

                TaskDialog.Show("STRUCT — Construction Sequence", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrConstructionSequence failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // FULL STRUCTURAL REPORT
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a comprehensive structural analysis report covering system
    /// classification, load paths, deflection, punching shear, wind loads, and sequencing.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrFullReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(uidoc.Document);
                var report = engine.GenerateFullStructuralReport();

                // Show summary in TaskDialog (truncated for dialog)
                var lines = report.Split('\n');
                var summary = string.Join("\n", lines.Take(40));
                if (lines.Length > 40) summary += $"\n... ({lines.Length - 40} more lines)";

                TaskDialog.Show("STRUCT — Full Analysis Report", summary);

                // Export full report to file
                var outputDir = OutputLocationHelper.GetTimestampedPath(uidoc.Document, "StructuralReport");
                try
                {
                    var filePath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(outputDir) ?? System.IO.Path.GetTempPath(),
                        $"STING_StructuralReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    System.IO.File.WriteAllText(filePath, report);
                    StingLog.Info($"Structural report exported: {filePath}");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Report export failed: {ex.Message}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrFullReport failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // VORONOI TRIBUTARY AREAS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculates and displays column tributary areas using true Voronoi tessellation.
    /// More accurate than grid-based approximation for irregular column layouts.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrVoronoiAreasCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var engine = new StructuralModelingEngine(uidoc.Document);
                var loads = engine.CalculateColumnLoadsVoronoi();

                if (loads.Count == 0)
                {
                    TaskDialog.Show("STRUCT — Voronoi", "No columns found.");
                    return Result.Succeeded;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Voronoi Tributary Areas: {loads.Count} columns");
                sb.AppendLine();

                double totalLoad = 0;
                foreach (var kvp in loads.OrderByDescending(k => k.Value))
                {
                    sb.AppendLine($"  Column {kvp.Key.Value}: {kvp.Value:F0} kN");
                    totalLoad += kvp.Value;
                }
                sb.AppendLine();
                sb.AppendLine($"Total accumulated load: {totalLoad:F0} kN");
                sb.AppendLine($"Average per column: {totalLoad / loads.Count:F0} kN");

                TaskDialog.Show("STRUCT — Voronoi Tributary Areas", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrVoronoiAreas failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // CLASH PRE-DETECTION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pre-checks for geometric clashes before placing structural elements.
    /// Prevents placement conflicts by analyzing existing geometry.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrClashPreCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var doc = uidoc.Document;

                // Check for beam clashes by picking two points
                var dlg = new TaskDialog("STRUCT — Clash Pre-Check");
                dlg.MainContent = "Pre-check for clashes before placing elements:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Check beam path (pick 2 points)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Check column location (pick 1 point)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                var engine = new StructuralModelingEngine(doc);
                ClashResult result;

                if (choice == TaskDialogResult.CommandLink1)
                {
                    var p1 = uidoc.Selection.PickPoint("Pick beam start point");
                    var p2 = uidoc.Selection.PickPoint("Pick beam end point");
                    result = engine.PreCheckBeamClash(
                        p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm, p1.Z * Units.FeetToMm,
                        p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm, p2.Z * Units.FeetToMm);
                }
                else
                {
                    var p = uidoc.Selection.PickPoint("Pick column location");
                    result = engine.PreCheckColumnClash(
                        p.X * Units.FeetToMm, p.Y * Units.FeetToMm);
                }

                var sb = new System.Text.StringBuilder();
                if (result.HasClashes)
                {
                    sb.AppendLine($"⚠ {result.Clashes.Count} clash(es) detected:");
                    foreach (var clash in result.Clashes)
                        sb.AppendLine($"  • {clash.ElementDescription} (overlap: {clash.OverlapMm:F0}mm)");

                    var clashIds = result.Clashes
                        .Select(c => c.ClashingElementId)
                        .Where(id => id != null && id != ElementId.InvalidElementId)
                        .ToList();
                    if (clashIds.Count > 0) uidoc.Selection.SetElementIds(clashIds);
                }
                else
                {
                    sb.AppendLine("✓ No clashes detected. Safe to place element.");
                }

                TaskDialog.Show("STRUCT — Clash Check", sb.ToString());
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrClashPreCheck failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
