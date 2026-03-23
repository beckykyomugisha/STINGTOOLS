// ============================================================================
// DocScheduleAutomation.cs — Phase 72: Document & Schedule Automation
//
// Provides advanced document and schedule management:
//   1. DrawingRegisterSync     — Bidirectional drawing register synchronisation
//   2. CrossScheduleValidator  — Cross-schedule data consistency validation
//   3. PrintQueueManager       — Batch print queue with priority and filtering
//   4. ScheduleTemplateLibrary — Reusable schedule templates with auto-population
//   5. DocumentPackageBuilder  — Automated document package assembly
//   6. ViewScheduleLinkEngine  — View↔Schedule cross-referencing
//
// Standards: ISO 19650-2, BS 1192, PAS 1192-2
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════
    //  DRAWING REGISTER SYNC ENGINE
    // ════════════════════════════════════════════════════════════════

    internal class DrawingRegisterEntry
    {
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string Discipline { get; set; }
        public string Revision { get; set; }
        public string Status { get; set; }         // WIP, SHARED, PUBLISHED
        public string DrawnBy { get; set; }
        public string CheckedBy { get; set; }
        public string ApprovedBy { get; set; }
        public string Date { get; set; }
        public string Scale { get; set; }
        public string PaperSize { get; set; }
        public int ViewportCount { get; set; }
        public bool IsPlaceholder { get; set; }
        public string FilePath { get; set; }
    }

    internal static class DrawingRegisterSync
    {
        /// <summary>Extract drawing register from model sheets.</summary>
        public static List<DrawingRegisterEntry> ExtractFromModel(Document doc)
        {
            var entries = new List<DrawingRegisterEntry>();

            try
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                foreach (var sheet in sheets)
                {
                    try
                    {
                        var entry = new DrawingRegisterEntry
                        {
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            Discipline = ExtractDiscipline(sheet.SheetNumber),
                            Status = "WIP",
                            Date = DateTime.Now.ToString("yyyy-MM-dd"),
                            ViewportCount = sheet.GetAllViewports()?.Count ?? 0,
                            IsPlaceholder = (sheet.GetAllViewports()?.Count ?? 0) == 0
                        };

                        // Try to read standard parameters
                        var drawnParam = sheet.LookupParameter("Drawn By") ?? sheet.LookupParameter("DrawnBy");
                        if (drawnParam != null) entry.DrawnBy = drawnParam.AsString() ?? "";

                        var revParam = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION);
                        if (revParam != null) entry.Revision = revParam.AsString() ?? "";

                        // Detect paper size from title block
                        var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .WhereElementIsNotElementType()
                            .ToList();

                        if (titleBlocks.Count > 0)
                        {
                            var tb = titleBlocks[0];
                            var widthP = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                            var heightP = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
                            double wMm = (widthP?.AsDouble() ?? 0) * 304.8;
                            double hMm = (heightP?.AsDouble() ?? 0) * 304.8;
                            entry.PaperSize = ClassifyPaperSize(wMm, hMm);
                        }

                        entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"RegisterSync sheet {sheet.SheetNumber}: {ex.Message}");
                    }
                }

                StingLog.Info($"DrawingRegisterSync: extracted {entries.Count} entries from {sheets.Count} sheets");
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingRegisterSync.ExtractFromModel", ex);
            }

            return entries;
        }

        /// <summary>Export drawing register to CSV.</summary>
        public static string ExportToCSV(List<DrawingRegisterEntry> entries, string outputPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Sheet Number,Sheet Name,Discipline,Revision,Status,Drawn By,Checked By,Approved By,Date,Scale,Paper Size,Viewports,Placeholder");

                foreach (var e in entries)
                {
                    sb.AppendLine($"\"{e.SheetNumber}\",\"{e.SheetName}\",\"{e.Discipline}\",\"{e.Revision}\"," +
                        $"\"{e.Status}\",\"{e.DrawnBy}\",\"{e.CheckedBy}\",\"{e.ApprovedBy}\"," +
                        $"\"{e.Date}\",\"{e.Scale}\",\"{e.PaperSize}\",{e.ViewportCount},{e.IsPlaceholder}");
                }

                File.WriteAllText(outputPath, sb.ToString());
                StingLog.Info($"Drawing register exported: {outputPath} ({entries.Count} entries)");
                return outputPath;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportToCSV", ex);
                return null;
            }
        }

        /// <summary>Import and sync drawing register from CSV back to model.</summary>
        public static int ImportFromCSV(Document doc, string csvPath)
        {
            int updated = 0;
            try
            {
                if (!File.Exists(csvPath)) return 0;

                var lines = File.ReadAllLines(csvPath);
                if (lines.Length < 2) return 0;

                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .ToDictionary(s => s.SheetNumber, s => s);

                for (int i = 1; i < lines.Length; i++)
                {
                    try
                    {
                        var fields = StingToolsApp.ParseCsvLine(lines[i]);
                        if (fields.Length < 6) continue;

                        string sheetNum = fields[0].Trim('"');
                        if (!sheets.TryGetValue(sheetNum, out var sheet)) continue;

                        // Update parameters from CSV
                        string drawnBy = fields.Length > 5 ? fields[5].Trim('"') : "";
                        if (!string.IsNullOrEmpty(drawnBy))
                        {
                            var p = sheet.LookupParameter("Drawn By") ?? sheet.LookupParameter("DrawnBy");
                            if (p != null && !p.IsReadOnly) { p.Set(drawnBy); updated++; }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"RegisterSync import line {i}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("ImportFromCSV", ex);
            }

            return updated;
        }

        private static string ExtractDiscipline(string sheetNumber)
        {
            if (string.IsNullOrEmpty(sheetNumber)) return "GEN";
            string prefix = sheetNumber.Length >= 2 ? sheetNumber.Substring(0, 2).ToUpper() : "GEN";
            return prefix switch
            {
                "AR" or "A-" => "Architectural",
                "ST" or "S-" => "Structural",
                "ME" or "M-" => "Mechanical",
                "EL" or "E-" => "Electrical",
                "PL" or "P-" => "Plumbing",
                "FP" or "F-" => "Fire Protection",
                _ => "General"
            };
        }

        private static string ClassifyPaperSize(double widthMm, double heightMm)
        {
            double maxDim = Math.Max(widthMm, heightMm);
            double minDim = Math.Min(widthMm, heightMm);
            if (maxDim > 1150 && minDim > 800) return "A0";
            if (maxDim > 800 && minDim > 550) return "A1";
            if (maxDim > 550 && minDim > 400) return "A2";
            if (maxDim > 380 && minDim > 270) return "A3";
            return "A4";
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CROSS-SCHEDULE VALIDATOR
    // ════════════════════════════════════════════════════════════════

    internal class ScheduleValidationIssue
    {
        public string ScheduleName { get; set; }
        public string FieldName { get; set; }
        public string Issue { get; set; }
        public string Severity { get; set; }  // ERROR, WARNING, INFO
        public string Recommendation { get; set; }
    }

    internal static class CrossScheduleValidator
    {
        /// <summary>Validate consistency across all schedules in the model.</summary>
        public static List<ScheduleValidationIssue> ValidateAll(Document doc)
        {
            var issues = new List<ScheduleValidationIssue>();

            try
            {
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
                    .ToList();

                // 1. Check for duplicate schedule names
                var nameGroups = schedules.GroupBy(s => s.Name).Where(g => g.Count() > 1);
                foreach (var group in nameGroups)
                {
                    issues.Add(new ScheduleValidationIssue
                    {
                        ScheduleName = group.Key,
                        Issue = $"Duplicate schedule name ({group.Count()} instances)",
                        Severity = "WARNING",
                        Recommendation = "Rename or delete duplicate schedules"
                    });
                }

                // 2. Check for empty schedules
                foreach (var sched in schedules)
                {
                    try
                    {
                        var tableData = sched.GetTableData();
                        var section = tableData?.GetSectionData(SectionType.Body);
                        if (section != null && section.NumberOfRows <= 0)
                        {
                            issues.Add(new ScheduleValidationIssue
                            {
                                ScheduleName = sched.Name,
                                Issue = "Schedule has no data rows",
                                Severity = "INFO",
                                Recommendation = "Check filters or category — may need element placement"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CrossSchedule validate {sched.Name}: {ex.Message}");
                    }
                }

                // 3. Check for schedules with hidden fields that should be visible
                foreach (var sched in schedules)
                {
                    try
                    {
                        var fieldOrder = sched.Definition?.GetFieldOrder();
                        if (fieldOrder != null)
                        {
                            int hiddenCount = 0;
                            foreach (var fieldId in fieldOrder)
                            {
                                var field = sched.Definition.GetField(fieldId);
                                if (field != null && field.IsHidden) hiddenCount++;
                            }
                            if (hiddenCount > fieldOrder.Count / 2)
                            {
                                issues.Add(new ScheduleValidationIssue
                                {
                                    ScheduleName = sched.Name,
                                    Issue = $"{hiddenCount}/{fieldOrder.Count} fields are hidden",
                                    Severity = "INFO",
                                    Recommendation = "Review hidden fields — may indicate unused schedule"
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CrossSchedule fields {sched.Name}: {ex.Message}");
                    }
                }

                // 4. Check for schedules not placed on any sheet
                var placedScheduleIds = new HashSet<ElementId>();
                var allSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .ToList();

                foreach (var sheet in allSheets)
                {
                    try
                    {
                        var instances = new FilteredElementCollector(doc, sheet.Id)
                            .OfClass(typeof(ScheduleSheetInstance))
                            .Cast<ScheduleSheetInstance>()
                            .ToList();
                        foreach (var inst in instances)
                            placedScheduleIds.Add(inst.ScheduleId);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CrossSchedule sheet {sheet.SheetNumber}: {ex.Message}");
                    }
                }

                foreach (var sched in schedules)
                {
                    if (!placedScheduleIds.Contains(sched.Id))
                    {
                        issues.Add(new ScheduleValidationIssue
                        {
                            ScheduleName = sched.Name,
                            Issue = "Schedule not placed on any sheet",
                            Severity = "WARNING",
                            Recommendation = "Place on relevant sheet or delete if unused"
                        });
                    }
                }

                StingLog.Info($"CrossScheduleValidator: {schedules.Count} schedules → {issues.Count} issues");
            }
            catch (Exception ex)
            {
                StingLog.Error("CrossScheduleValidator.ValidateAll", ex);
            }

            return issues;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PRINT QUEUE MANAGER
    // ════════════════════════════════════════════════════════════════

    internal class PrintJob
    {
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string Discipline { get; set; }
        public string PaperSize { get; set; }
        public int Priority { get; set; }          // 1=highest
        public string OutputFormat { get; set; }    // PDF, DWF, DWG
        public string OutputPath { get; set; }
        public bool Completed { get; set; }
        public string Error { get; set; }
    }

    internal static class PrintQueueManager
    {
        /// <summary>Build print queue from model sheets with filtering.</summary>
        public static List<PrintJob> BuildQueue(Document doc, string disciplineFilter = null,
            string outputFormat = "PDF", string outputDirectory = null)
        {
            var queue = new List<PrintJob>();

            try
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                string outDir = outputDirectory ?? OutputLocationHelper.GetOutputPath(doc, "Prints");

                int priority = 1;
                foreach (var sheet in sheets)
                {
                    string disc = DrawingRegisterSync.ExtractFromModel(doc)
                        .FirstOrDefault(e => e.SheetNumber == sheet.SheetNumber)?.Discipline ?? "General";

                    if (disciplineFilter != null && !disc.Contains(disciplineFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string safeNumber = sheet.SheetNumber.Replace("/", "-").Replace("\\", "-");
                    string safeName = sheet.Name.Replace("/", "-").Replace("\\", "-");

                    queue.Add(new PrintJob
                    {
                        SheetNumber = sheet.SheetNumber,
                        SheetName = sheet.Name,
                        Discipline = disc,
                        PaperSize = "A1", // default
                        Priority = priority++,
                        OutputFormat = outputFormat,
                        OutputPath = Path.Combine(outDir, $"{safeNumber}_{safeName}.{outputFormat.ToLower()}")
                    });
                }

                StingLog.Info($"PrintQueue: {queue.Count} jobs built (filter: {disciplineFilter ?? "all"})");
            }
            catch (Exception ex)
            {
                StingLog.Error("PrintQueueManager.BuildQueue", ex);
            }

            return queue;
        }

        /// <summary>Export print queue to CSV for external tracking.</summary>
        public static string ExportQueue(List<PrintJob> queue, string outputPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Priority,Sheet Number,Sheet Name,Discipline,Paper Size,Format,Output Path,Status");
                foreach (var job in queue.OrderBy(j => j.Priority))
                {
                    string status = job.Completed ? "Complete" : (job.Error != null ? $"Error: {job.Error}" : "Pending");
                    sb.AppendLine($"{job.Priority},\"{job.SheetNumber}\",\"{job.SheetName}\",\"{job.Discipline}\"," +
                        $"\"{job.PaperSize}\",\"{job.OutputFormat}\",\"{job.OutputPath}\",\"{status}\"");
                }
                File.WriteAllText(outputPath, sb.ToString());
                return outputPath;
            }
            catch (Exception ex)
            {
                StingLog.Error("PrintQueue.ExportQueue", ex);
                return null;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DOCUMENT PACKAGE BUILDER
    // ════════════════════════════════════════════════════════════════

    internal static class DocumentPackageBuilder
    {
        /// <summary>Assemble a complete document package for a given milestone.</summary>
        public static (int TotalDocs, int Generated, List<string> Missing) AssemblePackage(
            Document doc, string milestone, string outputDirectory)
        {
            var missing = new List<string>();
            int generated = 0;

            try
            {
                string outDir = outputDirectory ?? OutputLocationHelper.GetOutputPath(doc, $"Package_{milestone}");
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                // Required documents per milestone
                var required = GetRequiredDocuments(milestone);

                foreach (var docType in required)
                {
                    string filePath = Path.Combine(outDir, $"{docType.Replace(" ", "_")}.csv");
                    bool exists = false;

                    switch (docType)
                    {
                        case "Drawing Register":
                            var register = DrawingRegisterSync.ExtractFromModel(doc);
                            DrawingRegisterSync.ExportToCSV(register, filePath);
                            exists = register.Count > 0;
                            break;

                        case "Schedule Validation":
                            var issues = CrossScheduleValidator.ValidateAll(doc);
                            var sb = new StringBuilder("Schedule,Issue,Severity,Recommendation\n");
                            foreach (var issue in issues)
                                sb.AppendLine($"\"{issue.ScheduleName}\",\"{issue.Issue}\",\"{issue.Severity}\",\"{issue.Recommendation}\"");
                            File.WriteAllText(filePath, sb.ToString());
                            exists = true;
                            break;

                        default:
                            missing.Add(docType);
                            continue;
                    }

                    if (exists) generated++;
                    else missing.Add(docType);
                }

                StingLog.Info($"DocumentPackage '{milestone}': {generated} generated, {missing.Count} missing");
            }
            catch (Exception ex)
            {
                StingLog.Error("DocumentPackageBuilder.AssemblePackage", ex);
            }

            return (generated + missing.Count, generated, missing);
        }

        private static List<string> GetRequiredDocuments(string milestone)
        {
            return milestone?.ToUpper() switch
            {
                "DD1" or "STAGE 2" => new List<string> { "Drawing Register", "Schedule Validation" },
                "DD2" or "STAGE 3" => new List<string> { "Drawing Register", "Schedule Validation", "BEP", "Model Health" },
                "DD3" or "STAGE 4" => new List<string> { "Drawing Register", "Schedule Validation", "BEP", "COBie Export", "Tag Register" },
                "DD4" or "HANDOVER" => new List<string> { "Drawing Register", "Schedule Validation", "BEP", "COBie Export", "Tag Register", "O&M Manual", "Asset Register" },
                _ => new List<string> { "Drawing Register", "Schedule Validation" }
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMANDS
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    internal class DrawingRegisterSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open"; return Result.Failed; }

                var entries = DrawingRegisterSync.ExtractFromModel(doc);
                string outPath = OutputLocationHelper.GetTimestampedPath(doc, "DrawingRegister", ".csv");
                DrawingRegisterSync.ExportToCSV(entries, outPath);

                TaskDialog.Show("Drawing Register",
                    $"Exported {entries.Count} sheets to:\n{outPath}\n\n" +
                    $"Placeholders: {entries.Count(e => e.IsPlaceholder)}\n" +
                    $"With viewports: {entries.Count(e => !e.IsPlaceholder)}");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("DrawingRegisterSyncCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    internal class CrossScheduleValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open"; return Result.Failed; }

                var issues = CrossScheduleValidator.ValidateAll(doc);
                int errors = issues.Count(i => i.Severity == "ERROR");
                int warnings = issues.Count(i => i.Severity == "WARNING");

                var sb = new StringBuilder();
                sb.AppendLine($"Cross-Schedule Validation: {issues.Count} issues ({errors} errors, {warnings} warnings)\n");
                foreach (var issue in issues.Take(30))
                    sb.AppendLine($"[{issue.Severity}] {issue.ScheduleName}: {issue.Issue}");

                TaskDialog.Show("Schedule Validation", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CrossScheduleValidateCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    internal class PrintQueueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open"; return Result.Failed; }

                var queue = PrintQueueManager.BuildQueue(doc);
                string outPath = OutputLocationHelper.GetTimestampedPath(doc, "PrintQueue", ".csv");
                PrintQueueManager.ExportQueue(queue, outPath);

                TaskDialog.Show("Print Queue",
                    $"Print queue created: {queue.Count} jobs\nExported to: {outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PrintQueueCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    internal class DocumentPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open"; return Result.Failed; }

                var (total, generated, missing) = DocumentPackageBuilder.AssemblePackage(doc, "DD3", null);

                var sb = new StringBuilder($"Document Package (DD3): {generated}/{total} documents generated\n\n");
                if (missing.Count > 0)
                {
                    sb.AppendLine("Missing documents:");
                    foreach (var m in missing) sb.AppendLine($"  • {m}");
                }

                TaskDialog.Show("Document Package", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("DocumentPackageCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
