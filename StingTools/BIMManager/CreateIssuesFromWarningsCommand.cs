using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING — Create Issues From Warnings
    //
    //  Phase D Top-5 #5 (fix/create-issues-from-warnings):
    //    The "From Warnings" button in BIMCoordinationCenter previously dispatched
    //    RaiseIssueCommand, which silently launched the IssueWizard and created a
    //    single blank manual issue — discarding every Revit warning the user
    //    expected to triage. This command is the real implementation:
    //
    //      1. Scan Revit warnings via WarningsEngine.ScanWarnings (cached, classified)
    //      2. Apply a user-picked scope: critical-only / critical+high / all classified
    //      3. Group warnings by (Category, AutoFix-bool) so one issue covers the
    //         "Highlighted walls overlap × 17 elements" cluster — matches the BIM-
    //         coordination workflow of "fix this whole class of warning"
    //      4. Mint deterministic source_hash per group:
    //            sha256(category + severity + first-100-chars-of-desc + sorted elt ids)
    //         → first 12 hex chars. Re-runs with the same warnings produce identical
    //         hashes → no duplicates.
    //      5. For each group: build a JObject via BIMManagerEngine.CreateIssue so the
    //         schema matches what RaiseIssueCommand / IssueDashboard / UpdateIssue
    //         expect (issue_id, type, priority, status, element_ids, comments, …).
    //      6. Tag every minted issue with the source_hash so subsequent runs can
    //         dedup in O(n).
    //      7. TaskDialog summary with three clear cases: created-N, all-already-exist,
    //         zero-warnings.
    //
    //  Issue type mapping:
    //    Critical severity → NCR (Non-Conformance Report) priority CRITICAL
    //    High severity     → SI  (Site Instruction)       priority HIGH
    //    Medium severity   → RFI                          priority MEDIUM
    //    (Low / Info are filtered out unless user picks "all classified", and they
    //     never appear in the classified Warnings list with severity above Medium
    //     anyway — see the scope filter below.)
    //
    //  This file deliberately uses BIMManagerEngine.CreateIssue / LoadJsonArray /
    //  SaveJsonFile / GetBIMManagerFilePath for schema compatibility with the rest
    //  of the issue tracker. WarningsEngine.CreateIssuesFromWarnings (legacy, in
    //  Core/WarningsManager.cs) writes a different, hand-rolled JSON shape that
    //  IssueDashboard cannot read; that legacy method is left in place for the
    //  callers documented in Core/Phase75Enhancements.cs but is NOT used here.
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateIssuesFromWarningsCommand : IExternalCommand
    {
        // Filter scope picked by user.
        private enum ScopeMode
        {
            CriticalOnly,           // Severity == Critical
            CriticalAndHigh,        // Severity <= High (default)
            AllClassified           // Severity <= Medium (skip Low/Info)
        }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("STING", "No document open.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;

            // ── 1. Scan warnings (uses 30s cache inside WarningsEngine) ────────
            WarningReport report;
            try
            {
                report = WarningsEngine.ScanWarnings(doc);
            }
            catch (Exception ex)
            {
                StingLog.Error("CreateIssuesFromWarnings: ScanWarnings failed", ex);
                TaskDialog.Show("STING Issue Tracker",
                    "Failed to scan Revit warnings. See StingTools.log for details.");
                return Result.Failed;
            }

            if (report == null || report.Warnings == null || report.Warnings.Count == 0)
            {
                TaskDialog.Show("STING Issue Tracker — From Warnings",
                    "No Revit warnings in this model.\n\n" +
                    "Nothing to convert into issues. Open 'Warnings Dashboard' first to " +
                    "review the model warnings list, or re-run this command after the " +
                    "model develops some warnings.");
                return Result.Succeeded;
            }

            // ── 2. Scope picker (TaskDialog command-link pattern) ──────────────
            int nCrit = report.Warnings.Count(w => w.Severity == WarningSeverity.Critical);
            int nHigh = report.Warnings.Count(w => w.Severity == WarningSeverity.High);
            int nMed  = report.Warnings.Count(w => w.Severity == WarningSeverity.Medium);

            var scopeDlg = new TaskDialog("STING — Create Issues From Warnings");
            scopeDlg.MainInstruction = $"Found {report.Warnings.Count} classified Revit warning(s). Convert which?";
            scopeDlg.MainContent =
                "Warnings are grouped by category — one issue per (category, fixability) " +
                "cluster, with all failing elements linked. Re-running this command on " +
                "the same warnings will NOT create duplicates.";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Critical only → NCR ({nCrit})",
                "Blocks handover or causes data loss");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Critical + High → NCR + SI ({nCrit + nHigh})",
                "Recommended for milestone gate checks");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                $"All classified → NCR + SI + RFI ({nCrit + nHigh + nMed})",
                "Includes Medium severity (excludes Low / Info)");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            scopeDlg.DefaultButton = TaskDialogResult.CommandLink2;

            var scopeResult = scopeDlg.Show();
            ScopeMode scope;
            switch (scopeResult)
            {
                case TaskDialogResult.CommandLink1: scope = ScopeMode.CriticalOnly; break;
                case TaskDialogResult.CommandLink2: scope = ScopeMode.CriticalAndHigh; break;
                case TaskDialogResult.CommandLink3: scope = ScopeMode.AllClassified; break;
                default: return Result.Cancelled;
            }

            // ── 3. Filter warnings to chosen scope ─────────────────────────────
            var filtered = report.Warnings.Where(w =>
            {
                switch (scope)
                {
                    case ScopeMode.CriticalOnly:    return w.Severity == WarningSeverity.Critical;
                    case ScopeMode.CriticalAndHigh: return w.Severity <= WarningSeverity.High;
                    case ScopeMode.AllClassified:   return w.Severity <= WarningSeverity.Medium;
                    default: return false;
                }
            }).ToList();

            if (filtered.Count == 0)
            {
                TaskDialog.Show("STING Issue Tracker — From Warnings",
                    "No warnings match the chosen severity scope. Nothing created.");
                return Result.Succeeded;
            }

            // ── 4. Load existing issues, build hash index for dedup ────────────
            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var existingIssues = BIMManagerEngine.LoadJsonArray(issuesPath);

            var existingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var iss in existingIssues)
            {
                string h = iss?["source_hash"]?.ToString();
                if (!string.IsNullOrEmpty(h)) existingHashes.Add(h);
            }

            // ── 5. Group warnings by (Category, CanAutoFix) ────────────────────
            var groups = filtered
                .GroupBy(w => new { w.Category, w.CanAutoFix })
                .OrderBy(g => g.Min(w => (int)w.Severity)) // critical groups first
                .ToList();

            int created = 0;
            int skippedDup = 0;
            var createdIds = new List<string>();
            string revision = "";
            try { revision = PhaseAutoDetect.DetectProjectRevision(doc) ?? ""; }
            catch (Exception ex) { StingLog.Warn($"CreateIssuesFromWarnings revision: {ex.Message}"); }

            foreach (var grp in groups)
            {
                var grpList = grp.ToList();
                var maxSev = grpList.Min(w => w.Severity); // min enum = highest severity

                // Pick issue type + priority from the worst-severity warning in group
                string issueType;
                string priority;
                if (maxSev == WarningSeverity.Critical) { issueType = "NCR"; priority = "CRITICAL"; }
                else if (maxSev == WarningSeverity.High) { issueType = "SI"; priority = "HIGH"; }
                else { issueType = "RFI"; priority = "MEDIUM"; }

                // Collect unique failing + additional element ids across the whole group
                var elementIds = new HashSet<ElementId>();
                foreach (var cw in grpList)
                {
                    if (cw.FailingElements != null)
                        foreach (var eid in cw.FailingElements) elementIds.Add(eid);
                    if (cw.AdditionalElements != null)
                        foreach (var eid in cw.AdditionalElements) elementIds.Add(eid);
                }

                // Pick representative description (longest, capped at 200 chars for title)
                string repDesc = grpList
                    .Select(w => w.Description ?? "")
                    .OrderByDescending(s => s.Length)
                    .FirstOrDefault() ?? "";

                string title = grpList.Count == 1
                    ? Truncate(repDesc, 180)
                    : $"{grp.Key.Category} — {grpList.Count}× {Truncate(repDesc, 120)}";

                // ── Deterministic source_hash ──────────────────────────────
                // Inputs: category + worst-severity + first 100 chars of desc + sorted element ids.
                // Same warnings on a re-run → same hash → skip.
                string hashInput = string.Join("|",
                    "warn-v1",
                    grp.Key.Category.ToString(),
                    maxSev.ToString(),
                    Truncate(repDesc, 100),
                    string.Join(",", elementIds.Select(e => e.Value).OrderBy(v => v)));
                string sourceHash = Sha256Short(hashInput, 12);

                if (existingHashes.Contains(sourceHash))
                {
                    skippedDup++;
                    StingLog.Info($"CreateIssuesFromWarnings: skipped duplicate group {sourceHash} ({grp.Key.Category}, {grpList.Count} warnings)");
                    continue;
                }

                // Pick discipline from first warning that has one
                string discipline = grpList
                    .Select(w => w.Discipline)
                    .FirstOrDefault(d => !string.IsNullOrEmpty(d) && d != "XX") ?? "Z";

                // Build description body
                var descBody = new StringBuilder();
                descBody.AppendLine($"Auto-created from {grpList.Count} Revit warning(s) in category '{grp.Key.Category}'.");
                descBody.AppendLine($"Severity: {maxSev}");
                descBody.AppendLine($"Auto-fixable: {(grp.Key.CanAutoFix ? "Yes" : "No")}");
                if (!string.IsNullOrEmpty(grpList[0].FixStrategy))
                    descBody.AppendLine($"Suggested fix: {grpList[0].FixStrategy}");
                descBody.AppendLine($"Linked elements: {elementIds.Count}");
                if (grpList.Count > 1)
                {
                    descBody.AppendLine();
                    descBody.AppendLine("Sample warnings:");
                    foreach (var sample in grpList.Take(3))
                        descBody.AppendLine($"  • {Truncate(sample.Description ?? "", 200)}");
                    if (grpList.Count > 3) descBody.AppendLine($"  … and {grpList.Count - 3} more");
                }
                descBody.AppendLine();
                descBody.AppendLine($"source_hash: {sourceHash} (re-run dedup key)");

                // Mint next issue id of the right type
                string nextId = BIMManagerEngine.GetNextIssueId(existingIssues, issueType);

                var issue = BIMManagerEngine.CreateIssue(
                    nextId, issueType, priority,
                    title, descBody.ToString(),
                    "" /* assignee — left blank, set via UpdateIssue / AssignIssues */,
                    discipline,
                    elementIds,
                    "Warnings Scan" /* view_name */,
                    doc);

                // Mark the issue with the hash + provenance so re-runs dedup
                issue["source"] = "warning";
                issue["source_hash"] = sourceHash;
                issue["warning_category"] = grp.Key.Category.ToString();
                issue["warning_severity"] = maxSev.ToString();
                issue["warning_auto_fixable"] = grp.Key.CanAutoFix;
                if (!string.IsNullOrEmpty(revision)) issue["revision"] = revision;

                existingIssues.Add(issue);
                existingHashes.Add(sourceHash);
                createdIds.Add(nextId);
                created++;

                StingLog.Info($"CreateIssuesFromWarnings: created {nextId} ({issueType}, {priority}) — hash {sourceHash}, {elementIds.Count} elements");
            }

            // ── 6. Persist if anything changed ─────────────────────────────────
            if (created > 0)
            {
                try
                {
                    BIMManagerEngine.SaveJsonFile(issuesPath, existingIssues);
                }
                catch (Exception ex)
                {
                    StingLog.Error("CreateIssuesFromWarnings: SaveJsonFile failed", ex);
                    TaskDialog.Show("STING Issue Tracker — From Warnings",
                        $"Created {created} issue(s) in memory but failed to write " +
                        $"issues.json:\n\n{ex.Message}\n\nSee StingTools.log.");
                    return Result.Failed;
                }
            }

            // ── 7. Summary TaskDialog ──────────────────────────────────────────
            var summary = new StringBuilder();
            if (created == 0 && skippedDup == 0)
            {
                summary.AppendLine("No issues created.");
                summary.AppendLine();
                summary.AppendLine($"Scanned {filtered.Count} warning(s) but none produced groups eligible for issue creation.");
            }
            else if (created == 0 && skippedDup > 0)
            {
                summary.AppendLine($"All {skippedDup} warning group(s) already exist as issues — no duplicates created.");
                summary.AppendLine();
                summary.AppendLine("This command is idempotent: re-running on the same warnings produces zero new issues.");
                summary.AppendLine();
                summary.AppendLine("If you want fresh issues, resolve the underlying warnings (or delete the existing issues) and re-run.");
            }
            else
            {
                summary.AppendLine($"Created {created} issue(s) from {filtered.Count} Revit warning(s).");
                if (skippedDup > 0)
                    summary.AppendLine($"Skipped {skippedDup} group(s) that already had matching issues (idempotent dedup).");
                summary.AppendLine();
                summary.AppendLine($"Scope: {ScopeLabel(scope)}");
                summary.AppendLine("New issue IDs:");
                foreach (var id in createdIds.Take(10))
                    summary.AppendLine($"  • {id}");
                if (createdIds.Count > 10) summary.AppendLine($"  … and {createdIds.Count - 10} more");
                summary.AppendLine();
                summary.AppendLine("Open the Issue Dashboard to assign, prioritise, or close the new issues.");
            }

            TaskDialog.Show("STING Issue Tracker — From Warnings", summary.ToString());
            return Result.Succeeded;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        private static string Sha256Short(string input, int hexChars)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                string full = sb.ToString();
                return full.Length >= hexChars ? full.Substring(0, hexChars) : full;
            }
        }

        private static string ScopeLabel(ScopeMode scope)
        {
            switch (scope)
            {
                case ScopeMode.CriticalOnly:    return "Critical only (NCR)";
                case ScopeMode.CriticalAndHigh: return "Critical + High (NCR + SI)";
                case ScopeMode.AllClassified:   return "All classified (NCR + SI + RFI)";
                default: return "(unknown)";
            }
        }
    }
}
