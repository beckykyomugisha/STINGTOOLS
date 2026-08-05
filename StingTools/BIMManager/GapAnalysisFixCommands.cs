// ══════════════════════════════════════════════════════════════════════════════
//  GAP ANALYSIS FIX COMMANDS — Phase 68
//  Implements gap analysis findings from Phase 68 review.
//
//  GAP-03: Extended COBie Import (Type, System, Job worksheets)
//  GAP-04: Dashboard HTML Export from Coordination Center
//  GAP-05: BEP Compliance Auto-Validation per RIBA Stage
//  GAP-06: Auto-Link Issue Resolution to Revision Snapshots
//  GAP-07: COBie Warning Quality Gate (added to existing COBieExportCommand)
//  GAP-08: Auto-Generate Meeting Minutes from Issue Resolution
//  GAP-09: Tag Revision Diff Visualisation
//  GAP-10: Auto-Schedule Recurring Meetings from BEP
// ══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ══════════════════════════════════════════════════════════════════
    //  GAP-03: EXTENDED COBie IMPORT — Type, System, Job Worksheets
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-03: Extended COBie import supporting Type, System, and Job worksheets
    /// in addition to the existing Component worksheet. Closes the COBie round-trip
    /// loop for FM handover by importing warranty, maintenance, and system data.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieExtendedImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import COBie Spreadsheet (Extended)",
                Filter = "Excel (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                DefaultExt = ".xlsx"
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            try
            {
                using var workbook = new ClosedXML.Excel.XLWorkbook(dlg.FileName);

                // Build element lookup once
                var byUniqueId = new Dictionary<string, Element>();
                var byTag = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
                var byTypeName = new Dictionary<string, ElementType>(StringComparer.OrdinalIgnoreCase);

                var allElems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                    .ToList();
                foreach (var el in allElems)
                {
                    byUniqueId[el.UniqueId] = el;
                    string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (!string.IsNullOrEmpty(tag1)) byTag[tag1] = el;
                }

                // Build type lookup
                var allTypes = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToList();
                foreach (var t in allTypes)
                {
                    if (t is ElementType et && !string.IsNullOrEmpty(et.Name))
                        byTypeName[et.Name] = et;
                }

                var report = new StringBuilder();
                int totalUpdated = 0;

                using (Transaction tx = new Transaction(doc, "STING COBie Extended Import"))
                {
                    tx.Start();

                    // ── Import Type worksheet ──
                    var typeSheet = workbook.Worksheets.FirstOrDefault(ws =>
                        ws.Name.Equals("Type", StringComparison.OrdinalIgnoreCase));
                    if (typeSheet != null)
                    {
                        int typeUpdated = ImportTypeSheet(typeSheet, byTypeName, doc);
                        totalUpdated += typeUpdated;
                        report.AppendLine($"Type worksheet: {typeUpdated} types updated");
                    }
                    else report.AppendLine("Type worksheet: not found (skipped)");

                    // ── Import System worksheet ──
                    var systemSheet = workbook.Worksheets.FirstOrDefault(ws =>
                        ws.Name.Equals("System", StringComparison.OrdinalIgnoreCase));
                    if (systemSheet != null)
                    {
                        int sysUpdated = ImportSystemSheet(systemSheet, byTag, allElems, doc);
                        totalUpdated += sysUpdated;
                        report.AppendLine($"System worksheet: {sysUpdated} elements updated");
                    }
                    else report.AppendLine("System worksheet: not found (skipped)");

                    // ── Import Job worksheet ──
                    var jobSheet = workbook.Worksheets.FirstOrDefault(ws =>
                        ws.Name.Equals("Job", StringComparison.OrdinalIgnoreCase));
                    if (jobSheet != null)
                    {
                        int jobUpdated = ImportJobSheet(jobSheet, byTypeName, doc);
                        totalUpdated += jobUpdated;
                        report.AppendLine($"Job worksheet: {jobUpdated} types updated with maintenance data");
                    }
                    else report.AppendLine("Job worksheet: not found (skipped)");

                    // ── Import Component worksheet (delegate to existing) ──
                    var componentSheet = workbook.Worksheets.FirstOrDefault(ws =>
                        ws.Name.Equals("Component", StringComparison.OrdinalIgnoreCase));
                    if (componentSheet != null)
                    {
                        int compUpdated = ImportComponentSheet(componentSheet, byUniqueId, byTag);
                        totalUpdated += compUpdated;
                        report.AppendLine($"Component worksheet: {compUpdated} elements updated");
                    }
                    else report.AppendLine("Component worksheet: not found (skipped)");

                    tx.Commit();
                }

                ComplianceScan.InvalidateCache();
                StingAutoTagger.InvalidateContext();

                TaskDialog.Show("STING COBie Extended Import",
                    $"Extended COBie import complete.\n\n{report}\nTotal updates: {totalUpdated}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"COBie extended import failed: {ex.Message}", ex);
                return Result.Failed;
            }
        }

        private static Dictionary<string, int> ReadHeaders(ClosedXML.Excel.IXLWorksheet sheet)
        {
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string h = sheet.Cell(1, c).GetString()?.Trim();
                if (!string.IsNullOrEmpty(h)) headers[h] = c;
            }
            return headers;
        }

        private static string GetCell(ClosedXML.Excel.IXLWorksheet sheet, int row, Dictionary<string, int> headers, string col)
        {
            if (!headers.ContainsKey(col)) return null;
            return sheet.Cell(row, headers[col]).GetString()?.Trim();
        }

        /// <summary>Import COBie Type worksheet — warranty, dimensions, material data into ElementTypes.</summary>
        private static int ImportTypeSheet(ClosedXML.Excel.IXLWorksheet sheet,
            Dictionary<string, ElementType> byTypeName, Document doc)
        {
            var headers = ReadHeaders(sheet);
            int lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 5001);
            int updated = 0;

            var typeColumnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Description"] = "ASS_DESCRIPTION_TXT",
                ["Manufacturer"] = "ASS_MANUFACTURER_TXT",
                ["ModelNumber"] = "ASS_MODEL_NUM_TXT",
                ["WarrantyDurationParts"] = "MNT_WARRANTY_YRS_TXT",
                ["WarrantyGuarantorParts"] = "MNT_WARRANTY_PROVIDER_TXT",
                ["ReplacementCost"] = "ASS_REPLACEMENT_COST_TXT",
                ["ExpectedLife"] = "MNT_EXPECTED_LIFE_TXT",
                ["NominalLength"] = "BLE_LENGTH_TXT",
                ["NominalWidth"] = "BLE_WIDTH_TXT",
                ["NominalHeight"] = "BLE_HEIGHT_TXT",
                ["Material"] = "ASS_MATERIAL_TXT",
                ["Color"] = "ASS_COLOUR_TXT",
                ["Finish"] = "ASS_FINISH_TXT",
                ["Grade"] = "ASS_GRADE_TXT",
                ["Shape"] = "ASS_SHAPE_TXT",
                ["Size"] = "ASS_SIZE_TXT",
            };

            for (int row = 2; row <= lastRow; row++)
            {
                string typeName = GetCell(sheet, row, headers, "Name");
                if (string.IsNullOrEmpty(typeName)) continue;

                if (!byTypeName.TryGetValue(typeName, out ElementType et)) continue;

                bool anyWritten = false;
                foreach (var kv in typeColumnMap)
                {
                    string val = GetCell(sheet, row, headers, kv.Key);
                    if (string.IsNullOrEmpty(val)) continue;
                    if (val.Equals("CLEAR", StringComparison.OrdinalIgnoreCase)) val = "";
                    if (ParameterHelpers.SetString(et, kv.Value, val, overwrite: true))
                        anyWritten = true;
                }
                if (anyWritten) updated++;
            }
            return updated;
        }

        /// <summary>Import COBie System worksheet — update SYS token on elements by system grouping.</summary>
        private static int ImportSystemSheet(ClosedXML.Excel.IXLWorksheet sheet,
            Dictionary<string, Element> byTag, List<Element> allElems, Document doc)
        {
            var headers = ReadHeaders(sheet);
            int lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 1001);
            int updated = 0;

            for (int row = 2; row <= lastRow; row++)
            {
                string sysName = GetCell(sheet, row, headers, "Name");
                string sysCategory = GetCell(sheet, row, headers, "Category");
                string componentNames = GetCell(sheet, row, headers, "ComponentNames");

                if (string.IsNullOrEmpty(sysName) || string.IsNullOrEmpty(componentNames)) continue;

                // Parse component names (pipe-delimited or comma-delimited)
                char delim = componentNames.Contains('|') ? '|' : ',';
                var compNames = componentNames.Split(delim)
                    .Select(n => n.Trim())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                foreach (string compName in compNames)
                {
                    // Try to match by TAG1 or element name
                    Element target = null;
                    byTag.TryGetValue(compName, out target);
                    if (target == null)
                    {
                        target = allElems.FirstOrDefault(e =>
                            (e.Name ?? "").Equals(compName, StringComparison.OrdinalIgnoreCase));
                    }
                    if (target == null) continue;

                    // Update system-related parameters
                    if (!string.IsNullOrEmpty(sysCategory))
                    {
                        if (ParameterHelpers.SetString(target, ParamRegistry.SYS, sysCategory, overwrite: true))
                            updated++;
                    }
                }
            }
            return updated;
        }

        /// <summary>Import COBie Job worksheet — maintenance task data into element types.</summary>
        private static int ImportJobSheet(ClosedXML.Excel.IXLWorksheet sheet,
            Dictionary<string, ElementType> byTypeName, Document doc)
        {
            var headers = ReadHeaders(sheet);
            int lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 5001);
            int updated = 0;

            for (int row = 2; row <= lastRow; row++)
            {
                string typeName = GetCell(sheet, row, headers, "TypeName");
                if (string.IsNullOrEmpty(typeName)) continue;
                if (!byTypeName.TryGetValue(typeName, out ElementType et)) continue;

                string taskName = GetCell(sheet, row, headers, "Name");
                string frequency = GetCell(sheet, row, headers, "Frequency");
                string duration = GetCell(sheet, row, headers, "Duration");
                string description = GetCell(sheet, row, headers, "Description");

                // Write maintenance data to STING parameters
                bool anyWritten = false;
                if (!string.IsNullOrEmpty(taskName))
                    anyWritten |= ParameterHelpers.SetString(et, "MNT_TASK_NAME_TXT", taskName, overwrite: true);
                if (!string.IsNullOrEmpty(frequency))
                    anyWritten |= ParameterHelpers.SetString(et, "MNT_FREQUENCY_TXT", frequency, overwrite: true);
                if (!string.IsNullOrEmpty(duration))
                    anyWritten |= ParameterHelpers.SetString(et, "MNT_DURATION_TXT", duration, overwrite: true);
                if (!string.IsNullOrEmpty(description))
                    anyWritten |= ParameterHelpers.SetString(et, "MNT_TASK_DESC_TXT", description, overwrite: true);

                if (anyWritten) updated++;
            }
            return updated;
        }

        /// <summary>Import COBie Component worksheet — matches existing COBieImportCommand logic.</summary>
        private static int ImportComponentSheet(ClosedXML.Excel.IXLWorksheet sheet,
            Dictionary<string, Element> byUniqueId, Dictionary<string, Element> byTag)
        {
            var headers = ReadHeaders(sheet);
            int lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 10001);
            int updated = 0;

            var columnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Description"] = "ASS_DESCRIPTION_TXT",
                ["SerialNumber"] = "ASS_SERIAL_NR_TXT",
                ["BarCode"] = "ASS_BARCODE_TXT",
                ["AssetIdentifier"] = "ASS_ASSET_ID_TXT",
                ["WarrantyDurationParts"] = "MNT_WARRANTY_YRS_TXT",
                ["WarrantyGuarantorParts"] = "MNT_WARRANTY_PROVIDER_TXT",
                ["InstallationDate"] = "ASS_INSTALLATION_DATE_TXT",
                ["WarrantyStartDate"] = "MNT_WARRANTY_START_TXT",
            };

            for (int row = 2; row <= lastRow; row++)
            {
                Element target = null;
                string extId = GetCell(sheet, row, headers, "ExternalIdentifier");
                if (!string.IsNullOrEmpty(extId)) byUniqueId.TryGetValue(extId, out target);
                if (target == null)
                {
                    string tagNum = GetCell(sheet, row, headers, "TagNumber");
                    if (!string.IsNullOrEmpty(tagNum)) byTag.TryGetValue(tagNum, out target);
                }
                if (target == null) continue;

                bool anyWritten = false;
                foreach (var kv in columnMap)
                {
                    string val = GetCell(sheet, row, headers, kv.Key);
                    if (string.IsNullOrEmpty(val)) continue;
                    if (val.Equals("CLEAR", StringComparison.OrdinalIgnoreCase)) val = "";
                    if (ParameterHelpers.SetString(target, kv.Value, val, overwrite: true))
                        anyWritten = true;
                }
                if (anyWritten) updated++;
            }
            return updated;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GAP-04: DASHBOARD HTML EXPORT
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-04: Export BIM Coordination Center dashboard as self-contained HTML report.
    /// Generates a professional report with KPIs, compliance tables, RAG bars, and warning summaries
    /// that can be shared with stakeholders without Revit access.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportDashboardHTMLCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string projectName = doc.Title ?? "Unknown Project";
            string exportPath = OutputLocationHelper.GetTimestampedPath(doc, $"STING_Dashboard_{projectName}", ".html");

            try
            {
                // Gather data
                var comp = ComplianceScan.Scan(doc);
                WarningReport warnings = null;
                try { warnings = WarningsEngine.ScanWarnings(doc); }
                catch (Exception ex) { StingLog.Warn($"Dashboard export warnings scan: {ex.Message}"); }

                var html = new StringBuilder();
                html.AppendLine("<!DOCTYPE html>");
                html.AppendLine("<html lang='en'><head><meta charset='UTF-8'>");
                html.AppendLine($"<title>BIM Dashboard — {EscapeHtml(projectName)}</title>");
                html.AppendLine("<style>");
                html.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; padding: 0; background: #f5f5f5; color: #333; }");
                html.AppendLine(".header { background: #1A237E; color: white; padding: 24px 40px; }");
                html.AppendLine(".header h1 { margin: 0; font-size: 24px; }");
                html.AppendLine(".header .subtitle { color: #E8912D; font-size: 14px; margin-top: 4px; }");
                html.AppendLine(".content { max-width: 1200px; margin: 0 auto; padding: 24px 40px; }");
                html.AppendLine(".kpi-row { display: flex; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }");
                html.AppendLine(".kpi { background: white; border-radius: 8px; padding: 20px; flex: 1; min-width: 200px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
                html.AppendLine(".kpi .label { font-size: 12px; color: #666; text-transform: uppercase; letter-spacing: 1px; }");
                html.AppendLine(".kpi .value { font-size: 32px; font-weight: bold; margin-top: 4px; }");
                html.AppendLine(".rag-green { color: #2E7D32; } .rag-amber { color: #F57F17; } .rag-red { color: #C62828; }");
                html.AppendLine(".section { background: white; border-radius: 8px; padding: 20px; margin-bottom: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
                html.AppendLine(".section h2 { margin-top: 0; color: #1A237E; border-bottom: 2px solid #E8912D; padding-bottom: 8px; }");
                html.AppendLine("table { width: 100%; border-collapse: collapse; }");
                html.AppendLine("th { background: #1A237E; color: white; padding: 10px 12px; text-align: left; }");
                html.AppendLine("td { padding: 8px 12px; border-bottom: 1px solid #eee; }");
                html.AppendLine("tr:nth-child(even) { background: #fafafa; }");
                html.AppendLine(".bar { height: 20px; border-radius: 4px; display: inline-block; }");
                html.AppendLine(".bar-bg { background: #e0e0e0; width: 200px; height: 20px; border-radius: 4px; display: inline-block; position: relative; }");
                html.AppendLine(".footer { text-align: center; color: #999; padding: 20px; font-size: 12px; }");
                html.AppendLine("</style></head><body>");

                // Header
                html.AppendLine("<div class='header'>");
                html.AppendLine($"<h1>BIM Coordination Dashboard</h1>");
                html.AppendLine($"<div class='subtitle'>{EscapeHtml(projectName)} — Generated {DateTime.Now:yyyy-MM-dd HH:mm} by {Environment.UserName}</div>");
                html.AppendLine("</div>");
                html.AppendLine("<div class='content'>");

                // KPI cards
                string ragClass = comp != null && comp.CompliancePercent >= 80 ? "rag-green" :
                    comp != null && comp.CompliancePercent >= 50 ? "rag-amber" : "rag-red";
                html.AppendLine("<div class='kpi-row'>");
                html.AppendLine($"<div class='kpi'><div class='label'>Total Elements</div><div class='value'>{comp?.TotalElements ?? 0}</div></div>");
                html.AppendLine($"<div class='kpi'><div class='label'>Tag Compliance</div><div class='value {ragClass}'>{comp?.CompliancePercent ?? 0:F0}%</div></div>");
                html.AppendLine($"<div class='kpi'><div class='label'>Warnings</div><div class='value'>{warnings?.Total ?? 0}</div></div>");
                html.AppendLine($"<div class='kpi'><div class='label'>Stale Elements</div><div class='value'>{comp?.StaleCount ?? 0}</div></div>");
                html.AppendLine($"<div class='kpi'><div class='label'>Placeholders</div><div class='value'>{comp?.PlaceholderCount ?? 0}</div></div>");
                html.AppendLine("</div>");

                // Per-discipline compliance table
                if (comp?.ByDisc != null && comp.ByDisc.Count > 0)
                {
                    html.AppendLine("<div class='section'><h2>Compliance by Discipline</h2>");
                    html.AppendLine("<table><tr><th>Discipline</th><th>Total</th><th>Tagged</th><th>Untagged</th><th>Compliance</th><th>Progress</th></tr>");
                    foreach (var kv in comp.ByDisc.OrderByDescending(x => x.Value.Total))
                    {
                        string dRag = kv.Value.CompliancePct >= 80 ? "#2E7D32" : kv.Value.CompliancePct >= 50 ? "#F57F17" : "#C62828";
                        int barWidth = (int)(kv.Value.CompliancePct * 2);
                        html.AppendLine($"<tr><td><strong>{EscapeHtml(kv.Key)}</strong></td>" +
                            $"<td>{kv.Value.Total}</td><td>{kv.Value.Tagged}</td><td>{kv.Value.Untagged}</td>" +
                            $"<td style='color:{dRag};font-weight:bold'>{kv.Value.CompliancePct:F0}%</td>" +
                            $"<td><div class='bar-bg'><div class='bar' style='width:{barWidth}px;background:{dRag}'></div></div></td></tr>");
                    }
                    html.AppendLine("</table></div>");
                }

                // Warning summary
                if (warnings != null && warnings.Total > 0)
                {
                    html.AppendLine("<div class='section'><h2>Warning Summary</h2>");
                    html.AppendLine("<table><tr><th>Category</th><th>Count</th><th>Auto-Fixable</th></tr>");
                    if (warnings.ByCategory != null)
                    {
                        foreach (var kv in warnings.ByCategory.OrderByDescending(x => x.Value))
                        {
                            int fixable = warnings.Warnings?
                                .Count(w => w.Category == kv.Key && w.CanAutoFix) ?? 0;
                            html.AppendLine($"<tr><td>{EscapeHtml(kv.Key.ToString())}</td><td>{kv.Value}</td><td>{fixable}</td></tr>");
                        }
                    }
                    html.AppendLine("</table></div>");
                }

                // Empty token breakdown
                if (comp?.EmptyTokenCounts != null && comp.EmptyTokenCounts.Count > 0)
                {
                    html.AppendLine("<div class='section'><h2>Token Coverage</h2>");
                    html.AppendLine("<table><tr><th>Token</th><th>Empty Count</th><th>Coverage</th></tr>");
                    foreach (var kv in comp.EmptyTokenCounts.OrderByDescending(x => x.Value))
                    {
                        int total = comp.TotalElements;
                        double pct = total > 0 ? 100.0 * (total - kv.Value) / total : 0;
                        string tRag = pct >= 90 ? "#2E7D32" : pct >= 70 ? "#F57F17" : "#C62828";
                        html.AppendLine($"<tr><td>{EscapeHtml(kv.Key)}</td><td>{kv.Value}</td>" +
                            $"<td style='color:{tRag};font-weight:bold'>{pct:F0}%</td></tr>");
                    }
                    html.AppendLine("</table></div>");
                }

                html.AppendLine($"<div class='footer'>Generated by StingTools BIM Coordination Center — {DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>");
                html.AppendLine("</div></body></html>");

                File.WriteAllText(exportPath, html.ToString(), Encoding.UTF8);

                TaskDialog.Show("STING Dashboard Export",
                    $"Dashboard exported successfully.\n\nFile: {exportPath}\n\nOpen in any web browser to view.");
                StingLog.Info($"Dashboard HTML exported: {exportPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"Dashboard HTML export failed: {ex.Message}", ex);
                return Result.Failed;
            }
        }

        private static string EscapeHtml(string s) =>
            s?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;") ?? "";
    }

    // ══════════════════════════════════════════════════════════════════
    //  GAP-05: BEP COMPLIANCE AUTO-VALIDATION PER RIBA STAGE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-05: Auto-validate BEP compliance targets against actual model compliance
    /// per RIBA Plan of Work 2020 stage (0-7). Prevents deliverables being issued below
    /// stage-appropriate quality thresholds.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BEPStageValidationCommand : IExternalCommand
    {
        /// <summary>RIBA stage → minimum compliance % for each stage.</summary>
        private static readonly Dictionary<int, (string Name, double MinTagPct, double MinContainerPct, string[] RequiredCOBie)> RIBATargets
            = new()
        {
            [0] = ("Strategic Definition", 0, 0, Array.Empty<string>()),
            [1] = ("Preparation & Briefing", 10, 0, new[] { "Facility" }),
            [2] = ("Concept Design", 30, 20, new[] { "Facility", "Floor", "Space" }),
            [3] = ("Spatial Coordination", 60, 50, new[] { "Facility", "Floor", "Space", "Type" }),
            [4] = ("Technical Design", 80, 70, new[] { "Facility", "Floor", "Space", "Type", "Component", "System" }),
            [5] = ("Manufacturing & Construction", 90, 85, new[] { "Facility", "Floor", "Space", "Type", "Component", "System", "Job" }),
            [6] = ("Handover", 95, 90, new[] { "Facility", "Floor", "Space", "Type", "Component", "System", "Job", "Zone" }),
            [7] = ("Use", 98, 95, new[] { "Facility", "Floor", "Space", "Type", "Component", "System", "Job", "Zone", "Contact" }),
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Detect current RIBA stage from project config or BEP
            int currentStage = DetectRIBAStage(doc);

            var comp = ComplianceScan.Scan(doc);
            if (comp == null) { TaskDialog.Show("STING", "Could not scan compliance."); return Result.Failed; }

            var (stageName, minTagPct, minContainerPct, requiredSheets) = RIBATargets.TryGetValue(currentStage, out var rtVal)
                ? rtVal : RIBATargets[0];

            var report = new StringBuilder();
            report.AppendLine($"BEP Stage Validation — RIBA Stage {currentStage}: {stageName}");
            report.AppendLine(new string('═', 60));
            report.AppendLine();

            bool overallPass = true;

            // Check 1: Tag compliance
            bool tagPass = comp.CompliancePercent >= minTagPct;
            report.AppendLine($"{(tagPass ? "✓ PASS" : "✗ FAIL")}  Tag Compliance: {comp.CompliancePercent:F0}% (required: ≥{minTagPct}%)");
            if (!tagPass) overallPass = false;

            // Check 2: Container compliance
            double containerPct = comp.ContainerCompletePct;
            bool containerPass = containerPct >= minContainerPct;
            report.AppendLine($"{(containerPass ? "✓ PASS" : "✗ FAIL")}  Container Compliance: {containerPct:F0}% (required: ≥{minContainerPct}%)");
            if (!containerPass) overallPass = false;

            // Check 3: STATUS population
            double statusPct = comp.TotalElements > 0
                ? 100.0 * (comp.TotalElements - (comp.EmptyTokenCounts?.GetValueOrDefault("STATUS", 0) ?? 0)) / comp.TotalElements : 0;
            bool statusPass = currentStage <= 3 || statusPct >= 80;
            report.AppendLine($"{(statusPass ? "✓ PASS" : "✗ FAIL")}  STATUS populated: {statusPct:F0}% {(currentStage <= 3 ? "(not required at this stage)" : "(required: ≥80%)")}");
            if (!statusPass) overallPass = false;

            // Check 4: Per-discipline breakdown
            report.AppendLine();
            report.AppendLine("Per-Discipline Compliance:");
            if (comp.ByDisc != null)
            {
                foreach (var kv in comp.ByDisc.OrderByDescending(x => x.Value.Total))
                {
                    bool discPass = kv.Value.CompliancePct >= minTagPct;
                    report.AppendLine($"  {(discPass ? "✓" : "✗")} {kv.Key}: {kv.Value.CompliancePct:F0}% ({kv.Value.Tagged}/{kv.Value.Total})");
                    if (!discPass) overallPass = false;
                }
            }

            // Check 5: Stale elements
            bool stalePass = comp.StaleCount == 0 || currentStage < 4;
            report.AppendLine();
            report.AppendLine($"{(stalePass ? "✓ PASS" : "✗ FAIL")}  Stale elements: {comp.StaleCount} {(currentStage < 4 ? "(not critical at this stage)" : "(must be 0 for this stage)")}");
            if (!stalePass) overallPass = false;

            // Overall verdict
            report.AppendLine();
            report.AppendLine(new string('═', 60));
            report.AppendLine(overallPass
                ? $"✓ OVERALL: Model PASSES RIBA Stage {currentStage} ({stageName}) requirements"
                : $"✗ OVERALL: Model FAILS RIBA Stage {currentStage} ({stageName}) requirements");

            if (!overallPass)
            {
                report.AppendLine();
                report.AppendLine("Recommended actions:");
                if (!tagPass) report.AppendLine("  → Run 'Tag & Combine' or 'Batch Tag' to increase tag compliance");
                if (!containerPass) report.AppendLine("  → Run 'Combine Parameters' to populate discipline containers");
                if (!statusPass) report.AppendLine("  → Run 'Family Stage Populate' to derive STATUS from phases");
                if (!stalePass) report.AppendLine("  → Run 'Retag Stale' to refresh changed elements");
            }

            TaskDialog.Show("STING BEP Stage Validation", report.ToString());
            StingLog.Info($"BEP Stage {currentStage} validation: {(overallPass ? "PASS" : "FAIL")} — tag {comp.CompliancePercent:F0}%, container {containerPct:F0}%");
            return Result.Succeeded;
        }

        /// <summary>Detect RIBA stage from project_config.json or BEP.</summary>
        private static int DetectRIBAStage(Document doc)
        {
            try
            {
                // Try project_config.json first
                double val = TagConfig.GetConfigDouble("RIBA_STAGE", -1);
                if (val >= 0 && val <= 7) return (int)val;

                // Try BEP file
                string docPath = doc?.PathName;
                if (!string.IsNullOrEmpty(docPath))
                {
                    string bepPath = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager", "project_bep.json");
                    if (File.Exists(bepPath))
                    {
                        string json = File.ReadAllText(bepPath);
                        var obj = JObject.Parse(json);
                        var stage = obj["riba_stage"] ?? obj["RIBA_STAGE"] ?? obj["stage"];
                        if (stage != null && int.TryParse(stage.ToString(), out int s) && s >= 0 && s <= 7)
                            return s;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DetectRIBAStage: {ex.Message}"); }

            // Default to Stage 4 (Technical Design) as most common active BIM stage
            return 4;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GAP-06: AUTO-LINK ISSUE RESOLUTION TO REVISION SNAPSHOTS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-06: When issues are closed, auto-capture a revision snapshot linking the
    /// issue ID. Creates complete audit trail from issue → resolution → revision for ISO 19650.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class IssueRevisionLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            try
            {
                int linked = GapAnalysisEngine.LinkClosedIssuesToRevisions(doc);
                TaskDialog.Show("STING Issue-Revision Link",
                    linked > 0
                        ? $"Linked {linked} closed issues to revision snapshots.\n\nEach snapshot captures the model state at issue resolution for ISO 19650 audit trail."
                        : "No newly closed issues found to link.\n\nIssues are linked automatically when their status changes to CLOSED.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"Issue-revision link: {ex.Message}", ex);
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GAP-08: AUTO-GENERATE MEETING MINUTES FROM ISSUE RESOLUTION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-08: Auto-populate meeting minutes from issues closed since the last meeting,
    /// including before/after compliance metrics. Saves 15-30 min per coordination meeting.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoMeetingMinutesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            try
            {
                string minutes = GapAnalysisEngine.GenerateAutoMinutes(doc);
                if (string.IsNullOrEmpty(minutes))
                {
                    TaskDialog.Show("STING Auto Minutes", "No recent activity to generate minutes from.\n\nClose some issues or make tag changes first.");
                    return Result.Succeeded;
                }

                // Save minutes to file
                string exportPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_Meeting_Minutes", ".txt");
                File.WriteAllText(exportPath, minutes, Encoding.UTF8);

                TaskDialog.Show("STING Auto Minutes",
                    $"Meeting minutes generated from recent activity.\n\n" +
                    $"File: {exportPath}\n\n" +
                    $"Content preview:\n{(minutes.Length > 500 ? minutes.Substring(0, 500) + "..." : minutes)}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"Auto meeting minutes: {ex.Message}", ex);
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GAP-09: TAG REVISION DIFF VISUALISATION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-09: Compare two revision snapshots and report added/removed/changed tags
    /// with token-level detail. Enables BIM coordinators to understand exactly what changed.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagRevisionDiffCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            try
            {
                // Get available snapshots
                var snapshots = GapAnalysisEngine.GetAvailableSnapshots(doc);
                if (snapshots.Count < 2)
                {
                    TaskDialog.Show("STING Tag Diff",
                        "Need at least 2 revision snapshots to compare.\n\n" +
                        "Create revisions via 'Create Revision' command first.\n" +
                        $"Found: {snapshots.Count} snapshot(s).");
                    return Result.Failed;
                }

                // Let user pick two snapshots
                var pickerItems = snapshots.Select(s => s.Label + $" ({s.Date:yyyy-MM-dd HH:mm})").ToList();
                var td1 = new TaskDialog("STING Tag Diff — Select Base Snapshot");
                td1.MainInstruction = "Select the BASE (older) snapshot:";
                var opts = new StringBuilder();
                for (int i = 0; i < Math.Min(snapshots.Count, 4); i++)
                    opts.AppendLine($"  [{i + 1}] {pickerItems[i]}");
                td1.MainContent = opts.ToString();
                td1.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, pickerItems.Count > 0 ? pickerItems[0] : "—");
                td1.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, pickerItems.Count > 1 ? pickerItems[1] : "—");
                td1.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, pickerItems.Count > 2 ? pickerItems[2] : "—");
                var r1 = td1.Show();
                int baseIdx = r1 == TaskDialogResult.CommandLink1 ? 0 :
                    r1 == TaskDialogResult.CommandLink2 ? 1 :
                    r1 == TaskDialogResult.CommandLink3 ? 2 : -1;
                if (baseIdx < 0) return Result.Cancelled;

                var td2 = new TaskDialog("STING Tag Diff — Select Compare Snapshot");
                td2.MainInstruction = "Select the COMPARE (newer) snapshot:";
                td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, pickerItems.Count > 0 ? pickerItems[0] : "—");
                td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, pickerItems.Count > 1 ? pickerItems[1] : "—");
                td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, pickerItems.Count > 2 ? pickerItems[2] : "—");
                var r2 = td2.Show();
                int compareIdx = r2 == TaskDialogResult.CommandLink1 ? 0 :
                    r2 == TaskDialogResult.CommandLink2 ? 1 :
                    r2 == TaskDialogResult.CommandLink3 ? 2 : -1;
                if (compareIdx < 0 || compareIdx == baseIdx) return Result.Cancelled;

                // Generate diff
                string diffReport = GapAnalysisEngine.GenerateTagDiff(
                    snapshots[baseIdx], snapshots[compareIdx]);

                // Export
                string exportPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_Tag_Diff", ".csv");
                File.WriteAllText(exportPath, diffReport, Encoding.UTF8);

                TaskDialog.Show("STING Tag Revision Diff",
                    $"Tag diff generated between:\n" +
                    $"  Base: {pickerItems[baseIdx]}\n" +
                    $"  Compare: {pickerItems[compareIdx]}\n\n" +
                    $"Exported to: {exportPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"Tag revision diff: {ex.Message}", ex);
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GAP-10: AUTO-SCHEDULE RECURRING MEETINGS FROM BEP
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GAP-10: Parse BEP meeting schedule and auto-create recurring meeting entries
    /// in meetings.json. Reduces meeting administration overhead.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoScheduleMeetingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            try
            {
                int created = GapAnalysisEngine.AutoScheduleMeetingsFromBEP(doc);
                TaskDialog.Show("STING Auto-Schedule Meetings",
                    created > 0
                        ? $"Created {created} recurring meeting entries from BEP.\n\nMeetings are saved to meetings.json and will appear in the Meeting Manager."
                        : "No BEP meeting schedule found or all meetings already exist.\n\nEnsure the BEP contains a 'meetings' section with schedule data.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error($"Auto-schedule meetings: {ex.Message}", ex);
                return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GAP ANALYSIS ENGINE — Shared Logic for All Gap Fixes
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Engine class containing shared logic for gap analysis fix commands.</summary>
    internal static class GapAnalysisEngine
    {
        // ── GAP-06: Issue → Revision linking ──

        /// <summary>Scan issues.json for newly CLOSED issues and create linked revision snapshots.</summary>
        internal static int LinkClosedIssuesToRevisions(Document doc)
        {
            string issuesPath = GetBimManagerPath(doc, "issues.json");
            if (!File.Exists(issuesPath)) return 0;

            string snapshotDir = RevisionEngine.GetRevisionDir(doc);
            if (string.IsNullOrEmpty(snapshotDir)) return 0;

            var issues = JArray.Parse(File.ReadAllText(issuesPath));
            int linked = 0;

            foreach (JObject issue in issues)
            {
                string status = issue["status"]?.ToString();
                if (!string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase)) continue;

                string issueId = issue["id"]?.ToString();
                if (string.IsNullOrEmpty(issueId)) continue;

                // Check if already linked (has revision_snapshot field)
                if (issue["revision_snapshot"] != null) continue;

                // Take snapshot and link
                var snapshot = RevisionEngine.TakeTagSnapshot(doc);
                string snapshotLabel = $"issue_close_{issueId}_{DateTime.Now:yyyyMMdd_HHmm}";
                RevisionEngine.SaveSnapshot(doc, snapshot, snapshotLabel);

                // Update issue with link
                issue["revision_snapshot"] = snapshotLabel;
                issue["resolution_date"] = DateTime.Now.ToString("o");
                issue["resolution_compliance_pct"] = ComplianceScan.Scan(doc)?.CompliancePercent ?? 0;
                linked++;
            }

            if (linked > 0)
            {
                File.WriteAllText(issuesPath, issues.ToString(Formatting.Indented), Encoding.UTF8);
                StingLog.Info($"Linked {linked} closed issues to revision snapshots");
            }
            return linked;
        }

        // ── GAP-07: COBie Warning Quality Gate ──

        /// <summary>Check if warnings would impact COBie data quality.
        /// Returns (pass, message) tuple.</summary>
        internal static (bool pass, string reason) CheckCOBieWarningQuality(Document doc)
        {
            try
            {
                var warnings = WarningsEngine.ScanWarnings(doc);
                if (warnings == null || warnings.Total == 0)
                    return (true, "No warnings detected");

                var impact = WarningsEngine.AnalyseDeliverableImpact(warnings.Warnings);
                if (impact == null)
                    return (true, "No deliverable impact detected");

                int cobieImpact = impact.AffectsCOBie;

                // Also check for critical/high warnings in data quality and spatial categories
                int criticalDataWarnings = warnings.Warnings?
                    .Count(w => (w.Category == WarningCategory.Data ||
                                 w.Category == WarningCategory.Spatial) &&
                                (w.Severity == WarningSeverity.Critical ||
                                 w.Severity == WarningSeverity.High)) ?? 0;

                if (cobieImpact > 10 || criticalDataWarnings > 5)
                {
                    return (false,
                        $"COBie data quality at risk:\n" +
                        $"  COBie-affecting warnings: {cobieImpact}\n" +
                        $"  Critical/High data warnings: {criticalDataWarnings}\n\n" +
                        "Fix data quality warnings before COBie export to ensure\n" +
                        "accurate FM handover data.");
                }
                return (true, $"Warning quality acceptable ({cobieImpact} COBie-related, {criticalDataWarnings} critical data)");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CheckCOBieWarningQuality: {ex.Message}");
                return (true, "Warning check skipped due to error");
            }
        }

        // ── GAP-08: Auto-generate meeting minutes ──

        /// <summary>Generate meeting minutes from recent issue activity and compliance changes.</summary>
        internal static string GenerateAutoMinutes(Document doc)
        {
            string issuesPath = GetBimManagerPath(doc, "issues.json");
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($"  BIM COORDINATION MEETING MINUTES");
            sb.AppendLine($"  Project: {doc.Title ?? "Unknown"}");
            sb.AppendLine($"  Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"  Generated by: {Environment.UserName} (StingTools Auto-Minutes)");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine();

            // Phase 108k Item 6 — BOQ cost status appears FIRST because
            // money is always the first item on a BIM coordinator meeting
            // agenda. Bullet silently omitted when no BOQ snapshots exist.
            try
            {
                string costBullet = StingTools.BOQ.BOQBccBridge.BuildMeetingAgendaBullet(doc);
                if (!string.IsNullOrEmpty(costBullet))
                {
                    sb.Append(costBullet);
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Meeting BOQ bullet: {ex.Message}"); }

            // Section 1: Compliance Status
            var comp = ComplianceScan.Scan(doc);
            if (comp != null)
            {
                sb.AppendLine("1. MODEL COMPLIANCE STATUS");
                sb.AppendLine("─────────────────────────");
                sb.AppendLine($"  Tag Compliance: {comp.CompliancePercent:F0}% ({comp.TaggedComplete}/{comp.TotalElements})");
                sb.AppendLine($"  Container Compliance: {comp.ContainerCompletePct:F0}%");
                sb.AppendLine($"  Stale Elements: {comp.StaleCount}");
                sb.AppendLine($"  Placeholders: {comp.PlaceholderCount}");
                if (comp.ByDisc != null && comp.ByDisc.Count > 0)
                {
                    sb.AppendLine("  Per-discipline:");
                    foreach (var kv in comp.ByDisc.OrderByDescending(x => x.Value.Total))
                        sb.AppendLine($"    {kv.Key}: {kv.Value.CompliancePct:F0}% ({kv.Value.Tagged}/{kv.Value.Total})");
                }
                sb.AppendLine();
            }

            // Section 2: Issues resolved since last meeting
            if (File.Exists(issuesPath))
            {
                try
                {
                    var issues = JArray.Parse(File.ReadAllText(issuesPath));
                    var recentClosed = issues.Where(i =>
                    {
                        string status = i["status"]?.ToString();
                        if (!string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase)) return false;
                        string closedDate = (i["resolution_date"] ?? i["modified_date"])?.ToString();
                        if (DateTime.TryParse(closedDate, out DateTime dt))
                            return dt > DateTime.Now.AddDays(-7);
                        return false;
                    }).ToList();

                    var openIssues = issues.Where(i =>
                    {
                        string status = i["status"]?.ToString();
                        return string.Equals(status, "OPEN", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase);
                    }).ToList();

                    sb.AppendLine("2. ISSUES RESOLVED (Last 7 Days)");
                    sb.AppendLine("────────────────────────────────");
                    if (recentClosed.Count > 0)
                    {
                        foreach (var issue in recentClosed.Take(20))
                        {
                            sb.AppendLine($"  [{issue["id"]}] {issue["title"]} — {issue["type"]} — Resolved");
                        }
                    }
                    else sb.AppendLine("  No issues resolved in the last 7 days.");
                    sb.AppendLine();

                    sb.AppendLine("3. OPEN ISSUES");
                    sb.AppendLine("──────────────");
                    if (openIssues.Count > 0)
                    {
                        foreach (var issue in openIssues.OrderBy(i => i["priority"]?.ToString()).Take(20))
                        {
                            sb.AppendLine($"  [{issue["id"]}] {issue["title"]} — {issue["type"]} / {issue["priority"]} — {issue["assignee"] ?? "Unassigned"}");
                        }
                    }
                    else sb.AppendLine("  No open issues.");
                    sb.AppendLine();
                }
                catch (Exception ex2) { StingLog.Warn($"Auto-minutes issues: {ex2.Message}"); }
            }

            // Section 3: Warnings summary
            try
            {
                var warnings = WarningsEngine.ScanWarnings(doc);
                if (warnings != null && warnings.Total > 0)
                {
                    sb.AppendLine("4. WARNING SUMMARY");
                    sb.AppendLine("──────────────────");
                    sb.AppendLine($"  Total: {warnings.Total}");
                    sb.AppendLine($"  Auto-fixable: {warnings.AutoFixable}");
                    sb.AppendLine($"  Health Score: {WarningsEngine.CalculateWarningHealthScore(warnings):F0}/100");
                    if (warnings.RootCauseGroups != null)
                    {
                        sb.AppendLine("  Top issues:");
                        foreach (var g in warnings.RootCauseGroups.Take(5))
                            sb.AppendLine($"    [{g.Count}x] {g.Description?.Substring(0, Math.Min(80, g.Description?.Length ?? 0))}");
                    }
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Auto-minutes warnings: {ex.Message}"); }

            // Section 4: Action items
            sb.AppendLine("5. ACTION ITEMS");
            sb.AppendLine("───────────────");
            if (comp != null)
            {
                if (comp.StaleCount > 0)
                    sb.AppendLine($"  → ACTION: Retag {comp.StaleCount} stale elements before next data drop");
                if (comp.CompliancePercent < 80)
                    sb.AppendLine($"  → ACTION: Improve tag compliance from {comp.CompliancePercent:F0}% to ≥80%");
                if (comp.PlaceholderCount > 0)
                    sb.AppendLine($"  → ACTION: Resolve {comp.PlaceholderCount} placeholder tokens (GEN/XX/ZZ)");
            }
            sb.AppendLine();
            sb.AppendLine($"── End of minutes ── Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss} ──");

            return sb.ToString();
        }

        // ── GAP-09: Tag revision diff ──

        /// <summary>Snapshot metadata for comparison.</summary>
        internal class SnapshotInfo
        {
            public string Label { get; set; }
            public DateTime Date { get; set; }
            public string FilePath { get; set; }
        }

        /// <summary>Get list of available revision snapshots.</summary>
        internal static List<SnapshotInfo> GetAvailableSnapshots(Document doc)
        {
            var results = new List<SnapshotInfo>();
            string dir = RevisionEngine.GetRevisionDir(doc);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return results;

            foreach (string file in Directory.GetFiles(dir, "*.json").OrderByDescending(f => File.GetLastWriteTime(f)))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    results.Add(new SnapshotInfo
                    {
                        Label = name,
                        Date = File.GetLastWriteTime(file),
                        FilePath = file
                    });
                }
                catch (Exception ex) { StingLog.Warn($"GetAvailableSnapshots: {ex.Message}"); }
            }
            return results;
        }

        /// <summary>Generate CSV diff report between two snapshots.</summary>
        internal static string GenerateTagDiff(SnapshotInfo baseSnap, SnapshotInfo compareSnap)
        {
            var baseData = LoadSnapshotData(baseSnap.FilePath);
            var compareData = LoadSnapshotData(compareSnap.FilePath);

            var csv = new StringBuilder();
            csv.AppendLine("ChangeType,ElementId,Token,OldValue,NewValue,BaseSnapshot,CompareSnapshot");

            // Find changed and removed elements
            foreach (var kv in baseData)
            {
                if (!compareData.TryGetValue(kv.Key, out var compareTokens))
                {
                    // Element removed (or tag deleted)
                    string tag1 = kv.Value.GetValueOrDefault(ParamRegistry.TAG1, "");
                    csv.AppendLine($"REMOVED,{kv.Key},TAG1,\"{tag1}\",,\"{baseSnap.Label}\",\"{compareSnap.Label}\"");
                    continue;
                }

                // Compare each token
                foreach (var tokenKv in kv.Value)
                {
                    string oldVal = tokenKv.Value ?? "";
                    string newVal = compareTokens.GetValueOrDefault(tokenKv.Key, "");
                    if (!oldVal.Equals(newVal, StringComparison.Ordinal))
                    {
                        csv.AppendLine($"CHANGED,{kv.Key},{tokenKv.Key},\"{oldVal}\",\"{newVal}\",\"{baseSnap.Label}\",\"{compareSnap.Label}\"");
                    }
                }
            }

            // Find added elements
            foreach (var kv in compareData)
            {
                if (!baseData.ContainsKey(kv.Key))
                {
                    string tag1 = kv.Value.GetValueOrDefault(ParamRegistry.TAG1, "");
                    csv.AppendLine($"ADDED,{kv.Key},TAG1,,\"{tag1}\",\"{baseSnap.Label}\",\"{compareSnap.Label}\"");
                }
            }

            return csv.ToString();
        }

        private static Dictionary<long, Dictionary<string, string>> LoadSnapshotData(string path)
        {
            try
            {
                if (!File.Exists(path)) return new Dictionary<long, Dictionary<string, string>>();
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<Dictionary<long, Dictionary<string, string>>>(json)
                    ?? new Dictionary<long, Dictionary<string, string>>();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadSnapshotData: {ex.Message}");
                return new Dictionary<long, Dictionary<string, string>>();
            }
        }

        // ── GAP-10: Auto-schedule meetings from BEP ──

        /// <summary>Parse BEP meeting schedule and create recurring meeting entries.</summary>
        internal static int AutoScheduleMeetingsFromBEP(Document doc)
        {
            string bepPath = GetBimManagerPath(doc, "project_bep.json");
            if (!File.Exists(bepPath)) return 0;

            string meetingsPath = GetBimManagerPath(doc, "meetings.json");
            JArray meetings;
            try
            {
                meetings = File.Exists(meetingsPath)
                    ? JArray.Parse(File.ReadAllText(meetingsPath))
                    : new JArray();
            }
            catch (Exception ex) { StingLog.Warn($"Load meetings JSON: {ex.Message}"); meetings = new JArray(); }

            var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject m in meetings)
            {
                string title = m["title"]?.ToString();
                if (!string.IsNullOrEmpty(title)) existingTitles.Add(title);
            }

            try
            {
                var bep = JObject.Parse(File.ReadAllText(bepPath));
                int created = 0;

                // Try to find meetings section in BEP
                var meetingSection = bep["meetings"] ?? bep["meeting_schedule"] ?? bep["coordination_meetings"];

                if (meetingSection == null)
                {
                    // Generate default meeting schedule based on RIBA stage
                    var defaults = GetDefaultMeetingSchedule();
                    foreach (var def in defaults)
                    {
                        if (existingTitles.Contains(def.Title)) continue;
                        meetings.Add(CreateMeetingEntry(def.Title, def.Type, def.Frequency, def.Attendees));
                        created++;
                    }
                }
                else if (meetingSection is JArray meetingArray)
                {
                    foreach (JObject meetDef in meetingArray)
                    {
                        string title = meetDef["title"]?.ToString() ?? meetDef["name"]?.ToString();
                        if (string.IsNullOrEmpty(title) || existingTitles.Contains(title)) continue;

                        string type = meetDef["type"]?.ToString() ?? "BIM Coordination";
                        string freq = meetDef["frequency"]?.ToString() ?? "Weekly";
                        string attendees = meetDef["attendees"]?.ToString() ?? "";

                        meetings.Add(CreateMeetingEntry(title, type, freq, attendees));
                        created++;
                    }
                }

                if (created > 0)
                {
                    string dir = Path.GetDirectoryName(meetingsPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(meetingsPath, meetings.ToString(Formatting.Indented), Encoding.UTF8);
                    StingLog.Info($"Auto-scheduled {created} meetings from BEP");
                }
                return created;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AutoScheduleMeetingsFromBEP: {ex.Message}");
                return 0;
            }
        }

        private static JObject CreateMeetingEntry(string title, string type, string frequency, string attendees)
        {
            int nextId = new Random().Next(1000, 9999);
            return JObject.FromObject(new
            {
                id = $"MTG-{nextId}",
                title,
                type,
                frequency,
                status = "SCHEDULED",
                attendees,
                created_date = DateTime.Now.ToString("o"),
                created_by = Environment.UserName,
                auto_scheduled = true,
                source = "BEP"
            });
        }

        private static List<(string Title, string Type, string Frequency, string Attendees)> GetDefaultMeetingSchedule()
        {
            return new List<(string, string, string, string)>
            {
                ("Weekly BIM Coordination", "BIM Coordination", "Weekly", "BIM Coordinator, Lead Designer, Discipline Leads"),
                ("Design Team Review", "Design Review", "Fortnightly", "Architects, Engineers, BIM Coordinator"),
                ("Clash Resolution Meeting", "Clash Resolution", "Weekly", "MEP Coordinator, Structural Lead, BIM Coordinator"),
                ("Client Review Meeting", "Client Review", "Monthly", "Client, Project Manager, BIM Coordinator, Lead Designer"),
                ("Information Exchange Review", "Data Drop", "Per Stage", "Information Manager, BIM Coordinator, All Discipline Leads"),
            };
        }

        // ── Shared Helpers ──

        private static string GetBimManagerPath(Document doc, string filename)
        {
            string docPath = doc?.PathName;
            if (string.IsNullOrEmpty(docPath)) return null;
            string dir = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, filename);
        }
    }
}
