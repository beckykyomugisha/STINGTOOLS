// ============================================================================
// PlasteringCommands.cs — Plastering Plugin IExternalCommand Classes
//
// 10 commands covering the full plastering lifecycle:
//   1. PlasterMaterialBrowserCommand    — Browse BS EN 998-1 material database
//   2. PlasterSurfaceAnalyzeCommand     — Analyze selected wall substrate
//   3. PlasterMixDesignCommand          — Design multi-coat build-up
//   4. PlasterCoverageCalcCommand       — Coverage, bags, cost, carbon
//   5. PlasterQualityChecklistCommand   — 15-point BS EN 13914 QA checklist
//   6. PlasterAddLayersCommand          — Inject plaster layers into wall type
//   7. PlasterSmartApplyCommand         — Full 7-step intelligent pipeline
//   8. PlasterBatchApplyCommand         — Batch plaster all walls by filter
//   9. PlasterScheduleExportCommand     — Export plaster schedule to CSV
//  10. PlasterCompareSpecCommand        — Compare internal vs external specs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;

namespace StingTools.Model
{
    // ══════════════════════════════════════════════════════════════════
    // 1. MATERIAL BROWSER — BS EN 998-1 Database
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterMaterialBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var materials = PlasterMaterialScience.GetAllMaterials();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("PLASTER MATERIAL DATABASE (BS EN 998-1)");
                sb.AppendLine(new string('═', 55));

                foreach (var m in materials)
                {
                    sb.AppendLine($"\n{m.Name}");
                    sb.AppendLine($"  Type: {m.Type}, Class: {m.BSClassification}");
                    sb.AppendLine($"  Mix: {m.MixRatio}, Density: {m.DensityKgM3}kg/m³");
                    sb.AppendLine($"  Strength: {m.CompressiveStrengthMPa:F1}MPa, λ={m.ThermalConductivity}W/mK");
                    sb.AppendLine($"  Coverage: {m.CoverageM2PerBag}m²/bag, £{m.CostPerBag:F2}/bag");
                    sb.AppendLine($"  Curing: {m.CuringTimeHours}h, Fire: {m.FireResistanceMinutes}min");
                }

