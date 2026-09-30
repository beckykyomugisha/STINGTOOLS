// AccSyncIssueStatusCommand.cs — ACC → STING issue-closure reconciliation.
//
// The other half of the loop: ACC_PullClashes escalates triaged clashes to ACC
// Issues and records each (clash signature → ACC issue id) in
// _BIM_COORD/acc/pushed_clashes.json. This command pulls the current ACC issue
// statuses and reconciles them against that escalation log:
//   - counts how many escalated clashes are now CLOSED in ACC,
//   - UNTRACKS the closed ones (removes them from the dedup map) so that if the
//     same clash recurs in a later pull it is re-raised rather than silently
//     skipped. The ORIGIN of each one stays in acc/acc_issue_origins.json, which
//     nothing prunes, so ACC_ImportIssues still recognises it as STING-raised (A15),
//   - reports what is still open / not found, and writes a closure CSV.
//
// Note on identity: ACC clashes and STING's own clash kernel (clashes.json) use
// different id spaces (ACC object dbIds vs Revit ElementIds), so this reconciles
// the ESCALATION log (what STING raised in ACC), not the STING clash store —
// which is the accurate thing to do. Read-only; network I/O only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccSyncIssueStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Step 4 of the unattended KUT cycle: every message goes through Report, which
            // logs instead of opening a modal window when nobody is there to click OK.
            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_SyncIssueStatus");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC sync issue status");   // IM-18: project container ids first
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                return AccProjectSettingsFile.NotConfigured(policy, creds, "ACC — Sync Issue Status", "Nothing was reconciled.");
            }

            string sidecar = AccPullClashesCommand.SidecarPath(doc);
            var pushedMap = AccPullClashesCommand.LoadPushed(sidecar, out string pushedErr);
            // A6: unreadable is not empty. Syncing against an empty map would report "nothing
            // tracked" and, worse, the save below would overwrite the corrupt file with {}.
            if (pushedMap == null)
            {
                AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                    "The escalation record could not be read, so nothing was synced and the file was left untouched:\n" +
                    pushedErr + "\n\nRepair or restore it (JSON: clash signature -> ACC issue id), then re-run.");
                StingLog.Warn("ACC_SyncIssueStatus REFUSED: " + pushedErr);
                return Result.Failed;
            }
            // A15: the origin record keeps every escalation STING raised, closed or not, so
            // ACC_ImportIssues can still recognise it after this sync stops tracking it.
            string originsPath = AccPullClashesCommand.OriginsPath(doc);
            var origins = AccIssueOrigins.Load(originsPath, out string originsErr);
            if (origins == null)
            {
                AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                    "The ACC issue origin record could not be read, so nothing was un-tracked (un-tracking would lose " +
                    "the only record that STING raised those issues):\n" + originsErr);
                StingLog.Warn("ACC_SyncIssueStatus REFUSED: " + originsErr);
                return Result.Failed;
            }
            if (pushedMap.Count == 0)
            {
                AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                    "No escalated clashes are tracked yet.\n\nRun ACC Pull Clashes and push some to ACC Issues first.");
                return Result.Succeeded;
            }

            AccFetchResult<List<AccIssue>> pull;
            try { pull = AccIssueSync.PullIssuesAsync(creds).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC SyncIssueStatus pull", ex); AccPullClashesCommand.Report(policy, "ACC", "Issue pull failed: " + ex.Message); return Result.Failed; }

            // The load-bearing branch. Reconciling against a failed or PARTIAL read marks
            // every unseen escalation NOT_FOUND, which reads as "ACC deleted our issues",
            // and then writes the sidecar — so an expired token silently un-tracked nothing
            // while a page-2 failure un-tracked a subset and called it a full sync.
            // Nothing below this point may run unless the whole list was read.
            if (!pull.Succeeded)
            {
                AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                    AccCommandOutcome.FailureMessage("the ACC issue list", pull.Status, pull.HttpStatus,
                        pull.Detail, creds.ProjectId) +
                    "\nThe escalation record was left untouched — nothing was un-tracked.");
                StingLog.Warn($"ACC_SyncIssueStatus FAILED ({pull.Status}) reading issues on container " +
                              $"'{creds.ProjectId}': {pull.Detail}");
                return Result.Failed;
            }
            var issues = pull.Value;

            var statusById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var i in issues)
                if (!string.IsNullOrEmpty(i.Id)) statusById[i.Id] = i.Status;

            int closed = 0, open = 0, missing = 0;
            var rows = new List<string> { "Signature,IssueId,Status,Action" };
            var toUntrack = new List<string>();
            foreach (var kv in pushedMap)
            {
                if (!statusById.TryGetValue(kv.Value, out string st))
                {
                    missing++;
                    rows.Add($"{Csv(kv.Key)},{Csv(kv.Value)},NOT_FOUND,keep");
                    continue;
                }
                if (AccIssueSync.IsClosedStatus(st))
                {
                    closed++;
                    toUntrack.Add(kv.Key);
                    rows.Add($"{Csv(kv.Key)},{Csv(kv.Value)},{Csv(st)},untrack");
                }
                else { open++; rows.Add($"{Csv(kv.Key)},{Csv(kv.Value)},{Csv(st)},keep"); }
            }

            // Origins FIRST: an escalation is only un-tracked once its origin is on disk.
            if (toUntrack.Count > 0 && origins.Absorb(AccIssueImport.ClashEscalationOrigin, pushedMap, DateTime.UtcNow) > 0 &&
                !origins.TrySave(originsPath, out string originSaveErr))
            {
                AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                    $"{closed} escalated clash(es) are closed in ACC, but the origin record could not be written, so " +
                    "nothing was un-tracked:\n" + originSaveErr);
                StingLog.Warn("ACC_SyncIssueStatus: origin record save failed — " + originSaveErr);
                return Result.Failed;
            }
            foreach (var sig in toUntrack) pushedMap.Remove(sig);   // closed → re-raise on recurrence
            string saveErr = toUntrack.Count > 0 ? AccPullClashesCommand.SavePushed(sidecar, pushedMap) : null;

            string csvPath = null;
            try
            {
                csvPath = OutputLocationHelper.GetRoutedPath(doc, "Issue", $"STING_ACC_IssueSync_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(csvPath, rows, Encoding.UTF8);
            }
            catch (Exception ex) { StingLog.Warn("ACC IssueSync CSV: " + ex.Message); }

            var sb = new StringBuilder();
            sb.AppendLine($"Escalated clashes tracked: {closed + open + missing}");
            sb.AppendLine($"Now CLOSED in ACC:         {closed}  (untracked — will re-raise if they recur)");
            sb.AppendLine($"Still open:                {open}");
            sb.AppendLine($"Issue not found:           {missing}  (kept; may have been deleted in ACC)");
            sb.AppendLine($"Still tracked after sync:  {pushedMap.Count}");
            if (csvPath != null) { sb.AppendLine(); sb.AppendLine("CSV: " + csvPath); }

            AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                $"{closed} escalated clash(es) resolved in ACC\n\n" + sb.ToString());
            StingLog.Info($"ACC_SyncIssueStatus: closed={closed} open={open} missing={missing} tracked={pushedMap.Count}");
            return Result.Succeeded;
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
