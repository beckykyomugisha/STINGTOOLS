// AccImportIssuesCommand.cs — ACC Issues → STING issue register (ACC_ImportIssues).
//
// Pulls every issue in the project's ACC Issues container and merges it into the STING
// issue register (IssueStore → CoordStores.Issues), so the BIM Coordination Center issue
// list, its KPIs and the `has_open_issues` workflow gate see issues coordinators raised in
// ACC. The merge rules (identity, the four ACC-owned fields, three-way conflict detection,
// never-delete, the round trip with STING's own escalations) live in the Revit-free
// V6/AccIssueImport.cs and are tested there; this file is the shell.
//
// ReadOnly: it changes no model element. It writes only the JSON issue register (through
// IssueBatch, which owns the path, the atomic write, id minting and the audit chain) and a
// routed CSV report.
//
// A failed or PARTIAL pull is refused before the register is opened. Merging a partial
// read would be harmless for creates but would report every unseen ACC issue as missing,
// and a coordinator reading "N issues no longer in ACC" would act on a lie.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccImportIssuesCommand : IExternalCommand
    {
        private const string Title = "ACC — Import Issues";

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Unattended projects (a scheduled KUT cycle) must not sit on a modal dialog.
            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_ImportIssues");

            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC import issues");   // IM-18
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                AccPullClashesCommand.Report(policy, Title,
                    "ACC credentials are not configured (acc_credentials.json). Nothing was imported.");
                StingLog.Warn("ACC_ImportIssues: credentials not configured — nothing imported.");
                return Result.Cancelled;
            }

            AccFetchResult<List<AccIssue>> pull;
            try { pull = AccIssueSync.PullIssuesAsync(creds).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                StingLog.Error("ACC_ImportIssues pull", ex);
                AccPullClashesCommand.Report(policy, Title,
                    "Issue pull failed: " + ex.Message + "\nThe STING issue register was left untouched.");
                return Result.Failed;
            }

            // Nothing below may run on an incomplete read. See the file header.
            if (!pull.Succeeded)
            {
                AccPullClashesCommand.Report(policy, Title,
                    AccCommandOutcome.FailureMessage("the ACC issue list", pull.Status, pull.HttpStatus,
                        pull.Detail, creds.ProjectId) +
                    "\nThe STING issue register was left untouched — nothing was imported.");
                StingLog.Warn($"ACC_ImportIssues FAILED ({pull.Status}) reading issues on container " +
                              $"'{creds.ProjectId}': {pull.Detail}");
                return Result.Failed;
            }

            // Round trip: ACC issues STING itself created, keyed by ACC id.
            var origins = new Dictionary<string, AccOriginLink>(StringComparer.Ordinal);
            string clashSidecar = AccPullClashesCommand.SidecarPath(doc);
            AccOriginLink.AddSidecar(origins, AccIssueImport.ClashEscalationOrigin,
                AccPullClashesCommand.LoadPushed(clashSidecar));
            try
            {
                string gapSidecar = Path.Combine(Path.GetDirectoryName(clashSidecar) ?? "", "pushed_lifecycle_gaps.json");
                AccOriginLink.AddSidecar(origins, AccIssueImport.LifecycleGapOrigin,
                    AccPullClashesCommand.LoadPushed(gapSidecar));
            }
            catch (Exception ex) { StingLog.Warn("ACC_ImportIssues lifecycle-gap sidecar: " + ex.Message); }

            string user = Environment.UserName;
            try { user = doc.Application?.Username ?? user; }
            catch (Exception ex) { StingLog.Warn("ACC_ImportIssues user: " + ex.Message); }

            AccIssueImportResult result;
            using (var batch = IssueStore.Begin(doc))
            {
                if (!batch.Ok)
                {
                    AccPullClashesCommand.Report(policy, Title,
                        "The STING issue register (issues.json) exists but could not be read, so nothing " +
                        "was imported — writing to it would overwrite a live register with a partial one.");
                    StingLog.Warn("ACC_ImportIssues refused: issues.json unreadable.");
                    return Result.Failed;
                }

                result = AccIssueImport.Merge(batch.Rows,
                    pull.Value.Select(AccIssueImportRecord.From).Where(r => r != null),
                    origins, new IssueBatchAccWriter(batch), DateTime.Now, user);
                batch.Commit();
            }

            string csvPath = WriteReport(doc, result);

            var sb = new StringBuilder();
            sb.AppendLine($"ACC issues read:          {result.Pulled}" +
                          (pull.Status == AccFetchStatus.EmptyOk ? "  (the container has no issues)" : ""));
            sb.AppendLine($"New in STING:             {result.Created.Count}");
            sb.AppendLine($"Updated from ACC:         {result.Updated.Count}  ({result.StatusChanges} status change(s))");
            if (result.Linked.Count > 0)
                sb.AppendLine($"Linked to STING rows:     {result.Linked.Count}  (issues STING pushed to ACC)");
            sb.AppendLine($"Unchanged:                {result.Unchanged}");
            sb.AppendLine($"CONFLICTS:                {result.Conflicts.Count}  (edited in both STING and ACC — local value kept)");
            sb.AppendLine($"No longer in ACC:         {result.MissingFromAcc.Count}  (kept in STING, not deleted)");
            if (result.Skipped > 0)
                sb.AppendLine($"Skipped:                  {result.Skipped}  (no id, or repeated in the pull)");
            if (result.Conflicts.Count > 0)
            {
                sb.AppendLine();
                foreach (var c in result.Conflicts.Take(10))
                    sb.AppendLine($"  {c.IssueId} [{c.Field}]  STING: '{Short(c.LocalValue)}'  ACC: '{Short(c.AccValue)}'");
                if (result.Conflicts.Count > 10) sb.AppendLine($"  … and {result.Conflicts.Count - 10} more (see CSV)");
            }
            if (csvPath != null) { sb.AppendLine(); sb.AppendLine("CSV: " + csvPath); }

            if (policy.MayPrompt)
            {
                new TaskDialog(Title)
                {
                    MainInstruction = result.Conflicts.Count > 0
                        ? $"{result.Conflicts.Count} conflict(s) need a decision"
                        : $"{result.Created.Count} new, {result.Updated.Count} updated from ACC",
                    MainContent = sb.ToString(),
                }.Show();
            }
            else StingLog.Info($"{Title}: {sb}");

            StingLog.Info($"ACC_ImportIssues: pulled={result.Pulled} created={result.Created.Count} " +
                          $"updated={result.Updated.Count} linked={result.Linked.Count} status={result.StatusChanges} " +
                          $"conflicts={result.Conflicts.Count} missing={result.MissingFromAcc.Count} skipped={result.Skipped}");
            return Result.Succeeded;
        }

        private static string Short(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 40 ? s : s.Substring(0, 37) + "...";
        }

        private static string WriteReport(Document doc, AccIssueImportResult r)
        {
            try
            {
                var rows = new List<string> { "IssueId,AccIssueId,Action,Field,Base,Local,Acc" };
                foreach (var x in r.Created)  rows.Add(Line(x, "created"));
                foreach (var x in r.Linked)   rows.Add(Line(x, "linked"));
                foreach (var x in r.Updated)  rows.Add(Line(x, "updated"));
                foreach (var c in r.Conflicts)
                    rows.Add(string.Join(",", Csv(c.IssueId), Csv(c.AccIssueId), "CONFLICT",
                                         Csv(c.Field), Csv(c.BaseValue), Csv(c.LocalValue), Csv(c.AccValue)));
                foreach (var id in r.MissingFromAcc) rows.Add($"{Csv(id)},,not_in_acc,,,,");
                string path = OutputLocationHelper.GetRoutedPath(doc, "Issue",
                    $"STING_ACC_IssueImport_{DateTime.Now:yyyyMMdd_HHmm}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("ACC_ImportIssues CSV: " + ex.Message); return null; }
        }

        private static string Line(JObject row, string action)
            => $"{Csv(IssueSchema.IdOf(row))},{Csv((string)row[AccIssueImport.AccIdField])},{action},,,,";

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        /// <summary>Routes the merge's writes through the register's batch, so created rows get
        /// minted ids, the audit entry and the server push, and status changes are audited.</summary>
        private sealed class IssueBatchAccWriter : IAccIssueWriter
        {
            private readonly IssueBatch _batch;
            public IssueBatchAccWriter(IssueBatch batch) { _batch = batch; }
            public string MintId(string type) => _batch.MintId(type);
            public void Add(JObject row) => _batch.Adopt(row, IssueSource.Acc);
            public bool SetStatus(JObject row, string canonicalStatus, string note)
                => _batch.SetStatus(IssueSchema.IdOf(row), canonicalStatus, note);
            public void Touched(JObject row) => _batch.MarkModified();
        }
    }
}
