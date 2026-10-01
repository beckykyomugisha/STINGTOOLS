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
//
// INCREMENTAL. After a successful import the start time of its pull is recorded in
// _BIM_COORD/acc/acc_issue_import_state.json (AccIssueImportState). The next run asks ACC
// only for issues updated since then (filter[updatedAt]); such a read is merged with
// completePull: false, so "no longer in ACC" is not reported from it. A full read happens
// when there is no state, the container changed, the last full read is over a week old, or
// a person asks for one (ACC_ImportIssuesFull / the card's "Import Issues (full)").
// The watermark moves only when pull, merge and register write all succeeded.
//
// AUTO-IMPORT. AccIssueRealtimeBridge queues this command when the Planscape server relays
// an ACC issue webhook. Such a run shows no dialog (nobody asked for it at that moment) and
// logs instead; it re-checks "autoImportIssues": true here, on the Revit thread, against the
// ACTIVE document, so the signal cannot import into a project that did not opt in.

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

        // Set by AccIssueRealtimeBridge immediately before it queues this command; consumed here.

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
            => Run(cmd, forceFull: false);

        /// <param name="automatic">True only when AccIssueRealtimeBridge's own ExternalEvent runs
        /// it for a server signal. Passed explicitly — never a shared flag a manual run can pick up.</param>
        internal Result Run(ExternalCommandData cmd, bool forceFull, bool automatic = false)
        {
            bool auto = automatic;
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null)
            {
                if (auto) { StingLog.Info("ACC_ImportIssues (auto): no document open — skipped."); return Result.Cancelled; }
                TaskDialog.Show("STING", "No document open."); return Result.Failed;
            }
            Document doc = ctx.Doc;

            // Unattended projects (a scheduled KUT cycle) must not sit on a modal dialog.
            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_ImportIssues");
            if (auto && !policy.AutoImportIssues)
            {
                StingLog.Info("ACC_ImportIssues (auto): an ACC issue signal arrived, but this project's " +
                              "acc_settings.json does not set \"autoImportIssues\": true — nothing imported.");
                return Result.Cancelled;
            }
            bool interactive = policy.MayPrompt && !auto;

            string statePath = StatePath(doc);
            var state = AccIssueImportState.Load(statePath, out string stateWarn);
            if (!string.IsNullOrEmpty(stateWarn)) StingLog.Warn("ACC_ImportIssues: " + stateWarn);

            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC import issues");   // IM-18
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                return AccProjectSettingsFile.NotConfigured(policy, creds, "ACC — Import Issues", "Nothing was imported.", interactive);
            }

            DateTime pullStartedUtc = DateTime.UtcNow;
            DateTime? since = state.SinceFor(creds.ProjectId, forceFull, pullStartedUtc, out string windowReason);
            StingLog.Info("ACC_ImportIssues: " + windowReason);

            AccFetchResult<List<AccIssue>> pull;
            try { pull = AccIssueSync.PullIssuesAsync(creds, updatedSince: since).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                StingLog.Error("ACC_ImportIssues pull", ex);
                Say(interactive,
                    "Issue pull failed: " + ex.Message + "\nThe STING issue register was left untouched.");
                return Result.Failed;
            }

            // Nothing below may run on an incomplete read. See the file header.
            if (!pull.Succeeded)
            {
                Say(interactive,
                    AccCommandOutcome.FailureMessage("the ACC issue list", pull.Status, pull.HttpStatus,
                        pull.Detail, creds.ProjectId) +
                    "\nThe STING issue register was left untouched — nothing was imported.");
                StingLog.Warn($"ACC_ImportIssues FAILED ({pull.Status}) reading issues on container " +
                              $"'{creds.ProjectId}': {pull.Detail}");
                return Result.Failed;
            }

            // Round trip: ACC issues STING itself created, keyed by ACC id.
            //
            // A15: the append-only origin record comes FIRST. It is the one source nothing
            // prunes - ACC_SyncIssueStatus removes closed escalations from pushed_clashes.json,
            // and reading only that file lost the origin of every issue ACC had closed. The two
            // tracking sidecars are still read after it, for escalations that predate it.
            var linkNotes = new List<string>();
            var origins = new Dictionary<string, AccOriginLink>(StringComparer.Ordinal);
            string clashSidecar = AccPullClashesCommand.SidecarPath(doc);
            string accDir = Path.GetDirectoryName(clashSidecar) ?? "";
            var originRecord = AccIssueOrigins.Load(Path.Combine(accDir, AccIssueOrigins.FileName), out string originErr);
            if (originRecord != null) originRecord.AddTo(origins);
            else linkNotes.Add("the ACC issue origin record could not be read, so issues STING raised may import " +
                               "as ACC-owned: " + originErr);
            foreach (var (file, origin) in new[]
                     {
                         (AccPushedMap.ClashFileName, AccIssueImport.ClashEscalationOrigin),
                         (AccPushedMap.LifecycleGapFileName, AccIssueImport.LifecycleGapOrigin),
                     })
            {
                var map = AccPushedMap.Load(Path.Combine(accDir, file), out string mapErr);
                if (map != null) AccOriginLink.AddSidecar(origins, origin, map);
                else linkNotes.Add($"{file} could not be read, so issues it names may import as ACC-owned: {mapErr}");
            }

            var records = pull.Value.Select(AccIssueImportRecord.From).Where(r => r != null).ToList();

            // Round trip, second source: the clash signature STING wrote into a custom attribute
            // when it escalated (issueCustomAttributes.clashSignature). Only consulted when that
            // mapping is configured; the sidecar wins where both name an issue.
            if (policy.IssueCustomAttributes.TryGetValue("clashSignature", out string sigTitle))
            {
                try
                {
                    var fields = AccIssueFields.ResolveAsync(creds,
                        new Dictionary<string, string> { ["clashSignature"] = sigTitle }, null).GetAwaiter().GetResult();
                    string defId = fields.DefinitionIdFor("clashSignature");
                    if (defId != null)
                    {
                        int n = AccOriginLink.AddFromAttribute(origins, AccIssueImport.ClashEscalationOrigin, records, defId);
                        if (n > 0) linkNotes.Add($"{n} escalated clash issue(s) recognised by their '{sigTitle}' attribute");
                    }
                    linkNotes.AddRange(fields.Problems);
                }
                catch (Exception ex) { linkNotes.Add("reading the clash-signature attribute failed: " + ex.Message); }
            }

            string user = Environment.UserName;
            try { user = doc.Application?.Username ?? user; }
            catch (Exception ex) { StingLog.Warn("ACC_ImportIssues user: " + ex.Message); }

            // Assignee NAMES. ACC gives only ids; the project's member list turns them into
            // names. Best effort: the list needs a Project/Account Admin sign-in, and an import
            // must not fail because a label is unavailable - but the report says so, and the
            // rows keep the id rather than a guessed name.
            string namesNote;
            try
            {
                var dir = AccProjectMembers.GetDirectoryAsync(creds).GetAwaiter().GetResult();
                if (dir.Succeeded)
                {
                    int named = 0, assigned = 0;
                    foreach (var r in records)
                    {
                        // E6: the raiser's name, from the same member list (the id is kept when unknown).
                        if (!string.IsNullOrEmpty(r.CreatedBy)) r.CreatedByName = dir.Value.NameFor(r.CreatedBy, "user");
                        if (string.IsNullOrEmpty(r.AssignedTo)) continue;
                        assigned++;
                        r.AssignedToName = dir.Value.NameFor(r.AssignedTo, r.AssignedToType);
                        if (r.AssignedToName.Length > 0) named++;
                    }
                    namesNote = $"{named} of {assigned} assignee(s) named from {dir.Value.Users.Count} project member(s)";
                }
                else namesNote = "assignee NAMES not available (ids kept): " + dir.Detail;
            }
            catch (Exception ex) { namesNote = "assignee NAMES not available (ids kept): " + ex.Message; }
            StingLog.Info("ACC_ImportIssues: " + namesNote);

            AccIssueImportResult result;
            using (var batch = IssueStore.Begin(doc))
            {
                if (!batch.Ok)
                {
                    Say(interactive,
                        "The STING issue register (issues.json) exists but could not be read, so nothing " +
                        "was imported — writing to it would overwrite a live register with a partial one.");
                    StingLog.Warn("ACC_ImportIssues refused: issues.json unreadable.");
                    return Result.Failed;
                }

                result = AccIssueImport.Merge(batch.Rows, records,
                    origins, new IssueBatchAccWriter(batch), DateTime.Now, user, completePull: since == null);
                try { batch.Commit(); }
                catch (Exception ex)
                {
                    // The watermark must not move past changes that never reached the register.
                    StingLog.Error("ACC_ImportIssues: writing the issue register failed", ex);
                    Say(interactive, "The ACC issues were read but the STING issue register could not be written: " +
                        ex.Message + "\nThe incremental watermark was not advanced, so the next run reads the same window again.");
                    return Result.Failed;
                }
            }

            // E8: the watermark is ACC's time (the pull's first Date header, else the newest
            // updatedAt), never this workstation's clock; the measured skew is logged.
            TimeSpan? skew = pull.ServerDateUtc.HasValue ? pull.ServerDateUtc.Value - pullStartedUtc : (TimeSpan?)null;
            if (skew.HasValue)
            {
                string skewLine = $"ACC_ImportIssues: clock skew ACC - this PC = {skew.Value.TotalSeconds:F0} s";
                if (skew.Value.Duration() > AccIssueImportState.Overlap) StingLog.Warn(skewLine + " — larger than the read overlap; the watermark uses ACC's clock");
                else StingLog.Info(skewLine);
            }
            DateTime? mark = AccIssueImportState.AccWatermark(pull.ServerDateUtc, (pull.Value ?? new List<AccIssue>()).Select(i => i.UpdatedAt),
                state.LastSuccessUtc, out string markBasis);
            StingLog.Info("ACC_ImportIssues watermark: " + markBasis);
            if (!state.RecordSuccess(creds.ProjectId, mark, wasFull: since == null, fullReadAtUtc: pull.ServerDateUtc ?? pullStartedUtc, skew: skew))
                StingLog.Warn("ACC_ImportIssues: " + markBasis + " — the next run reads the same window again.");
            if (!state.Save(statePath, out string saveErr))
                StingLog.Warn($"ACC_ImportIssues: import state not saved ({saveErr}) — the next run does a full read.");

            string csvPath = WriteReport(doc, result);

            var sb = new StringBuilder();
            sb.AppendLine("Read:                     " + windowReason);
            sb.AppendLine($"ACC issues read:          {result.Pulled}" +
                          (pull.Status == AccFetchStatus.EmptyOk
                              ? (since == null ? "  (the container has no issues)" : "  (none changed in the window)") : ""));
            sb.AppendLine($"New in STING:             {result.Created.Count}");
            sb.AppendLine($"Updated from ACC:         {result.Updated.Count}  ({result.StatusChanges} status change(s))");
            if (result.Linked.Count > 0)
                sb.AppendLine($"Linked to STING rows:     {result.Linked.Count}  (issues STING pushed to ACC)");
            sb.AppendLine($"Unchanged:                {result.Unchanged}");
            sb.AppendLine($"CONFLICTS:                {result.Conflicts.Count}  (edited in both STING and ACC — local value kept)");
            sb.AppendLine($"Changed in STING, not ACC:{result.LocalAhead.Count,4}  (kept locally; NOT sent — use 'Push Issue Changes')");
            sb.AppendLine(result.Incremental
                ? "No longer in ACC:         not checked (incremental read — a full read re-checks)"
                : $"No longer in ACC:         {result.MissingFromAcc.Count}  (kept in STING, not deleted)");
            sb.AppendLine($"Assignees:                {namesNote}");
            if (result.Skipped > 0)
                sb.AppendLine($"Skipped:                 {result.Skipped}  (no id, or repeated in the pull)");
            if (result.Conflicts.Count > 0)
            {
                sb.AppendLine();
                foreach (var c in result.Conflicts.Take(10))
                    sb.AppendLine($"  {c.IssueId} [{c.Field}]  STING: '{Short(c.LocalValue)}'  ACC: '{Short(c.AccValue)}'");
                if (result.Conflicts.Count > 10) sb.AppendLine($"  … and {result.Conflicts.Count - 10} more (see CSV)");
            }
            if (result.LocalAhead.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Changed in STING, not in ACC:");
                foreach (var l in result.LocalAhead.Take(10))
                    sb.AppendLine($"  {l.IssueId} [{l.Field}]  STING: '{Short(l.LocalValue)}'  ACC: '{Short(l.AccValue)}'");
                if (result.LocalAhead.Count > 10) sb.AppendLine($"  … and {result.LocalAhead.Count - 10} more (see CSV)");
            }
            foreach (var n in linkNotes) sb.AppendLine("Note: " + n);
            if (csvPath != null) { sb.AppendLine(); sb.AppendLine("CSV: " + csvPath); }

            if (interactive)
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
                          $"conflicts={result.Conflicts.Count} localAhead={result.LocalAhead.Count} " +
                          $"missing={(result.Incremental ? "n/a" : result.MissingFromAcc.Count.ToString())} " +
                          $"skipped={result.Skipped} incremental={result.Incremental} auto={auto}");
            return Result.Succeeded;
        }

        /// <summary>A result for whoever is there: a dialog for a person, the log otherwise.</summary>
        private static void Say(bool interactive, string text)
        {
            if (interactive) TaskDialog.Show(Title, text);
            else StingLog.Info($"{Title}: {text}");
        }

        /// <summary>_BIM_COORD/acc/acc_issue_import_state.json, resolved through StingPaths.</summary>
        internal static string StatePath(Document doc)
        {
            try
            {
                string dir = StingPaths.MetaFile(doc, "_BIM_COORD", "acc");
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, AccIssueImportState.FileName);
            }
            catch (Exception ex) { StingLog.Warn("ACC_ImportIssues state path: " + ex.Message); return null; }
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
                var rows = new List<string> { "IssueId,AccIssueId,Action,Field,Base,Local,Acc,AssigneeId,AssigneeName" };
                foreach (var x in r.Created)  rows.Add(Line(x, "created"));
                foreach (var x in r.Linked)   rows.Add(Line(x, "linked"));
                foreach (var x in r.Updated)  rows.Add(Line(x, "updated"));
                foreach (var c in r.Conflicts)
                    rows.Add(string.Join(",", Csv(c.IssueId), Csv(c.AccIssueId), "CONFLICT",
                                         Csv(c.Field), Csv(c.BaseValue), Csv(c.LocalValue), Csv(c.AccValue), "", ""));
                foreach (var l in r.LocalAhead)
                    rows.Add(string.Join(",", Csv(l.IssueId), Csv(l.AccIssueId), Csv("changed in STING, not in ACC"),
                                         Csv(l.Field), Csv(l.BaseValue), Csv(l.LocalValue), Csv(l.AccValue), "", ""));
                foreach (var id in r.MissingFromAcc) rows.Add($"{Csv(id)},,not_in_acc,,,,,,");
                string path = OutputLocationHelper.GetRoutedPath(doc, "Issue",
                    $"STING_ACC_IssueImport_{DateTime.Now:yyyyMMdd_HHmm}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("ACC_ImportIssues CSV: " + ex.Message); return null; }
        }

        private static string Line(JObject row, string action)
            => $"{Csv(IssueSchema.IdOf(row))},{Csv((string)row[AccIssueImport.AccIdField])},{action},,,,," +
               $"{Csv((string)row[AccIssueImport.AssignedIdField])},{Csv((string)row[AccIssueImport.AssignedNameField])}";

        private static string Csv(string s) => AccCsv.Cell(s);   // E10: quoted + formula-guarded

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

    /// <summary>ACC_ImportIssuesFull: the same import, reading EVERY issue regardless of the
    /// incremental watermark — the way to re-check "no longer in ACC" on demand.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccImportIssuesFullCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
            => new AccImportIssuesCommand().Run(cmd, forceFull: true);
    }
}
