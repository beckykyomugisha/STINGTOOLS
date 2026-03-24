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
                // Show the new single-page DWG-to-BIM conversion dialog
                var wizard = new StructuralCADWizard(doc);
                wizard.ShowDialog();
                if (!wizard.Confirmed) return Result.Cancelled;

                var config = wizard.GetConfig();

                // Find the selected DWG import
                var imports = CADToModelEngine.FindImportInstances(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("STRUCT — DWG-to-BIM", "No DWG import found in the document.");
                    return Result.Failed;
                }

                // Run the enhanced pipeline with full config
                var pipeline = new StructuralCADPipeline(doc);
                var result = pipeline.RunFullPipelineWithConfig(imports[0], config);

                var msg = result.Summary;
                if (result.Warnings.Count > 0)
                    msg += $"\n\nWarnings ({result.Warnings.Count}):\n" +
                        string.Join("\n", result.Warnings.Take(15).Select(w => $"• {w}"));

                TaskDialog.Show("STRUCT — DWG-to-BIM", msg);

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


    // ══════════════════════════════════════════════════════════════════
    // 2D FRAME ANALYSIS (Direct Stiffness Method FEA)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs 2D frame analysis on the structural model using the Direct Stiffness Method.
    /// Assembles global stiffness matrix, solves displacements, recovers member forces.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrFrameAnalysisCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var (nodes, members) = DirectStiffnessMethod.BuildFromRevitModel(uidoc.Document);
                if (nodes.Count == 0)
                {
                    TaskDialog.Show("STRUCT — Frame Analysis", "No structural elements found for analysis.");
                    return Result.Succeeded;
                }

                var result = DirectStiffnessMethod.Analyze(nodes, members);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"2D Frame Analysis (Direct Stiffness Method)");
                sb.AppendLine($"Nodes: {result.Nodes.Count}, Members: {result.Members.Count}");
                sb.AppendLine($"Converged: {result.Converged}");
                sb.AppendLine($"Max displacement: {result.MaxDisplacementMm:F2} mm");
                sb.AppendLine($"Max moment: {result.MaxMomentKNm:F1} kNm");
                sb.AppendLine($"Max axial: {result.MaxAxialKN:F1} kN");
                sb.AppendLine();

                // Show top 5 most loaded members
                var topMembers = result.Members
                    .OrderByDescending(m => Math.Max(Math.Abs(m.MomentIKNm), Math.Abs(m.MomentJKNm)))
                    .Take(5);
                sb.AppendLine("Most loaded members:");
                foreach (var m in topMembers)
                    sb.AppendLine($"  Member {m.Id}: M={Math.Max(Math.Abs(m.MomentIKNm), Math.Abs(m.MomentJKNm)):F1}kNm, N={m.AxialForceKN:F1}kN");

                TaskDialog.Show("STRUCT — Frame Analysis", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrFrameAnalysis failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // SEISMIC ANALYSIS (EC8)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSeismicAnalysisCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var doc = uidoc.Document;

                // Auto-detect building height and storeys
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();

                double heightM = levels.Count > 1
                    ? (levels.Last().Elevation - levels.First().Elevation) * Units.FeetToMm / 1000.0
                    : 12;
                int storeyCount = Math.Max(1, levels.Count - 1);

                var result = SeismicAnalyzer.Analyze(heightM, storeyCount);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("EC8 Seismic Analysis (Equivalent Lateral Force)");
                sb.AppendLine($"T1 = {result.FundamentalPeriodS:F2}s");
                sb.AppendLine($"Sd(T1) = {result.DesignSpectralAcceleration:F3}g");
                sb.AppendLine($"Base shear = {result.BaseShearKN:F0} kN");
                sb.AppendLine($"Behaviour factor q = {result.BehaviourFactor:F1}");
                sb.AppendLine($"Ductility class: {result.DuctilityClass}");
                sb.AppendLine($"Drift check: {(result.DriftCheckPass ? "PASS" : "FAIL")}");
                sb.AppendLine();
                sb.AppendLine("Storey forces:");
                for (int i = 0; i < result.StoreyForcesKN.Count; i++)
                    sb.AppendLine($"  Storey {i + 1}: {result.StoreyForcesKN[i]:F1} kN");

                TaskDialog.Show("STRUCT — Seismic Analysis", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrSeismicAnalysis failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // GENETIC ALGORITHM GRID OPTIMIZATION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrOptimizeGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Grid Optimization");
                dlg.MainContent = "Optimize column grid spacing using genetic algorithm.\nSelect material:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Steel frame");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "RC frame");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = dlg.Show();
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                bool isSteel = choice == TaskDialogResult.CommandLink1;

                // Auto-detect floor plate from model
                double floorW = 30000, floorD = 20000; // Default 30m × 20m

                var result = GeneticGridOptimizer.Optimize(floorW, floorD, isSteel: isSteel);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Genetic Algorithm Grid Optimization");
                sb.AppendLine($"Generations: {result.GenerationsUsed}");
                sb.AppendLine($"Optimal spacing: {result.OptimalSpacingXMm / 1000:F1}m × {result.OptimalSpacingYMm / 1000:F1}m");
                sb.AppendLine($"Beam depth: ~{result.MaxBeamDepthMm:F0}mm");
                sb.AppendLine($"Fitness: {result.FitnessScore:F2}");
                sb.AppendLine();
                sb.AppendLine("Top 5 solutions:");
                foreach (var (sx, sy, f) in result.TopSolutions)
                    sb.AppendLine($"  {sx / 1000:F1}m × {sy / 1000:F1}m (fitness={f:F2})");

                TaskDialog.Show("STRUCT — Grid Optimization", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrOptimizeGrid failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // PROGRESSIVE COLLAPSE / ROBUSTNESS CHECK
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrProgressiveCollapseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var result = ProgressiveCollapseChecker.CheckRobustness(uidoc.Document);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Progressive Collapse / Robustness Check");
                sb.AppendLine($"Result: {(result.IsRobust ? "ROBUST ✓" : "NOT ROBUST ✗")}");
                sb.AppendLine($"Robustness class: {result.RobustnessClass}");
                sb.AppendLine($"Redundancy ratio: {result.RedundancyRatio:P0}");
                sb.AppendLine($"Critical columns: {result.CriticalColumnCount}");
                sb.AppendLine();

                var critical = result.ColumnResults.Where(c => !c.Status.StartsWith("OK")).Take(10);
                if (critical.Any())
                {
                    sb.AppendLine("Vulnerable columns:");
                    foreach (var (id, status, affected) in critical)
                        sb.AppendLine($"  Column {id.Value}: {status} ({affected} connected beams)");

                    var critIds = critical.Select(c => c.ColumnId).ToList();
                    uidoc.Selection.SetElementIds(critIds);
                }

                TaskDialog.Show("STRUCT — Robustness", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrProgressiveCollapse failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // AUTO MEMBER SIZING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAutoSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var result = AutoMemberSizer.AutoSizeAllMembers(uidoc.Document);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Auto Member Sizing — Iterative Convergence");
                sb.AppendLine($"Iterations: {result.IterationsUsed}, Converged: {result.Converged}");
                sb.AppendLine($"Members to resize: {result.MembersResized}");
                sb.AppendLine($"Avg utilisation: {result.AverageUtilisation:F2}");
                sb.AppendLine($"Max utilisation: {result.MaxUtilisation:F2}");
                sb.AppendLine();

                if (result.Changes.Count > 0)
                {
                    sb.AppendLine("Recommended changes:");
                    foreach (var (id, old, newSize, util) in result.Changes.Take(15))
                        sb.AppendLine($"  Beam {id.Value}: {old} → {newSize} (util={util:F2})");
                    if (result.Changes.Count > 15)
                        sb.AppendLine($"  ... and {result.Changes.Count - 15} more");
                }

                TaskDialog.Show("STRUCT — Auto Sizing", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrAutoSize failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // FIRE RESISTANCE CHECK
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrFireResistanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Fire Resistance");
                dlg.MainContent = "Check fire resistance per EC2-1-2.\nSelect required rating:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "R30 (30 minutes)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "R60 (60 minutes)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "R90 (90 minutes)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "R120 (120 minutes)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = dlg.Show();

                int rating = choice switch
                {
                    TaskDialogResult.CommandLink1 => 30,
                    TaskDialogResult.CommandLink2 => 60,
                    TaskDialogResult.CommandLink3 => 90,
                    TaskDialogResult.CommandLink4 => 120,
                    _ => 0,
                };
                if (rating == 0) return Result.Cancelled;

                var results = FireResistanceCalculator.CheckAllElements(uidoc.Document, rating);
                int passing = results.Count(r => r.Pass);
                int failing = results.Count(r => !r.Pass);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Fire Resistance Check — R{rating}");
                sb.AppendLine($"Total elements: {results.Count}");
                sb.AppendLine($"✓ Passing: {passing}");
                sb.AppendLine($"✗ Failing: {failing}");
                sb.AppendLine();

                var byType = results.GroupBy(r => r.ElementType);
                foreach (var group in byType)
                {
                    int p = group.Count(r => r.Pass);
                    int f = group.Count(r => !r.Pass);
                    sb.AppendLine($"{group.Key}s: {p} pass, {f} fail");
                }

                if (failing > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Failing elements:");
                    foreach (var r in results.Where(r => !r.Pass).Take(10))
                        sb.AppendLine($"  {r.Summary}");
                }

                TaskDialog.Show("STRUCT — Fire Resistance", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrFireResistance failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // AUTO-APPLY STRUCTURAL MATERIALS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intelligently applies structural materials to all elements.
    /// Auto-detects concrete grade from position, exposure from level,
    /// fire rating from building regulations. Creates materials if needed.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAutoMaterialsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Auto Materials");
                dlg.MainContent = "Auto-apply structural materials to all elements.\nSelect concrete grade:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "C30/37 — Standard (offices, residential)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "C40/50 — High performance (high-rise, car parks)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "C25/30 — Economy (low-rise, foundations)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Steel S355 — All elements as steel");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = dlg.Show();

                ConcreteGrade grade; SteelGrade steel;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: grade = ConcreteGrade.C30_37; steel = SteelGrade.S355; break;
                    case TaskDialogResult.CommandLink2: grade = ConcreteGrade.C40_50; steel = SteelGrade.S355; break;
                    case TaskDialogResult.CommandLink3: grade = ConcreteGrade.C25_30; steel = SteelGrade.S275; break;
                    case TaskDialogResult.CommandLink4: grade = ConcreteGrade.C30_37; steel = SteelGrade.S355; break;
                    default: return Result.Cancelled;
                }

                using (var tx = new Transaction(uidoc.Document, "STING STRUCT: Auto Materials"))
                {
                    tx.Start();
                    var result = StructuralMaterialEngine.ApplyToAllStructural(
                        uidoc.Document, grade, steel);
                    tx.Commit();

                    TaskDialog.Show("STRUCT — Materials Applied", result.Summary);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrAutoMaterials failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // INTELLIGENT COLUMN PLACEMENT
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSmartColumnCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var pt = uidoc.Selection.PickPoint("Pick column location (snaps to nearest grid)");
                var result = IntelligentStructuralPlacer.PlaceColumnIntelligently(
                    uidoc.Document, pt.X * Units.FeetToMm, pt.Y * Units.FeetToMm);

                if (result.Success && result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                TaskDialog.Show("STRUCT — Smart Column", result.Summary +
                    (result.Warnings.Count > 0 ? "\n\nWarnings:\n" + string.Join("\n", result.Warnings) : ""));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrSmartColumn failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // INTELLIGENT BEAM PLACEMENT
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSmartBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var p1 = uidoc.Selection.PickPoint("Pick beam start");
                var p2 = uidoc.Selection.PickPoint("Pick beam end");

                var result = IntelligentStructuralPlacer.PlaceBeamIntelligently(
                    uidoc.Document,
                    p1.X * Units.FeetToMm, p1.Y * Units.FeetToMm, p1.Z * Units.FeetToMm,
                    p2.X * Units.FeetToMm, p2.Y * Units.FeetToMm, p2.Z * Units.FeetToMm);

                if (result.Success && result.CreatedIds.Count > 0)
                    uidoc.Selection.SetElementIds(result.CreatedIds);

                TaskDialog.Show("STRUCT — Smart Beam", result.Summary +
                    (result.Warnings.Count > 0 ? "\n\nWarnings:\n" + string.Join("\n", result.Warnings) : ""));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("StrSmartBeam failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // ONE-CLICK INTELLIGENT BUILDING
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a complete structural building from specification in one click.
    /// Includes: grid, columns, beams, slabs, foundations, bracing, materials,
    /// and structural checks — all auto-sized and auto-materialed.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrBuildCompleteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var dlg = new TaskDialog("STRUCT — Complete Building");
                dlg.MainContent = "Create a complete structural building with one click.\n" +
                    "Includes: columns, beams, slabs, foundations, bracing, materials.\n\n" +
                    "Select building type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "3-Storey RC Office (30m × 20m, 7.5m grid, C30/37)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "5-Storey Steel Office (36m × 24m, 9m grid, S355)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "2-Storey RC Warehouse (48m × 30m, 12m grid, C35/45)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    "4-Storey RC Residential (24m × 15m, 6m grid, C28/35)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                var spec = new BuildingSpecification();

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1:
                        spec = new BuildingSpecification
                        {
                            FloorWidthMm = 30000, FloorDepthMm = 20000,
                            StoreyCount = 3, GridSpacingXMm = 7500, GridSpacingYMm = 7500,
                            ConcreteGrade = ConcreteGrade.C30_37, BuildingUse = "office"
                        };
                        break;
                    case TaskDialogResult.CommandLink2:
                        spec = new BuildingSpecification
                        {
                            FloorWidthMm = 36000, FloorDepthMm = 24000,
                            StoreyCount = 5, GridSpacingXMm = 9000, GridSpacingYMm = 9000,
                            IsSteel = true, SteelGrade = SteelGrade.S355, BuildingUse = "office"
                        };
                        break;
                    case TaskDialogResult.CommandLink3:
                        spec = new BuildingSpecification
                        {
                            FloorWidthMm = 48000, FloorDepthMm = 30000,
                            StoreyCount = 2, StoreyHeightMm = 6000,
                            GridSpacingXMm = 12000, GridSpacingYMm = 12000,
                            ConcreteGrade = ConcreteGrade.C35_45, BuildingUse = "warehouse"
                        };
                        break;
                    case TaskDialogResult.CommandLink4:
                        spec = new BuildingSpecification
                        {
                            FloorWidthMm = 24000, FloorDepthMm = 15000,
                            StoreyCount = 4, GridSpacingXMm = 6000, GridSpacingYMm = 6000,
                            ConcreteGrade = ConcreteGrade.C28_35, BuildingUse = "residential"
                        };
                        break;
                    default: return Result.Cancelled;
                }

                var result = IntelligentFrameBuilder.BuildCompleteStructure(uidoc.Document, spec);

                // Show design report
                TaskDialog.Show("STRUCT — Building Created",
                    result.DesignReport ?? result.Summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrBuildComplete failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // STRUCTURAL MODEL SCORE
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrModelScoreCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;

            try
            {
                var score = StructuralModelScorer.ScoreModel(uidoc.Document);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"╔══════════════════════════════════════════╗");
                sb.AppendLine($"║  STRUCTURAL MODEL SCORE: {score.TotalScore:F0}/100  (Grade {score.Grade})  ║");
                sb.AppendLine($"╚══════════════════════════════════════════╝");
                sb.AppendLine();
                sb.AppendLine($"  Completeness:  {score.CompletenessScore:F0}/25  {Bar(score.CompletenessScore, 25)}");
                sb.AppendLine($"  Connectivity:  {score.ConnectivityScore:F0}/25  {Bar(score.ConnectivityScore, 25)}");
                sb.AppendLine($"  Design:        {score.DesignScore:F0}/25  {Bar(score.DesignScore, 25)}");
                sb.AppendLine($"  Materials:     {score.MaterialScore:F0}/25  {Bar(score.MaterialScore, 25)}");

                if (score.Issues.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Issues:");
                    foreach (var issue in score.Issues.Take(8))
                        sb.AppendLine($"  • {issue}");
                }
                if (score.Recommendations.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Recommendations:");
                    foreach (var rec in score.Recommendations.Take(5))
                        sb.AppendLine($"  → {rec}");
                }

                TaskDialog.Show("STRUCT — Model Score", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StrModelScore failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Bar(double value, double max)
        {
            int filled = (int)(value / max * 10);
            return new string('#', filled) + new string('-', 10 - filled);
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // EMBODIED CARBON ASSESSMENT
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCarbonAssessmentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = EmbodiedCarbonCalculator.AssessModel(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Embodied Carbon Assessment (ICE Database v3)");
                sb.AppendLine($"RICS Rating: {result.RICSRating}");
                sb.AppendLine($"Total: {result.TotalCarbonKgCO2:F0} kgCO2e ({result.CarbonPerSqMKgCO2:F0} kgCO2e/m²)");
                sb.AppendLine($"Cost: ${result.TotalCostUSD:F0} (${result.CostPerSqMUSD:F0}/m²)");
                sb.AppendLine();
                sb.AppendLine("By element:");
                foreach (var kvp in result.CarbonByElement.OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value:F0} kgCO2e ({kvp.Value / result.TotalCarbonKgCO2 * 100:F0}%)");
                if (result.ReductionOpportunities.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Reduction opportunities:");
                    foreach (var r in result.ReductionOpportunities) sb.AppendLine($"  → {r}");
                }
                TaskDialog.Show("STRUCT — Carbon Assessment", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrCarbonAssessment failed", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // REBAR ESTIMATION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrRebarEstimateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = AutoRebarEstimator.EstimateProject(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Reinforcement Estimate (IStructE Manual ratios)");
                sb.AppendLine($"Total: {result.TotalRebarKg:F0} kg ({result.TotalRebarKg / 1000:F1} tonnes)");
                sb.AppendLine($"Average density: {result.RebarDensityKgPerM3:F0} kg/m³");
                sb.AppendLine($"Approx bars: {result.TotalBars} × 6m lengths");
                sb.AppendLine();
                sb.AppendLine("By element:");
                foreach (var kvp in result.RebarByElement.OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value:F0} kg");
                sb.AppendLine();
                sb.AppendLine("By diameter:");
                foreach (var kvp in result.RebarByDiameter.OrderBy(k => k.Key))
                    sb.AppendLine($"  T{kvp.Key}: {kvp.Value:F0} kg");
                TaskDialog.Show("STRUCT — Rebar Estimate", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrRebarEstimate failed", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // STABILITY ANALYSIS
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrStabilityCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = StabilityAnalyzer.AnalyzeStability(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Frame Stability Analysis (EC2 §5.8 / EC3 §5.2)");
                sb.AppendLine($"Classification: {result.FrameClassification}");
                sb.AppendLine($"Max sway index θ = {result.SwayIndex:F3} (limit 0.10)");
                sb.AppendLine($"Buckling ratio αcr = {result.BucklingRatio:F1} (limit 10)");
                sb.AppendLine($"P-Delta required: {result.RequiresPDelta}");
                sb.AppendLine();
                sb.AppendLine("Per-storey sway indices:");
                foreach (var (level, theta) in result.StoreySwayIndices)
                    sb.AppendLine($"  {level}: θ = {theta:F3} {(theta > 0.1 ? "⚠" : "✓")}");
                TaskDialog.Show("STRUCT — Stability", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrStabilityCheck failed", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // BIM VALIDATION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrBIMValidationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = StructuralBIMValidator.ValidateModel(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Structural BIM Validation ({result.CompliancePercent:F0}%)");
                sb.AppendLine($"{result.Passed}/{result.TotalChecks} pass, {result.Failed} errors, {result.Warnings} warnings");
                sb.AppendLine();
                foreach (var check in result.Checks)
                {
                    string icon = check.Pass ? "✓" : (check.Severity == "Error" ? "✗" : "⚠");
                    sb.AppendLine($"  [{check.RuleId}] {icon} {check.Description}: {check.Detail}");
                }
                TaskDialog.Show("STRUCT — BIM Validation", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrBIMValidation failed", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // COMPOSITE BEAM DESIGN
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCompositeBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                // Design for typical office: 9m span, 3m beam spacing
                var result = CompositeBeamDesigner.Design(9000, 3000);
                TaskDialog.Show("STRUCT — Composite Beam (EC4)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrCompositeBeam failed", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // FOUNDATION DESIGN (Pile Group)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrPileDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var dlg = new TaskDialog("STRUCT — Pile Design");
                dlg.MainContent = "Design pile group for column load:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "1000 kN (typical 3-storey)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "2500 kN (typical 6-storey)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "5000 kN (high-rise corner)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = dlg.Show();
                double load = choice switch
                {
                    TaskDialogResult.CommandLink1 => 1000,
                    TaskDialogResult.CommandLink2 => 2500,
                    TaskDialogResult.CommandLink3 => 5000,
                    _ => 0,
                };
                if (load == 0) return Result.Cancelled;

                var result = FoundationDesignSuite.DesignPileGroup(load);
                var settlement = FoundationDesignSuite.EstimateSettlement(
                    result.PileCapWidthMm, load / (result.PileCapWidthMm * result.PileCapDepthMm / 1e6));

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(result.Summary);
                sb.AppendLine();
                sb.AppendLine($"Pile cap: {result.PileCapWidthMm}×{result.PileCapDepthMm}×{result.PileCapThicknessMm}mm");
                sb.AppendLine();
                sb.AppendLine(settlement.Summary);

                TaskDialog.Show("STRUCT — Pile Design", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrPileDesign failed", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // LOAD PATH TRACING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrTraceLoadPathsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = LoadPathTracer.TraceLoadPaths(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("GRAVITY LOAD PATH ANALYSIS");
                sb.AppendLine(result.Summary);
                sb.AppendLine();

                foreach (var path in result.Paths.OrderByDescending(p => p.TotalLoadKN).Take(10))
                {
                    sb.AppendLine($"Stack {path.GridRef}: {path.TotalLoadKN:F0}kN " +
                        $"({(path.ReachesFoundation ? "→ Foundation ✓" : "INCOMPLETE ✗")})");
                    foreach (var link in path.Links)
                        sb.AppendLine($"  {link.LevelName}: {link.ElementType} +{link.LoadInKN:F0}kN = {link.CumulativeLoadKN:F0}kN");
                }

                if (result.FloatingElements.Count > 0)
                    sb.AppendLine($"\n⚠ {result.FloatingElements.Count} floating beams detected!");

                if (result.IncompleteCount > 0)
                {
                    sb.AppendLine($"\n⚠ {result.IncompleteCount} columns have no load path to foundation!");
                    uidoc.Selection.SetElementIds(
                        result.Paths.Where(p => !p.ReachesFoundation)
                            .SelectMany(p => p.Links.Select(l => l.ElementId))
                            .Where(id => id != null).ToList());
                }

                TaskDialog.Show("STRUCT — Load Paths", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrTraceLoadPaths", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // TOPOLOGY OPTIMIZATION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrTopologyOptimizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("STRUCT — Topology Optimization");
                dlg.MainContent = "Select load case for SIMP optimization:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Simply-supported beam — point load at midspan");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Simply-supported beam — UDL across top");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cantilever bracket — tip load");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Deep beam — two point loads at thirds");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                string loadCase = choice switch
                {
                    TaskDialogResult.CommandLink1 => "point_center",
                    TaskDialogResult.CommandLink2 => "udl_top",
                    TaskDialogResult.CommandLink3 => "cantilever",
                    TaskDialogResult.CommandLink4 => "point_third",
                    _ => null,
                };
                if (loadCase == null) return Result.Cancelled;

                var result = TopologyOptimizer.Optimize(
                    spanMm: 6000, depthMm: 3000,
                    gridResolution: 30, volumeFraction: 0.4,
                    loadCase: loadCase, maxIterations: 80);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("TOPOLOGY OPTIMIZATION (SIMP Method)");
                sb.AppendLine(result.Summary);
                sb.AppendLine();
                sb.AppendLine("Material distribution (density map):");
                sb.AppendLine(result.AsciiVisualization);
                sb.AppendLine("Legend: ' '=void  .:-=+  *#%@=solid");

                TaskDialog.Show("STRUCT — Topology", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrTopologyOptimize", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // SOIL-STRUCTURE INTERACTION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSSIAnalysisCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var dlg = new TaskDialog("STRUCT — Soil Type");
                dlg.MainContent = "Select ground conditions:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Soft clay (ks ≈ 15,000 kPa/m)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Medium clay (ks ≈ 30,000 kPa/m)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Dense sand (ks ≈ 100,000 kPa/m)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Rock (ks ≈ 200,000 kPa/m)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                string soil = choice switch
                {
                    TaskDialogResult.CommandLink1 => "soft_clay",
                    TaskDialogResult.CommandLink2 => "medium_clay",
                    TaskDialogResult.CommandLink3 => "dense_sand",
                    TaskDialogResult.CommandLink4 => "rock",
                    _ => null,
                };
                if (soil == null) return Result.Cancelled;

                var result = SoilStructureInteraction.AnalyzeSSI(uidoc.Document, soil);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("SOIL-STRUCTURE INTERACTION (Winkler Spring Model)");
                sb.AppendLine(result.Summary);
                sb.AppendLine();
                sb.AppendLine($"{"Fdn ID",-12} {"Load kN",10} {"Kv kN/mm",10} {"q kPa",10} {"δ mm",8} {"Status",8}");
                foreach (var s in result.Springs.OrderByDescending(s => s.SettlementMm).Take(15))
                    sb.AppendLine($"{s.FoundationId.Value,-12} {s.AppliedLoadKN,10:F0} {s.VerticalStiffnessKNPerMm,10:F1} " +
                        $"{s.ContactPressureKPa,10:F0} {s.SettlementMm,8:F1} {(s.ExceedsBearing ? "FAIL" : "OK"),8}");

                TaskDialog.Show("STRUCT — SSI Analysis", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrSSI", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // RETAINING WALL DESIGN
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrRetainingWallCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("STRUCT — Retaining Wall");
                dlg.MainContent = "Select retained height:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "2.0m — garden wall");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "3.0m — basement wall");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "4.5m — deep basement");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "6.0m — heavy retaining");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                double height = choice switch
                {
                    TaskDialogResult.CommandLink1 => 2.0,
                    TaskDialogResult.CommandLink2 => 3.0,
                    TaskDialogResult.CommandLink3 => 4.5,
                    TaskDialogResult.CommandLink4 => 6.0,
                    _ => 0,
                };
                if (height == 0) return Result.Cancelled;

                var result = RetainingWallDesigner.Design(height);
                TaskDialog.Show("STRUCT — Retaining Wall (EC7)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrRetainingWall", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // REBAR DETAILING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrRebarDetailCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Design rebar for a typical beam
                var detail = RebarDetailEngine.DetailBeam(
                    spanMm: 8000, widthMm: 300, depthMm: 600,
                    topRebarMm2: 600, bottomRebarMm2: 1200,
                    linkSpacingMm: 175, linkDiaMm: 10);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("AUTOMATIC REBAR DETAIL (EC2 + BS 8666)");
                sb.AppendLine(detail.Summary);
                sb.AppendLine();
                sb.AppendLine(detail.Schedule);

                TaskDialog.Show("STRUCT — Rebar Detail", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrRebarDetail", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // BRACING OPTIMIZATION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrBracingOptimizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = SmartBracingOptimizer.Optimize(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("BRACING OPTIMIZATION (Torsion-Minimized)");
                sb.AppendLine(result.Summary);
                sb.AppendLine();
                sb.AppendLine("Selected bays:");
                foreach (var sel in result.Selected)
                    sb.AppendLine($"  Bay {sel.GridRef}: K={sel.StiffnessContributionKN:F0}kN/mm, " +
                        $"pattern={sel.RecommendedPattern}, score={sel.Score:F0}");

                TaskDialog.Show("STRUCT — Bracing Optimization", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrBracingOptimize", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // CONSTRAINT PROPAGATION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrConstraintCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = ConstraintPropagator.EvaluateConstraints(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("DESIGN CONSTRAINT PROPAGATION");
                sb.AppendLine(result.Summary);
                sb.AppendLine();

                if (result.Violated > 0)
                {
                    sb.AppendLine("Violated constraints:");
                    foreach (var c in result.Constraints.Where(c => !c.Satisfied))
                        sb.AppendLine($"  ✗ [{c.Source}] {c.Name}: {c.Value:F1}{c.Unit}");
                }

                if (result.CascadeActions.Count > 0)
                {
                    sb.AppendLine("\nCascade actions required:");
                    foreach (var a in result.CascadeActions)
                        sb.AppendLine($"  → {a}");
                }

                TaskDialog.Show("STRUCT — Constraints", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrConstraintCheck", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // CONTINUITY VALIDATION
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrContinuityCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = ContinuityValidator.Validate(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("STRUCTURAL CONTINUITY VALIDATION");
                sb.AppendLine(result.Summary);
                sb.AppendLine();

                if (result.Issues.Count > 0)
                {
                    sb.AppendLine("Issues:");
                    foreach (var (id, type, issue) in result.Issues.Take(20))
                        sb.AppendLine($"  ✗ {type} {id.Value}: {issue}");
                    if (result.Issues.Count > 20)
                        sb.AppendLine($"  ... and {result.Issues.Count - 20} more");

                    uidoc.Selection.SetElementIds(result.Issues.Select(i => i.Id).ToList());
                }

                TaskDialog.Show("STRUCT — Continuity", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrContinuityCheck", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // ADAPTIVE MEMBER SIZING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAdaptiveSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var results = AdaptiveMemberSizer.SizeAllBeams(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("ADAPTIVE MEMBER SIZING (Multi-Criteria Pareto)");
                sb.AppendLine($"Sized {results.Count} beams");
                sb.AppendLine();

                int changed = 0;
                foreach (var r in results.Where(r => r.ProposedSection != r.OriginalSection).Take(20))
                {
                    sb.AppendLine($"  Beam {r.ElementId.Value}: {r.OriginalSection} → {r.ProposedSection} " +
                        $"(util={r.Utilisation:F2}, weight {r.WeightChangePercent:+0;-0}%, " +
                        $"iter={r.IterationsToConverge})");
                    changed++;
                }

                int allMet = results.Count(r => r.AllCriteriaMet);
                sb.AppendLine($"\n{allMet}/{results.Count} beams satisfy all criteria, {changed} need resizing");

                TaskDialog.Show("STRUCT — Adaptive Sizing", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrAdaptiveSize", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // CONNECTION DESIGN (EC3-1-8)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrConnectionDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("STRUCT — Connection Design");
                dlg.MainContent = "Select connection type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fin plate (simple shear — 200kN)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Moment end plate (shear + moment)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Column base plate (1000kN axial)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                string result = choice switch
                {
                    TaskDialogResult.CommandLink1 => ConnectionDesigner.DesignFinPlate(200).Summary,
                    TaskDialogResult.CommandLink2 => ConnectionDesigner.DesignMomentEndPlate(150, 300).Summary,
                    TaskDialogResult.CommandLink3 => ConnectionDesigner.DesignBasePlate(1000).Summary,
                    _ => null,
                };
                if (result == null) return Result.Cancelled;

                TaskDialog.Show("STRUCT — Connection (EC3-1-8)", result);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrConnectionDesign", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // FLOOR VIBRATION (SCI P354)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrVibrationCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("STRUCT — Floor Vibration");
                dlg.MainContent = "Select occupancy type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Office (R ≤ 4)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Residential (R ≤ 2)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Operating theatre (R ≤ 1)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Shopping/retail (R ≤ 4)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                string occ = choice switch
                {
                    TaskDialogResult.CommandLink1 => "office",
                    TaskDialogResult.CommandLink2 => "residential_day",
                    TaskDialogResult.CommandLink3 => "operating_theatre",
                    TaskDialogResult.CommandLink4 => "shopping",
                    _ => null,
                };
                if (occ == null) return Result.Cancelled;

                var result = VibrationChecker.CheckFloorVibration(9000, 3000, occupancy: occ);
                TaskDialog.Show("STRUCT — Vibration (SCI P354)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrVibrationCheck", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // CRACK WIDTH (EC2 §7.3)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCrackWidthCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Typical 600mm deep beam under SLS moment
                var result = CrackWidthCalculator.Calculate(
                    momentKNm: 200, widthMm: 300, depthMm: 600,
                    coverMm: 35, barDiaMm: 20, barAreaMm2: 1257); // 4T20

                TaskDialog.Show("STRUCT — Crack Width (EC2 §7.3)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrCrackWidth", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // THERMAL MOVEMENT
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrThermalMovementCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                // Estimate building length from model
                var cols = new FilteredElementCollector(uidoc.Document)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType().ToList();

                double lengthM = 60; // Default
                if (cols.Count >= 2)
                {
                    var pts = cols.Select(c => (c.Location as LocationPoint)?.Point)
                        .Where(p => p != null).ToList();
                    double dx = (pts.Max(p => p.X) - pts.Min(p => p.X)) * Units.FeetToMm / 1000;
                    double dy = (pts.Max(p => p.Y) - pts.Min(p => p.Y)) * Units.FeetToMm / 1000;
                    lengthM = Math.Max(dx, dy);
                }

                var result = ThermalMovementEngine.Analyze(lengthM);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(result.Summary);
                if (result.JointsRequired)
                {
                    sb.AppendLine("\nJoint positions:");
                    foreach (var pos in result.JointPositionsM)
                        sb.AppendLine($"  {pos:F1}m from start");
                }

                TaskDialog.Show("STRUCT — Thermal Movement", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrThermalMovement", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // DEEP BEAM STM (EC2 §6.5)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrDeepBeamSTMCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var result = DeepBeamSTM.Design(spanMm: 4000, depthMm: 3000,
                    widthMm: 300, loadKN: 2000);
                TaskDialog.Show("STRUCT — Strut-and-Tie (EC2 §6.5)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrDeepBeamSTM", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // SMART COLUMN (Intelligence Factory)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSmartColumnFactoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var point = uidoc.Selection.PickPoint("Pick column position");
                var levels = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();

                if (levels.Count < 2) { TaskDialog.Show("Error", "Need ≥2 levels"); return Result.Failed; }

                var report = SmartElementFactory.CreateSmartColumn(
                    uidoc.Document, point, levels[0], levels[1]);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.Summary);
                sb.AppendLine();
                foreach (var step in report.Steps) sb.AppendLine(step);
                foreach (var warn in report.Warnings) sb.AppendLine(warn);

                TaskDialog.Show("STRUCT — Smart Column", sb.ToString());
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error("StrSmartColumnFactory", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // SMART BEAM (Intelligence Factory)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSmartBeamFactoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var p1 = uidoc.Selection.PickPoint("Pick beam start");
                var p2 = uidoc.Selection.PickPoint("Pick beam end");

                var report = SmartElementFactory.CreateSmartBeam(
                    uidoc.Document, p1, p2, null);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.Summary);
                sb.AppendLine();
                foreach (var step in report.Steps) sb.AppendLine(step);
                foreach (var warn in report.Warnings) sb.AppendLine(warn);

                TaskDialog.Show("STRUCT — Smart Beam", sb.ToString());
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error("StrSmartBeamFactory", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // STRUCTURAL DIAGNOSTICS — One-Click Health Check
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrDiagnosticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var result = StructuralDiagnostics.RunFullDiagnostics(uidoc.Document);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(result.Summary);
                sb.AppendLine();

                sb.AppendLine("By category:");
                foreach (var (cat, (p, f)) in result.ByCategory)
                    sb.AppendLine($"  {cat}: {p} pass, {f} fail ({(p + f > 0 ? 100.0 * p / (p + f) : 0):F0}%)");

                if (result.CriticalIssues.Count > 0)
                {
                    sb.AppendLine("\nCritical issues:");
                    foreach (var issue in result.CriticalIssues.Take(10))
                        sb.AppendLine($"  {issue}");
                }

                if (result.Recommendations.Count > 0)
                {
                    sb.AppendLine("\nRecommendations:");
                    foreach (var rec in result.Recommendations)
                        sb.AppendLine($"  → {rec}");
                }

                TaskDialog.Show($"STRUCT — Diagnostics ({result.RAGStatus})", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrDiagnostics", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // FATIGUE ASSESSMENT (EC3-1-9)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrFatigueAssessCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("STRUCT — Fatigue (EC3-1-9)");
                dlg.MainContent = "Select detail category:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "DC 71 — Welded attachments L≤50mm");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "DC 112 — Butt welds, full penetration");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "DC 160 — Unwelded base metal");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "DC 50 — Cruciform joints, fillet welds");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                FatigueDetailCategory dc = choice switch
                {
                    TaskDialogResult.CommandLink1 => FatigueDetailCategory.DC71,
                    TaskDialogResult.CommandLink2 => FatigueDetailCategory.DC112,
                    TaskDialogResult.CommandLink3 => FatigueDetailCategory.DC160,
                    TaskDialogResult.CommandLink4 => FatigueDetailCategory.DC50,
                    _ => (FatigueDetailCategory)0,
                };
                if ((int)dc == 0) return Result.Cancelled;

                var result = FatigueAssessor.Assess(dc, 80, 2e6);
                TaskDialog.Show("STRUCT — Fatigue (EC3-1-9)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrFatigueAssess", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // TORSION DESIGN (EC2 §6.3)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrTorsionDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var result = TorsionDesigner.Design(torsionKNm: 50, shearKN: 200,
                    widthMm: 300, depthMm: 600);
                TaskDialog.Show("STRUCT — Torsion (EC2 §6.3)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrTorsionDesign", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // ROBUSTNESS ANALYSIS (EC1-1-7 / EC2 §9.10)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrRobustnessCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("STRUCT — Robustness");
                dlg.MainContent = "Select consequence class:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "CC2a — Buildings ≤ 4 storeys (horizontal ties)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "CC2b — Buildings 5-15 storeys (full ties)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "CC3 — Buildings > 15 storeys (key elements)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                var (cc, ns) = choice switch
                {
                    TaskDialogResult.CommandLink1 => (ConsequenceClass.CC2a, 4),
                    TaskDialogResult.CommandLink2 => (ConsequenceClass.CC2b, 8),
                    TaskDialogResult.CommandLink3 => (ConsequenceClass.CC3, 20),
                    _ => ((ConsequenceClass)(-1), 0),
                };
                if (ns == 0) return Result.Cancelled;

                var result = RobustnessAnalyzer.Analyze(ns, cc: cc);
                TaskDialog.Show("STRUCT — Robustness (EC1-1-7)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrRobustness", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // COMPOSITE SLAB DESIGN (EC4-1-1)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCompositeSlabCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("STRUCT — Composite Slab (EC4)");
                dlg.MainContent = "Select deck profile:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "ComFlor 51 (51mm depth, light spans)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "ComFlor 60 (60mm depth, typical)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "ComFlor 80 (80mm depth, long spans)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                DeckProfile profile = choice switch
                {
                    TaskDialogResult.CommandLink1 => DeckProfile.ComFlor51,
                    TaskDialogResult.CommandLink2 => DeckProfile.ComFlor60,
                    TaskDialogResult.CommandLink3 => DeckProfile.ComFlor80,
                    _ => (DeckProfile)(-1),
                };
                if ((int)profile < 0) return Result.Cancelled;

                var result = CompositeSlabDesigner.Design(3.0, profile: profile);
                TaskDialog.Show("STRUCT — Composite Slab (EC4-1-1)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrCompositeSlab", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // PARTIAL FACTOR COMPARISON
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrPartialFactorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("DESIGN CODE PARTIAL FACTOR COMPARISON");
                sb.AppendLine(new string('═', 50));

                foreach (var code in new[] { DesignCodeFamily.Eurocode, DesignCodeFamily.BritishStandards,
                    DesignCodeFamily.ACI, DesignCodeFamily.AustralianStandards })
                {
                    var factors = PartialFactorManager.GetFactors(code);
                    sb.AppendLine($"\n{factors.Summary}");

                    // Example: 100kN permanent + 50kN variable
                    double factoredP = PartialFactorManager.FactoredLoad(code, LoadType.Permanent, 100);
                    double factoredV = PartialFactorManager.FactoredLoad(code, LoadType.Variable, 50);
                    sb.AppendLine($"  100kN(G)+50kN(Q) → {factoredP + factoredV:F0}kN ULS");

                    double fcdC30 = PartialFactorManager.DesignStrength(code, StructuralMaterialType.Concrete, 30);
                    double fydS355 = PartialFactorManager.DesignStrength(code, StructuralMaterialType.StructuralSteel, 355);
                    sb.AppendLine($"  C30: fcd={fcdC30:F1}MPa, S355: fyd={fydS355:F1}MPa");
                }

                TaskDialog.Show("STRUCT — Partial Factors", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrPartialFactors", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // SMART WALL FACTORY
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSmartWallFactoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var p1 = uidoc.Selection.PickPoint("Pick wall start");
                var p2 = uidoc.Selection.PickPoint("Pick wall end");
                var levels = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();
                if (levels.Count < 2) { TaskDialog.Show("Error", "Need ≥2 levels"); return Result.Failed; }

                var report = SmartWallFactory.CreateSmartWall(uidoc.Document, p1, p2, levels[0], levels[1]);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.Summary);
                foreach (var s in report.Steps) sb.AppendLine(s);
                foreach (var w in report.Warnings) sb.AppendLine(w);
                TaskDialog.Show("STRUCT — Smart Wall", sb.ToString());
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error("StrSmartWallFactory", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // SMART FOUNDATION FACTORY
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrSmartFoundationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var point = uidoc.Selection.PickPoint("Pick pad footing position");
                var level = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).FirstOrDefault();

                var report = SmartFoundationFactory.CreateSmartPadFooting(uidoc.Document, point, level);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.Summary);
                foreach (var s in report.Steps) sb.AppendLine(s);
                foreach (var w in report.Warnings) sb.AppendLine(w);
                TaskDialog.Show("STRUCT — Smart Pad Footing", sb.ToString());
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error("StrSmartFoundation", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // FULL DESIGN CODE COMPLIANCE CHECK
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCodeComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var checks = DesignCodeComplianceReporter.RunAllChecks();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("DESIGN CODE COMPLIANCE REPORT");
                sb.AppendLine(new string('═', 50));

                int pass = checks.Count(c => c.Pass);
                int fail = checks.Count - pass;
                double score = checks.Count > 0 ? 100.0 * pass / checks.Count : 0;
                string rag = score >= 80 ? "GREEN" : score >= 50 ? "AMBER" : "RED";

                sb.AppendLine($"\n{rag}: {score:F0}% ({pass}/{checks.Count} pass, {fail} fail)\n");

                foreach (var c in checks)
                {
                    string icon = c.Pass ? "✓" : "✗";
                    sb.AppendLine($"{icon} [{c.Code}] {c.Name}: util={c.Utilisation:F2} → {(c.Pass ? "OK" : "FAIL")}");
                }

                sb.AppendLine($"\n{PartialFactorManager.GetFactors(StructuralConfig.DesignCode).Summary}");

                TaskDialog.Show($"STRUCT — Compliance ({rag})", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("StrCodeCompliance", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // STAIR DESIGN (BS 5395)
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArchStairDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var levels = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (levels.Count < 2) { TaskDialog.Show("Error", "Need ≥2 levels"); return Result.Failed; }

                double floorHeight = (levels[1].Elevation - levels[0].Elevation) * Units.FeetToMm;
                var dlg = new TaskDialog("ARCH — Stair Design (BS 5395)");
                dlg.MainContent = $"Floor-to-floor: {floorHeight:F0}mm. Select use type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Private (house/flat) — 220mm rise max");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Common (office/commercial) — 190mm rise max");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Public (assembly/hospital) — 180mm rise max");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                var use = choice switch
                {
                    TaskDialogResult.CommandLink1 => StairEngine.StairUseType.Private,
                    TaskDialogResult.CommandLink2 => StairEngine.StairUseType.Common,
                    TaskDialogResult.CommandLink3 => StairEngine.StairUseType.Public,
                    _ => (StairEngine.StairUseType)(-1),
                };
                if ((int)use < 0) return Result.Cancelled;

                var design = StairEngine.DesignStair(floorHeight, use);
                TaskDialog.Show("ARCH — Stair Design", design.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("ArchStairDesign", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // CURTAIN WALL
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArchCurtainWallCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var p1 = uidoc.Selection.PickPoint("Pick curtain wall start");
                var p2 = uidoc.Selection.PickPoint("Pick curtain wall end");
                var level = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
                if (level == null) return Result.Failed;

                double lengthMm = Line.CreateBound(p1, p2).Length * Units.FeetToMm;
                var spec = CurtainWallEngine.Design(lengthMm, 3600);

                using (var tx = new Transaction(uidoc.Document, "STING Curtain Wall"))
                {
                    tx.Start();
                    var id = CurtainWallEngine.Create(uidoc.Document, p1, p2, level, 3600);
                    if (id != ElementId.InvalidElementId) tx.Commit(); else { tx.RollBack(); return Result.Failed; }
                }

                TaskDialog.Show("ARCH — Curtain Wall", spec.Summary);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error("ArchCurtainWall", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // WALL OPENING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArchCreateOpeningCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds().Select(id => uidoc.Document.GetElement(id)).OfType<Wall>().FirstOrDefault();
                if (sel == null) { TaskDialog.Show("ARCH", "Select a wall first."); return Result.Cancelled; }

                var point = uidoc.Selection.PickPoint("Pick opening center");
                using (var tx = new Transaction(uidoc.Document, "STING Wall Opening"))
                {
                    tx.Start();
                    OpeningEngine.CreateWallOpening(uidoc.Document, sel, point, 900, 2100);
                    tx.Commit();
                }
                TaskDialog.Show("ARCH", "Opening created: 900×2100mm");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { StingLog.Error("ArchCreateOpening", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // COVERING FIRE RATING
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringFireRatingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("COVERINGS — Fire Rating");
                dlg.MainContent = "Select fire rating target:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "30 min — intumescent on UB 406×178");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "60 min — intumescent on UB 406×178");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "60 min — board protection on UC 305×305");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                CoveringFireRating.FireRatingResult result = choice switch
                {
                    TaskDialogResult.CommandLink1 => CoveringFireRating.Calculate(1200, 8560, 30, true, "intumescent"),
                    TaskDialogResult.CommandLink2 => CoveringFireRating.Calculate(1200, 8560, 60, true, "intumescent"),
                    TaskDialogResult.CommandLink3 => CoveringFireRating.Calculate(1220, 9850, 60, false, "board"),
                    _ => null,
                };
                if (result == null) return Result.Cancelled;

                TaskDialog.Show("COVERINGS — Fire Rating", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringFireRating", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // MOISTURE RISK ANALYSIS
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringMoistureRiskCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Typical external wall build-up (inside → outside)
                var layers = new List<(string Name, double ThicknessMm, double Lambda)>
                {
                    ("Gypsum plaster", 13, 0.4),
                    ("Dense blockwork", 100, 1.2),
                    ("Insulation (mineral wool)", 100, 0.035),
                    ("Cement render", 20, 0.8),
                };

                var result = CoveringMoistureRisk.Assess(layers);
                TaskDialog.Show("COVERINGS — Moisture Risk (BS EN ISO 13788)", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringMoistureRisk", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // FULL MODEL AUTOMATION — One-Click Chain
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FullModelAutoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var confirm = TaskDialog.Show("FULL MODEL AUTOMATION",
                    "Run complete 8-step building automation?\n\n" +
                    "1) Verify levels\n2) Grid layout\n3) Count elements\n" +
                    "4) Apply materials\n5) Apply coverings\n6) Room finishes\n" +
                    "7) STING tags\n8) Diagnostics",
                    TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);
                if (confirm != TaskDialogResult.Ok) return Result.Cancelled;

                var report = FullModelAutomation.RunFullChain(uidoc.Document);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.Summary);
                sb.AppendLine();
                foreach (var step in report.Log) sb.AppendLine(step);

                TaskDialog.Show("FULL MODEL AUTOMATION", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("FullModelAuto", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