                TaskDialog.Show("PLASTER — Materials (BS EN 998-1)", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterMaterialBrowser", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 2. SURFACE ANALYZE — Substrate Detection & Suction Classification
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterSurfaceAnalyzeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds();
                var walls = sel.Select(id => uidoc.Document.GetElement(id)).OfType<Wall>().ToList();

                if (walls.Count == 0)
                {
                    TaskDialog.Show("PLASTER", "Select one or more walls first.");
                    return Result.Cancelled;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"SURFACE ANALYSIS — {walls.Count} wall(s)");
                sb.AppendLine(new string('═', 50));

                foreach (var wall in walls.Take(5))
                {
                    var substrate = SurfaceAnalyzer.DetectSubstrate(wall);
                    var analysis = SurfaceAnalyzer.AnalyzeSurface(substrate, false);
                    sb.AppendLine($"\nWall {wall.Id.Value} ({wall.WallType.Name}):");
                    sb.AppendLine(analysis.Summary);
                }

                if (walls.Count > 5) sb.AppendLine($"\n... and {walls.Count - 5} more walls");

                TaskDialog.Show("PLASTER — Surface Analysis", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterSurfaceAnalyze", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 3. MIX DESIGN — Multi-Coat Build-Up Specification
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterMixDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("PLASTER — Mix Design");
                dlg.MainContent = "Select application:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Internal — Dense block substrate");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Internal — Plasterboard (skim only)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "External — 3-coat render (blockwork)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "External — Insulating render system");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                PlasterBuildUp buildUp = choice switch
                {
                    TaskDialogResult.CommandLink1 => PlasterMixDesigner.DesignBuildUp(SubstrateType.DenseBlock, false),
                    TaskDialogResult.CommandLink2 => PlasterMixDesigner.DesignBuildUp(SubstrateType.Plasterboard, false),
                    TaskDialogResult.CommandLink3 => PlasterMixDesigner.DesignBuildUp(SubstrateType.DenseBlock, true),
                    TaskDialogResult.CommandLink4 => PlasterMixDesigner.DesignBuildUp(SubstrateType.LightweightBlock, true, requiresInsulation: true),
                    _ => null,
                };
                if (buildUp == null) return Result.Cancelled;

                TaskDialog.Show("PLASTER — Mix Design (BS EN 13914)", buildUp.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterMixDesign", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 4. COVERAGE CALCULATOR — Area, Bags, Cost, Carbon
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterCoverageCalcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds();
                var walls = sel.Select(id => uidoc.Document.GetElement(id)).OfType<Wall>().ToList();

                if (walls.Count == 0)
                {
                    // Fall back to all walls in view
                    walls = new FilteredElementCollector(uidoc.Document, uidoc.Document.ActiveView.Id)
                        .OfClass(typeof(Wall)).Cast<Wall>().ToList();
                }

                if (walls.Count == 0)
                {
                    TaskDialog.Show("PLASTER", "No walls found.");
                    return Result.Cancelled;
                }

                var result = PlasterCoverageEngine.CalculateCoverage(
                    uidoc.Document, walls, PlasterConfig.InternalRenderThicknessMm, false);

                TaskDialog.Show("PLASTER — Coverage Calculator", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterCoverageCalc", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 5. QUALITY CHECKLIST — 15-Point BS EN 13914
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterQualityChecklistCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var buildUp = PlasterMixDesigner.DesignBuildUp(SubstrateType.DenseBlock, false);
                var surface = SurfaceAnalyzer.AnalyzeSurface(SubstrateType.DenseBlock, false);
                var checks = PlasterQualityInspector.GenerateChecklist(buildUp, surface);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("PLASTER QA CHECKLIST (BS EN 13914 / BS 8000-10)");
                sb.AppendLine(new string('═', 55));

                int passed = checks.Count(c => c.Pass);
                sb.AppendLine($"\nOverall: {passed}/{checks.Count} PASS\n");

                foreach (var c in checks)
                {
                    string icon = c.Pass ? "✓" : "✗";
                    sb.AppendLine($"{icon} [{c.Number:D2}] {c.Description}");
                    sb.AppendLine($"    Standard: {c.Standard}");
                    sb.AppendLine($"    Criteria: {c.Criteria}");
                }

                TaskDialog.Show("PLASTER — QA Checklist", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterQualityChecklist", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 6. ADD LAYERS — Inject Plaster into Wall Type
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterAddLayersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds();
                var wall = sel.Select(id => uidoc.Document.GetElement(id)).OfType<Wall>().FirstOrDefault();
                if (wall == null) { TaskDialog.Show("PLASTER", "Select a wall."); return Result.Cancelled; }

                var dlg = new TaskDialog("PLASTER — Add Layers");
                dlg.MainContent = "Apply plaster to:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Interior face (backing + skim)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Exterior face (3-coat render)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                bool isExternal = choice == TaskDialogResult.CommandLink2;
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                var substrate = SurfaceAnalyzer.DetectSubstrate(wall);
                var buildUp = PlasterMixDesigner.DesignBuildUp(substrate, isExternal);

                using (var tx = new Transaction(uidoc.Document, "STING Plaster Layers"))
                {
                    tx.Start();
                    var result = PlasterLayerBuilder.AddPlasterLayers(
                        uidoc.Document, wall.WallType, buildUp, !isExternal);
                    tx.Commit();

                    TaskDialog.Show("PLASTER — Layers Added", result.Summary);
                }

                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterAddLayers", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 7. SMART APPLY — Full 7-Step Intelligent Pipeline
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterSmartApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds();
                var walls = sel.Select(id => uidoc.Document.GetElement(id)).OfType<Wall>().ToList();
                if (walls.Count == 0) { TaskDialog.Show("PLASTER", "Select walls first."); return Result.Cancelled; }

                var dlg = new TaskDialog("PLASTER — Smart Apply");
                dlg.MainContent = $"Plaster {walls.Count} wall(s):";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Internal plaster (backing + skim)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "External render (3-coat system)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                bool isExternal = choice == TaskDialogResult.CommandLink2;
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                SmartPlasterFactory.PlasteringReport report;
                using (var tx = new Transaction(uidoc.Document, "STING Smart Plaster"))
                {
                    tx.Start();
                    report = SmartPlasterFactory.PlasterWalls(uidoc.Document, walls, isExternal);
                    if (report.Success) tx.Commit(); else tx.RollBack();
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.Summary);
                sb.AppendLine();
                foreach (var s in report.Steps) sb.AppendLine(s);
                foreach (var w in report.Warnings) sb.AppendLine($"⚠ {w}");

                TaskDialog.Show("PLASTER — Smart Apply", sb.ToString());
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("PlasterSmartApply", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 8. BATCH APPLY — All Walls by Category Filter
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterBatchApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var allWalls = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(Wall)).Cast<Wall>()
                    .Where(w => w.WallType.Kind == WallKind.Basic)
                    .ToList();

                if (allWalls.Count == 0) { TaskDialog.Show("PLASTER", "No basic walls found."); return Result.Cancelled; }

                var dlg = new TaskDialog("PLASTER — Batch Apply");
                dlg.MainContent = $"Found {allWalls.Count} basic walls.\n" +
                    "Apply plaster to ALL walls?";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Internal plaster — all {allWalls.Count} walls");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Internal plaster — current view only");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                IList<Wall> scope;

                if (choice == TaskDialogResult.CommandLink1)
                    scope = allWalls;
                else if (choice == TaskDialogResult.CommandLink2)
                    scope = new FilteredElementCollector(uidoc.Document, uidoc.Document.ActiveView.Id)
                        .OfClass(typeof(Wall)).Cast<Wall>()
                        .Where(w => w.WallType.Kind == WallKind.Basic).ToList();
                else return Result.Cancelled;

                SmartPlasterFactory.PlasteringReport report;
                using (var tx = new Transaction(uidoc.Document, "STING Batch Plaster"))
                {
                    tx.Start();
                    report = SmartPlasterFactory.PlasterWalls(uidoc.Document, scope, false);
                    if (report.Success) tx.Commit(); else tx.RollBack();
                }

                TaskDialog.Show("PLASTER — Batch Result", report.Summary);
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("PlasterBatchApply", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 9. SCHEDULE EXPORT — Plaster Schedule to CSV
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterScheduleExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var walls = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(Wall)).Cast<Wall>()
                    .Where(w => w.WallType.Kind == WallKind.Basic).ToList();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Wall ID,Wall Type,Level,Length (mm),Height (mm),Area (m²),Substrate,Plaster Spec,Thickness (mm),Bags,Cost (£)");

                foreach (var wall in walls)
                {
                    var substrate = SurfaceAnalyzer.DetectSubstrate(wall);
                    var buildUp = PlasterMixDesigner.DesignBuildUp(substrate, false);
                    var heightP = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    double hFt = heightP?.AsDouble() ?? 10;
                    double lFt = (wall.Location as LocationCurve)?.Curve.Length ?? 0;
                    double areaM2 = hFt * lFt * Units.SqFtToSqM;
                    var mat = PlasterMaterialScience.RecommendMaterial(substrate, false);
                    int bags = (int)Math.Ceiling(areaM2 / Math.Max(mat.CoverageM2PerBag, 0.1));

                    var levelParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    string level = levelParam != null ? (uidoc.Document.GetElement(levelParam.AsElementId()) as Level)?.Name ?? "—" : "—";

                    sb.AppendLine($"{wall.Id.Value},{wall.WallType.Name},{level}," +
                        $"{lFt * Units.FeetToMm:F0},{hFt * Units.FeetToMm:F0},{areaM2:F1}," +
                        $"{substrate},{buildUp.Coats.FirstOrDefault()?.Material ?? "—"}," +
                        $"{buildUp.TotalThicknessMm:F0},{bags},{bags * mat.CostPerBag:F0}");
                }

                // Save to file
                var outputPath = OutputLocationHelper.GetTimestampedPath(uidoc.Document, "PlasterSchedule", "csv");
                System.IO.File.WriteAllText(outputPath, sb.ToString());

                TaskDialog.Show("PLASTER — Schedule Export",
                    $"Exported {walls.Count} walls to:\n{outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterScheduleExport", ex); message = ex.Message; return Result.Failed; }
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // 10. COMPARE SPEC — Internal vs External Side-by-Side
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlasterCompareSpecCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var intBuildUp = PlasterMixDesigner.DesignBuildUp(SubstrateType.DenseBlock, false);
                var extBuildUp = PlasterMixDesigner.DesignBuildUp(SubstrateType.DenseBlock, true);
                var intMat = PlasterMaterialScience.RecommendMaterial(SubstrateType.DenseBlock, false);
                var extMat = PlasterMaterialScience.RecommendMaterial(SubstrateType.DenseBlock, true);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("PLASTER SPECIFICATION COMPARISON");
                sb.AppendLine(new string('═', 55));

                sb.AppendLine("\n┌─ INTERNAL ──────────────────────────────┐");
                sb.AppendLine(intBuildUp.Summary);
                sb.AppendLine($"  Material: {intMat.Name} ({intMat.BSClassification})");
                sb.AppendLine($"  λ = {intMat.ThermalConductivity} W/mK, fire = {intMat.FireResistanceMinutes}min");

                sb.AppendLine("\n┌─ EXTERNAL ──────────────────────────────┐");
                sb.AppendLine(extBuildUp.Summary);
                sb.AppendLine($"  Material: {extMat.Name} ({extMat.BSClassification})");
                sb.AppendLine($"  λ = {extMat.ThermalConductivity} W/mK, fire = {extMat.FireResistanceMinutes}min");

                sb.AppendLine("\n┌─ KEY DIFFERENCES ───────────────────────┐");
                sb.AppendLine($"  Thickness: INT {intBuildUp.TotalThicknessMm:F0}mm vs EXT {extBuildUp.TotalThicknessMm:F0}mm");
                sb.AppendLine($"  Coats: INT {intBuildUp.Coats.Count} vs EXT {extBuildUp.Coats.Count}");
                sb.AppendLine($"  Drying: INT {intBuildUp.TotalDryingDays:F1}d vs EXT {extBuildUp.TotalDryingDays:F1}d");
                sb.AppendLine($"  Cost/bag: INT £{intMat.CostPerBag:F2} vs EXT £{extMat.CostPerBag:F2}");

                TaskDialog.Show("PLASTER — Specification Comparison", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlasterCompareSpec", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
