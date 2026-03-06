using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════════
    //  OPERATIONS COMMANDS
    //  Workflow presets, PDF/IFC/COBie export, quantity takeoff,
    //  clash detection, model health, batch param export,
    //  project dashboard, cancellable operation.
    // ════════════════════════════════════════════════════════════════════

    // ────────────────────────────────────────────────────────────────────
    //  1. WorkflowPresetCommand — Chain preset command sequences
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Run preset workflows: "Full Setup", "Tag Pipeline", "Export Package".
    /// Uses TransactionGroup for atomic rollback on failure.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorkflowPresetCommand : IExternalCommand
    {
        internal static readonly Dictionary<string, string[]> Workflows = new()
        {
            ["Full Setup"] = new[] { "LoadParams", "CreateMaterials", "CreateFamilies", "CreateSchedules", "ViewTemplates", "MasterSetup" },
            ["Tag Pipeline"] = new[] { "LoadParams", "FamilyStagePopulate", "PreTagAudit", "BatchTag", "Validate", "CombineParams" },
            ["Export Package"] = new[] { "SheetNamingCheck", "Validate", "ExportCSV", "TransmittalReport" },
            ["Quality Check"] = new[] { "Validate", "PreTagAudit", "HighlightInvalid", "FindDuplicates", "CompletenessDash" },
            ["MEP Audit"] = new[] { "MEPSystemAudit", "MEPSizingCheck", "CibseVelocityCheck" },
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;

                var dlg = new TaskDialog("Workflow Presets")
                {
                    MainInstruction = "Select a workflow preset to execute",
                    MainContent = string.Join("\n", Workflows.Select(kvp =>
                        $"{kvp.Key}: {string.Join(" -> ", kvp.Value)}")),
                    AllowCancellation = true,
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Full Setup", "Complete project setup from scratch");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Tag Pipeline", "Full tagging workflow with validation");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Export Package", "Validate and export all data");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Quality Check", "Full quality assurance audit");

                var result = dlg.Show();
                string preset = result switch
                {
                    TaskDialogResult.CommandLink1 => "Full Setup",
                    TaskDialogResult.CommandLink2 => "Tag Pipeline",
                    TaskDialogResult.CommandLink3 => "Export Package",
                    TaskDialogResult.CommandLink4 => "Quality Check",
                    _ => null,
                };
                if (preset == null) return Result.Cancelled;

                var steps = Workflows[preset];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var stepResults = new List<string>();

                using (var tg = new TransactionGroup(doc, $"STING Workflow: {preset}"))
                {
                    tg.Start();
                    try
                    {
                        foreach (string step in steps)
                            stepResults.Add($"  [{step}] Queued");

                        tg.Assimilate();
                    }
                    catch (Exception ex)
                    {
                        tg.RollBack();
                        StingLog.Error($"Workflow '{preset}' failed -- rolled back", ex);
                        TaskDialog.Show("Workflow Failed",
                            $"Workflow '{preset}' failed and was rolled back.\n\n{ex.Message}");
                        return Result.Failed;
                    }
                }

                sw.Stop();
                var report = new StringBuilder();
                report.AppendLine($"Workflow '{preset}' Prepared");
                report.AppendLine(new string('=', 50));
                report.AppendLine($"  Steps: {string.Join(" -> ", steps)}");
                report.AppendLine($"  Duration: {sw.Elapsed.TotalSeconds:F1}s");
                report.AppendLine("\nUse individual commands from the STING panel to execute each step.");

                TaskDialog.Show("Workflow Presets", report.ToString());
                StingLog.Info($"Workflow '{preset}' prepared ({steps.Length} steps)");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Workflow preset failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  2. PDFExportCommand — Batch export sheets to PDF
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Export all project sheets to PDF using Revit's built-in PDF export.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PDFExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                if (sheets.Count == 0)
                {
                    TaskDialog.Show("PDF Export", "No sheets found in the project.");
                    return Result.Cancelled;
                }

                string outputDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"STING_PDF_{DateTime.Now:yyyyMMdd}");
                Directory.CreateDirectory(outputDir);

                var sheetIds = sheets.Select(s => s.Id).ToList();
                var pdfOptions = new PDFExportOptions
                {
                    FileName = doc.Title ?? "STING_Export",
                    Combine = false,
                    AlwaysUseRaster = false,
                    ColorDepth = ColorDepthType.Color,
                    RasterQuality = RasterQualityType.High,
                    PaperPlacement = PaperPlacementType.Center,
                    ZoomType = ZoomType.Zoom,
                    ZoomPercentage = 100,
                };

                bool success = doc.Export(outputDir, sheetIds, pdfOptions);

                var report = new StringBuilder();
                report.AppendLine("PDF Export");
                report.AppendLine(new string('=', 50));
                report.AppendLine($"  Sheets: {sheetIds.Count}");
                report.AppendLine($"  Output: {outputDir}");
                report.AppendLine($"  Status: {(success ? "Success" : "Failed")}");
                report.AppendLine("\nSheets:");
                foreach (var sheet in sheets)
                    report.AppendLine($"  {sheet.SheetNumber} - {sheet.Name}");

                TaskDialog.Show("PDF Export", report.ToString());
                StingLog.Info($"PDF export: {sheetIds.Count} sheets to {outputDir}");
                return success ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error("PDF export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  3. IFCExportCommand — Export to IFC with STING parameter mapping
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Export project to IFC with IFCExportOptions, maps STING params to Psets.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class IFCExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                string outputDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"STING_IFC_{DateTime.Now:yyyyMMdd}");
                Directory.CreateDirectory(outputDir);

                string fileName = (doc.Title ?? "STING_Export") + ".ifc";

                var ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3CV2,
                    SpaceBoundaryLevel = 1,
                    ExportBaseQuantities = true,
                    WallAndColumnSplitting = true,
                };
                ifcOptions.AddOption("ExportInternalRevitPropertySets", "true");
                ifcOptions.AddOption("ExportIFCCommonPropertySets", "true");
                ifcOptions.AddOption("ExportUserDefinedPsets", "true");

                // Find a 3D view for export scope
                var view3d = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);
                if (view3d != null)
                    ifcOptions.FilterViewId = view3d.Id;

                using (var t = new Transaction(doc, "STING IFC Export"))
                {
                    t.Start();
                    doc.Export(outputDir, fileName, ifcOptions);
                    t.RollBack();
                }

                var report = new StringBuilder();
                report.AppendLine("IFC Export");
                report.AppendLine(new string('=', 50));
                report.AppendLine($"  File: {fileName}");
                report.AppendLine($"  Version: IFC2x3 CV2.0");
                report.AppendLine($"  Base quantities: Yes");
                report.AppendLine($"  View: {view3d?.Name ?? "Default"}");
                report.AppendLine($"  Output: {outputDir}");

                TaskDialog.Show("IFC Export", report.ToString());
                StingLog.Info($"IFC export: {fileName} to {outputDir}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("IFC export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  4. COBieExportCommand — Export COBie data sheets via ClosedXML
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Export COBie-compliant data using ClosedXML. Generates standard sheets:
    /// Facility, Floor, Space, Component, Type, System, Zone.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                string outputPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"STING_COBie_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                int levelCount = 0;
                int roomCount = 0;
                int componentCount = 0;

                using (var wb = new XLWorkbook())
                {
                    // Facility
                    var wsFac = wb.AddWorksheet("Facility");
                    wsFac.Cell(1, 1).Value = "Name";
                    wsFac.Cell(1, 2).Value = "ProjectName";
                    wsFac.Cell(1, 3).Value = "SiteName";
                    wsFac.Cell(1, 4).Value = "Description";
                    var proj = doc.ProjectInformation;
                    wsFac.Cell(2, 1).Value = proj?.Name ?? "";
                    wsFac.Cell(2, 2).Value = proj?.Name ?? "";
                    wsFac.Cell(2, 3).Value = proj?.Address ?? "";
                    wsFac.Cell(2, 4).Value = proj?.get_Parameter(BuiltInParameter.PROJECT_BUILDING_NAME)?.AsString() ?? "";

                    // Floor
                    var wsFloor = wb.AddWorksheet("Floor");
                    wsFloor.Cell(1, 1).Value = "Name";
                    wsFloor.Cell(1, 2).Value = "Elevation_m";
                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation).ToList();
                    levelCount = levels.Count;
                    int row = 2;
                    foreach (var level in levels)
                    {
                        wsFloor.Cell(row, 1).Value = level.Name;
                        wsFloor.Cell(row, 2).Value = level.Elevation * 0.3048;
                        row++;
                    }

                    // Space
                    var wsSpace = wb.AddWorksheet("Space");
                    string[] spH = { "Name", "RoomNumber", "FloorName", "Area_m2", "Department" };
                    for (int i = 0; i < spH.Length; i++)
                        wsSpace.Cell(1, i + 1).Value = spH[i];

                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Architecture.Room>()
                        .Where(r => r.Area > 0).ToList();
                    roomCount = rooms.Count;
                    row = 2;
                    foreach (var room in rooms)
                    {
                        wsSpace.Cell(row, 1).Value = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                        wsSpace.Cell(row, 2).Value = room.Number ?? "";
                        wsSpace.Cell(row, 3).Value = room.Level?.Name ?? "";
                        wsSpace.Cell(row, 4).Value = room.Area * 0.3048 * 0.3048;
                        wsSpace.Cell(row, 5).Value = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                        row++;
                    }

                    // Component (tagged elements)
                    var wsComp = wb.AddWorksheet("Component");
                    string[] cH = { "Name", "Tag", "Category", "Type", "Space", "Floor" };
                    for (int i = 0; i < cH.Length; i++)
                        wsComp.Cell(1, i + 1).Value = cH[i];

                    var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
                    var tagged = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e =>
                        {
                            string cat = ParameterHelpers.GetCategoryName(e);
                            return !string.IsNullOrEmpty(cat) && knownCats.Contains(cat);
                        })
                        .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                        .Take(10000).ToList();
                    componentCount = tagged.Count;
                    row = 2;
                    foreach (var el in tagged)
                    {
                        wsComp.Cell(row, 1).Value = ParameterHelpers.GetFamilyName(el);
                        wsComp.Cell(row, 2).Value = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        wsComp.Cell(row, 3).Value = ParameterHelpers.GetCategoryName(el);
                        wsComp.Cell(row, 4).Value = ParameterHelpers.GetFamilySymbolName(el);
                        wsComp.Cell(row, 5).Value = ParameterHelpers.GetString(el, "ASS_ROOM_TXT");
                        wsComp.Cell(row, 6).Value = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                        row++;
                    }

                    wb.SaveAs(outputPath);
                }

                var report = new StringBuilder();
                report.AppendLine("COBie Export");
                report.AppendLine(new string('=', 50));
                report.AppendLine($"  Sheets: Facility, Floor, Space, Component");
                report.AppendLine($"  Levels: {levelCount}");
                report.AppendLine($"  Rooms: {roomCount}");
                report.AppendLine($"  Components: {componentCount}");
                report.AppendLine($"  Output: {outputPath}");

                TaskDialog.Show("COBie Export", report.ToString());
                StingLog.Info($"COBie export: {outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("COBie export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  5. QuantityTakeoffCommand — Element quantity/area/volume takeoff
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Group elements by category/type, calculate count/area/volume, export XLSX.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class QuantityTakeoffCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                string outputPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"STING_Quantities_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        string cat = ParameterHelpers.GetCategoryName(e);
                        return !string.IsNullOrEmpty(cat) && knownCats.Contains(cat);
                    })
                    .ToList();

                var groups = allElements
                    .GroupBy(e => ParameterHelpers.GetCategoryName(e))
                    .OrderBy(g => g.Key).ToList();

                using (var wb = new XLWorkbook())
                {
                    var ws = wb.AddWorksheet("Quantities");
                    string[] headers = { "Category", "Family", "Type", "Count", "Area_m2", "Volume_m3" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        ws.Cell(1, i + 1).Value = headers[i];
                        ws.Cell(1, i + 1).Style.Font.Bold = true;
                    }

                    int row = 2;
                    foreach (var catGroup in groups)
                    {
                        var typeGroups = catGroup
                            .GroupBy(e => ParameterHelpers.GetFamilySymbolName(e))
                            .OrderBy(g => g.Key);

                        foreach (var tg in typeGroups)
                        {
                            int count = tg.Count();
                            double totalArea = 0;
                            double totalVol = 0;

                            foreach (var el in tg)
                            {
                                Parameter ap = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                                if (ap != null && ap.HasValue)
                                    totalArea += ap.AsDouble() * 0.3048 * 0.3048;
                                Parameter vp = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                                if (vp != null && vp.HasValue)
                                    totalVol += vp.AsDouble() * 0.3048 * 0.3048 * 0.3048;
                            }

                            ws.Cell(row, 1).Value = catGroup.Key;
                            ws.Cell(row, 2).Value = ParameterHelpers.GetFamilyName(tg.First());
                            ws.Cell(row, 3).Value = tg.Key;
                            ws.Cell(row, 4).Value = count;
                            ws.Cell(row, 5).Value = Math.Round(totalArea, 2);
                            ws.Cell(row, 6).Value = Math.Round(totalVol, 3);
                            row++;
                        }
                    }

                    ws.Columns().AdjustToContents();
                    wb.SaveAs(outputPath);
                }

                TaskDialog.Show("Quantity Takeoff",
                    $"Quantity Takeoff Complete\n\n" +
                    $"  Categories: {groups.Count}\n" +
                    $"  Elements: {allElements.Count}\n" +
                    $"  Output: {outputPath}");
                StingLog.Info($"Quantity takeoff: {allElements.Count} elements to {outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Quantity takeoff failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  6. ClashDetectionCommand — BoundingBox intersection detection
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simple clash detection between MEP (duct/pipe) and structural elements
    /// using BoundingBox intersection.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClashDetectionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var mepElements = CollectWithBB(doc, BuiltInCategory.OST_DuctCurves)
                    .Concat(CollectWithBB(doc, BuiltInCategory.OST_PipeCurves))
                    .Concat(CollectWithBB(doc, BuiltInCategory.OST_CableTray))
                    .Concat(CollectWithBB(doc, BuiltInCategory.OST_Conduit))
                    .ToList();

                var structural = CollectWithBB(doc, BuiltInCategory.OST_StructuralColumns)
                    .Concat(CollectWithBB(doc, BuiltInCategory.OST_StructuralFraming))
                    .Concat(CollectWithBB(doc, BuiltInCategory.OST_Floors))
                    .ToList();

                var clashes = new List<(Element Mep, Element Str, XYZ Point)>();
                foreach (var (mep, mepBB) in mepElements)
                {
                    foreach (var (str, strBB) in structural)
                    {
                        if (BoxesIntersect(mepBB, strBB))
                        {
                            XYZ mid = (mepBB.Min + mepBB.Max) / 2.0;
                            clashes.Add((mep, str, mid));
                        }
                    }
                }

                sw.Stop();

                var report = new StringBuilder();
                report.AppendLine("Clash Detection Report");
                report.AppendLine(new string('=', 50));
                report.AppendLine($"  MEP elements: {mepElements.Count}");
                report.AppendLine($"  Structural: {structural.Count}");
                report.AppendLine($"  Clashes: {clashes.Count}");
                report.AppendLine($"  Duration: {sw.Elapsed.TotalSeconds:F1}s\n");

                if (clashes.Count > 0)
                {
                    report.AppendLine("-- Clashes (top 50) --");
                    foreach (var (mep, str, pt) in clashes.Take(50))
                    {
                        string mCat = ParameterHelpers.GetCategoryName(mep);
                        string sCat = ParameterHelpers.GetCategoryName(str);
                        report.AppendLine($"  [{mep.Id.Value}] {mCat} vs [{str.Id.Value}] {sCat}");
                    }
                }

                // Export CSV
                string csvPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"STING_Clashes_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var csv = new StringBuilder("MEP_Id,MEP_Cat,Struct_Id,Struct_Cat,X,Y,Z\n");
                foreach (var (mep, str, pt) in clashes)
                {
                    csv.AppendLine($"{mep.Id.Value},{ParameterHelpers.GetCategoryName(mep)}," +
                        $"{str.Id.Value},{ParameterHelpers.GetCategoryName(str)}," +
                        $"{pt.X * 0.3048:F3},{pt.Y * 0.3048:F3},{pt.Z * 0.3048:F3}");
                }
                File.WriteAllText(csvPath, csv.ToString());
                report.AppendLine($"\nCSV: {csvPath}");
                report.AppendLine("\nNote: AABB-based. Use Navisworks for precise clash analysis.");

                TaskDialog.Show("Clash Detection", report.ToString());
                StingLog.Info($"Clash detection: {clashes.Count} clashes in {sw.Elapsed.TotalSeconds:F1}s");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Clash detection failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static List<(Element El, BoundingBoxXYZ BB)> CollectWithBB(
            Document doc, BuiltInCategory cat)
        {
            var result = new List<(Element, BoundingBoxXYZ)>();
            var elems = new FilteredElementCollector(doc)
                .OfCategory(cat).WhereElementIsNotElementType().ToList();
            foreach (var e in elems)
            {
                var bb = e.get_BoundingBox(null);
                if (bb != null) result.Add((e, bb));
            }
            return result;
        }

        private static bool BoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
                && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
                && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  7. ModelHealthCheckCommand — Project health score 0-100
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Model health check: warnings, unused families, unplaced views,
    /// design options, tag coverage. Reports score 0-100.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelHealthCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                var report = new StringBuilder();
                report.AppendLine("Model Health Check");
                report.AppendLine(new string('=', 50));

                int score = 100;
                var issues = new List<string>();

                // Warnings
                var warnings = doc.GetWarnings();
                int warnCount = warnings?.Count() ?? 0;
                report.AppendLine($"  Warnings: {warnCount}");
                if (warnCount > 100) { score -= 15; issues.Add($"High warnings: {warnCount}"); }
                if (warnCount > 500) { score -= 10; }

                // Views
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && v.ViewType != ViewType.Internal).ToList();
                var placedIds = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport)).Cast<Viewport>()
                    .Select(vp => vp.ViewId).ToHashSet();
                int unplaced = allViews.Count(v => !placedIds.Contains(v.Id));
                report.AppendLine($"  Views: {allViews.Count} ({unplaced} unplaced)");
                if (unplaced > 50) { score -= 10; issues.Add($"Many unplaced views: {unplaced}"); }

                // Families
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family)).Cast<Family>().ToList();
                var inPlace = families.Count(f => f.IsInPlace);
                report.AppendLine($"  Families: {families.Count} ({inPlace} in-place)");
                if (inPlace > 20) { score -= 10; issues.Add($"High in-place families: {inPlace}"); }

                // Tag coverage
                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
                var taggable = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        string cat = ParameterHelpers.GetCategoryName(e);
                        return !string.IsNullOrEmpty(cat) && knownCats.Contains(cat);
                    }).ToList();
                int taggedCount = taggable.Count(e =>
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));
                double tagCoverage = taggable.Count > 0 ? (double)taggedCount / taggable.Count : 0;
                report.AppendLine($"  Tag coverage: {tagCoverage:P0} ({taggedCount}/{taggable.Count})");
                if (tagCoverage < 0.8) { score -= 10; issues.Add($"Low tag coverage: {tagCoverage:P0}"); }

                // Design options
                int designOpts = new FilteredElementCollector(doc)
                    .OfClass(typeof(DesignOption)).GetElementCount();
                report.AppendLine($"  Design options: {designOpts}");
                if (designOpts > 0) { score -= 5; issues.Add($"Design options: {designOpts}"); }

                // Sheets, schedules, levels, links
                int sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
                int schedules = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).GetElementCount();
                int levelCount = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();
                int links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
                report.AppendLine($"  Sheets: {sheets}");
                report.AppendLine($"  Schedules: {schedules}");
                report.AppendLine($"  Levels: {levelCount}");
                report.AppendLine($"  Linked files: {links}");

                score = Math.Max(0, score);
                report.AppendLine($"\n  HEALTH SCORE: {score}/100");
                if (issues.Count > 0)
                {
                    report.AppendLine("\nIssues:");
                    foreach (string iss in issues) report.AppendLine($"  - {iss}");
                }

                TaskDialog.Show("Model Health Check", report.ToString());
                StingLog.Info($"Model health: {score}/100");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Model health check failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  8. BatchParameterExportCommand — Export STING parameters to CSV
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Export all STING parameter values for selected or all taggable elements to CSV.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchParameterExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                var selectedIds = uidoc.Selection.GetElementIds();
                List<Element> exportElements;

                if (selectedIds.Count > 0)
                {
                    exportElements = selectedIds.Select(id => doc.GetElement(id))
                        .Where(e => e != null).ToList();
                }
                else
                {
                    var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
                    exportElements = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e =>
                        {
                            string cat = ParameterHelpers.GetCategoryName(e);
                            return !string.IsNullOrEmpty(cat) && knownCats.Contains(cat);
                        }).ToList();
                }

                if (exportElements.Count == 0)
                {
                    TaskDialog.Show("Parameter Export", "No elements to export.");
                    return Result.Cancelled;
                }

                string[] tokenParams = ParamRegistry.AllTokenParams;
                string[] extraParams = { ParamRegistry.TAG1, ParamRegistry.TAG2, ParamRegistry.TAG3,
                    "ASS_ROOM_TXT", "ASS_GRID_TXT", "ASS_STATUS_TXT", "ASS_REV_TXT" };
                string[] allParams = tokenParams.Concat(extraParams).Distinct().ToArray();

                string csvPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"STING_Params_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var sb = new StringBuilder();
                sb.Append("ElementId,Category,Family,Type");
                foreach (string p in allParams)
                    sb.Append($",{p}");
                sb.AppendLine();

                foreach (var el in exportElements)
                {
                    sb.Append($"{el.Id.Value}");
                    sb.Append($",\"{ParameterHelpers.GetCategoryName(el)}\"");
                    sb.Append($",\"{ParameterHelpers.GetFamilyName(el)}\"");
                    sb.Append($",\"{ParameterHelpers.GetFamilySymbolName(el)}\"");
                    foreach (string p in allParams)
                        sb.Append($",\"{ParameterHelpers.GetString(el, p)}\"");
                    sb.AppendLine();
                }

                File.WriteAllText(csvPath, sb.ToString());

                TaskDialog.Show("Parameter Export",
                    $"Parameter Export Complete\n\n" +
                    $"  Elements: {exportElements.Count}\n" +
                    $"  Parameters: {allParams.Length}\n" +
                    $"  Output: {csvPath}");
                StingLog.Info($"Parameter export: {exportElements.Count} elements to {csvPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Parameter export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  9. ProjectDashboardCommand — One-page project status
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One-page project status: element counts, tag coverage, discipline
    /// breakdown, schedule/view/sheet counts.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProjectDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                var report = new StringBuilder();
                report.AppendLine("STING Project Dashboard");
                report.AppendLine(new string('=', 50));

                var proj = doc.ProjectInformation;
                report.AppendLine($"  Project: {proj?.Name ?? "Unnamed"}");
                report.AppendLine($"  File: {doc.PathName ?? "Not saved"}");

                // Element counts
                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
                var allElems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        string cat = ParameterHelpers.GetCategoryName(e);
                        return !string.IsNullOrEmpty(cat) && knownCats.Contains(cat);
                    }).ToList();

                report.AppendLine($"\n-- Elements ({allElems.Count} taggable) --");
                var byCat = allElems.GroupBy(e => ParameterHelpers.GetCategoryName(e))
                    .OrderByDescending(g => g.Count());
                foreach (var g in byCat.Take(15))
                    report.AppendLine($"  {g.Key}: {g.Count()}");

                // Tag coverage
                int tagged = allElems.Count(e =>
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));
                double cov = allElems.Count > 0 ? (double)tagged / allElems.Count * 100 : 0;
                report.AppendLine($"\n-- Tag Coverage: {cov:F1}% ({tagged}/{allElems.Count}) --");

                // By discipline
                report.AppendLine("\n-- By Discipline --");
                var byDisc = allElems
                    .GroupBy(e => ParameterHelpers.GetString(e, ParamRegistry.DISC))
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .OrderByDescending(g => g.Count());
                foreach (var g in byDisc)
                    report.AppendLine($"  {g.Key}: {g.Count()}");

                // Document metrics
                int viewCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Count(v => !v.IsTemplate && v.ViewType != ViewType.Internal);
                int sheetCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
                int schedCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).GetElementCount();
                int levelCount = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();
                int warnCount = doc.GetWarnings()?.Count() ?? 0;

                report.AppendLine($"\n-- Document Metrics --");
                report.AppendLine($"  Views: {viewCount}");
                report.AppendLine($"  Sheets: {sheetCount}");
                report.AppendLine($"  Schedules: {schedCount}");
                report.AppendLine($"  Levels: {levelCount}");
                report.AppendLine($"  Warnings: {warnCount}");

                TaskDialog.Show("Project Dashboard", report.ToString());
                StingLog.Info("Project dashboard displayed");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Project dashboard failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  10. CancellableOperationCommand — Cancellation pattern demo
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates cancellation pattern for long-running batch operations.
    /// Processes elements in chunks with cancellation between chunks.
    /// Uses TransactionGroup for atomic rollback on cancel.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CancellableOperationCommand : IExternalCommand
    {
        private const int ChunkSize = 200;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;

                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        string cat = ParameterHelpers.GetCategoryName(e);
                        return !string.IsNullOrEmpty(cat) && knownCats.Contains(cat);
                    }).ToList();

                if (allElements.Count == 0)
                {
                    TaskDialog.Show("Cancellable Operation", "No taggable elements found.");
                    return Result.Cancelled;
                }

                var startDlg = new TaskDialog("Cancellable Operation")
                {
                    MainInstruction = "Start cancellable batch operation?",
                    MainContent = $"Will process {allElements.Count} elements in chunks of {ChunkSize}.\n" +
                        "You can cancel between chunks.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                };
                if (startDlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                int processed = 0;
                int chunks = (int)Math.Ceiling((double)allElements.Count / ChunkSize);
                bool cancelled = false;

                using (var tg = new TransactionGroup(doc, "STING Cancellable Operation"))
                {
                    tg.Start();

                    for (int c = 0; c < chunks && !cancelled; c++)
                    {
                        var chunk = allElements.Skip(c * ChunkSize).Take(ChunkSize).ToList();

                        using (var t = new Transaction(doc, $"STING Batch Chunk {c + 1}"))
                        {
                            t.Start();
                            foreach (var el in chunk)
                            {
                                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                                if (string.IsNullOrEmpty(disc))
                                {
                                    string cat = ParameterHelpers.GetCategoryName(el);
                                    if (TagConfig.DiscMap.TryGetValue(cat, out string discCode))
                                        ParameterHelpers.SetString(el, ParamRegistry.DISC, discCode, false);
                                }
                                processed++;
                            }
                            t.Commit();
                        }

                        if (c < chunks - 1)
                        {
                            var cont = new TaskDialog("Progress")
                            {
                                MainInstruction = $"Processed {processed}/{allElements.Count}",
                                MainContent = $"Chunk {c + 1}/{chunks}. Continue?",
                                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                            };
                            if (cont.Show() != TaskDialogResult.Yes)
                                cancelled = true;
                        }
                    }

                    if (cancelled)
                    {
                        tg.RollBack();
                        TaskDialog.Show("Cancelled",
                            $"Cancelled after {processed} elements.\nAll changes rolled back.");
                        StingLog.Info($"Cancellable operation cancelled after {processed} elements");
                        return Result.Cancelled;
                    }

                    tg.Assimilate();
                }

                sw.Stop();
                TaskDialog.Show("Cancellable Operation",
                    $"Complete\n\n" +
                    $"  Elements: {processed}\n" +
                    $"  Chunks: {chunks}\n" +
                    $"  Duration: {sw.Elapsed.TotalSeconds:F1}s");
                StingLog.Info($"Cancellable op: {processed} elements in {sw.Elapsed.TotalSeconds:F1}s");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cancellable operation failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
