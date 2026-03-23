// ============================================================================
// PlasteringCommands.cs — Integrated Coverings, Plastering & Painting Commands
//
// 12 commands supporting walls, beams, columns, floors, ceilings + painting:
//   1. CoveringMaterialBrowserCommand   — Browse BLE plaster + paint database
//   2. CoveringSubstrateAnalyzeCommand  — Analyze ANY element substrate
//   3. CoveringMixDesignCommand         — Multi-coat plaster build-up
//   4. CoveringPaintSystemCommand       — Paint system spec (DFT, VOC, coats)
//   5. CoveringCoverageCalcCommand      — Element-agnostic coverage calculator
//   6. CoveringAddLayersCommand         — Inject layers into wall compound types
//   7. CoveringSmartApplyCommand        — Full pipeline on selected elements
//   8. CoveringBatchApplyCommand        — Batch all walls/beams/columns
//   9. CoveringPaintApplyCommand        — Paint system on any elements
//  10. CoveringRoomScheduleCommand      — Room finish schedule generation
//  11. CoveringQualityCheckCommand      — QA checklist (plaster or paint)
//  12. CoveringScheduleExportCommand    — Export covering schedule to CSV
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Model
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringMaterialBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var mats = CoveringMaterialDatabase.GetAllMaterials();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"COVERING MATERIALS ({mats.Count} items)");
                sb.AppendLine(new string('═', 50));
                sb.AppendLine("\n── PLASTER/RENDER ──");
                foreach (var m in CoveringMaterialDatabase.FindPlasters())
                    sb.AppendLine($"  {m.Name} — {m.ThicknessMm:F0}mm, {m.DensityKgM3}kg/m³, {m.MixRatio}, £{m.CostPerUnit:F2}/{m.UnitType}");
                sb.AppendLine("\n── PAINT/COATING ──");
                foreach (var m in CoveringMaterialDatabase.FindPaints())
                    sb.AppendLine($"  {m.Name} — {m.DFTMicrons:F0}μm DFT, {m.SpreadRateM2PerLitre}m²/L, {m.CoatsRequired} coats, {m.FinishType}, VOC={m.VOCgPerLitre}g/L");
                TaskDialog.Show("COVERINGS — Materials", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringMaterialBrowser", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringSubstrateAnalyzeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds()
                    .Select(id => uidoc.Document.GetElement(id)).Where(e => e != null).ToList();
                if (sel.Count == 0) { TaskDialog.Show("COVERINGS", "Select elements first."); return Result.Cancelled; }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"SUBSTRATE ANALYSIS — {sel.Count} element(s)");
                sb.AppendLine(new string('═', 50));
                foreach (var el in sel.Take(8))
                {
                    var r = SubstrateDetector.Detect(el);
                    sb.AppendLine($"\n{el.Category?.Name ?? "?"} [{el.Id.Value}]: {r.Summary}");
                }
                TaskDialog.Show("COVERINGS — Substrate Analysis", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringSubstrateAnalyze", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringPaintSystemCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("COVERINGS — Paint System");
                dlg.MainContent = "Select paint type:";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Vinyl Matt Emulsion (internal)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Oil-Based Gloss (woodwork)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Masonry Paint (external)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Intumescent Fire Paint (steel)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                var (type, substrate) = choice switch
                {
                    TaskDialogResult.CommandLink1 => (CoveringType.EmulsionPaint, SubstrateType.InSituConcrete),
                    TaskDialogResult.CommandLink2 => (CoveringType.GlossPaint, SubstrateType.Timber),
                    TaskDialogResult.CommandLink3 => (CoveringType.MasonryPaint, SubstrateType.DenseBlock),
                    TaskDialogResult.CommandLink4 => (CoveringType.Intumescent, SubstrateType.SteelSection),
                    _ => ((CoveringType)(-1), SubstrateType.DenseBlock),
                };
                if ((int)type < 0) return Result.Cancelled;

                var spec = PaintSpecificationEngine.DesignPaintSystem(substrate, type == CoveringType.MasonryPaint, type);
                TaskDialog.Show("COVERINGS — Paint System (BS 6150)", spec.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringPaintSystem", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringCoverageCalcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds()
                    .Select(id => uidoc.Document.GetElement(id)).Where(e => e != null).ToList();
                if (sel.Count == 0)
                    sel = new FilteredElementCollector(uidoc.Document, uidoc.Document.ActiveView.Id)
                        .WhereElementIsNotElementType().Where(e => e.Category != null).Take(500).ToList();

                var material = CoveringMaterialDatabase.FindByType(CoveringType.InternalPlaster);
                var result = ElementCoverageCalculator.Calculate(uidoc.Document, sel, PlasterConfig.InternalRenderMm, material);
                TaskDialog.Show("COVERINGS — Coverage", result.Summary);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringCoverageCalc", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringSmartApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var sel = uidoc.Selection.GetElementIds()
                    .Select(id => uidoc.Document.GetElement(id)).Where(e => e != null).ToList();
                if (sel.Count == 0) { TaskDialog.Show("COVERINGS", "Select walls, beams, or columns."); return Result.Cancelled; }

                var dlg = new TaskDialog("COVERINGS — Smart Apply");
                dlg.MainContent = $"Apply covering to {sel.Count} element(s):";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Internal plaster (backing + skim)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "External render (3-coat system)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Internal paint (emulsion, 2 coats)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Intumescent coating (steel fire protection)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = dlg.Show();
                bool isExt, isPaint;
                CoveringType covType;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: isExt = false; isPaint = false; covType = CoveringType.InternalPlaster; break;
                    case TaskDialogResult.CommandLink2: isExt = true; isPaint = false; covType = CoveringType.ExternalRender; break;
                    case TaskDialogResult.CommandLink3: isExt = false; isPaint = true; covType = CoveringType.EmulsionPaint; break;
                    case TaskDialogResult.CommandLink4: isExt = false; isPaint = true; covType = CoveringType.Intumescent; break;
                    default: return Result.Cancelled;
                }

                SmartCoveringFactory.CoveringReport report;
                using (var tx = new Transaction(uidoc.Document, "STING Smart Covering"))
                {
                    tx.Start();
                    report = SmartCoveringFactory.ApplyCovering(uidoc.Document, sel, isExt, isPaint, covType);
                    if (report.Success) tx.Commit(); else tx.RollBack();
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(report.Summary);
                foreach (var s in report.Steps) sb.AppendLine(s);
                foreach (var w in report.Warnings) sb.AppendLine($"⚠ {w}");
                TaskDialog.Show("COVERINGS — Smart Apply", sb.ToString());
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("CoveringSmartApply", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringBatchApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var doc = uidoc.Document;
                var allElements = new List<Element>();
                allElements.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                    .Where(w => w.WallType.Kind == WallKind.Basic));
                allElements.AddRange(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType());
                allElements.AddRange(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType());

                if (allElements.Count == 0) { TaskDialog.Show("COVERINGS", "No walls/beams/columns found."); return Result.Cancelled; }

                int walls = allElements.Count(e => e is Wall);
                int beams = allElements.Count(e => e.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralFraming);
                int cols = allElements.Count(e => e.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns);

                var confirm = TaskDialog.Show("COVERINGS — Batch",
                    $"Apply internal plaster to {walls} walls + {beams} beams + {cols} columns?",
                    TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);
                if (confirm != TaskDialogResult.Ok) return Result.Cancelled;

                SmartCoveringFactory.CoveringReport report;
                using (var tx = new Transaction(doc, "STING Batch Covering"))
                {
                    tx.Start();
                    report = SmartCoveringFactory.ApplyCovering(doc, allElements, false, false);
                    if (report.Success) tx.Commit(); else tx.RollBack();
                }
                TaskDialog.Show("COVERINGS — Batch Result", report.Summary);
                return report.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("CoveringBatchApply", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringRoomScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var schedule = RoomFinishScheduler.GenerateSchedule(uidoc.Document);
                if (schedule.Count == 0) { TaskDialog.Show("COVERINGS", "No placed rooms found."); return Result.Cancelled; }

                using (var tx = new Transaction(uidoc.Document, "STING Room Finishes"))
                {
                    tx.Start();
                    int written = RoomFinishScheduler.WriteToRooms(uidoc.Document, schedule);
                    tx.Commit();

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"ROOM FINISH SCHEDULE — {schedule.Count} rooms, {written} updated\n");
                    foreach (var r in schedule.Take(10))
                        sb.AppendLine($"  {r.RoomNumber} {r.RoomName}: Wall={r.WallFinish}, " +
                            $"Floor={r.FloorFinish}, Ceiling={r.CeilingFinish}");
                    if (schedule.Count > 10) sb.AppendLine($"\n  ... and {schedule.Count - 10} more rooms");
                    TaskDialog.Show("COVERINGS — Room Finishes", sb.ToString());
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringRoomSchedule", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringQualityCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new TaskDialog("COVERINGS — QA Checklist");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Plaster QA (BS EN 13914)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Paint QA (BS 6150)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = dlg.Show();
                bool isPaint = choice == TaskDialogResult.CommandLink2;
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2) return Result.Cancelled;

                var checks = CoveringQualityInspector.GenerateChecklist(isPaint);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{(isPaint ? "PAINT" : "PLASTER")} QA CHECKLIST");
                sb.AppendLine(new string('═', 50));
                foreach (var c in checks)
                    sb.AppendLine($"✓ [{c.Number:D2}] {c.Description}\n    {c.Standard}: {c.Criteria}");
                TaskDialog.Show("COVERINGS — QA", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringQualityCheck", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoveringScheduleExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            if (uidoc?.Document == null) return Result.Failed;
            try
            {
                var doc = uidoc.Document;
                var allElements = new List<Element>();
                allElements.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Where(w => w.WallType.Kind == WallKind.Basic));
                allElements.AddRange(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType());
                allElements.AddRange(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType());

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Element ID,Category,Type Name,Level,Substrate,Finish Spec,Area (m²),Cost (£)");

                foreach (var el in allElements)
                {
                    var sub = SubstrateDetector.Detect(el);
                    string typeName = el is Wall w ? w.WallType.Name : (el is FamilyInstance fi ? fi.Symbol?.Name ?? "" : "");
                    string finish = ParameterHelpers.GetString(el, "ASS_FINISH_TXT");
                    if (string.IsNullOrEmpty(finish)) finish = $"{sub.Substrate} — needs covering";
                    double area = sub.Target == CoveringTarget.Beam ? ElementCoverageCalculator.Calculate(doc, new[] { el }, 13, null).NetAreaM2 :
                        sub.Target == CoveringTarget.Column ? ElementCoverageCalculator.Calculate(doc, new[] { el }, 13, null).NetAreaM2 :
                        (el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0) * Units.SqFtToSqM;

                    sb.AppendLine($"{el.Id.Value},{el.Category?.Name},{typeName},,{sub.Substrate},{finish},{area:F1},{area * PlasterConfig.PlasterLabourPerM2:F0}");
                }

                var path = OutputLocationHelper.GetTimestampedPath(doc, "CoveringSchedule", "csv");
                System.IO.File.WriteAllText(path, sb.ToString());
                TaskDialog.Show("COVERINGS — Export", $"Exported {allElements.Count} elements to:\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CoveringScheduleExport", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
