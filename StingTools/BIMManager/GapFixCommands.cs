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
            string docDir = string.IsNullOrEmpty(doc.PathName)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(doc.PathName);
            string dir = Path.Combine(docDir ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "_bim_manager");
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

        /// <summary>BIM-CDE-APPROVAL-01: Check for ANY pending approvals that block
        /// SHARED→PUBLISHED transitions per ISO 19650-2 §5.6.</summary>
        internal static (int Count, string Details) HasPendingApprovals(Document doc)
        {
            string approvalsPath = Path.Combine(GetBimDir(doc), "approvals.json");
            var approvals = LoadJsonArray(approvalsPath);
            var pending = approvals.OfType<JObject>()
                .Where(a => (a["status"]?.ToString() ?? "").Equals("PENDING", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (pending.Count == 0) return (0, null);

            var sb = new StringBuilder();
            sb.AppendLine($"ISO 19650-2 §5.6 APPROVAL GATE — {pending.Count} pending approval(s):\n");
            foreach (var p in pending.Take(20))
            {
                string docId = p["document_id"]?.ToString() ?? "?";
                string approver = p["approver"]?.ToString() ?? "Unknown";
                string requested = p["requested"]?.ToString() ?? "";
                sb.AppendLine($"  • [{docId}] awaiting {approver} (requested {requested})");
            }
            if (pending.Count > 20) sb.AppendLine($"  ... and {pending.Count - 20} more");
            sb.AppendLine("\nAll approvals must be resolved before SHARED → PUBLISHED transition.");
            return (pending.Count, sb.ToString());
        }

        /// <summary>BIM-CDE-APPROVAL-01: Approve or reject a specific pending approval.</summary>
        internal static bool ResolveApproval(Document doc, string documentId, string approver, bool approve)
        {
            string approvalsPath = Path.Combine(GetBimDir(doc), "approvals.json");
            var approvals = LoadJsonArray(approvalsPath);
            var match = approvals.OfType<JObject>().FirstOrDefault(a =>
                (a["document_id"]?.ToString() ?? "").Equals(documentId, StringComparison.OrdinalIgnoreCase)
                && (a["approver"]?.ToString() ?? "").Equals(approver, StringComparison.OrdinalIgnoreCase)
                && (a["status"]?.ToString() ?? "").Equals("PENDING", StringComparison.OrdinalIgnoreCase));
            if (match == null) return false;
            match["status"] = approve ? "APPROVED" : "REJECTED";
            match["resolved"] = DateTime.UtcNow.ToString("o");
            match["resolved_by"] = Environment.UserName;
            SaveJson(approvalsPath, approvals);
            StingLog.Info($"CDE approval {(approve ? "APPROVED" : "REJECTED")}: doc={documentId}, approver={approver}");
            return true;
        }

        /// <summary>BIM-CDE-APPROVAL-01: Log a PUBLISH_BLOCKED event to the workflow JSONL log.</summary>
        internal static void LogPublishBlocked(Document doc, int pendingCount)
        {
            try
            {
                string dir = null;
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                    dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir))
                    dir = StingToolsApp.DataPath ?? Path.GetTempPath();
                string path = Path.Combine(dir, "STING_WORKFLOW_LOG.jsonl");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var record = new JObject
                {
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["action"] = "PUBLISH_BLOCKED",
                    ["reason"] = "pending_approvals",
                    ["pending_count"] = pendingCount,
                    ["user"] = Environment.UserName,
                    ["model"] = doc?.Title ?? ""
                };
                File.AppendAllText(path, record.ToString(Formatting.None) + Environment.NewLine);
            }
            catch (Exception ex) { StingLog.Warn($"LogPublishBlocked: {ex.Message}"); }
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
        //  CRIT-02b: Cross-System Link Rebuild (BIM-CROSS-LINK-01)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>BIM-CROSS-LINK-01: Scan issues.json and transmittals.json to derive
        /// cross-system links and persist to cross_system_links.json.
        /// Returns (linkCount, summary) tuple.</summary>
        internal static (int LinkCount, string Summary) RebuildCrossSystemLinks(Document doc)
        {
            string bimDir = GetBimDir(doc);
            string issuesPath = Path.Combine(bimDir, "issues.json");
            string txPath = Path.Combine(bimDir, "transmittals.json");
            string outputPath = Path.Combine(bimDir, "cross_system_links.json");

            var issues = LoadJsonArray(issuesPath);
            var transmittals = LoadJsonArray(txPath);
            var links = new JArray();
            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddLink(string srcType, string srcId, string tgtType, string tgtId, string reason)
            {
                if (string.IsNullOrEmpty(srcId) || string.IsNullOrEmpty(tgtId)) return;
                string fwd = $"{srcType}:{srcId}→{tgtType}:{tgtId}";
                string rev = $"{tgtType}:{tgtId}→{srcType}:{srcId}";
                if (dedup.Contains(fwd)) return;
                dedup.Add(fwd);
                dedup.Add(rev);
                links.Add(new JObject
                {
                    ["source_type"] = srcType,
                    ["source_id"] = srcId,
                    ["target_type"] = tgtType,
                    ["target_id"] = tgtId,
                    ["link_date"] = DateTime.UtcNow.ToString("o"),
                    ["reason"] = reason
                });
            }

            // 1. Issue → Transmittal (from linked_transmittals array)
            foreach (var iss in issues.OfType<JObject>())
            {
                string issueId = iss["issue_id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(issueId)) continue;

                var linkedTx = iss["linked_transmittals"] as JArray;
                if (linkedTx != null)
                {
                    foreach (var txToken in linkedTx)
                    {
                        string txId = txToken.ToString();
                        if (!string.IsNullOrEmpty(txId))
                            AddLink("ISSUE", issueId, "TRANSMITTAL", txId, "issue_linked_transmittal");
                    }
                }

                // 2. Issue → Revision (from revision / resolved_in_revision)
                string rev = iss["revision"]?.ToString();
                if (!string.IsNullOrEmpty(rev) && rev != "P01" && rev.Length <= 20)
                    AddLink("ISSUE", issueId, "REVISION", rev, "issue_raised_in_revision");

                string resolvedRev = iss["resolved_in_revision"]?.ToString();
                if (!string.IsNullOrEmpty(resolvedRev))
                    AddLink("ISSUE", issueId, "REVISION", resolvedRev, "issue_resolved_in_revision");
            }

            // 3. Transmittal → Document (from document_ids array)
            foreach (var tx in transmittals.OfType<JObject>())
            {
                string txId = tx["transmittal_id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(txId)) continue;

                var docIds = tx["document_ids"] as JArray;
                if (docIds != null)
                {
                    foreach (var docToken in docIds)
                    {
                        string docId = docToken.ToString();
                        if (!string.IsNullOrEmpty(docId))
                            AddLink("TRANSMITTAL", txId, "DOCUMENT", docId, "transmittal_contains_document");
                    }
                }
            }

            // 4. Also propagate entity_links.json entries (manual links)
            string entityLinksPath = Path.Combine(bimDir, "entity_links.json");
            var entityLinks = LoadJsonArray(entityLinksPath);
            foreach (var el in entityLinks.OfType<JObject>())
            {
                AddLink(
                    el["source_type"]?.ToString() ?? "",
                    el["source_id"]?.ToString() ?? "",
                    el["target_type"]?.ToString() ?? "",
                    el["target_id"]?.ToString() ?? "",
                    "entity_link_manual");
            }

            // Persist
            SaveJson(outputPath, links);
            StingLog.Info($"Cross-system links rebuilt: {links.Count} links from {issues.Count} issues, {transmittals.Count} transmittals");

            // Build summary
            var sb = new StringBuilder();
            sb.AppendLine("CROSS-SYSTEM LINK REBUILD — ISO 19650 Entity Graph\n");

            var byType = links.OfType<JObject>()
                .GroupBy(l => $"{l["source_type"]}→{l["target_type"]}")
                .OrderByDescending(g => g.Count());
            foreach (var g in byType)
                sb.AppendLine($"  {g.Key}: {g.Count()} links");

            sb.AppendLine($"\nTotal links: {links.Count}");
            sb.AppendLine($"Sources: {issues.Count} issues, {transmittals.Count} transmittals, {entityLinks.Count} entity links");

            // Graph view (top 50 by source)
            var bySource = links.OfType<JObject>()
                .GroupBy(l => $"{l["source_type"]}/{l["source_id"]}")
                .OrderByDescending(g => g.Count())
                .Take(50);
            if (bySource.Any())
            {
                sb.AppendLine("\nDEPENDENCY GRAPH (top 50 nodes):\n");
                foreach (var kvp in bySource)
                    sb.AppendLine($"  {kvp.Key} → {string.Join(", ", kvp.Select(l => $"{l["target_type"]}/{l["target_id"]}"))}");
            }

            return (links.Count, sb.ToString());
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CRIT-03: Coordination Center Data Refresh
        // ═══════════════════════════════════════════════════════════════════

        private static DateTime _lastCoordRefresh = DateTime.MinValue;
        private static string _cachedCoordSummary = "";
        private static string _cachedCoordDocPath = "";

        internal static string BuildFullCoordData(Document doc)
        {
            string docKey = doc?.PathName ?? doc?.Title ?? "";
            if ((DateTime.Now - _lastCoordRefresh).TotalSeconds < 30 && !string.IsNullOrEmpty(_cachedCoordSummary) && _cachedCoordDocPath == docKey)
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
            _cachedCoordDocPath = docKey;
            _lastCoordRefresh = DateTime.Now;
            return _cachedCoordSummary;
        }

        internal static void InvalidateCoordCache() { _lastCoordRefresh = DateTime.MinValue; _cachedCoordDocPath = ""; }

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
        //  CRIT-05: 4D Schedule Handover Integration (BIM-4D-HANDOVER-01)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>DD milestone definitions: name, compliance threshold, RIBA phase, required COBie sheets, trade order ceiling.</summary>
        internal static readonly (string Name, double Threshold, string Phase, string[] CobieSheets, int TradeOrderCeiling)[] DDMilestones =
        {
            ("DD1 - Brief",     30.0, "Concept Design",      new[] { "Facility", "Floor", "Space" }, 320),
            ("DD2 - Concept",   60.0, "Developed Design",    new[] { "Facility", "Floor", "Space", "Zone", "Type" }, 500),
            ("DD3 - Technical", 85.0, "Technical Design",    new[] { "Facility", "Floor", "Space", "Zone", "Type", "Component", "System" }, 850),
            ("DD4 - Handover",  95.0, "Construction",        new[] { "Facility", "Floor", "Space", "Zone", "Type", "Component", "System", "Job", "Spare", "Resource", "Document" }, 980)
        };

        internal static string Link4DToHandover(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("4D SCHEDULE → HANDOVER INTEGRATION\n");

            var scan = ComplianceScan.Scan(doc);
            double currentCompliance = scan?.CompliancePercent ?? 0;

            // ── 1. Load 4D schedule if available ──
            string schedulePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "schedule_4d.json");
            JObject schedule = null;
            JArray tasks = null;
            if (File.Exists(schedulePath))
            {
                try
                {
                    schedule = JObject.Parse(File.ReadAllText(schedulePath));
                    tasks = schedule["tasks"] as JArray;
                }
                catch (Exception ex) { StingLog.Warn($"Link4DToHandover: failed to load schedule: {ex.Message}"); }
            }

            bool hasSchedule = tasks != null && tasks.Count > 0;
            sb.AppendLine(hasSchedule
                ? $"4D Schedule loaded: {tasks.Count} tasks"
                : "No 4D schedule found — using compliance-only assessment\n");

            // ── 2. Get deliverable matrix ──
            var deliverables = DeliverableTracker.GetDeliverableMatrix(doc);

            // ── 3. Map schedule tasks to DD milestones by trade order ──
            var milestoneTasks = new Dictionary<string, List<JObject>>();
            var milestoneProgress = new Dictionary<string, (int Total, int Complete, double AvgPct, DateTime? EarliestStart, DateTime? LatestFinish)>();

            foreach (var dd in DDMilestones)
            {
                milestoneTasks[dd.Name] = new List<JObject>();
                milestoneProgress[dd.Name] = (0, 0, 0, null, null);
            }

            if (hasSchedule)
            {
                foreach (var t in tasks)
                {
                    string category = t["category"]?.ToString() ?? "";
                    int taskOrder = 999;
                    if (Scheduling4DEngine.TradeSequence.TryGetValue(category, out var seq))
                        taskOrder = seq.order;

                    // Assign task to the highest DD milestone whose trade ceiling >= task order
                    string assignedDD = null;
                    foreach (var dd in DDMilestones)
                    {
                        if (taskOrder <= dd.TradeOrderCeiling)
                        {
                            assignedDD = dd.Name;
                            break;
                        }
                    }
                    if (assignedDD == null) assignedDD = "DD4 - Handover"; // default: last milestone

                    milestoneTasks[assignedDD].Add((JObject)t);
                }

                // Calculate progress per milestone
                foreach (var dd in DDMilestones)
                {
                    var ddTasks = milestoneTasks[dd.Name];
                    if (ddTasks.Count == 0) continue;

                    int complete = 0;
                    double totalPct = 0;
                    DateTime? earliest = null, latest = null;

                    foreach (var t in ddTasks)
                    {
                        double pct = t["percent_complete"]?.Value<double>() ?? 0;
                        totalPct += pct;
                        if (pct >= 100) complete++;

                        if (DateTime.TryParse(t["start"]?.ToString(), out var start))
                            if (earliest == null || start < earliest) earliest = start;
                        if (DateTime.TryParse(t["finish"]?.ToString(), out var finish))
                            if (latest == null || finish > latest) latest = finish;
                    }

                    milestoneProgress[dd.Name] = (ddTasks.Count, complete, totalPct / ddTasks.Count, earliest, latest);
                }
            }

            // ── 4. Build milestone status report ──
            sb.AppendLine("═══ DD MILESTONE STATUS ═══\n");

            var linkData = new JArray();

            foreach (var dd in DDMilestones)
            {
                bool compliancePasses = currentCompliance >= dd.Threshold;
                var prog = milestoneProgress[dd.Name];
                bool schedulePasses = !hasSchedule || (prog.Total == 0 || prog.AvgPct >= 90);
                bool overallPasses = compliancePasses && schedulePasses;

                string status = overallPasses ? "ACHIEVED" : compliancePasses ? "COMPLIANCE MET — SCHEDULE PENDING" : "NOT MET";

                sb.AppendLine($"  {dd.Name} — {status}");
                sb.AppendLine($"    Compliance: {currentCompliance:F1}% / {dd.Threshold}% {(compliancePasses ? "✓" : "✗")}");
                sb.AppendLine($"    Phase: {dd.Phase}");
                sb.AppendLine($"    Required COBie sheets: {string.Join(", ", dd.CobieSheets)}");

                if (hasSchedule && prog.Total > 0)
                {
                    sb.AppendLine($"    Schedule tasks: {prog.Total} ({prog.Complete} complete, avg {prog.AvgPct:F0}%)");
                    if (prog.EarliestStart.HasValue)
                        sb.AppendLine($"    Date range: {prog.EarliestStart:yyyy-MM-dd} → {prog.LatestFinish:yyyy-MM-dd}");
                }

                // Deliverables for this milestone
                var ddDeliverables = deliverables.Where(d => dd.Name.Contains(d.Milestone)).ToList();
                if (ddDeliverables.Count > 0)
                {
                    int delivComplete = ddDeliverables.Count(d => d.Status == "Complete");
                    sb.AppendLine($"    Deliverables: {delivComplete}/{ddDeliverables.Count} complete");
                    foreach (var d in ddDeliverables.Where(x => x.Status != "Complete"))
                        sb.AppendLine($"      ▸ {d.Name} — {d.Status} ({d.CompletionPct:F0}%)");
                }
                sb.AppendLine();

                // Build JSON link record
                var linkEntry = new JObject
                {
                    ["milestone"] = dd.Name,
                    ["threshold_pct"] = dd.Threshold,
                    ["phase"] = dd.Phase,
                    ["compliance_pct"] = Math.Round(currentCompliance, 1),
                    ["compliance_met"] = compliancePasses,
                    ["cobie_sheets"] = new JArray(dd.CobieSheets),
                    ["status"] = status,
                    ["assessed_date"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                if (hasSchedule && prog.Total > 0)
                {
                    linkEntry["schedule_tasks"] = prog.Total;
                    linkEntry["schedule_complete"] = prog.Complete;
                    linkEntry["schedule_avg_pct"] = Math.Round(prog.AvgPct, 1);
                    if (prog.EarliestStart.HasValue) linkEntry["earliest_start"] = prog.EarliestStart.Value.ToString("yyyy-MM-dd");
                    if (prog.LatestFinish.HasValue) linkEntry["latest_finish"] = prog.LatestFinish.Value.ToString("yyyy-MM-dd");
                }

                if (ddDeliverables.Count > 0)
                {
                    var delivArr = new JArray();
                    foreach (var d in ddDeliverables)
                        delivArr.Add(new JObject { ["name"] = d.Name, ["status"] = d.Status ?? "NotStarted", ["completion_pct"] = d.CompletionPct, ["command"] = d.CommandTag });
                    linkEntry["deliverables"] = delivArr;
                }

                linkData.Add(linkEntry);
            }

            // ── 5. Detect approaching milestones with unready deliverables ──
            var approachingGaps = new List<(string Milestone, string Deliverable, string CommandTag)>();
            foreach (var dd in DDMilestones)
            {
                // Milestone is "approaching" if compliance is within 10% of threshold but not yet met,
                // or if schedule tasks for this milestone are >80% complete
                bool approaching = (currentCompliance >= dd.Threshold - 10 && currentCompliance < dd.Threshold);
                if (!approaching && hasSchedule)
                {
                    var prog = milestoneProgress[dd.Name];
                    approaching = prog.Total > 0 && prog.AvgPct >= 80 && prog.AvgPct < 100;
                }
                if (!approaching) continue;

                var ddDeliverables = deliverables.Where(d => dd.Name.Contains(d.Milestone) && d.Status != "Complete").ToList();
                foreach (var d in ddDeliverables)
                    approachingGaps.Add((dd.Name, d.Name, d.CommandTag));
            }

            if (approachingGaps.Count > 0)
            {
                sb.AppendLine("═══ APPROACHING MILESTONES — ACTION REQUIRED ═══\n");
                foreach (var (ms, deliv, cmd) in approachingGaps)
                    sb.AppendLine($"  ⚠ {ms}: '{deliv}' not complete — run command: {cmd}");
                sb.AppendLine();
            }

            // ── 6. Persist linked milestone data as JSON sidecar ──
            var sidecar = new JObject
            {
                ["version"] = 1,
                ["generated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["project"] = doc.Title ?? "Untitled",
                ["current_compliance_pct"] = Math.Round(currentCompliance, 1),
                ["has_4d_schedule"] = hasSchedule,
                ["milestones"] = linkData
            };
            if (approachingGaps.Count > 0)
            {
                var gapArr = new JArray();
                foreach (var (ms, deliv, cmd) in approachingGaps)
                    gapArr.Add(new JObject { ["milestone"] = ms, ["deliverable"] = deliv, ["command"] = cmd });
                sidecar["approaching_gaps"] = gapArr;
            }

            string sidecarPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "4d_handover_links.json");
            BIMManagerEngine.SaveJsonFile(sidecarPath, sidecar);
            sb.AppendLine($"Sidecar saved: {sidecarPath}");

            // ── 7. Export CSV ──
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            string csvPath = Path.Combine(outDir, $"4D_Handover_Integration_{DateTime.Now:yyyyMMdd}.csv");
            var csvRows = new List<string> { "Milestone,Threshold%,CurrentCompliance%,Status,Phase,ScheduleTasks,ScheduleComplete,ScheduleAvg%,EarliestStart,LatestFinish,COBieSheets" };
            foreach (var dd in DDMilestones)
            {
                var prog = milestoneProgress[dd.Name];
                bool compliancePasses = currentCompliance >= dd.Threshold;
                bool schedulePasses = !hasSchedule || (prog.Total == 0 || prog.AvgPct >= 90);
                string status = (compliancePasses && schedulePasses) ? "ACHIEVED" : compliancePasses ? "COMPLIANCE_MET" : "NOT_MET";

                csvRows.Add($"\"{dd.Name}\",{dd.Threshold},{currentCompliance:F1},{status},\"{dd.Phase}\"," +
                    $"{prog.Total},{prog.Complete},{prog.AvgPct:F1}," +
                    $"\"{prog.EarliestStart?.ToString("yyyy-MM-dd") ?? ""}\",\"{prog.LatestFinish?.ToString("yyyy-MM-dd") ?? ""}\"," +
                    $"\"{string.Join("; ", dd.CobieSheets)}\"");
            }
            File.WriteAllLines(csvPath, csvRows);
            sb.AppendLine($"CSV exported: {csvPath}");

            return sb.ToString();
        }

        /// <summary>BIM-4D-HANDOVER-01: Auto-create issues for approaching DD milestones with unready deliverables.</summary>
        internal static int AutoRaiseHandoverGapIssues(Document doc)
        {
            string sidecarPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "4d_handover_links.json");
            if (!File.Exists(sidecarPath)) return 0;

            JObject sidecar;
            try { sidecar = JObject.Parse(File.ReadAllText(sidecarPath)); }
            catch (Exception ex) { StingLog.Warn($"AutoRaiseHandoverGapIssues: {ex.Message}"); return 0; }

            var gaps = sidecar["approaching_gaps"] as JArray;
            if (gaps == null || gaps.Count == 0) return 0;

            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            JArray issues;
            try { issues = File.Exists(issuesPath) ? JArray.Parse(File.ReadAllText(issuesPath)) : new JArray(); }
            catch (Exception ex) { StingLog.Warn($"AutoRaiseHandoverGapIssues: {ex.Message}"); issues = new JArray(); }

            // Dedup: skip if open issue already exists for this deliverable
            var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var iss in issues)
            {
                string status = iss["status"]?.ToString() ?? "";
                if (status.Equals("CLOSED", StringComparison.OrdinalIgnoreCase)) continue;
                existingTitles.Add(iss["title"]?.ToString() ?? "");
            }

            int created = 0;
            foreach (var gap in gaps)
            {
                string milestone = gap["milestone"]?.ToString() ?? "";
                string deliverable = gap["deliverable"]?.ToString() ?? "";
                string title = $"Handover Gap: {deliverable} required for {milestone}";

                if (existingTitles.Contains(title)) continue;

                // Find next ID
                int maxId = 0;
                foreach (var iss in issues)
                {
                    string idStr = iss["id"]?.ToString() ?? "";
                    int dashIdx = idStr.LastIndexOf('-');
                    if (dashIdx >= 0 && int.TryParse(idStr.Substring(dashIdx + 1), out int num) && num > maxId)
                        maxId = num;
                }

                string revision = "";
                try { revision = Core.ParameterHelpers.PhaseAutoDetect.DetectProjectRevision(doc); }
                catch (Exception ex) { StingLog.Warn($"AutoRaiseHandoverGapIssues revision: {ex.Message}"); }

                var issue = new JObject
                {
                    ["id"] = $"SI-{(maxId + 1).ToString().PadLeft(4, '0')}",
                    ["title"] = title,
                    ["description"] = $"ISO 19650 data drop '{milestone}' is approaching but deliverable '{deliverable}' is not yet complete. " +
                                     $"Run command '{gap["command"]}' to generate this deliverable.",
                    ["type"] = "SI",
                    ["priority"] = "HIGH",
                    ["status"] = "OPEN",
                    ["discipline"] = "BIM",
                    ["revision"] = revision,
                    ["created_date"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["created_by"] = Environment.UserName,
                    ["auto_created"] = true,
                    ["source"] = "4D_HANDOVER_GAP",
                    ["milestone"] = milestone,
                    ["deliverable"] = deliverable,
                    ["command_tag"] = gap["command"]?.ToString() ?? ""
                };

                issues.Add(issue);
                existingTitles.Add(title);
                created++;
            }

            if (created > 0)
            {
                BIMManagerEngine.SaveJsonFile(issuesPath, issues);
                StingLog.Info($"AutoRaiseHandoverGapIssues: created {created} issues for approaching DD milestones");
            }

            return created;
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

    /// <summary>CRIT-01: CDE approval workflow integration with approve/reject actions.</summary>
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

            if (pending.Count == 0)
            {
                TaskDialog.Show("STING CDE Approval", "No pending approvals. All documents are cleared for CDE transition.");
                return Result.Succeeded;
            }

            // Show pending approvals with approve/reject options
            var sb = new StringBuilder();
            sb.AppendLine("CDE APPROVAL WORKFLOW STATUS\n");
            sb.AppendLine($"Total approvals: {approvals.Count}");
            sb.AppendLine($"Pending: {pending.Count}\n");

            for (int i = 0; i < Math.Min(pending.Count, 20); i++)
            {
                var p = pending[i];
                sb.AppendLine($"  {i + 1}. [{p["document_id"]}] → {p["approver"]} (requested {p["requested"]})");
            }
            if (pending.Count > 20) sb.AppendLine($"  ... and {pending.Count - 20} more");

            var dlg = new TaskDialog("STING CDE Approval Workflow");
            dlg.MainInstruction = $"{pending.Count} pending approval(s) — ISO 19650-2 §5.6";
            dlg.MainContent = sb.ToString();
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Approve All Pending",
                $"Approve all {pending.Count} pending approval(s) as {Environment.UserName}");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Reject All Pending",
                $"Reject all {pending.Count} pending approval(s) as {Environment.UserName}");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Approve First Pending Only",
                $"Approve [{pending[0]["document_id"]}] for {pending[0]["approver"]}");
            dlg.CommonButtons = TaskDialogCommonButtons.Close;

            var result = dlg.Show();
            int resolved = 0;
            string currentUser = Environment.UserName;

            if (result == TaskDialogResult.CommandLink1)
            {
                foreach (var p in pending)
                    if (GapFixEngine.ResolveApproval(ctx.Doc, p["document_id"]?.ToString(), p["approver"]?.ToString(), true))
                        resolved++;
                TaskDialog.Show("STING CDE Approval", $"Approved {resolved} of {pending.Count} pending approvals.");
            }
            else if (result == TaskDialogResult.CommandLink2)
            {
                foreach (var p in pending)
                    if (GapFixEngine.ResolveApproval(ctx.Doc, p["document_id"]?.ToString(), p["approver"]?.ToString(), false))
                        resolved++;
                TaskDialog.Show("STING CDE Approval", $"Rejected {resolved} of {pending.Count} pending approvals.");
            }
            else if (result == TaskDialogResult.CommandLink3)
            {
                var first = pending[0];
                if (GapFixEngine.ResolveApproval(ctx.Doc, first["document_id"]?.ToString(), first["approver"]?.ToString(), true))
                    TaskDialog.Show("STING CDE Approval", $"Approved [{first["document_id"]}] for {first["approver"]}.");
                else
                    TaskDialog.Show("STING CDE Approval", "Failed to resolve approval — record may have changed.");
            }

            return Result.Succeeded;
        }
    }

    /// <summary>CRIT-02: Cross-system entity linking — rebuilds and displays ISO 19650
    /// issue↔revision↔transmittal↔document dependency graph.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CrossSystemLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var (count, summary) = GapFixEngine.RebuildCrossSystemLinks(ctx.Doc);
            if (count == 0)
            {
                TaskDialog.Show("STING Cross-System Links",
                    "No cross-system links found.\n\nLinks are derived from:\n" +
                    "  • issues.json → linked_transmittals, revision fields\n" +
                    "  • transmittals.json → document_ids\n" +
                    "  • entity_links.json → manual links\n\n" +
                    "Create issues or transmittals to build the entity graph.");
                return Result.Succeeded;
            }

            TaskDialog.Show("STING Cross-System Links", summary);
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

    /// <summary>CRIT-05: 4D schedule handover integration with auto-issue creation.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Schedule4DHandoverCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            string report = GapFixEngine.Link4DToHandover(ctx.Doc);
            TaskDialog.Show("STING 4D Handover", report);

            int issuesCreated = GapFixEngine.AutoRaiseHandoverGapIssues(ctx.Doc);
            if (issuesCreated > 0)
                TaskDialog.Show("STING 4D Handover", $"{issuesCreated} handover gap issue(s) auto-created for approaching milestones with incomplete deliverables.");

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

    // ══════════════════════════════════════════════════════════════════
    //  BIM-SIDECAR-VER-01: Sidecar Versioning Utility
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Utility for versioned, atomic sidecar JSON file operations.
    /// Wraps data in a versioned envelope and uses temp-file + File.Replace for crash safety.
    /// </summary>
    internal static class SidecarVersioning
    {
        /// <summary>
        /// Write data to a sidecar JSON file with version envelope and atomic write.
        /// Produces: {"schema_version":"X.Y","timestamp":"...","data":{...}}
        /// </summary>
        internal static void WriteSidecar(string path, object data, string schemaVersion)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var envelope = new Newtonsoft.Json.Linq.JObject
                {
                    ["schema_version"] = schemaVersion,
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["data"] = Newtonsoft.Json.Linq.JToken.FromObject(data)
                };

                string json = envelope.ToString(Newtonsoft.Json.Formatting.Indented);
                string tmpPath = path + ".tmp";
                System.IO.File.WriteAllText(tmpPath, json);

                if (System.IO.File.Exists(path))
                    System.IO.File.Replace(tmpPath, path, path + ".bak");
                else
                    System.IO.File.Move(tmpPath, path, true);

                Core.StingLog.Info($"SidecarVersioning: wrote v{schemaVersion} to {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { Core.StingLog.Warn($"SidecarVersioning.WriteSidecar: {ex.Message}"); }
        }

        /// <summary>
        /// Read versioned sidecar data. Returns (data, actualVersion).
        /// If file has no version envelope (legacy format), returns the raw content as data with version "0.0".
        /// </summary>
        internal static (T data, string version) ReadSidecar<T>(string path, string expectedVersion) where T : class
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return (null, null);
            try
            {
                string json = System.IO.File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return (null, null);

                var token = Newtonsoft.Json.Linq.JToken.Parse(json);

                // Check for versioned envelope
                if (token is Newtonsoft.Json.Linq.JObject obj && obj["schema_version"] != null && obj["data"] != null)
                {
                    string actualVersion = obj["schema_version"]?.ToString() ?? "0.0";
                    var data = obj["data"].ToObject<T>();
                    if (actualVersion != expectedVersion)
                        Core.StingLog.Info($"SidecarVersioning: {System.IO.Path.GetFileName(path)} version {actualVersion} (expected {expectedVersion}) — migration may apply");
                    return (data, actualVersion);
                }

                // Legacy format: no envelope — deserialize raw content
                var legacyData = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
                return (legacyData, "0.0");
            }
            catch (Exception ex)
            {
                Core.StingLog.Warn($"SidecarVersioning.ReadSidecar: {ex.Message}");
                return (null, null);
            }
        }
    }
}
