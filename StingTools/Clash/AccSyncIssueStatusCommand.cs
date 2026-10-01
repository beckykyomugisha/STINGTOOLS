// AccSyncIssueStatusCommand.cs — ACC → STING issue-closure reconciliation.
//
// The other half of the loop: ACC_PullClashes escalates triaged clashes to ACC
// Issues and records each (clash signature → ACC issue id) in
// _BIM_COORD/acc/pushed_clashes.json. This command pulls the current ACC issue
// statuses and reconciles them against that escalation log:
//   - counts how many escalated clashes are now CLOSED in ACC,
//   - UNTRACKS a closed one only when its clash is absent from the latest COMPLETE
//     pull (acc/acc_clash_presence.json), so a real recurrence is re-raised. One
//     closed or voided while the clash persists is HELD (closedInAcc in the origin
//     record) and ACC_PullClashes will not raise it again while it persists (E1).
//     An issue deleted in ACC (NOT_FOUND) is untracked and reported. The ORIGIN of each one stays in acc/acc_issue_origins.json, which
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

            // E1: the decision is Revit-free (AccEscalationReconcile). An escalation leaves
            // tracking for good only when its clash is absent from a COMPLETE pull; one closed
            // or voided in ACC while the clash persists is HELD, so the pull stops re-raising it.
            string presencePath = Path.Combine(Path.GetDirectoryName(sidecar) ?? string.Empty, AccClashPresence.FileName);
            var presence = AccClashPresence.Load(presencePath, out string presenceErr);
            if (presence == null)
                StingLog.Warn("ACC_SyncIssueStatus: clash presence record unreadable, closed escalations are held, not resolved: " + presenceErr);
            var present = presence?.Present();   // null: no complete pull known -> hold, never assume gone
            var decisions = AccEscalationReconcile.Decide(pushedMap, statusById, AccIssueSync.IsClosedStatus, present);

            int closed = 0, held = 0, open = 0, missing = 0;
            var rows = new List<string> { "Signature,IssueId,Status,Action" };
            var toUntrack = new List<string>();
            var now = DateTime.UtcNow;
            foreach (var d in decisions)
            {
                string action;
                switch (d.Action)
                {
                    case AccEscalationAction.Untrack: closed++; action = "untrack (resolved)"; break;
                    case AccEscalationAction.HoldClosedStillClashing:
                        held++; action = "untrack + hold (closed in ACC, still clashing)";
                        origins.Hold(AccIssueImport.ClashEscalationOrigin, d.Signature, d.IssueId, d.Status, now);
                        break;
                    case AccEscalationAction.UntrackNotFound: missing++; action = "untrack (issue not found in ACC)"; break;
                    default: open++; action = "keep"; break;
                }
                if (d.RemovesFromTracking) toUntrack.Add(d.Signature);
                rows.Add($"{Csv(d.Signature)},{Csv(d.IssueId)},{Csv(d.Status)},{Csv(action)}");
            }

            // Origins FIRST: an escalation is only un-tracked once its origin (and any hold) is on disk.
            if (toUntrack.Count > 0)
            {
                origins.Absorb(AccIssueImport.ClashEscalationOrigin, pushedMap, now);
                if (!origins.TrySave(originsPath, out string originSaveErr))
                {
                    AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                        $"{toUntrack.Count} escalation(s) would leave tracking, but the origin record could not be written, so " +
                        "nothing was un-tracked:\n" + originSaveErr);
                    StingLog.Warn("ACC_SyncIssueStatus: origin record save failed — " + originSaveErr);
                    return Result.Failed;
                }
            }
            foreach (var sig in toUntrack) pushedMap.Remove(sig);
            string saveErr = toUntrack.Count > 0 ? AccPullClashesCommand.SavePushed(sidecar, pushedMap) : null;

            string csvPath = null;
            try
            {
                csvPath = OutputLocationHelper.GetRoutedPath(doc, "Issue", $"STING_ACC_IssueSync_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(csvPath, rows, Encoding.UTF8);
            }
            catch (Exception ex) { StingLog.Warn("ACC IssueSync CSV: " + ex.Message); }

            var sb = new StringBuilder();
            sb.AppendLine($"Escalated clashes tracked: {decisions.Count}");
            sb.AppendLine($"Resolved (closed in ACC, clash gone from the latest complete pull): {closed}  (untracked — re-raised if they recur)");
            sb.AppendLine($"Closed in ACC, STILL CLASHING: {held}  (held — not re-raised while the clash persists)");
            if (present == null && held > 0)
                sb.AppendLine("  (no complete clash pull is on record, so a closed escalation cannot be shown resolved — run ACC Pull Clashes)");
            sb.AppendLine($"Still open:                {open}");
            sb.AppendLine($"Issue NOT FOUND in ACC:    {missing}  (untracked — deleted in ACC; re-raised if the clash is still present)");
            sb.AppendLine($"Still tracked after sync:  {pushedMap.Count}");
            if (!string.IsNullOrEmpty(saveErr)) sb.AppendLine("WARNING: the tracking record could not be saved: " + saveErr);
            if (csvPath != null) { sb.AppendLine(); sb.AppendLine("CSV: " + csvPath); }

            AccPullClashesCommand.Report(policy, "ACC — Sync Issue Status",
                $"{closed} escalated clash(es) resolved in ACC\n\n" + sb.ToString());
            StingLog.Info($"ACC_SyncIssueStatus: resolved={closed} heldStillClashing={held} open={open} notFound={missing} tracked={pushedMap.Count}");
            return string.IsNullOrEmpty(saveErr) ? Result.Succeeded : Result.Failed;
        }

        private static string Csv(string s) => AccCsv.Cell(s);   // E10: quoted + formula-guarded
    }
}
