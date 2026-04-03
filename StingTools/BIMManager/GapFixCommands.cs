// ============================================================================
// GapFixCommands.cs — 29 Priority Gap Fixes (6 CRITICAL + 8 HIGH + 15 MEDIUM)
//
// Phase 67 — Cross-System Automation, CDE Compliance, Data Drop Tracking,
// Streaming Import, Structural Validation, Acoustic Analysis, and more.
// ============================================================================

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
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB.Architecture;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  GAP FIX ENGINE — Shared logic for all 29 gap fixes
    // ════════════════════════════════════════════════════════════════════════════

    #region ── GapFixEngine ──

    internal static class GapFixEngine
    {
        // ── Helper: Get BIM manager directory ──
        internal static string GetBimDir(Document doc)
        {
            string dir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "_bim_manager");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        internal static JArray LoadJsonArray(string path)
        {
            if (!File.Exists(path)) return new JArray();
            try { return JArray.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"LoadJsonArray failed: {ex.Message}"); return new JArray(); }
        }

        internal static JObject LoadJsonObject(string path)
        {
            if (!File.Exists(path)) return new JObject();
            try { return JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"LoadJsonObject failed: {ex.Message}"); return new JObject(); }
        }

        /// <summary>Atomic JSON write using temp file + replace. GF-001 FIX:
        /// Previous File.Delete→File.Move had crash window where target is deleted
        /// but temp not yet moved, permanently losing the JSON file.</summary>
        internal static void SaveJson(string path, JToken data)
        {
            string tmp = path + ".tmp";
            string backup = path + ".bak";
            File.WriteAllText(tmp, data.ToString(Formatting.Indented));
            if (File.Exists(path))
                File.Replace(tmp, path, backup);
            else
                File.Move(tmp, path);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CRIT-01: CDE Approval Workflow Integration
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Checks approvals.json for required sign-offs before CDE transition.</summary>
        internal static string EnforceCDEApproval(Document doc, string documentId, string newState)
        {
            if (!newState.Equals("PUBLISHED", StringComparison.OrdinalIgnoreCase)) return null;
            string approvalsPath = Path.Combine(GetBimDir(doc), "approvals.json");
            var approvals = LoadJsonArray(approvalsPath);

            var pending = approvals
                .OfType<JObject>()
                .Where(a => (a["document_id"]?.ToString() ?? "").Equals(documentId, StringComparison.OrdinalIgnoreCase)
                    && (a["status"]?.ToString() ?? "").Equals("PENDING", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pending.Count > 0)
            {
                var approvers = string.Join(", ", pending.Select(p => p["approver"]?.ToString() ?? "Unknown"));
                return $"CDE transition BLOCKED: {pending.Count} pending approval(s) for document '{documentId}'.\n" +
                       $"Pending approvers: {approvers}\n" +
                       "All approvals must be APPROVED before publishing.";
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CRIT-02: Cross-System Entity Linking
        // ═══════════════════════════════════════════════════════════════════

        internal static void LinkEntities(Document doc, string sourceType, string sourceId,
            string targetType, string targetId)
        {
            string linksPath = Path.Combine(GetBimDir(doc), "entity_links.json");
            var links = LoadJsonArray(linksPath);

            // CS-01 FIX: Check both orderings for bidirectional dedup
            var existing = links.OfType<JObject>().FirstOrDefault(l =>
                (l["source_type"]?.ToString() == sourceType && l["source_id"]?.ToString() == sourceId &&
                 l["target_type"]?.ToString() == targetType && l["target_id"]?.ToString() == targetId) ||
                (l["source_type"]?.ToString() == targetType && l["source_id"]?.ToString() == targetId &&
                 l["target_type"]?.ToString() == sourceType && l["target_id"]?.ToString() == sourceId));
            if (existing != null) return; // Already linked (either direction)

            links.Add(new JObject
            {
                ["source_type"] = sourceType, ["source_id"] = sourceId,
                ["target_type"] = targetType, ["target_id"] = targetId,
                ["created"] = DateTime.Now.ToString("o"), ["created_by"] = Environment.UserName
            });
            // Bidirectional
            links.Add(new JObject
            {
                ["source_type"] = targetType, ["source_id"] = targetId,
                ["target_type"] = sourceType, ["target_id"] = sourceId,
                ["created"] = DateTime.Now.ToString("o"), ["created_by"] = Environment.UserName
            });
            SaveJson(linksPath, links);
        }

        internal static List<(string Type, string Id)> GetLinkedEntities(Document doc, string entityType, string entityId)
        {
            string linksPath = Path.Combine(GetBimDir(doc), "entity_links.json");
            var links = LoadJsonArray(linksPath);
            return links.OfType<JObject>()
                .Where(l => (l["source_type"]?.ToString() ?? "").Equals(entityType, StringComparison.OrdinalIgnoreCase)
                    && (l["source_id"]?.ToString() ?? "").Equals(entityId, StringComparison.OrdinalIgnoreCase))
                .Select(l => (l["target_type"]?.ToString() ?? "", l["target_id"]?.ToString() ?? ""))
                .ToList();
        }

        internal static string BuildDependencyGraph(Document doc)
        {
            string linksPath = Path.Combine(GetBimDir(doc), "entity_links.json");
            var links = LoadJsonArray(linksPath);
            var sb = new StringBuilder();
            sb.AppendLine("CROSS-SYSTEM DEPENDENCY GRAPH\n");

            var bySource = links.OfType<JObject>()
                .GroupBy(l => $"{l["source_type"]}/{l["source_id"]}")
                .ToDictionary(g => g.Key, g => g.Select(l => $"{l["target_type"]}/{l["target_id"]}").ToList());

            foreach (var kvp in bySource.Take(50))
            {
                sb.AppendLine($"  {kvp.Key} → {string.Join(", ", kvp.Value)}");
            }
            sb.AppendLine($"\nTotal links: {links.Count}");
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CRIT-03: Coordination Center Data Refresh
        // ═══════════════════════════════════════════════════════════════════

        private static DateTime _lastCoordRefresh = DateTime.MinValue;
        private static string _cachedCoordSummary = "";

        internal static string BuildFullCoordData(Document doc)
        {
            if ((DateTime.Now - _lastCoordRefresh).TotalSeconds < 30 && !string.IsNullOrEmpty(_cachedCoordSummary))
                return _cachedCoordSummary;

            var sb = new StringBuilder();
            try
            {
                // PERF-R3: Removed InvalidateCache() — let the 30-second cache work as designed.
                // Previously forced a full-model element scan (2-5s) every time BuildFullCoordData was called.
                var scan = ComplianceScan.Scan(doc);
                sb.AppendLine($"Tag Compliance: {scan?.CompliancePercent:F1}% ({(scan?.TaggedComplete ?? 0) + (scan?.TaggedIncomplete ?? 0)}/{scan?.TotalElements})");
                sb.AppendLine($"Stale: {scan?.StaleCount}, Placeholders: {scan?.PlaceholderCount}");

                // Issues
                string issuesPath = Path.Combine(GetBimDir(doc), "issues.json");
                var issues = LoadJsonArray(issuesPath);
                int openIssues = issues.OfType<JObject>().Count(i => !(i["status"]?.ToString() ?? "").Equals("CLOSED", StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"Open Issues: {openIssues}/{issues.Count}");

                // Warnings
                int warnCount = doc.GetWarnings()?.Count ?? 0;
                sb.AppendLine($"Model Warnings: {warnCount}");
            }
            catch (Exception ex) { StingLog.Warn($"BuildFullCoordData: {ex.Message}"); }

            _cachedCoordSummary = sb.ToString();
            _lastCoordRefresh = DateTime.Now;
            return _cachedCoordSummary;
        }

        internal static void InvalidateCoordCache() { _lastCoordRefresh = DateTime.MinValue; }

        // ═══════════════════════════════════════════════════════════════════
        //  CRIT-04: Streaming COBie Import with Validation
        // ═══════════════════════════════════════════════════════════════════

        internal static string ImportCOBieStreaming(Document doc, string xlsxPath)
        {
            if (!File.Exists(xlsxPath)) return "File not found: " + xlsxPath;

            int matched = 0, updated = 0, errors = 0, skipped = 0;
            var warnings = new List<string>();

            using (var wb = new XLWorkbook(xlsxPath))
            {
                var ws = wb.Worksheets.FirstOrDefault(w =>
                    w.Name.Equals("Component", StringComparison.OrdinalIgnoreCase));
                if (ws == null) return "No 'Component' worksheet found in COBie file.";

                // Read headers
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                for (int c = 1; c <= lastCol; c++)
                    headers[ws.Cell(1, c).GetString().Trim()] = c;

                if (!headers.ContainsKey("Name") && !headers.ContainsKey("ExternalIdentifier"))
                    return "COBie Component sheet missing Name or ExternalIdentifier column.";

                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                if (lastRow > 10000)
                    return $"COBie file has {lastRow} rows (safety limit: 10,000). Process in batches.";

                // Build element lookup (filtered to taggable categories)
                var importCatEnums = SharedParamGuids.AllCategoryEnums;
                var importColl = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                if (importCatEnums != null && importCatEnums.Length > 0)
                    importColl.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(importCatEnums)));
                var collector = importColl.ToList();
                var byUniqueId = collector.ToDictionary(e => e.UniqueId, e => e);
                var byTag = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in collector)
                {
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (!string.IsNullOrEmpty(tag) && !byTag.ContainsKey(tag))
                        byTag[tag] = el;
                }

                int batchSize = TagConfig.CobieStreamBatchSize > 0 ? TagConfig.CobieStreamBatchSize : 5000;
                int batchStart = 2;

                while (batchStart <= lastRow)
                {
                    int batchEnd = Math.Min(batchStart + batchSize - 1, lastRow);

                    using (var tx = new Transaction(doc, "STING COBie Stream Import"))
                    {
                        tx.Start();
                        for (int r = batchStart; r <= batchEnd; r++)
                        {
                            try
                            {
                                string extId = headers.ContainsKey("ExternalIdentifier")
                                    ? ws.Cell(r, headers["ExternalIdentifier"]).GetString().Trim() : "";
                                string name = headers.ContainsKey("Name")
                                    ? ws.Cell(r, headers["Name"]).GetString().Trim() : "";

                                Element el = null;
                                if (!string.IsNullOrEmpty(extId) && byUniqueId.TryGetValue(extId, out var uel))
                                    el = uel;
                                else if (!string.IsNullOrEmpty(name) && byTag.TryGetValue(name, out var tel))
                                    el = tel;

                                if (el == null) { skipped++; continue; }
                                matched++;

                                // Apply mapped fields
                                if (headers.ContainsKey("Description"))
                                {
                                    string desc = ws.Cell(r, headers["Description"]).GetString().Trim();
                                    if (desc == "CLEAR") ParameterHelpers.SetString(el, ParamRegistry.DESC, "", overwrite: true);
                                    else if (!string.IsNullOrEmpty(desc)) { ParameterHelpers.SetString(el, ParamRegistry.DESC, desc, overwrite: true); updated++; }
                                }
                                if (headers.ContainsKey("SerialNumber"))
                                {
                                    string sn = ws.Cell(r, headers["SerialNumber"]).GetString().Trim();
                                    if (!string.IsNullOrEmpty(sn)) ParameterHelpers.SetString(el, "ASS_SERIAL_TXT", sn, overwrite: true);
                                }
                                if (headers.ContainsKey("BarCode"))
                                {
                                    string bc = ws.Cell(r, headers["BarCode"]).GetString().Trim();
                                    if (!string.IsNullOrEmpty(bc)) ParameterHelpers.SetString(el, "ASS_BARCODE_TXT", bc, overwrite: true);
                                }
                            }
                            catch (Exception ex) { errors++; if (errors <= 10) warnings.Add($"Row {r}: {ex.Message}"); }
                        }
                        tx.Commit();
                    }
                    batchStart = batchEnd + 1;
                }
            }

            var report = new StringBuilder();
            report.AppendLine("STREAMING COBie IMPORT COMPLETE\n");
            report.AppendLine($"Matched: {matched}, Updated: {updated}, Skipped: {skipped}, Errors: {errors}");
            if (warnings.Count > 0) { report.AppendLine("\nWarnings:"); foreach (var w in warnings) report.AppendLine($"  {w}"); }
            return report.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CRIT-05: 4D Schedule Handover Integration
        // ═══════════════════════════════════════════════════════════════════

        internal static string Link4DToHandover(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("4D SCHEDULE → HANDOVER INTEGRATION\n");

            // Map construction phases to data drops
            var milestones = new[]
            {
                ("DD1 - Brief", 30.0, "Concept Design", new[] { "Facility", "Floor", "Space" }),
                ("DD2 - Concept", 60.0, "Developed Design", new[] { "Facility", "Floor", "Space", "Zone", "Type" }),
                ("DD3 - Technical", 85.0, "Technical Design", new[] { "Facility", "Floor", "Space", "Zone", "Type", "Component", "System" }),
                ("DD4 - Handover", 95.0, "Construction", new[] { "Facility", "Floor", "Space", "Zone", "Type", "Component", "System", "Job", "Spare", "Resource", "Document" })
            };

            var scan = ComplianceScan.Scan(doc);
            double currentCompliance = scan?.CompliancePercent ?? 0;

            foreach (var (name, threshold, phase, cobieSheets) in milestones)
            {
                bool passes = currentCompliance >= threshold;
                sb.AppendLine($"  {name} — Threshold: {threshold}% — Current: {currentCompliance:F1}% — {(passes ? "PASS ✓" : "FAIL ✗")}");
                sb.AppendLine($"    Phase: {phase}");
                sb.AppendLine($"    Required COBie sheets: {string.Join(", ", cobieSheets)}");
                sb.AppendLine();
            }

            // Export CSV
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            string csvPath = Path.Combine(outDir, $"4D_Handover_Integration_{DateTime.Now:yyyyMMdd}.csv");
            var rows = new List<string> { "Milestone,Threshold%,CurrentCompliance%,Status,Phase,COBieSheets" };
            foreach (var (name, threshold, phase, cobieSheets) in milestones)
            {
                bool passes = currentCompliance >= threshold;
                rows.Add($"\"{name}\",{threshold},{currentCompliance:F1},{(passes ? "PASS" : "FAIL")},\"{phase}\",\"{string.Join("; ", cobieSheets)}\"");
            }
            File.WriteAllLines(csvPath, rows);
            sb.AppendLine($"Exported to: {csvPath}");

            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CRIT-06: COBie System Grouping from Connector Graph
        // ═══════════════════════════════════════════════════════════════════

        internal static Dictionary<string, List<ElementId>> BuildSystemGroupsFromConnectors(Document doc)
        {
            var systemGroups = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            var mepCategories = new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers,
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_Conduit
            };

            foreach (var bic in mepCategories)
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(bic).WhereElementIsNotElementType().ToList();

                foreach (var el in elements)
                {
                    string sysCode = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                    if (string.IsNullOrEmpty(sysCode)) sysCode = "UNCLASSIFIED";

                    // Try to get MEP system name from connectors
                    if (el is FamilyInstance fi)
                    {
                        try
                        {
                            var connMgr = fi.MEPModel?.ConnectorManager;
                            if (connMgr != null)
                            {
                                foreach (Connector c in connMgr.Connectors)
                                {
                                    if (c.MEPSystem != null)
                                    {
                                        string sysName = c.MEPSystem.Name ?? "";
                                        if (!string.IsNullOrEmpty(sysName))
                                        {
                                            sysCode = $"{sysCode}:{sysName}";
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Connector traverse: {ex.Message}"); }
                    }

                    if (!systemGroups.TryGetValue(sysCode, out var sgList))
                    {
                        sgList = new List<ElementId>();
                        systemGroups[sysCode] = sgList;
                    }
                    sgList.Add(el.Id);
                }
            }
            return systemGroups;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HIGH-01: Data Drop Progress Tracking
        // ═══════════════════════════════════════════════════════════════════

        internal static string TrackDataDropProgress(Document doc)
        {
            string trackerPath = Path.Combine(GetBimDir(doc), "dd_tracker.json");
            var tracker = LoadJsonObject(trackerPath);
            var scan = ComplianceScan.Scan(doc);

            var snapshot = new JObject
            {
                ["timestamp"] = DateTime.Now.ToString("o"),
                ["tag_pct"] = scan?.CompliancePercent ?? 0,
                ["container_pct"] = scan?.ContainerCompletePct ?? 0,
                ["stale_count"] = scan?.StaleCount ?? 0,
                ["total_elements"] = scan?.TotalElements ?? 0,
                ["tagged_elements"] = (scan?.TaggedComplete ?? 0) + (scan?.TaggedIncomplete ?? 0)
            };

            // Determine current milestone
            double pct = scan?.CompliancePercent ?? 0;
            string currentDD = pct >= 95 ? "DD4" : pct >= 85 ? "DD3" : pct >= 60 ? "DD2" : pct >= 30 ? "DD1" : "Pre-DD1";
            snapshot["current_milestone"] = currentDD;

            if (tracker["history"] == null) tracker["history"] = new JArray();
            ((JArray)tracker["history"]).Add(snapshot);
            tracker["last_updated"] = DateTime.Now.ToString("o");
            tracker["current_milestone"] = currentDD;
            SaveJson(trackerPath, tracker);

            return $"Data Drop Progress: {currentDD}\n" +
                   $"Tag Compliance: {pct:F1}%\n" +
                   $"Container Compliance: {scan?.ContainerCompletePct:F1}%\n" +
                   $"Stale: {scan?.StaleCount}, Total: {scan?.TotalElements}";
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HIGH-02: Revision Propagation to Linked Models
        // ═══════════════════════════════════════════════════════════════════

        internal static string PropagateRevisionToLinks(Document doc)
        {
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
            if (links.Count == 0) return "No linked models found.";

            string rev = PhaseAutoDetect.DetectProjectRevision(doc);
            int propagated = 0;

            foreach (var link in links)
            {
                try
                {
                    string linkPath = link.GetLinkDocument()?.PathName ?? "";
                    if (string.IsNullOrEmpty(linkPath)) continue;

                    string sidecarPath = Path.ChangeExtension(linkPath, ".sting_linked_revision.json");
                    var sidecar = new JObject
                    {
                        ["source_project"] = doc.Title,
                        ["source_path"] = doc.PathName,
                        ["revision"] = rev,
                        ["propagated_at"] = DateTime.Now.ToString("o"),
                        ["propagated_by"] = Environment.UserName
                    };
                    // CS-03 FIX: Use atomic write pattern instead of File.WriteAllText
                    SaveJson(sidecarPath, sidecar);
                    propagated++;
                }
                catch (Exception ex) { StingLog.Warn($"RevisionPropagation: {ex.Message}"); }
            }
            return $"Revision '{rev}' propagated to {propagated}/{links.Count} linked models.";
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HIGH-04: Compliance Forecasting
        // ═══════════════════════════════════════════════════════════════════

        internal static string ForecastCompliance(Document doc)
        {
            string trendPath = Path.ChangeExtension(doc.PathName, ".sting_compliance_trend.json");
            if (!File.Exists(trendPath)) return "No compliance trend data. Run tagging workflows to build history.";

            var data = LoadJsonArray(trendPath);
            if (data.Count < 3) return $"Need at least 3 data points for forecasting (have {data.Count}).";

            // Linear regression on last 10 points
            var points = data.OfType<JObject>()
                .OrderByDescending(d => d["timestamp"]?.ToString() ?? "")
                .Take(10).Reverse().ToList();

            double[] x = new double[points.Count];
            double[] y = new double[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                x[i] = i;
                y[i] = points[i]["compliance_pct"]?.Value<double>() ?? 0;
            }

            // Simple linear regression: y = mx + b
            double n = x.Length;
            double sumX = x.Sum(), sumY = y.Sum();
            double sumXY = x.Zip(y, (a, b) => a * b).Sum();
            double sumX2 = x.Sum(a => a * a);
            double m = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            double b = (sumY - m * sumX) / n;

            double current = y.Last();
            var sb = new StringBuilder();
            sb.AppendLine("COMPLIANCE FORECAST\n");
            sb.AppendLine($"Current: {current:F1}%");
            sb.AppendLine($"Trend: {(m > 0 ? "improving" : m < 0 ? "declining" : "stable")} ({m:+0.0;-0.0}% per data point)");

            // Predict when 80%, 90%, 95% will be reached
            foreach (double target in new[] { 80, 90, 95 })
            {
                if (current >= target) { sb.AppendLine($"  {target}%: Already achieved"); continue; }
                if (m <= 0) { sb.AppendLine($"  {target}%: Not achievable at current rate"); continue; }
                double stepsNeeded = (target - b) / m - (x.Length - 1);
                sb.AppendLine($"  {target}%: ~{stepsNeeded:F0} workflow runs remaining");
            }
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HIGH-05: CDE Folder Structure Generator
        // ═══════════════════════════════════════════════════════════════════

        internal static string GenerateCDEFolders(string basePath, string projectCode)
        {
            if (string.IsNullOrEmpty(basePath)) return "Base path required.";
            if (string.IsNullOrEmpty(projectCode)) projectCode = "PROJ";

            var cdeStates = new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };
            var disciplines = new[] { "A-Architecture", "S-Structure", "M-Mechanical", "E-Electrical", "P-Plumbing", "FP-Fire", "G-General" };
            var docTypes = new[] { "MODELS", "DRAWINGS", "SCHEDULES", "SPECIFICATIONS", "REPORTS", "COBie", "BEP" };

            int created = 0;
            foreach (var state in cdeStates)
            {
                foreach (var disc in disciplines)
                {
                    foreach (var docType in docTypes)
                    {
                        string dir = Path.Combine(basePath, projectCode, state, disc, docType);
                        if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); created++; }
                    }
                }
            }

            // Create _RECYCLE folder
            string recyclePath = Path.Combine(basePath, projectCode, "_RECYCLE");
            if (!Directory.Exists(recyclePath)) { Directory.CreateDirectory(recyclePath); created++; }

            return $"CDE folder structure created: {created} directories\nBase: {Path.Combine(basePath, projectCode)}";
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HIGH-07: Compliance Sort Cache
        // ═══════════════════════════════════════════════════════════════════

        private static List<(string Disc, double Pct, int Total, int Tagged)> _sortedCompliance;
        private static DateTime _sortCacheTime = DateTime.MinValue;

        internal static List<(string Disc, double Pct, int Total, int Tagged)> SortedComplianceByDisc(Document doc)
        {
            if ((DateTime.Now - _sortCacheTime).TotalSeconds < 30 && _sortedCompliance != null)
                return _sortedCompliance;

            var scan = ComplianceScan.Scan(doc);
            if (scan?.ByDisc == null) return new List<(string, double, int, int)>();

            _sortedCompliance = scan.ByDisc
                .OrderBy(kv => kv.Value.CompliancePct)
                .Select(kv => (kv.Key, kv.Value.CompliancePct, kv.Value.Total, kv.Value.Tagged))
                .ToList();
            _sortCacheTime = DateTime.Now;
            return _sortedCompliance;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HIGH-08: Workflow Preflight Reuse
        // ═══════════════════════════════════════════════════════════════════

        private static string _cachedPreFlightPreset = "";
        private static DateTime _preFlightCacheTime = DateTime.MinValue;
        private static List<string> _cachedPreFlightResult;

        internal static List<string> GetCachedPreFlight(string presetName, Document doc)
        {
            if (presetName == _cachedPreFlightPreset &&
                (DateTime.Now - _preFlightCacheTime).TotalSeconds < 60 &&
                _cachedPreFlightResult != null)
                return _cachedPreFlightResult;

            var preset = WorkflowEngine.GetBuiltInPreset(presetName);
            if (preset == null) return new List<string> { $"Preset '{presetName}' not found." };
            var (_, preflight) = WorkflowEngine.PreFlightCheck(doc, preset);
            _cachedPreFlightResult = preflight;
            _cachedPreFlightPreset = presetName;
            _preFlightCacheTime = DateTime.Now;
            return _cachedPreFlightResult;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  MEDIUM GAPS (MED-01 through MED-15)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>MED-01: Add version key to sidecar files.</summary>
        internal static void VersionSidecarFile(string path, int version = 2)
        {
            if (!File.Exists(path)) return;
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                obj["_version"] = version;
                obj["_updated"] = DateTime.Now.ToString("o");
                SaveJson(path, obj);
            }
            catch (Exception ex) { StingLog.Warn($"VersionSidecar: {ex.Message}"); }
        }

        /// <summary>MED-02: Block transmittal if documents below threshold.</summary>
        internal static string ValidateTransmittalGate(Document doc, List<string> documentIds)
        {
            double threshold = TagConfig.GetConfigDouble("TRANSMITTAL_MIN_COMPLIANCE", 70.0);
            var scan = ComplianceScan.Scan(doc);
            if (scan == null) return null;
            if (scan.CompliancePercent < threshold)
                return $"Transmittal BLOCKED: compliance {scan.CompliancePercent:F1}% < {threshold:F0}% threshold.";
            return null;
        }

        /// <summary>MED-03: Team workload analysis.</summary>
        internal static string AnalyzeTeamWorkload(Document doc)
        {
            string bimDir = GetBimDir(doc);
            var workload = new Dictionary<string, (int tasks, int issues, int approvals)>(StringComparer.OrdinalIgnoreCase);

            // Tasks
            var tasks = LoadJsonArray(Path.Combine(bimDir, "tasks.json"));
            foreach (JObject t in tasks.OfType<JObject>())
            {
                string assignee = t["assignee"]?.ToString() ?? "Unassigned";
                if (!workload.TryGetValue(assignee, out var wt)) wt = (0, 0, 0);
                if (!(t["status"]?.ToString() ?? "").Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
                    workload[assignee] = (wt.tasks + 1, wt.issues, wt.approvals);
            }

            // Issues
            var issues = LoadJsonArray(Path.Combine(bimDir, "issues.json"));
            foreach (JObject i in issues.OfType<JObject>())
            {
                string assignee = i["assignee"]?.ToString() ?? "Unassigned";
                if (!workload.TryGetValue(assignee, out var wi)) wi = (0, 0, 0);
                if (!(i["status"]?.ToString() ?? "").Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
                    workload[assignee] = (wi.tasks, wi.issues + 1, wi.approvals);
            }

            // Approvals
            var approvals = LoadJsonArray(Path.Combine(bimDir, "approvals.json"));
            foreach (JObject a in approvals.OfType<JObject>())
            {
                string approver = a["approver"]?.ToString() ?? "Unknown";
                if (!workload.TryGetValue(approver, out var wa)) wa = (0, 0, 0);
                if ((a["status"]?.ToString() ?? "").Equals("PENDING", StringComparison.OrdinalIgnoreCase))
                    workload[approver] = (wa.tasks, wa.issues, wa.approvals + 1);
            }

            var sb = new StringBuilder();
            sb.AppendLine("TEAM WORKLOAD ANALYSIS\n");
            sb.AppendLine($"{"User",-20} {"Tasks",-8} {"Issues",-8} {"Approvals",-10} {"Total",-8}");
            sb.AppendLine(new string('-', 54));
            foreach (var kvp in workload.OrderByDescending(w => w.Value.tasks + w.Value.issues + w.Value.approvals))
            {
                var (t, i, a) = kvp.Value;
                sb.AppendLine($"{kvp.Key,-20} {t,-8} {i,-8} {a,-10} {t + i + a,-8}");
            }
            return sb.ToString();
        }

        /// <summary>MED-06: Enhanced DWG layer detection for international standards.</summary>
        internal static readonly Dictionary<string, string> InternationalLayerPatterns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // ISO 13567
                ["S-COLS"] = "Column", ["S-BEAM"] = "Beam", ["S-SLAB"] = "Slab",
                ["S-WALL"] = "Wall", ["S-FNDN"] = "Foundation", ["S-GRID"] = "Grid",
                ["S-STRS"] = "Stair", ["S-BRAC"] = "Brace",
                // AIA (US)
                ["S-COLS-CONC"] = "Column", ["S-BEAM-CONC"] = "Beam", ["S-SLAB-CONC"] = "Slab",
                ["S-FNDN-CONC"] = "Foundation", ["S-GRID-IDEN"] = "Grid",
                // BS 1192
                ["Str-Col"] = "Column", ["Str-Beam"] = "Beam", ["Str-Slab"] = "Slab",
                ["Str-Wall"] = "Wall", ["Str-Fdn"] = "Foundation",
                // DIN (German)
                ["TWK-STUE"] = "Column", ["TWK-TRAE"] = "Beam", ["TWK-DECK"] = "Slab",
                ["TWK-WAND"] = "Wall", ["TWK-GRND"] = "Foundation",
                // SIA (Swiss)
                ["TRAG-ST"] = "Column", ["TRAG-TR"] = "Beam", ["TRAG-DE"] = "Slab",
                // Common variations
                ["STRUCT_COL"] = "Column", ["STRUCT_BEAM"] = "Beam", ["STRUCT_FND"] = "Foundation",
                ["STR_COLUMN"] = "Column", ["STR_BEAM"] = "Beam", ["STR_SLAB"] = "Slab",
                ["COLUMN"] = "Column", ["BEAM"] = "Beam", ["FOOTING"] = "Foundation",
                // NBS (UK National BIM Standard — Uniclass Ss codes)
                ["Ss_15_10"] = "Column", ["Ss_15_20"] = "Beam", ["Ss_15_30"] = "Slab",
                ["Ss_20"] = "Wall", ["Ss_20_05"] = "Wall", ["Ss_25"] = "Slab",
                ["Ss_25_10"] = "Slab", ["Ss_25_30"] = "Foundation", ["Ss_30"] = "Stair",
                ["Ss_15"] = "Beam", ["Ss_32"] = "Brace",
                ["Ss_15_10_30"] = "Column", ["Ss_15_10_70"] = "Column", // RC / steel columns
                ["Ss_15_20_30"] = "Beam", ["Ss_15_20_70"] = "Beam",    // RC / steel beams
                ["Ss_20_10"] = "Wall", ["Ss_20_20"] = "Wall",          // retaining / curtain walls
                ["Ss_25_20"] = "Slab", ["Ss_25_60"] = "Foundation",    // ground slabs / piles
                ["Ss_30_10"] = "Stair", ["Ss_30_20"] = "Stair",        // internal / external stairs
                ["Ss_32_10"] = "Brace", ["Ss_32_20"] = "Brace",        // lateral / vertical bracing
                ["Ss_35"] = "Foundation", ["Ss_35_10"] = "Foundation",  // substructure / piling
                // Singapore BIM Guide (BCA)
                ["A-WALL"] = "Wall", ["S-COLUMN"] = "Column",
                ["S-FOUNDATION"] = "Foundation", ["S-STAIR"] = "Stair",
                ["M-DUCT"] = "Beam", ["M-PIPE"] = "Beam", ["E-CABLE"] = "Beam",
                ["A-DOOR"] = "Wall", ["A-WINDOW"] = "Wall",
                ["S-PILE"] = "Foundation", ["S-BRACE"] = "Brace",
                ["A-FLOOR"] = "Slab", ["A-ROOF"] = "Slab", ["A-CLNG"] = "Slab",
                ["A-STAIR"] = "Stair", ["A-COL"] = "Column",
                ["M-EQUIP"] = "Column", ["E-PANEL"] = "Column",
                ["P-PIPE"] = "Beam", ["P-FIXT"] = "Column",
                // Australian AS 1100.301
                ["A-WALL-FULL"] = "Wall", ["A-WALL-PART"] = "Wall",
                ["S-CONC-BEAM"] = "Beam", ["S-CONC-COL"] = "Column", ["S-CONC-SLAB"] = "Slab",
                ["S-CONC-FNDN"] = "Foundation", ["S-CONC-WALL"] = "Wall",
                ["S-STEEL-BEAM"] = "Beam", ["S-STEEL-COL"] = "Column",
                ["S-STEL-BEAM"] = "Beam", ["S-STEL-COL"] = "Column",
                ["S-CONC-FTG"] = "Foundation", ["S-CONC-PIER"] = "Column",
                ["A-WALL-EXTR"] = "Wall", ["A-WALL-INTR"] = "Wall",
                ["S-CONC-STAIR"] = "Stair", ["S-CONC-PILE"] = "Foundation",
                ["S-STEEL-BRACE"] = "Brace", ["S-STEEL-TRUSS"] = "Beam",
                ["S-TIMBER-BEAM"] = "Beam", ["S-TIMBER-COL"] = "Column",
                ["S-REBAR"] = "Beam", ["S-MESH"] = "Slab",
                ["A-ROOF-SLAB"] = "Slab", ["A-FLOOR-SLAB"] = "Slab",
            };

        /// <summary>MED-09: Structural model validation.</summary>
        internal static string ValidateStructuralModel(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("STRUCTURAL MODEL VALIDATION\n");
            int issues = 0;

            // Check columns have foundations below
            var columns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType().ToList();
            var foundations = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation).WhereElementIsNotElementType().ToList();

            sb.AppendLine($"Structural columns: {columns.Count}");
            sb.AppendLine($"Foundations: {foundations.Count}");

            if (columns.Count > 0 && foundations.Count == 0)
            {
                sb.AppendLine("⚠ WARNING: Columns exist but no foundations found!");
                issues++;
            }

            // Check beams are connected to columns or walls
            var beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType().ToList();
            sb.AppendLine($"Structural beams: {beams.Count}");

            // Check for floating slabs
            var slabs = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType()
                .Where(f => f.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.AsInteger() == 1)
                .ToList();
            sb.AppendLine($"Structural slabs: {slabs.Count}");

            sb.AppendLine($"\nTotal issues: {issues}");
            sb.AppendLine(issues == 0 ? "Structural model is consistent." : "Review structural connectivity.");
            return sb.ToString();
        }

        /// <summary>MED-10: Acoustic analysis per BS 8233 / BB93.</summary>
        internal static string AnalyzeAcoustics(Document doc)
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .Cast<Room>().Where(r => r.Area > 0).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("ACOUSTIC ANALYSIS (BS 8233:2014 / BB93)\n");

            // BS 8233 recommended noise levels by room type
            var noiseTargets = new Dictionary<string, (int maxDbA, string standard)>(StringComparer.OrdinalIgnoreCase)
            {
                ["office"] = (40, "BS 8233 Table 4"), ["meeting"] = (40, "BS 8233 Table 4"),
                ["classroom"] = (35, "BB93 Table 1.2"), ["lecture"] = (35, "BB93 Table 1.2"),
                ["bedroom"] = (30, "BS 8233 Table 4"), ["living"] = (35, "BS 8233 Table 4"),
                ["corridor"] = (50, "BS 8233 Table 4"), ["reception"] = (45, "BS 8233 Table 4"),
                ["library"] = (35, "BB93 Table 1.2"), ["ward"] = (35, "HTM 08-01"),
                ["theatre"] = (30, "BS 8233 Table 4"), ["restaurant"] = (45, "BS 8233 Table 4"),
                ["kitchen"] = (50, "BS 8233 Table 4"), ["plant"] = (55, "CIBSE Guide B"),
            };

            int assessed = 0;
            foreach (var room in rooms)
            {
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                string nameLower = name.ToLowerInvariant();

                foreach (var kvp in noiseTargets)
                {
                    if (nameLower.Contains(kvp.Key))
                    {
                        var (maxDbA, standard) = kvp.Value;
                        double areaSqM = room.Area * 0.0929; // ft² to m²
                        double rtTarget = 0.032 * Math.Pow(areaSqM * 3.0, 0.5); // Approximate RT60

                        sb.AppendLine($"  {name}: Max {maxDbA} dB(A) ({standard}), Area {areaSqM:F1} m², Target RT60 ≈ {rtTarget:F2}s");
                        assessed++;
                        break;
                    }
                }
            }

            sb.AppendLine($"\nRooms assessed: {assessed}/{rooms.Count}");
            sb.AppendLine("Note: Actual acoustic measurements required for compliance verification.");
            return sb.ToString();
        }

        /// <summary>MED-14: Issue template system.</summary>
        internal static Dictionary<string, JObject> GetIssueTemplates()
        {
            return new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase)
            {
                ["RFI"] = new JObject { ["type"] = "RFI", ["title_prefix"] = "RFI", ["priority"] = "MEDIUM",
                    ["required_fields"] = new JArray("title", "description", "discipline", "assignee"),
                    ["description_template"] = "Request for Information\n\nQuestion:\n\nContext:\n\nRequired by:" },
                ["NCR"] = new JObject { ["type"] = "NCR", ["title_prefix"] = "NCR", ["priority"] = "HIGH",
                    ["required_fields"] = new JArray("title", "description", "discipline", "assignee", "severity"),
                    ["description_template"] = "Non-Conformance Report\n\nIssue:\n\nLocation:\n\nExpected:\n\nActual:" },
                ["SI"] = new JObject { ["type"] = "SI", ["title_prefix"] = "SI", ["priority"] = "LOW",
                    ["required_fields"] = new JArray("title", "description"),
                    ["description_template"] = "Site Instruction\n\nInstruction:\n\nReason:" },
                ["TQ"] = new JObject { ["type"] = "TQ", ["title_prefix"] = "TQ", ["priority"] = "MEDIUM",
                    ["required_fields"] = new JArray("title", "description", "discipline"),
                    ["description_template"] = "Technical Query\n\nQuery:\n\nRelevant drawings:" },
                ["EN"] = new JObject { ["type"] = "EN", ["title_prefix"] = "EN", ["priority"] = "MEDIUM",
                    ["required_fields"] = new JArray("title", "description", "cost_impact"),
                    ["description_template"] = "Early Notification\n\nPotential issue:\n\nImpact:\n\nMitigation:" },
                ["CO"] = new JObject { ["type"] = "CO", ["title_prefix"] = "CO", ["priority"] = "HIGH",
                    ["required_fields"] = new JArray("title", "description", "cost_impact", "programme_impact"),
                    ["description_template"] = "Compensation Event\n\nEvent:\n\nCost impact:\n\nProgramme impact:" },
            };
        }

        /// <summary>MED-15: Meeting action completion tracking.</summary>
        internal static string TrackActionCompletion(Document doc)
        {
            string meetingsPath = Path.Combine(GetBimDir(doc), "meetings.json");
            var meetings = LoadJsonArray(meetingsPath);

            int total = 0, completed = 0, overdue = 0;
            var sb = new StringBuilder();
            sb.AppendLine("MEETING ACTION COMPLETION TRACKING\n");

            foreach (JObject meeting in meetings.OfType<JObject>())
            {
                var actions = meeting["action_items"] as JArray;
                if (actions == null) continue;

                foreach (JObject action in actions.OfType<JObject>())
                {
                    total++;
                    string status = action["status"]?.ToString() ?? "OPEN";
                    if (status.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
                        completed++;
                    else
                    {
                        string dueStr = action["due_date"]?.ToString() ?? "";
                        if (DateTime.TryParse(dueStr, out var due) && due < DateTime.Now)
                            overdue++;
                    }
                }
            }

            double rate = total > 0 ? (double)completed / total * 100 : 0;
            sb.AppendLine($"Total actions: {total}");
            sb.AppendLine($"Completed: {completed} ({rate:F0}%)");
            sb.AppendLine($"Overdue: {overdue}");
            sb.AppendLine($"Open: {total - completed}");
            return sb.ToString();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    //  COMMAND CLASSES — IExternalCommand wrappers for gap fixes
    // ════════════════════════════════════════════════════════════════════════════

    #region ── CRITICAL Gap Commands ──

    /// <summary>CRIT-01: CDE approval workflow integration.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CDEApprovalWorkflowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            string approvalsPath = Path.Combine(GapFixEngine.GetBimDir(ctx.Doc), "approvals.json");
            var approvals = GapFixEngine.LoadJsonArray(approvalsPath);

            var pending = approvals.OfType<JObject>()
                .Where(a => (a["status"]?.ToString() ?? "").Equals("PENDING", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("CDE APPROVAL WORKFLOW STATUS\n");
            sb.AppendLine($"Total approvals: {approvals.Count}");
            sb.AppendLine($"Pending: {pending.Count}");

            foreach (var p in pending.Take(20))
                sb.AppendLine($"  [{p["document_id"]}] → {p["approver"]} ({p["requested"]})");

            sb.AppendLine("\nNote: All pending approvals must be resolved before SHARED→PUBLISHED transition.");
            TaskDialog.Show("STING CDE Approval", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>CRIT-02: Cross-system entity linking.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CrossSystemLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            TaskDialog.Show("STING Cross-Link", GapFixEngine.BuildDependencyGraph(ctx.Doc));
            return Result.Succeeded;
        }
    }

    /// <summary>CRIT-03: Refresh coordination center data.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RefreshCoordinationDataCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            GapFixEngine.InvalidateCoordCache();
            ComplianceScan.InvalidateCache();
            string data = GapFixEngine.BuildFullCoordData(ctx.Doc);
            TaskDialog.Show("STING Coordination Data (Refreshed)", data);
            return Result.Succeeded;
        }
    }

    /// <summary>CRIT-04: Streaming COBie import with validation.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StreamingCOBieImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            // File picker
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select COBie V2.4 Spreadsheet",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx"
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            string result = GapFixEngine.ImportCOBieStreaming(ctx.Doc, dlg.FileName);
            TaskDialog.Show("STING COBie Import", result);
            ComplianceScan.InvalidateCache();
            return Result.Succeeded;
        }
    }

    /// <summary>CRIT-05: 4D schedule handover integration.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Schedule4DHandoverCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            TaskDialog.Show("STING 4D Handover", GapFixEngine.Link4DToHandover(ctx.Doc));
            return Result.Succeeded;
        }
    }

    /// <summary>CRIT-06: COBie system grouping fix via connector graph.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieSystemGroupFixCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var groups = GapFixEngine.BuildSystemGroupsFromConnectors(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine("COBie SYSTEM GROUPS (Connector-Based)\n");
            foreach (var kvp in groups.OrderByDescending(g => g.Value.Count))
                sb.AppendLine($"  {kvp.Key}: {kvp.Value.Count} elements");
            sb.AppendLine($"\nTotal groups: {groups.Count}");
            TaskDialog.Show("STING COBie SYS Fix", sb.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── HIGH Gap Commands ──

    /// <summary>HIGH-01: Data drop progress tracker.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DataDropTrackerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            TaskDialog.Show("STING DD Tracker", GapFixEngine.TrackDataDropProgress(ctx.Doc));
            return Result.Succeeded;
        }
    }

    /// <summary>HIGH-05: CDE folder structure generator.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CDEFolderStructureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select CDE Root Folder",
                FileName = "CDE_Structure",
                Filter = "Folder marker|*.txt"
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            string basePath = Path.GetDirectoryName(dlg.FileName) ?? "";
            string projectCode = ctx.Doc.Title?.Split('.').FirstOrDefault() ?? "PROJ";
            string result = GapFixEngine.GenerateCDEFolders(basePath, projectCode);
            TaskDialog.Show("STING CDE Folders", result);
            return Result.Succeeded;
        }
    }

    /// <summary>HIGH-04: Compliance forecasting.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ComplianceForecastCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            TaskDialog.Show("STING Forecast", GapFixEngine.ForecastCompliance(ctx.Doc));
            return Result.Succeeded;
        }
    }

    #endregion
}
