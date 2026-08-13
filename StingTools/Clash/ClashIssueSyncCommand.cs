// ══════════════════════════════════════════════════════════════════════════
//  ClashIssueSyncCommand.cs — wire the clash silo into the issue tracker. PM-2.
//
//  The audit (§3 HIGH) found clashes never reached issues.json: ClashSlaIntegration
//  built in-memory CoordIssue that flowed only to BCF, so nothing escalated or
//  bundled. This command reconciles clashes.json ↔ issues.json:
//    • each OPEN clash with no linked issue → a tracked "CLASH" issue in
//      issues.json (via the same BIMManagerEngine.CreateIssue factory the
//      warnings path uses, so the schema + SLA-by-priority are identical);
//    • each clash now Resolved/Void → its linked issue is CLOSED;
//    • the clash record back-links its issue id (LinkedIssueGuid);
//    • status is written through the one IssueStatusNormalizer so the workflow
//      gate `has_open_issues` finally sees clash issues.
//  A transmittal-ready CSV of the synced clash issues is written alongside so the
//  coordinator can bundle them in the next issue transmittal.
//
//  Command tag: Clash_SyncIssues.
// ══════════════════════════════════════════════════════════════════════════
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
using StingTools.BIMManager;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClashSyncIssuesCommand : IExternalCommand
    {
        private static readonly HashSet<string> OpenStates =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "New", "Active", "Assigned", "InReview", "Reintroduced" };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                string outDir = OutputLocationHelper.GetOutputDirectory(doc) ?? Path.GetTempPath();
                string clashesJson = ClashPersistence.CanonicalPath(doc);
                if (!File.Exists(clashesJson))
                {
                    StingResultPanel.Create("Clash → Issues")
                        .AddSection("NO CLASH RUN")
                        .Text($"No clashes.json found at:\n{clashesJson}\nRun a clash detection first (Clash_Run).")
                        .Show();
                    return Result.Cancelled;
                }

                var run = ClashPersistence.Load(clashesJson);
                if (run?.Clashes == null || run.Clashes.Count == 0)
                {
                    StingResultPanel.Create("Clash → Issues")
                        .AddSection("EMPTY").Text("The clash run holds no clashes.").Show();
                    return Result.Cancelled;
                }

                // Phase 2: through IssueStore, so clash issues get the same atomic minting,
                // the `issue.from_clash` audit entry and the server push everything else
                // gets. Previously this loaded, minted and saved on its own — and persisted
                // with a raw File.WriteAllText rather than the atomic writer, so a crash
                // mid-write could truncate the whole register.
                string issuesPath = IssueStore.PathFor(doc);
                using var batch = IssueStore.Begin(doc);
                if (!batch.Ok)
                {
                    StingResultPanel.Create("Clash → Issues")
                        .AddSection("BLOCKED")
                        .Text("The issue register exists but could not be read; nothing was written.").Show();
                    return Result.Cancelled;
                }
                var issues = batch.Rows;

                // Index existing clash-sourced issues by clash id.
                var byClashId = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in issues.OfType<JObject>())
                {
                    string cid = it["clash_id"]?.ToString();
                    if (!string.IsNullOrEmpty(cid)) byClashId[cid] = it;
                }

                int created = 0, closed = 0, skipped = 0;
                var syncedForTransmittal = new List<(string issueId, string clashId, string title, string priority)>();

                foreach (var clash in run.Clashes)
                {
                    if (clash == null || string.IsNullOrEmpty(clash.Id)) continue;
                    bool isOpen = OpenStates.Contains(clash.State ?? "Active");
                    string hash = "clash-v1|" + clash.Id;

                    if (isOpen)
                    {
                        string priority = PriorityFor(clash.Severity);
                        string catA = clash.ElementA?.Category ?? "?";
                        string catB = clash.ElementB?.Category ?? "?";
                        string title = $"Clash {clash.Id}: {catA} × {catB}";
                        var desc = new StringBuilder();
                        desc.AppendLine($"Auto-created from clash {clash.Id} (severity {clash.Severity ?? "?"}, state {clash.State}).");
                        desc.AppendLine($"A: {catA} [{clash.ElementA?.System}] id {clash.ElementA?.ElementId}");
                        desc.AppendLine($"B: {catB} [{clash.ElementB?.System}] id {clash.ElementB?.ElementId}");
                        if (!string.IsNullOrEmpty(clash.ResolutionHint)) desc.AppendLine($"Hint: {clash.ResolutionHint}");
                        desc.AppendLine($"source_hash: {hash}");

                        var elemIds = new List<ElementId>();
                        if (clash.ElementA != null && clash.ElementA.ElementId > 0) elemIds.Add(new ElementId((long)clash.ElementA.ElementId));
                        if (clash.ElementB != null && clash.ElementB.ElementId > 0) elemIds.Add(new ElementId((long)clash.ElementB.ElementId));

                        string disc = DiscFor(catA);
                        int before = batch.Created.Count;
                        // Dedup on (clash, source_hash) is now IssueStore's job — it also
                        // scopes the check to STILL-OPEN issues, so a clash that reopens
                        // after its issue was closed raises a fresh one instead of being
                        // silently skipped forever.
                        var issue = batch.Create(new IssueSpec
                        {
                            Type        = "CLASH",
                            Priority    = priority,
                            Title       = title,
                            Description = desc.ToString(),
                            Discipline  = disc,
                            ViewName    = "Clash Run",
                            Source      = IssueSource.Clash,
                            SourceHash  = hash,
                            ElementIds  = elemIds.Select(e => e.Value).ToList(),
                            Extra       = new JObject { ["clash_id"] = clash.Id },
                        });
                        if (issue == null) { skipped++; continue; }
                        if (batch.Created.Count == before) { skipped++; continue; }

                        string nextId = IssueSchema.IdOf(issue);
                        byClashId[clash.Id] = issue;
                        clash.LinkedIssueGuid = nextId;
                        created++;
                        syncedForTransmittal.Add((nextId, clash.Id, title, priority));
                    }
                    else
                    {
                        // Resolved / Void → close the linked issue if still open.
                        if (byClashId.TryGetValue(clash.Id, out var iss) && IssueSchema.IsOpen(iss))
                        {
                            // Routed through the batch so the transition is audited
                            // (`issue.status_changed`) and mirrored to the server.
                            if (batch.SetStatus(IssueSchema.IdOf(iss), "CLOSED",
                                                note: $"Clash {clash.Id} is no longer active"))
                                closed++;
                        }
                    }
                }

                // Persist atomically + emit audit entries + push to the server.
                batch.Commit();
                // Persist clashes.json so the issue back-links survive.
                try { ClashPersistence.Save(run, clashesJson); }
                catch (Exception ex) { StingLog.Warn($"Clash sync: re-save clashes.json: {ex.Message}"); }

                // Transmittal-ready manifest of the clash issues created this run.
                string csv = null;
                if (syncedForTransmittal.Count > 0)
                {
                    csv = Path.Combine(outDir, $"clash_issues_transmittal_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    var sb = new StringBuilder();
                    sb.AppendLine("IssueId,ClashId,Priority,Title");
                    foreach (var s in syncedForTransmittal)
                        sb.AppendLine($"{s.issueId},{s.clashId},{s.priority},\"{s.title.Replace("\"", "\"\"")}\"");
                    File.WriteAllText(csv, sb.ToString());
                }

                var panel = StingResultPanel.Create("Clash → Issues")
                    .SetSubtitle($"{run.Clashes.Count} clash(es) reconciled")
                    .AddSection("RESULT")
                    .Metric("Issues created", created.ToString())
                    .Metric("Issues closed (clash resolved)", closed.ToString())
                    .Metric("Skipped (already linked)", skipped.ToString())
                    .Text("Clash issues are now in issues.json with SLA-by-priority and visible to the "
                        + "has_open_issues workflow gate.");
                if (csv != null)
                    panel.AddSection("TRANSMITTAL").Text($"Bundle the new clash issues via the issue transmittal — "
                        + $"manifest:\n{csv}");
                panel.Show();
                StingLog.Info($"Clash→Issues: +{created} created, {closed} closed, {skipped} skipped.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Clash_SyncIssues", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        private static string PriorityFor(string severity)
        {
            string s = (severity ?? "").ToLowerInvariant();
            if (s.Contains("crit")) return "CRITICAL";
            if (s.Contains("high") || s.Contains("major") || s.Contains("hard")) return "HIGH";
            if (s.Contains("low") || s.Contains("minor") || s.Contains("clearance")) return "LOW";
            return "MEDIUM";
        }

        private static string DiscFor(string category)
        {
            string c = (category ?? "").ToLowerInvariant();
            if (c.Contains("duct") || c.Contains("mechanical") || c.Contains("hvac")) return "M";
            if (c.Contains("pipe") || c.Contains("plumbing")) return "P";
            if (c.Contains("cable") || c.Contains("conduit") || c.Contains("electrical")) return "E";
            if (c.Contains("structural") || c.Contains("beam") || c.Contains("column")) return "S";
            if (c.Contains("wall") || c.Contains("floor") || c.Contains("door") || c.Contains("window")) return "A";
            return "Z";
        }
    }
}
