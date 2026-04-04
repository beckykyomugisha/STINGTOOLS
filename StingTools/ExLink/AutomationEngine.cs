using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.ExLink
{
    // ════════════════════════════════════════════════════════════════════════
    //  AUTOMATION ENGINE — Batch processing for Revit models
    //
    //  AutomationEngine (internal static):
    //    ExportSheetsToPDF, ExportSheetsToDWG, ExportToNWC,
    //    ExportToIFC, RunModelAudit, GetModelStats
    //
    //  10 IExternalCommand classes:
    //    1. BatchPDFExportCommand
    //    2. BatchDWGExportCommand
    //    3. BatchNWCExportCommand
    //    4. BatchIFCExportCommand
    //    5. ModelAuditCommand
    //    6. ModelCompactCommand
    //    7. BackupCleanupCommand
    //    8. FamilyUpgradeCommand
    //    9. ModelStatsCommand
    //   10. BatchParamExportCommand
    // ════════════════════════════════════════════════════════════════════════

    #region -- Automation Engine --

    internal static class AutomationEngine
    {
        // ── PDF Export ──────────────────────────────────────────────────────

        internal static void ExportSheetsToPDF(Document doc, string outputDir, out int count, out List<string> warnings)
        {
            count = 0;
            warnings = new List<string>();

            try
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => s.CanBePrinted)
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                if (sheets.Count == 0) { warnings.Add("No printable sheets found."); return; }

                // Revit 2025+ supports PDF export via doc.Export with PDFExportOptions
                var pdfOptions = new PDFExportOptions
                {
                    FileName = "",
                    Combine = false,
                    ColorDepth = ColorDepthType.Color
                };

                foreach (var sheet in sheets)
                {
                    try
                    {
                        string safeName = SanitizeFileName($"{sheet.SheetNumber}_{sheet.Name}");
                        pdfOptions.FileName = safeName;

                        var viewIds = new List<ElementId> { sheet.Id };
                        doc.Export(outputDir, viewIds, pdfOptions);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Sheet {sheet.SheetNumber}: {ex.Message}");
                        StingLog.Warn($"PDF export failed for sheet {sheet.SheetNumber}: {ex.Message}");
                    }
                }

                StingLog.Info($"AutomationEngine: PDF export — {count}/{sheets.Count} sheets to {outputDir}");
            }
            catch (Exception ex)
            {
                warnings.Add($"PDF export error: {ex.Message}");
                StingLog.Error("AutomationEngine.ExportSheetsToPDF failed", ex);
            }
        }

        // ── DWG Export ──────────────────────────────────────────────────────

        internal static void ExportSheetsToDWG(Document doc, string outputDir, out int count, out List<string> warnings)
        {
            count = 0;
            warnings = new List<string>();

            try
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => s.CanBePrinted)
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                if (sheets.Count == 0) { warnings.Add("No printable sheets found."); return; }

                var dwgOptions = new DWGExportOptions
                {
                    FileVersion = ACADVersion.R2018,
                    MergedViews = true
                };

                foreach (var sheet in sheets)
                {
                    try
                    {
                        string safeName = SanitizeFileName($"{sheet.SheetNumber}_{sheet.Name}");
                        var viewIds = new List<ElementId> { sheet.Id };
                        doc.Export(outputDir, safeName, viewIds, dwgOptions);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Sheet {sheet.SheetNumber}: {ex.Message}");
                        StingLog.Warn($"DWG export failed for sheet {sheet.SheetNumber}: {ex.Message}");
                    }
                }

                StingLog.Info($"AutomationEngine: DWG export — {count}/{sheets.Count} sheets to {outputDir}");
            }
            catch (Exception ex)
            {
                warnings.Add($"DWG export error: {ex.Message}");
                StingLog.Error("AutomationEngine.ExportSheetsToDWG failed", ex);
            }
        }

        // ── NWC Export ──────────────────────────────────────────────────────

        internal static void ExportToNWC(Document doc, string outputDir, out bool success, out string resultMsg)
        {
            success = false;
            resultMsg = "";

            try
            {
                string safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(doc.PathName ?? doc.Title ?? "Model"));
                string nwcPath = Path.Combine(outputDir, safeName + ".nwc");

                var nwcOptions = new NavisworksExportOptions
                {
                    ExportScope = NavisworksExportScope.Model,
                    Coordinates = NavisworksCoordinates.Shared,
                    ConvertElementProperties = true,
                    ExportLinks = true,
                    ExportRoomAsAttribute = true,
                    ExportUrls = false,
                    FindMissingMaterials = true
                };

                doc.Export(outputDir, safeName, nwcOptions);
                success = true;
                resultMsg = $"NWC exported successfully.\n\n{nwcPath}";
                StingLog.Info($"AutomationEngine: NWC export — {nwcPath}");
            }
            catch (Exception ex)
            {
                resultMsg = $"NWC export failed: {ex.Message}";
                StingLog.Error("AutomationEngine.ExportToNWC failed", ex);
            }
        }

        // ── IFC Export ──────────────────────────────────────────────────────

        internal static void ExportToIFC(Document doc, string outputDir, out bool success, out string resultMsg)
        {
            success = false;
            resultMsg = "";

            try
            {
                string safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(doc.PathName ?? doc.Title ?? "Model"));

                var ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3,
                    ExportBaseQuantities = true,
                    SpaceBoundaryLevel = 1,
                    WallAndColumnSplitting = true
                };

                using var tx = new Transaction(doc, "STING IFC Export");
                tx.Start();
                doc.Export(outputDir, safeName, ifcOptions);
                tx.Commit();

                success = true;
                resultMsg = $"IFC exported.\n\n{Path.Combine(outputDir, safeName + ".ifc")}";
                StingLog.Info($"AutomationEngine: IFC export — {safeName}");
            }
            catch (Exception ex)
            {
                resultMsg = $"IFC export failed: {ex.Message}";
                StingLog.Error("AutomationEngine.ExportToIFC failed", ex);
            }
        }

        // ── Model Audit ─────────────────────────────────────────────────────

        internal static List<string> RunModelAudit(Document doc)
        {
            var results = new List<string>();

            try
            {
                // 1. Warnings
                var warnings = doc.GetWarnings();
                results.Add($"Warnings: {warnings.Count}");

                // 2. Element counts by category
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                results.Add($"Total Instances: {allElements}");

                var allTypes = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .GetElementCount();
                results.Add($"Total Types: {allTypes}");

                // 3. Views
                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .ToList();
                results.Add($"Views: {views.Count}");

                var sheets = views.OfType<ViewSheet>().ToList();
                results.Add($"Sheets: {sheets.Count}");

                var unplacedViews = views.Where(v => !(v is ViewSheet) && !IsViewOnSheet(doc, v)).ToList();
                results.Add($"Unplaced Views: {unplacedViews.Count}");

                // 4. Linked models
                var links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .GetElementCount();
                results.Add($"Linked Models: {links}");

                // 5. CAD imports
                var cadImports = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .GetElementCount();
                results.Add($"CAD Imports: {cadImports}");

                // 6. In-place families
                var inPlace = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Count(fi => fi.Symbol?.Family?.IsInPlace == true);
                results.Add($"In-Place Families: {inPlace}");

                // 7. Rooms
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                results.Add($"Rooms: {rooms}");

                // 8. Groups
                var groups = new FilteredElementCollector(doc)
                    .OfClass(typeof(Group))
                    .GetElementCount();
                results.Add($"Groups: {groups}");

                // 9. File size
                if (!string.IsNullOrEmpty(doc.PathName) && File.Exists(doc.PathName))
                {
                    var fi = new FileInfo(doc.PathName);
                    results.Add($"File Size: {fi.Length / (1024.0 * 1024.0):F1} MB");
                }

                StingLog.Info($"AutomationEngine: Model audit — {results.Count} checks");
            }
            catch (Exception ex)
            {
                results.Add($"Audit error: {ex.Message}");
                StingLog.Error("AutomationEngine.RunModelAudit failed", ex);
            }

            return results;
        }

        // ── Model Stats ─────────────────────────────────────────────────────

        internal static Dictionary<string, int> GetModelStats(Document doc)
        {
            var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var el in collector)
                {
                    string catName = el.Category?.Name ?? "(No Category)";
                    if (stats.ContainsKey(catName))
                        stats[catName]++;
                    else
                        stats[catName] = 1;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("AutomationEngine.GetModelStats failed", ex);
            }

            return stats;
        }

        // ── Batch Parameter Export ───────────────────────────────────────────

        internal static void ExportParametersToExcel(Document doc, string outputPath, out int count, out List<string> warnings)
        {
            count = 0;
            warnings = new List<string>();

            try
            {
                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .ToList();

                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.Worksheets.Add("Parameters");

                // Headers
                var headers = new[] { "Element ID", "Category", "Family", "Type", "Parameter Name", "Value", "Group", "Storage Type" };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = headers[c];
                }
                var headerRange = ws.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#6A1B9A");
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

                int row = 2;
                int maxRows = 100000; // Safety limit

                foreach (var el in elements)
                {
                    if (row > maxRows)
                    {
                        warnings.Add($"Row limit reached ({maxRows}). Export truncated.");
                        break;
                    }

                    foreach (Parameter p in el.Parameters)
                    {
                        if (row > maxRows) break;
                        if (!p.HasValue) continue;

                        string val;
                        switch (p.StorageType)
                        {
                            case StorageType.String: val = p.AsString() ?? ""; break;
                            case StorageType.Integer: val = p.AsInteger().ToString(); break;
                            case StorageType.Double: val = p.AsDouble().ToString("F4"); break;
                            case StorageType.ElementId: val = p.AsElementId()?.IntegerValue.ToString() ?? ""; break;
                            default: val = p.AsValueString() ?? ""; break;
                        }

                        ws.Cell(row, 1).Value = el.Id.IntegerValue;
                        ws.Cell(row, 2).Value = el.Category?.Name ?? "";
                        ws.Cell(row, 3).Value = (el is FamilyInstance fi) ? fi.Symbol?.Family?.Name ?? "" : "";
                        ws.Cell(row, 4).Value = (el is FamilyInstance fi2) ? fi2.Symbol?.Name ?? "" : el.Name ?? "";
                        ws.Cell(row, 5).Value = p.Definition?.Name ?? "";
                        ws.Cell(row, 6).Value = val;
                        ws.Cell(row, 7).Value = p.Definition?.GetGroupTypeId()?.TypeId ?? "";
                        ws.Cell(row, 8).Value = p.StorageType.ToString();
                        row++;
                        count++;
                    }
                }

                ws.Columns().AdjustToContents(1, 60);
                wb.SaveAs(outputPath);
                StingLog.Info($"AutomationEngine: Param export — {count} parameter values to {outputPath}");
            }
            catch (Exception ex)
            {
                warnings.Add($"Parameter export error: {ex.Message}");
                StingLog.Error("AutomationEngine.ExportParametersToExcel failed", ex);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        internal static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static bool IsViewOnSheet(Document doc, View v)
        {
            try
            {
                var vps = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .Any(vp => vp.ViewId == v.Id);
                return vps;
            }
            catch { return false; }
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    //  1. BatchPDFExportCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPDFExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }

                var outputDir = ExLinkHelpers.PickFolderPath("Select PDF output folder");
                if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

                AutomationEngine.ExportSheetsToPDF(ctx.Doc, outputDir, out int count, out var warnings);
                var msg = $"PDF Export Complete.\n\nSheets exported: {count}\nOutput: {outputDir}";
                if (warnings.Count > 0)
                    msg += $"\n\nWarnings ({warnings.Count}):\n{string.Join("\n", warnings.Take(10))}";
                TaskDialog.Show("STING — Batch PDF", msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BatchPDFExportCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  2. BatchDWGExportCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchDWGExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }

                var outputDir = ExLinkHelpers.PickFolderPath("Select DWG output folder");
                if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

                AutomationEngine.ExportSheetsToDWG(ctx.Doc, outputDir, out int count, out var warnings);
                var msg = $"DWG Export Complete.\n\nSheets exported: {count}\nOutput: {outputDir}";
                if (warnings.Count > 0)
                    msg += $"\n\nWarnings ({warnings.Count}):\n{string.Join("\n", warnings.Take(10))}";
                TaskDialog.Show("STING — Batch DWG", msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BatchDWGExportCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  3. BatchNWCExportCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchNWCExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }

                var outputDir = ExLinkHelpers.PickFolderPath("Select NWC output folder");
                if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

                AutomationEngine.ExportToNWC(ctx.Doc, outputDir, out bool ok, out string resultMsg);
                TaskDialog.Show("STING — NWC Export", resultMsg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BatchNWCExportCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  4. BatchIFCExportCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchIFCExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }

                var outputDir = ExLinkHelpers.PickFolderPath("Select IFC output folder");
                if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

                AutomationEngine.ExportToIFC(ctx.Doc, outputDir, out bool ok, out string resultMsg);
                TaskDialog.Show("STING — IFC Export", resultMsg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BatchIFCExportCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  5. ModelAuditCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutomationModelAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }

                var results = AutomationEngine.RunModelAudit(ctx.Doc);
                var msg = "═══ Model Audit Report ═══\n\n" + string.Join("\n", results);
                TaskDialog.Show("STING — Model Audit", msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelAuditCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  6. ModelCompactCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutomationModelCompactCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                // Purge unused elements via API
                int purged = 0;
                using (var tx = new Transaction(doc, "STING Model Compact"))
                {
                    tx.Start();

                    // Find unused view templates
                    var templates = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => v.IsTemplate)
                        .ToList();

                    // Check which templates are actually used
                    var usedTemplateIds = new HashSet<long>();
                    foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                    {
                        if (!v.IsTemplate && v.ViewTemplateId != ElementId.InvalidElementId)
                            usedTemplateIds.Add(v.ViewTemplateId.IntegerValue);
                    }

                    int unusedTemplates = 0;
                    foreach (var t in templates)
                    {
                        if (!usedTemplateIds.Contains(t.Id.IntegerValue))
                            unusedTemplates++;
                    }

                    tx.RollBack(); // Don't actually delete — just report
                }

                // File size info
                string sizeInfo = "";
                if (!string.IsNullOrEmpty(doc.PathName) && File.Exists(doc.PathName))
                {
                    var fi = new FileInfo(doc.PathName);
                    sizeInfo = $"\nCurrent file size: {fi.Length / (1024.0 * 1024.0):F1} MB";
                }

                var warnings = doc.GetWarnings();
                TaskDialog.Show("STING — Model Compact",
                    $"Model Compaction Analysis\n\n" +
                    $"Warnings: {warnings.Count}\n" +
                    $"Tip: Use Revit's Purge Unused (Manage tab) to remove unused families.{sizeInfo}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelCompactCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  7. BackupCleanupCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutomationBackupCleanupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                if (string.IsNullOrEmpty(doc.PathName))
                {
                    TaskDialog.Show("STING — Backup Cleanup", "Document has not been saved. No backup files to clean.");
                    return Result.Succeeded;
                }

                string dir = Path.GetDirectoryName(doc.PathName);
                string baseName = Path.GetFileNameWithoutExtension(doc.PathName);

                // Find backup files (*.0001.rvt, *.0002.rvt, etc.)
                var backups = Directory.GetFiles(dir, $"{baseName}.*.rvt", SearchOption.TopDirectoryOnly)
                    .Where(f => !string.Equals(f, doc.PathName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .ToList();

                if (backups.Count == 0)
                {
                    TaskDialog.Show("STING — Backup Cleanup", "No backup files found.");
                    return Result.Succeeded;
                }

                long totalBytes = backups.Sum(f => new FileInfo(f).Length);
                double totalMB = totalBytes / (1024.0 * 1024.0);

                var td = new TaskDialog("STING — Backup Cleanup")
                {
                    MainInstruction = $"Found {backups.Count} backup files ({totalMB:F1} MB)",
                    MainContent = $"Directory: {dir}\n\nFiles:\n{string.Join("\n", backups.Select(f => $"  {Path.GetFileName(f)} ({new FileInfo(f).Length / (1024.0 * 1024.0):F1} MB)"))}",
                    CommonButtons = TaskDialogCommonButtons.None
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Delete all backup files");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Keep backups (cancel)");

                var result = td.Show();
                if (result == TaskDialogResult.CommandLink1)
                {
                    int deleted = 0;
                    foreach (var f in backups)
                    {
                        try { File.Delete(f); deleted++; }
                        catch (Exception ex) { StingLog.Warn($"Could not delete backup: {f}: {ex.Message}"); }
                    }
                    TaskDialog.Show("STING — Backup Cleanup", $"Deleted {deleted} backup files.\nFreed: {totalMB:F1} MB");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BackupCleanupCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  8. FamilyUpgradeCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutomationFamilyUpgradeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                // Collect all loaded families
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => !f.IsInPlace)
                    .OrderBy(f => f.Name)
                    .ToList();

                // Categorize by editable/in-place/system
                int editable = families.Count(f => f.IsEditable);
                int inPlace = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Count(f => f.IsInPlace);

                var categoryGroups = families
                    .GroupBy(f => f.FamilyCategory?.Name ?? "(No Category)")
                    .OrderByDescending(g => g.Count())
                    .Take(15)
                    .Select(g => $"  {g.Key}: {g.Count()}")
                    .ToList();

                TaskDialog.Show("STING — Family Report",
                    $"═══ Family Analysis ═══\n\n" +
                    $"Loaded Families: {families.Count}\n" +
                    $"Editable: {editable}\n" +
                    $"In-Place: {inPlace}\n\n" +
                    $"Top Categories:\n{string.Join("\n", categoryGroups)}\n\n" +
                    $"Tip: Open families in Family Editor to upgrade to current Revit version.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FamilyUpgradeCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  9. ModelStatsCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutomationModelStatsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }

                var stats = AutomationEngine.GetModelStats(ctx.Doc);
                var topCategories = stats
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(25)
                    .Select(kvp => $"  {kvp.Key}: {kvp.Value:N0}")
                    .ToList();

                int total = stats.Values.Sum();
                TaskDialog.Show("STING — Model Statistics",
                    $"═══ Element Statistics ═══\n\n" +
                    $"Total Elements: {total:N0}\n" +
                    $"Categories: {stats.Count}\n\n" +
                    $"Top 25 Categories:\n{string.Join("\n", topCategories)}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelStatsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  10. BatchParamExportCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutomationBatchParamExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }

                var outputPath = ExLinkHelpers.PickSavePath("Parameter_Export", "xlsx");
                if (string.IsNullOrEmpty(outputPath)) return Result.Succeeded;

                AutomationEngine.ExportParametersToExcel(ctx.Doc, outputPath, out int count, out var warnings);
                var msg = $"Parameter Export Complete.\n\nParameters exported: {count:N0}\nOutput: {outputPath}";
                if (warnings.Count > 0)
                    msg += $"\n\nWarnings:\n{string.Join("\n", warnings.Take(5))}";
                TaskDialog.Show("STING — Batch Param Export", msg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BatchParamExportCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
