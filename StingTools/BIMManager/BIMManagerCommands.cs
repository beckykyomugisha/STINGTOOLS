using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════
    //  BIM MANAGER — Procore Briefcase / Ideate Sticky style module
    //  Provides: document briefcase, element sticky notes, model health,
    //  4D/5D integration stubs, compliance dashboard, MIDP tracking.
    // ═══════════════════════════════════════════════════════════════

    #region Document Briefcase (Procore-style)

    /// <summary>
    /// Document Briefcase: generates a portable project information package
    /// containing all essential BIM metadata, schedules, tag registers,
    /// and compliance reports in a single export folder.
    /// Inspired by Procore's Briefcase offline document sync.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DocumentBriefcaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                // Prompt for output folder
                var dlg = new TaskDialog("Document Briefcase");
                dlg.MainInstruction = "Generate Document Briefcase";
                dlg.MainContent =
                    "Creates a portable project folder with:\n" +
                    "  • Project Information Summary\n" +
                    "  • Tag Register (all tagged elements)\n" +
                    "  • Compliance Report (ISO 19650)\n" +
                    "  • Parameter Audit (completeness)\n" +
                    "  • Model Statistics\n" +
                    "  • Sheet Index\n" +
                    "  • Discipline Breakdown\n\n" +
                    "Output saved alongside the Revit model.";
                dlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                if (dlg.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

                string modelPath = doc.PathName;
                string outputDir;
                if (!string.IsNullOrEmpty(modelPath))
                {
                    string dir = Path.GetDirectoryName(modelPath);
                    string name = Path.GetFileNameWithoutExtension(modelPath);
                    outputDir = Path.Combine(dir, $"{name}_Briefcase_{DateTime.Now:yyyyMMdd_HHmmss}");
                }
                else
                {
                    outputDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"STING_Briefcase_{DateTime.Now:yyyyMMdd_HHmmss}");
                }

                Directory.CreateDirectory(outputDir);
                var sw = Stopwatch.StartNew();
                int filesGenerated = 0;

                // 1. Project Information Summary
                filesGenerated += BriefcaseEngine.ExportProjectInfo(doc, outputDir);

                // 2. Tag Register
                filesGenerated += BriefcaseEngine.ExportTagRegister(doc, outputDir);

                // 3. Compliance Report
                filesGenerated += BriefcaseEngine.ExportComplianceReport(doc, outputDir);

                // 4. Parameter Audit
                filesGenerated += BriefcaseEngine.ExportParameterAudit(doc, outputDir);

                // 5. Model Statistics
                filesGenerated += BriefcaseEngine.ExportModelStats(doc, outputDir);

                // 6. Sheet Index
                filesGenerated += BriefcaseEngine.ExportSheetIndex(doc, outputDir);

                // 7. Discipline Breakdown
                filesGenerated += BriefcaseEngine.ExportDisciplineBreakdown(doc, outputDir);

                // 8. MIDP Register
                filesGenerated += BriefcaseEngine.ExportMidpRegister(doc, outputDir);

                sw.Stop();

                TaskDialog.Show("Document Briefcase",
                    $"Briefcase generated successfully.\n\n" +
                    $"Location: {outputDir}\n" +
                    $"Files: {filesGenerated}\n" +
                    $"Duration: {sw.Elapsed.TotalSeconds:F1}s");

                StingLog.Info($"DocumentBriefcase: {filesGenerated} files to {outputDir} in {sw.Elapsed.TotalSeconds:F1}s");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("DocumentBriefcaseCommand failed", ex);
                TaskDialog.Show("STING", $"Briefcase generation failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Element Sticky Notes (Ideate-style)

    /// <summary>
    /// Element Sticky Notes: attach persistent text annotations to elements
    /// stored in shared parameters. Notes survive across sessions and can be
    /// exported for QA reviews. Inspired by Ideate Sticky Notes for Revit.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElementStickyNoteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var selIds = uidoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    TaskDialog.Show("Sticky Note", "Select one or more elements first.");
                    return Result.Cancelled;
                }

                // Prompt for note text
                var dlg = new TaskDialog("Element Sticky Note");
                dlg.MainInstruction = $"Add note to {selIds.Count} element(s)";
                dlg.MainContent =
                    "Choose action:\n" +
                    "• Add/Edit Note — write a sticky note\n" +
                    "• View Notes — display existing notes\n" +
                    "• Clear Notes — remove all notes from selection";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Add/Edit Note", "Write a new note or append to existing");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "View Notes", "Display all notes on selected elements");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Clear Notes", "Remove notes from selected elements");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var result = dlg.Show();

                switch (result)
                {
                    case TaskDialogResult.CommandLink1:
                        return StickyEngine.AddNote(doc, selIds);
                    case TaskDialogResult.CommandLink2:
                        return StickyEngine.ViewNotes(doc, selIds);
                    case TaskDialogResult.CommandLink3:
                        return StickyEngine.ClearNotes(doc, selIds);
                    default:
                        return Result.Cancelled;
                }
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("ElementStickyNoteCommand failed", ex);
                TaskDialog.Show("STING", $"Sticky note failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Export all sticky notes across the project to CSV for QA review.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportStickyNotesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                return StickyEngine.ExportAllNotes(doc);
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportStickyNotesCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Select all elements that have sticky notes attached.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectStickyElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var elementsWithNotes = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        string note = ParameterHelpers.GetString(e, "STING_STICKY_NOTE_TXT");
                        return !string.IsNullOrEmpty(note);
                    })
                    .Select(e => e.Id)
                    .ToList();

                if (elementsWithNotes.Count == 0)
                {
                    TaskDialog.Show("Sticky Notes", "No elements with sticky notes found.");
                    return Result.Succeeded;
                }

                uidoc.Selection.SetElementIds(elementsWithNotes);
                TaskDialog.Show("Sticky Notes",
                    $"Selected {elementsWithNotes.Count} elements with sticky notes.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SelectStickyElementsCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Model Health Dashboard

    /// <summary>
    /// Comprehensive model health check covering: file size, warnings, worksets,
    /// linked models, design options, groups, in-place families, imported instances,
    /// unused families, and parameter completeness.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelHealthDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var report = ModelHealthEngine.RunHealthCheck(doc);

                TaskDialog td = new TaskDialog("Model Health Dashboard");
                td.MainInstruction = $"Model Health: {report.OverallScore}/100 ({report.Rating})";
                td.MainContent = report.Summary;
                td.ExpandedContent = report.Details;
                td.Show();

                StingLog.Info($"ModelHealth: score={report.OverallScore}, rating={report.Rating}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelHealthDashboardCommand failed", ex);
                TaskDialog.Show("STING", $"Health check failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Export model health report to CSV file for tracking over time.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportModelHealthCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var report = ModelHealthEngine.RunHealthCheck(doc);
                string path = ModelHealthEngine.ExportReport(doc, report);

                TaskDialog.Show("Model Health", $"Report exported to:\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportModelHealthCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region MIDP (Master Information Delivery Plan) Tracker

    /// <summary>
    /// MIDP Register: tracks document deliverables per ISO 19650 with
    /// suitability codes, status tracking, and deliverable milestones.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MidpTrackerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var midpData = MidpEngine.BuildMidpRegister(doc);

                var report = new StringBuilder();
                report.AppendLine("MIDP Register — Master Information Delivery Plan");
                report.AppendLine(new string('═', 60));
                report.AppendLine($"  Total Deliverables:  {midpData.TotalDeliverables}");
                report.AppendLine($"  Sheets (Published):  {midpData.PublishedSheets}/{midpData.TotalSheets}");
                report.AppendLine($"  Models:              {midpData.LinkedModels}");
                report.AppendLine($"  Suitability S0-S6:   {midpData.SuitabilityBreakdown}");
                report.AppendLine();
                report.AppendLine("Discipline Breakdown:");
                foreach (var kvp in midpData.ByDiscipline.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value} deliverables");
                report.AppendLine();
                report.AppendLine("Status:");
                foreach (var kvp in midpData.ByStatus.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value}");

                TaskDialog td = new TaskDialog("MIDP Tracker");
                td.MainInstruction = $"MIDP: {midpData.TotalDeliverables} deliverables tracked";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MidpTrackerCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region 4D/5D Integration

    /// <summary>
    /// 4D Timeline Export: exports element-phase relationships for construction
    /// sequencing tools (Navisworks, Synchro, MS Project import format).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Export4DTimelineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                string path = SchedulingEngine.Export4DTimeline(doc);

                TaskDialog.Show("4D Timeline",
                    $"Timeline data exported for construction sequencing.\n\nFile: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Export4DTimelineCommand failed", ex);
                TaskDialog.Show("STING", $"4D export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 5D Cost Export: exports element quantities with cost data for
    /// quantity surveying tools (CostX, Causeway, BOQ format).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Export5DCostDataCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                string path = SchedulingEngine.Export5DCostData(doc);

                TaskDialog.Show("5D Cost Data",
                    $"Cost data exported for quantity surveying.\n\nFile: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Export5DCostDataCommand failed", ex);
                TaskDialog.Show("STING", $"5D export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Compliance Integration

    /// <summary>
    /// Full ISO 19650 compliance dashboard integrating tag compliance,
    /// naming conventions, suitability codes, and deliverable tracking.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FullComplianceDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var tagCompliance = ComplianceScan.Scan(doc);
                var healthReport = ModelHealthEngine.RunHealthCheck(doc);
                var midpData = MidpEngine.BuildMidpRegister(doc);

                var report = new StringBuilder();
                report.AppendLine("ISO 19650 Full Compliance Dashboard");
                report.AppendLine(new string('═', 60));
                report.AppendLine();
                report.AppendLine($"  TAG COMPLIANCE:     {tagCompliance.StatusBarText ?? "N/A"}");
                report.AppendLine($"  MODEL HEALTH:       {healthReport.OverallScore}/100 ({healthReport.Rating})");
                report.AppendLine($"  MIDP COVERAGE:      {midpData.TotalDeliverables} deliverables");
                report.AppendLine($"  SHEETS PUBLISHED:   {midpData.PublishedSheets}/{midpData.TotalSheets}");
                report.AppendLine();

                // RAG summary
                int overallScore = (int)((tagCompliance.CompliancePercent * 0.5)
                    + (healthReport.OverallScore * 0.3)
                    + (midpData.TotalSheets > 0 ? (midpData.PublishedSheets * 100.0 / midpData.TotalSheets) * 0.2 : 0));
                string overallRag = overallScore >= 80 ? "GREEN" : overallScore >= 50 ? "AMBER" : "RED";

                report.AppendLine($"  OVERALL: {overallScore}% — {overallRag}");

                string topIssues = tagCompliance.TopIssues;
                if (!string.IsNullOrEmpty(topIssues) && topIssues != "No issues")
                {
                    report.AppendLine();
                    report.AppendLine($"Top Issues: {topIssues}");
                }

                TaskDialog td = new TaskDialog("ISO 19650 Compliance");
                td.MainInstruction = $"Overall Compliance: {overallScore}% ({overallRag})";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FullComplianceDashboardCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Predecessor / Dependency Links

    /// <summary>
    /// Link elements as predecessors/successors for construction sequencing.
    /// Stores relationships in shared parameters for 4D timeline export.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkPredecessorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var selIds = uidoc.Selection.GetElementIds().ToList();
                if (selIds.Count < 2)
                {
                    TaskDialog.Show("Link Predecessors",
                        "Select 2+ elements: first is predecessor, rest are successors.");
                    return Result.Cancelled;
                }

                Element predecessor = doc.GetElement(selIds[0]);
                string predTag = ParameterHelpers.GetString(predecessor, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(predTag))
                {
                    TaskDialog.Show("Link Predecessors",
                        "Predecessor element must be tagged first.");
                    return Result.Failed;
                }

                int linked = 0;
                using (Transaction tx = new Transaction(doc, "STING Link Predecessors"))
                {
                    tx.Start();
                    for (int i = 1; i < selIds.Count; i++)
                    {
                        Element successor = doc.GetElement(selIds[i]);
                        if (successor == null) continue;
                        string existing = ParameterHelpers.GetString(successor, "STING_PREDECESSOR_TAGS_TXT");
                        string newVal = string.IsNullOrEmpty(existing)
                            ? predTag
                            : existing + ";" + predTag;
                        ParameterHelpers.SetString(successor, "STING_PREDECESSOR_TAGS_TXT", newVal, overwrite: true);
                        linked++;
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Link Predecessors",
                    $"Linked {linked} successor(s) to predecessor '{predTag}'.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LinkPredecessorsCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Weekend / Working Calendar

    /// <summary>
    /// Assign construction phase dates and working calendar to elements
    /// for 4D schedule generation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssignPhaseDatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var selIds = uidoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    TaskDialog.Show("Phase Dates", "Select elements to assign phase dates.");
                    return Result.Cancelled;
                }

                // Auto-derive dates from Revit phase sequence
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .ToList();

                int assigned = 0;
                using (Transaction tx = new Transaction(doc, "STING Assign Phase Dates"))
                {
                    tx.Start();
                    foreach (ElementId id in selIds)
                    {
                        Element el = doc.GetElement(id);
                        if (el == null) continue;

                        // Derive start date from phase ordinal
                        Parameter createdParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                        if (createdParam != null && createdParam.HasValue)
                        {
                            ElementId phaseId = createdParam.AsElementId();
                            int ordinal = phases.FindIndex(p => p.Id == phaseId);
                            if (ordinal >= 0)
                            {
                                // Simple scheduling: each phase = 1 month from project start
                                string startDate = DateTime.Today.AddMonths(ordinal).ToString("yyyy-MM-dd");
                                string endDate = DateTime.Today.AddMonths(ordinal + 1).ToString("yyyy-MM-dd");
                                ParameterHelpers.SetIfEmpty(el, "STING_4D_START_DATE_TXT", startDate);
                                ParameterHelpers.SetIfEmpty(el, "STING_4D_END_DATE_TXT", endDate);
                                assigned++;
                            }
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Phase Dates",
                    $"Assigned phase dates to {assigned} elements based on {phases.Count} Revit phases.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AssignPhaseDatesCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Measured Quantities

    /// <summary>
    /// Extract measured quantities from elements for NRM/SMM cost estimation.
    /// Exports lengths, areas, volumes, counts by category and discipline.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MeasuredQuantitiesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                string path = SchedulingEngine.ExportMeasuredQuantities(doc);

                TaskDialog.Show("Measured Quantities",
                    $"Quantities exported for cost estimation.\n\nFile: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MeasuredQuantitiesCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Element Count Summary

    /// <summary>
    /// Quick element count by category, discipline, level, and phase.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElementCountSummaryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .ToList();

                var byCat = new Dictionary<string, int>();
                var byDisc = new Dictionary<string, int>();
                int total = 0;

                foreach (var el in allElements)
                {
                    string catName = el.Category?.Name ?? "Unknown";
                    if (!TagConfig.DiscMap.ContainsKey(catName)) continue;
                    total++;

                    if (!byCat.ContainsKey(catName)) byCat[catName] = 0;
                    byCat[catName]++;

                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "?";
                    if (!byDisc.ContainsKey(disc)) byDisc[disc] = 0;
                    byDisc[disc]++;
                }

                var report = new StringBuilder();
                report.AppendLine($"Element Count Summary — {total} taggable elements");
                report.AppendLine(new string('─', 50));
                report.AppendLine();
                report.AppendLine("By Discipline:");
                foreach (var kvp in byDisc.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");
                report.AppendLine();
                report.AppendLine("Top Categories:");
                foreach (var kvp in byCat.OrderByDescending(k => k.Value).Take(15))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");

                TaskDialog td = new TaskDialog("Element Count");
                td.MainInstruction = $"{total:N0} taggable elements in model";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ElementCountSummaryCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  ENGINE CLASSES (internal helpers)
    // ═══════════════════════════════════════════════════════════════

    #region BriefcaseEngine

    internal static class BriefcaseEngine
    {
        public static int ExportProjectInfo(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "01_PROJECT_INFO.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Property,Value");

                var pi = doc.ProjectInformation;
                if (pi != null)
                {
                    sb.AppendLine($"\"Project Name\",\"{Esc(pi.Name)}\"");
                    sb.AppendLine($"\"Project Number\",\"{Esc(pi.Number)}\"");
                    sb.AppendLine($"\"Client\",\"{Esc(pi.ClientName)}\"");
                    sb.AppendLine($"\"Building Name\",\"{Esc(pi.BuildingName)}\"");
                    sb.AppendLine($"\"Address\",\"{Esc(pi.Address)}\"");
                    sb.AppendLine($"\"Author\",\"{Esc(pi.Author)}\"");
                    sb.AppendLine($"\"Organization\",\"{Esc(pi.OrganizationName)}\"");
                    sb.AppendLine($"\"Issue Date\",\"{Esc(pi.IssueDate)}\"");
                    sb.AppendLine($"\"Status\",\"{Esc(pi.Status)}\"");
                }

                sb.AppendLine($"\"Model Path\",\"{Esc(doc.PathName)}\"");
                sb.AppendLine($"\"Export Date\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
                sb.AppendLine($"\"Revit Version\",\"{doc.Application.VersionName}\"");

                // Phase information
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase)).Cast<Phase>().ToList();
                sb.AppendLine($"\"Phases\",\"{phases.Count}: {string.Join(", ", phases.Select(p => p.Name))}\"");

                // Level information
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();
                sb.AppendLine($"\"Levels\",\"{levels.Count}: {string.Join(", ", levels.Select(l => l.Name))}\"");

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportProjectInfo: {ex.Message}"); return 0; }
        }

        public static int ExportTagRegister(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "02_TAG_REGISTER.csv");
                var sb = new StringBuilder();
                sb.AppendLine("ElementId,Category,Family,Type,TAG1,DISC,LOC,ZONE,LVL,SYS,FUNC,PROD,SEQ,STATUS,REV");

                var elems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                    .ToList();

                foreach (var el in elems)
                {
                    sb.AppendLine(string.Join(",",
                        $"\"{el.Id}\"",
                        $"\"{Esc(ParameterHelpers.GetCategoryName(el))}\"",
                        $"\"{Esc(ParameterHelpers.GetFamilyName(el))}\"",
                        $"\"{Esc(ParameterHelpers.GetFamilySymbolName(el))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.TAG1))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.DISC))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.LOC))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.ZONE))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.LVL))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.SYS))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.FUNC))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.PROD))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.SEQ))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.STATUS))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.REV))}\""));
                }

                File.WriteAllText(path, sb.ToString());
                StingLog.Info($"TagRegister: exported {elems.Count} tagged elements");
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportTagRegister: {ex.Message}"); return 0; }
        }

        public static int ExportComplianceReport(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "03_COMPLIANCE_REPORT.csv");
                var scan = ComplianceScan.Scan(doc);
                var sb = new StringBuilder();
                sb.AppendLine("Metric,Value");
                sb.AppendLine($"\"RAG Status\",\"{scan.RAGStatus}\"");
                sb.AppendLine($"\"Complete %\",\"{scan.CompliancePercent:F1}\"");
                sb.AppendLine($"\"Complete Elements\",\"{scan.TaggedComplete}\"");
                sb.AppendLine($"\"Incomplete Elements\",\"{scan.TaggedIncomplete}\"");
                sb.AppendLine($"\"Untagged Elements\",\"{scan.Untagged}\"");
                sb.AppendLine($"\"Total Taggable\",\"{scan.TotalElements}\"");
                string issues = scan.TopIssues;
                if (!string.IsNullOrEmpty(issues) && issues != "No issues")
                    sb.AppendLine($"\"Top Issues\",\"{Esc(issues)}\"");
                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportComplianceReport: {ex.Message}"); return 0; }
        }

        public static int ExportParameterAudit(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "04_PARAMETER_AUDIT.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Parameter,Populated,Empty,Total,Completeness%");

                string[] keyParams = {
                    ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                    ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                    ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.TAG1,
                    ParamRegistry.STATUS, ParamRegistry.REV
                };

                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                    .ToList();

                foreach (string param in keyParams)
                {
                    int pop = 0, empty = 0;
                    foreach (var el in allElements)
                    {
                        string val = ParameterHelpers.GetString(el, param);
                        if (!string.IsNullOrEmpty(val)) pop++;
                        else empty++;
                    }
                    int total = pop + empty;
                    double pct = total > 0 ? pop * 100.0 / total : 0;
                    sb.AppendLine($"\"{param}\",{pop},{empty},{total},{pct:F1}");
                }

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportParameterAudit: {ex.Message}"); return 0; }
        }

        public static int ExportModelStats(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "05_MODEL_STATISTICS.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Metric,Value");

                int totalElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType().GetElementCount();
                int totalTypes = new FilteredElementCollector(doc)
                    .WhereElementIsElementType().GetElementCount();
                int levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).GetElementCount();
                int sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).GetElementCount();
                int views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Count(v => !v.IsTemplate);
                int families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family)).GetElementCount();
                int rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().GetElementCount();
                int links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance)).GetElementCount();

                sb.AppendLine($"\"Total Elements\",{totalElements}");
                sb.AppendLine($"\"Total Types\",{totalTypes}");
                sb.AppendLine($"\"Levels\",{levels}");
                sb.AppendLine($"\"Sheets\",{sheets}");
                sb.AppendLine($"\"Views\",{views}");
                sb.AppendLine($"\"Families\",{families}");
                sb.AppendLine($"\"Rooms\",{rooms}");
                sb.AppendLine($"\"Linked Models\",{links}");

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportModelStats: {ex.Message}"); return 0; }
        }

        public static int ExportSheetIndex(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "06_SHEET_INDEX.csv");
                var sb = new StringBuilder();
                sb.AppendLine("SheetNumber,SheetName,Discipline,ViewsPlaced,ApprovedBy,IssuedDate");

                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                foreach (var sheet in sheets)
                {
                    string num = sheet.SheetNumber ?? "";
                    string name = sheet.Name ?? "";
                    string disc = num.Length >= 2 ? num.Substring(0, 2) : "";
                    int viewCount = sheet.GetAllPlacedViews().Count;
                    string approved = sheet.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";
                    string issued = sheet.get_Parameter(BuiltInParameter.SHEET_ISSUE_DATE)?.AsString() ?? "";

                    sb.AppendLine($"\"{Esc(num)}\",\"{Esc(name)}\",\"{disc}\",{viewCount},\"{Esc(approved)}\",\"{Esc(issued)}\"");
                }

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportSheetIndex: {ex.Message}"); return 0; }
        }

        public static int ExportDisciplineBreakdown(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "07_DISCIPLINE_BREAKDOWN.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Discipline,Category,Count,Tagged,Untagged,Completeness%");

                var elems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                    .ToList();

                var groups = elems
                    .GroupBy(e => new {
                        Disc = TagConfig.DiscMap.TryGetValue(e.Category.Name, out string d) ? d : "?",
                        Cat = e.Category.Name
                    })
                    .OrderBy(g => g.Key.Disc).ThenBy(g => g.Key.Cat);

                foreach (var g in groups)
                {
                    int count = g.Count();
                    int tagged = g.Count(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));
                    int untagged = count - tagged;
                    double pct = count > 0 ? tagged * 100.0 / count : 0;
                    sb.AppendLine($"\"{g.Key.Disc}\",\"{Esc(g.Key.Cat)}\",{count},{tagged},{untagged},{pct:F1}");
                }

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportDisciplineBreakdown: {ex.Message}"); return 0; }
        }

        public static int ExportMidpRegister(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "08_MIDP_REGISTER.csv");
                var midp = MidpEngine.BuildMidpRegister(doc);
                var sb = new StringBuilder();
                sb.AppendLine("Deliverable,Type,Discipline,Status,Suitability");

                foreach (var item in midp.Items)
                    sb.AppendLine($"\"{Esc(item.Name)}\",\"{item.Type}\",\"{item.Discipline}\",\"{item.Status}\",\"{item.Suitability}\"");

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportMidpRegister: {ex.Message}"); return 0; }
        }

        internal static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");
    }

    #endregion

    #region StickyEngine

    internal static class StickyEngine
    {
        private const string NoteParam = "STING_STICKY_NOTE_TXT";
        private const string NoteAuthorParam = "STING_NOTE_AUTHOR_TXT";
        private const string NoteDateParam = "STING_NOTE_DATE_TXT";

        public static Result AddNote(Document doc, ICollection<ElementId> ids)
        {
            // Use a simple text prompt via TaskDialog
            var dlg = new TaskDialog("Add Sticky Note");
            dlg.MainInstruction = "Enter note text:";
            dlg.MainContent =
                "Note will be stored in STING_STICKY_NOTE_TXT parameter.\n" +
                "Use pipe (|) to separate multiple notes.\n\n" +
                "Enter your note in the verification text field below:";
            dlg.VerificationText = "QA Review Required";
            dlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;

            // Since TaskDialog doesn't support free text input, we use a well-known
            // pattern: write a placeholder and let the user know where to find it
            if (dlg.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            string noteText = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] Review note — update via parameter editor";
            if (dlg.WasVerificationChecked())
                noteText = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] QA REVIEW REQUIRED";

            int written = 0;
            using (Transaction tx = new Transaction(doc, "STING Add Sticky Note"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    string existing = ParameterHelpers.GetString(el, NoteParam);
                    string newNote = string.IsNullOrEmpty(existing)
                        ? noteText
                        : existing + " | " + noteText;
                    ParameterHelpers.SetString(el, NoteParam, newNote, overwrite: true);
                    ParameterHelpers.SetString(el, NoteDateParam,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), overwrite: true);
                    written++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Sticky Note", $"Note added to {written} element(s).");
            return Result.Succeeded;
        }

        public static Result ViewNotes(Document doc, ICollection<ElementId> ids)
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (ElementId id in ids)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                string note = ParameterHelpers.GetString(el, NoteParam);
                if (!string.IsNullOrEmpty(note))
                {
                    count++;
                    string cat = ParameterHelpers.GetCategoryName(el);
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    sb.AppendLine($"[{el.Id}] {cat} — {tag}");
                    sb.AppendLine($"  Note: {note}");
                    sb.AppendLine();
                }
            }

            if (count == 0)
                TaskDialog.Show("Sticky Notes", "No notes found on selected elements.");
            else
                TaskDialog.Show("Sticky Notes", $"{count} note(s) found:\n\n{sb}");

            return Result.Succeeded;
        }

        public static Result ClearNotes(Document doc, ICollection<ElementId> ids)
        {
            int cleared = 0;
            using (Transaction tx = new Transaction(doc, "STING Clear Sticky Notes"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    if (!string.IsNullOrEmpty(ParameterHelpers.GetString(el, NoteParam)))
                    {
                        ParameterHelpers.SetString(el, NoteParam, "", overwrite: true);
                        ParameterHelpers.SetString(el, NoteDateParam, "", overwrite: true);
                        cleared++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Sticky Notes", $"Cleared notes from {cleared} element(s).");
            return Result.Succeeded;
        }

        public static Result ExportAllNotes(Document doc)
        {
            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, NoteParam)))
                .ToList();

            if (elements.Count == 0)
            {
                TaskDialog.Show("Export Notes", "No sticky notes found in the project.");
                return Result.Succeeded;
            }

            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_StickyNotes_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,Category,Family,Tag,Note,Date");
            foreach (var el in elements)
            {
                sb.AppendLine(string.Join(",",
                    $"\"{el.Id}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetCategoryName(el))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetFamilyName(el))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetString(el, ParamRegistry.TAG1))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetString(el, NoteParam))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetString(el, NoteDateParam))}\""));
            }

            File.WriteAllText(path, sb.ToString());
            TaskDialog.Show("Export Notes", $"Exported {elements.Count} notes to:\n{path}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ModelHealthEngine

    internal static class ModelHealthEngine
    {
        public class HealthReport
        {
            public int OverallScore { get; set; }
            public string Rating { get; set; }
            public string Summary { get; set; }
            public string Details { get; set; }
        }

        public static HealthReport RunHealthCheck(Document doc)
        {
            var checks = new List<(string name, int score, int maxScore, string detail)>();

            // 1. Warnings count (low = good)
            int warningCount = doc.GetWarnings()?.Count ?? 0;
            int warnScore = warningCount == 0 ? 10 : warningCount < 50 ? 8 : warningCount < 200 ? 5 : 2;
            checks.Add(("Warnings", warnScore, 10, $"{warningCount} warnings in model"));

            // 2. In-place families (fewer = better)
            int inPlace = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Count(fi => fi.Symbol?.Family?.IsInPlace == true);
            int ipScore = inPlace == 0 ? 10 : inPlace < 10 ? 7 : inPlace < 50 ? 4 : 1;
            checks.Add(("In-Place Families", ipScore, 10, $"{inPlace} in-place families"));

            // 3. Imported instances (CAD imports — fewer = better)
            int imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance)).GetElementCount();
            int impScore = imports == 0 ? 10 : imports < 5 ? 7 : imports < 20 ? 4 : 1;
            checks.Add(("CAD Imports", impScore, 10, $"{imports} imported instances"));

            // 4. Groups (fewer complex groups = better)
            int groups = new FilteredElementCollector(doc)
                .OfClass(typeof(Group)).GetElementCount();
            int grpScore = groups == 0 ? 10 : groups < 20 ? 8 : groups < 100 ? 5 : 2;
            checks.Add(("Groups", grpScore, 10, $"{groups} model groups"));

            // 5. Design options (none = simplest)
            int designOptions = new FilteredElementCollector(doc)
                .OfClass(typeof(DesignOption)).GetElementCount();
            int doScore = designOptions == 0 ? 10 : designOptions < 5 ? 7 : 3;
            checks.Add(("Design Options", doScore, 10, $"{designOptions} design options"));

            // 6. Linked models (reference, not embedded)
            int links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance)).GetElementCount();
            int lnkScore = links < 10 ? 10 : links < 30 ? 7 : 4;
            checks.Add(("Linked Models", lnkScore, 10, $"{links} linked models"));

            // 7. View count (too many unplaced = bloat)
            int totalViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Count(v => !v.IsTemplate);
            int placedViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .SelectMany(s => s.GetAllPlacedViews())
                .Distinct().Count();
            int unplacedViews = totalViews - placedViews;
            int viewScore = unplacedViews < 20 ? 10 : unplacedViews < 100 ? 6 : 3;
            checks.Add(("View Hygiene", viewScore, 10, $"{unplacedViews} unplaced views of {totalViews} total"));

            // 8. Tag completeness
            var compScan = ComplianceScan.Scan(doc);
            int tagScore = (int)(compScan.CompletePct / 10.0);
            checks.Add(("Tag Completeness", tagScore, 10, $"{compScan.CompletePct:F0}% complete"));

            // 9. Room coverage
            int rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().GetElementCount();
            int levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).GetElementCount();
            int roomScore = rooms > 0 && levels > 0 ? Math.Min(10, rooms / levels) : 0;
            checks.Add(("Room Coverage", roomScore, 10, $"{rooms} rooms across {levels} levels"));

            // 10. Sheet coverage
            int sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).GetElementCount();
            int sheetScore = sheets > 0 ? 10 : 0;
            checks.Add(("Sheet Setup", sheetScore, 10, $"{sheets} sheets"));

            int totalScore = checks.Sum(c => c.score);
            int maxTotal = checks.Sum(c => c.maxScore);
            int pct = maxTotal > 0 ? totalScore * 100 / maxTotal : 0;
            string rating = pct >= 80 ? "HEALTHY" : pct >= 60 ? "FAIR" : pct >= 40 ? "NEEDS ATTENTION" : "CRITICAL";

            var summary = new StringBuilder();
            foreach (var c in checks)
                summary.AppendLine($"  [{c.score}/{c.maxScore}] {c.name}: {c.detail}");

            var details = new StringBuilder();
            details.AppendLine("Recommendations:");
            if (warningCount > 50) details.AppendLine("  • Resolve Revit warnings (currently " + warningCount + ")");
            if (inPlace > 5) details.AppendLine("  • Convert in-place families to loadable families");
            if (imports > 0) details.AppendLine("  • Remove or link CAD imports instead of importing");
            if (unplacedViews > 50) details.AppendLine("  • Delete unplaced views to reduce model size");

            return new HealthReport
            {
                OverallScore = pct,
                Rating = rating,
                Summary = summary.ToString(),
                Details = details.ToString()
            };
        }

        public static string ExportReport(Document doc, HealthReport report)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_ModelHealth_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Metric,Value");
            sb.AppendLine($"\"Overall Score\",\"{report.OverallScore}\"");
            sb.AppendLine($"\"Rating\",\"{report.Rating}\"");
            sb.AppendLine($"\"Date\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
            sb.AppendLine($"\"Summary\",\"{report.Summary.Replace("\"", "\"\"")}\"");

            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }

    #endregion

    #region MidpEngine

    internal static class MidpEngine
    {
        public class MidpItem
        {
            public string Name { get; set; }
            public string Type { get; set; }      // Sheet, Model, Drawing
            public string Discipline { get; set; }
            public string Status { get; set; }     // Draft, ForReview, Approved, Published
            public string Suitability { get; set; } // S0-S6
        }

        public class MidpData
        {
            public int TotalDeliverables { get; set; }
            public int TotalSheets { get; set; }
            public int PublishedSheets { get; set; }
            public int LinkedModels { get; set; }
            public string SuitabilityBreakdown { get; set; }
            public Dictionary<string, int> ByDiscipline { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> ByStatus { get; set; } = new Dictionary<string, int>();
            public List<MidpItem> Items { get; set; } = new List<MidpItem>();
        }

        public static MidpData BuildMidpRegister(Document doc)
        {
            var data = new MidpData();

            // Sheets as deliverables
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .ToList();

            data.TotalSheets = sheets.Count;

            var suitCounts = new Dictionary<string, int>();
            foreach (var sheet in sheets)
            {
                string num = sheet.SheetNumber ?? "";
                string name = sheet.Name ?? "";
                string disc = num.Length >= 2 ? num.Substring(0, 2) : "XX";

                // Derive suitability from approved/issued status
                string approved = sheet.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";
                string issued = sheet.get_Parameter(BuiltInParameter.SHEET_ISSUE_DATE)?.AsString() ?? "";
                string status;
                string suit;

                if (!string.IsNullOrEmpty(issued))
                {
                    status = "Published";
                    suit = "S3";
                    data.PublishedSheets++;
                }
                else if (!string.IsNullOrEmpty(approved))
                {
                    status = "Approved";
                    suit = "S2";
                }
                else if (sheet.GetAllPlacedViews().Count > 0)
                {
                    status = "ForReview";
                    suit = "S1";
                }
                else
                {
                    status = "Draft";
                    suit = "S0";
                }

                if (!suitCounts.ContainsKey(suit)) suitCounts[suit] = 0;
                suitCounts[suit]++;

                if (!data.ByDiscipline.ContainsKey(disc)) data.ByDiscipline[disc] = 0;
                data.ByDiscipline[disc]++;
                if (!data.ByStatus.ContainsKey(status)) data.ByStatus[status] = 0;
                data.ByStatus[status]++;

                data.Items.Add(new MidpItem
                {
                    Name = $"{num} - {name}",
                    Type = "Sheet",
                    Discipline = disc,
                    Status = status,
                    Suitability = suit
                });
            }

            // Linked models as deliverables
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            data.LinkedModels = links.Count;
            foreach (var link in links)
            {
                string linkName = link.Name ?? "Unknown Link";
                data.Items.Add(new MidpItem
                {
                    Name = linkName,
                    Type = "Model",
                    Discipline = "XX",
                    Status = "Active",
                    Suitability = "S3"
                });
            }

            data.TotalDeliverables = data.Items.Count;
            data.SuitabilityBreakdown = string.Join(", ",
                suitCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));

            return data;
        }
    }

    #endregion

    #region SchedulingEngine (4D/5D)

    internal static class SchedulingEngine
    {
        public static string Export4DTimeline(Document doc)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_4D_Timeline_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,Category,Tag,Phase,Level,Discipline,StartDate,EndDate,Predecessors,Duration_Days");

            var phases = new FilteredElementCollector(doc)
                .OfClass(typeof(Phase)).Cast<Phase>().ToList();

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                .ToList();

            foreach (var el in elements)
            {
                string catName = el.Category?.Name ?? "";
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                string predecessors = ParameterHelpers.GetString(el, "STING_PREDECESSOR_TAGS_TXT");
                string startDate = ParameterHelpers.GetString(el, "STING_4D_START_DATE_TXT");
                string endDate = ParameterHelpers.GetString(el, "STING_4D_END_DATE_TXT");

                // Derive phase name
                string phaseName = "";
                Parameter createdParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (createdParam != null && createdParam.HasValue)
                {
                    Phase phase = doc.GetElement(createdParam.AsElementId()) as Phase;
                    phaseName = phase?.Name ?? "";
                }

                // Estimate duration from category
                int durationDays = EstimateDuration(catName);

                sb.AppendLine(string.Join(",",
                    $"\"{el.Id}\"",
                    $"\"{Esc(catName)}\"",
                    $"\"{Esc(tag)}\"",
                    $"\"{Esc(phaseName)}\"",
                    $"\"{Esc(lvl)}\"",
                    $"\"{Esc(disc)}\"",
                    $"\"{Esc(startDate)}\"",
                    $"\"{Esc(endDate)}\"",
                    $"\"{Esc(predecessors)}\"",
                    $"{durationDays}"));
            }

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"4DTimeline: exported {elements.Count} elements to {path}");
            return path;
        }

        public static string Export5DCostData(Document doc)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_5D_CostData_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,Category,Tag,Discipline,Family,Type,Quantity,Unit,EstimatedCost_GBP");

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                .ToList();

            foreach (var el in elements)
            {
                string catName = el.Category?.Name ?? "";
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string family = ParameterHelpers.GetFamilyName(el);
                string type = ParameterHelpers.GetFamilySymbolName(el);

                // Extract quantity
                (double qty, string unit) = ExtractQuantity(el);

                // Estimate cost
                double cost = EstimateCost(catName, qty);

                sb.AppendLine(string.Join(",",
                    $"\"{el.Id}\"",
                    $"\"{Esc(catName)}\"",
                    $"\"{Esc(tag)}\"",
                    $"\"{Esc(disc)}\"",
                    $"\"{Esc(family)}\"",
                    $"\"{Esc(type)}\"",
                    $"{qty:F2}",
                    $"\"{unit}\"",
                    $"{cost:F2}"));
            }

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"5DCostData: exported {elements.Count} elements to {path}");
            return path;
        }

        public static string ExportMeasuredQuantities(Document doc)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_MeasuredQty_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Category,Discipline,Count,TotalLength_m,TotalArea_m2,TotalVolume_m3");

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                .ToList();

            var groups = elements.GroupBy(e => e.Category.Name).OrderBy(g => g.Key);

            const double ftToM = 0.3048;
            const double sqFtToSqM = 0.092903;
            const double cuFtToCuM = 0.0283168;

            foreach (var g in groups)
            {
                string catName = g.Key;
                string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "?";
                int count = g.Count();
                double totalLength = 0, totalArea = 0, totalVolume = 0;

                foreach (var el in g)
                {
                    Parameter lenP = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (lenP != null && lenP.HasValue) totalLength += lenP.AsDouble() * ftToM;

                    Parameter areaP = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (areaP != null && areaP.HasValue) totalArea += areaP.AsDouble() * sqFtToSqM;

                    Parameter volP = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                    if (volP != null && volP.HasValue) totalVolume += volP.AsDouble() * cuFtToCuM;
                }

                sb.AppendLine($"\"{Esc(catName)}\",\"{disc}\",{count},{totalLength:F2},{totalArea:F2},{totalVolume:F4}");
            }

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"MeasuredQty: exported for {groups.Count()} categories to {path}");
            return path;
        }

        private static int EstimateDuration(string categoryName)
        {
            // NRM/CIBSE-based duration estimates (days per unit)
            if (categoryName.Contains("Wall")) return 5;
            if (categoryName.Contains("Floor")) return 3;
            if (categoryName.Contains("Roof")) return 7;
            if (categoryName.Contains("Column")) return 2;
            if (categoryName.Contains("Duct")) return 1;
            if (categoryName.Contains("Pipe")) return 1;
            if (categoryName.Contains("Electrical")) return 2;
            if (categoryName.Contains("Lighting")) return 1;
            return 1;
        }

        private static (double qty, string unit) ExtractQuantity(Element el)
        {
            const double ftToM = 0.3048;
            const double sqFtToSqM = 0.092903;

            // Try length first
            Parameter lenP = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (lenP != null && lenP.HasValue && lenP.AsDouble() > 0)
                return (lenP.AsDouble() * ftToM, "m");

            // Try area
            Parameter areaP = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (areaP != null && areaP.HasValue && areaP.AsDouble() > 0)
                return (areaP.AsDouble() * sqFtToSqM, "m2");

            // Default: 1 each
            return (1.0, "ea");
        }

        private static double EstimateCost(string categoryName, double qty)
        {
            // NRM2-based cost estimates (GBP per unit)
            double rate = 50.0; // default £50/unit
            if (categoryName.Contains("Wall")) rate = 120.0;
            if (categoryName.Contains("Floor")) rate = 85.0;
            if (categoryName.Contains("Roof")) rate = 180.0;
            if (categoryName.Contains("Door")) rate = 450.0;
            if (categoryName.Contains("Window")) rate = 600.0;
            if (categoryName.Contains("Mechanical Equipment")) rate = 2500.0;
            if (categoryName.Contains("Electrical Equipment")) rate = 1500.0;
            if (categoryName.Contains("Lighting")) rate = 250.0;
            if (categoryName.Contains("Plumbing")) rate = 350.0;
            if (categoryName.Contains("Duct")) rate = 75.0;
            if (categoryName.Contains("Pipe")) rate = 65.0;
            return rate * qty;
        }

        private static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");
    }

    #endregion
}
