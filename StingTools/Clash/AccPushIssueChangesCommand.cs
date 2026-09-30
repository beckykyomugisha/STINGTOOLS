// AccPushIssueChangesCommand.cs — STING issue register → ACC Issues (ACC_PushIssueChanges).
//
// The other direction of ACC_ImportIssues. An issue imported from ACC and then changed in
// STING (closed on site, reassigned in a coordination meeting) is reported by the import as
// "changed in STING, not in ACC" and kept locally. This command sends those changes:
//
//   1. Candidates = register rows with an ACC id whose status / assignee differ from the
//      base recorded at the last import (AccIssuePush.FindCandidates).
//   2. Each is RE-READ from ACC. A field ACC changed since the base is a CONFLICT: reported,
//      skipped, never overwritten.
//   3. The status must round-trip and be in ACC's permittedStatuses for this user (when ACC
//      says); the assignee must be an editable attribute. Otherwise reported, not sent.
//   4. PATCH status / assignee, then POST a comment carrying the STING status note and any
//      register comments not yet sent.
//   5. The row's base advances ONLY for what ACC confirmed. A failed or ambiguous PATCH
//      leaves the row as it was, so the next run offers the same change again.
//
// Interactive: a preview, then an explicit Yes. Unattended (acc_settings.json
// "unattended": true): writes only when "pushIssueChanges": true, otherwise it reports what
// WOULD be sent and writes nothing to ACC. See AccOperatingPolicy.IssuePushMode.
//
// Manual transaction mode because it writes the issue register (JSON, through IssueBatch);
// it opens no Revit transaction and changes no model element.

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
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccPushIssueChangesCommand : IExternalCommand
    {
        private const string Title = "ACC — Push Issue Changes";

        /// <summary>Upper bound per run: every candidate costs a read and up to two writes
        /// against a rate-limited API. The rest are offered on the next run.</summary>
        private const int MaxPerRun = 200;

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_PushIssueChanges");
            var mode = policy.IssuePushMode;
            bool interactive = mode == AccIssuePushMode.PreviewAndConfirm;

            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC push issue changes");
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                return AccProjectSettingsFile.NotConfigured(policy, creds, "ACC — Push Issue Changes", "Nothing was sent.", interactive);
            }

            string user = Environment.UserName;
            try { user = doc.Application?.Username ?? user; }
            catch (Exception ex) { StingLog.Warn("ACC_PushIssueChanges user: " + ex.Message); }

            using (var batch = IssueStore.Begin(doc))
            {
                if (!batch.Ok)
                {
                    Say(interactive, "The STING issue register (issues.json) exists but could not be read, so " +
                                     "nothing was sent — there is no trustworthy record of what changed.");
                    return Result.Failed;
                }

                var candidates = AccIssuePush.FindCandidates(batch.Rows);
                if (candidates.Count == 0)
                {
                    Say(interactive, "No ACC issue has been changed in STING since it was last imported. Nothing to send.\n\n" +
                                     "(Only issues imported with 'Import Issues' carry the record this needs.)");
                    return Result.Succeeded;
                }
                int deferred = Math.Max(0, candidates.Count - MaxPerRun);
                candidates = candidates.Take(MaxPerRun).ToList();

                // ── Plan: re-read every candidate from ACC ────────────────────────
                var plans = new List<AccPushPlanItem>();
                var readFailures = new List<string>();

                // A changed assignee is resolved against the project's members (one cached read),
                // the same way clash escalation resolves its configured assignee.
                AccProjectDirectory members = null;
                string membersFailure = "";
                if (candidates.Any(c => c.Changes.Any(ch => ch.Field == "assigned_to" && !string.IsNullOrWhiteSpace(ch.LocalValue))))
                {
                    try
                    {
                        var dir = AccProjectMembers.GetDirectoryAsync(creds).GetAwaiter().GetResult();
                        if (dir.Succeeded) members = dir.Value; else membersFailure = dir.Detail;
                    }
                    catch (Exception ex) { membersFailure = ex.Message; }
                }

                foreach (var c in candidates)
                {
                    AccFetchResult<AccIssue> cur;
                    try { cur = AccIssueSync.GetIssueAsync(creds, c.AccIssueId).GetAwaiter().GetResult(); }
                    catch (Exception ex) { readFailures.Add($"{c.IssueId}: {ex.Message}"); continue; }
                    if (!cur.Succeeded)
                    {
                        readFailures.Add($"{c.IssueId}: {cur.Detail}");
                        if (cur.Status == AccFetchStatus.AuthFailed) break;   // every other read fails the same way
                        continue;
                    }
                    plans.Add(AccIssuePush.Plan(c, cur.Value, members, membersFailure));
                }

                var ready = plans.Where(p => p.HasWrite).ToList();
                var preview = Describe(plans, readFailures, deferred);

                // Fields ACC already agrees with need no write; the base just catches up. Safe
                // in every mode because it records a fact ACC itself just reported.
                foreach (var p in plans.Where(p => p.AgreeFields.Count > 0))
                {
                    AccIssuePush.ApplyAgreement(p);
                    batch.MarkModified();
                }

                if (ready.Count == 0)
                {
                    batch.Commit();
                    Say(interactive, "Nothing can be sent.\n\n" + preview);
                    return readFailures.Count > 0 ? Result.Failed : Result.Succeeded;
                }

                if (mode == AccIssuePushMode.ReportOnly)
                {
                    batch.Commit();
                    StingLog.Info($"{Title}: unattended project without \"pushIssueChanges\": true — {ready.Count} " +
                                  "change(s) WOULD be sent; nothing was written to ACC.\n" + preview);
                    return Result.Succeeded;
                }

                if (mode == AccIssuePushMode.PreviewAndConfirm)
                {
                    var dlg = new TaskDialog(Title)
                    {
                        MainInstruction = $"Send {ready.Count} change(s) to ACC?",
                        MainContent = preview + "\n\nEach change is written to the ACC issue under your Autodesk " +
                                      "sign-in and appears in its activity log.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                        AllowCancellation = true,
                    };
                    if (dlg.Show() != TaskDialogResult.Yes)
                    {
                        batch.Commit();
                        return Result.Cancelled;
                    }
                }

                // ── Write ──────────────────────────────────────────────────────────
                var log = new List<string[]>();   // IssueId, AccIssueId, Action, Detail
                int pushed = 0, failed = 0, commented = 0, commentFailed = 0, ambiguous = 0;
                foreach (var p in ready)
                {
                    var c = p.Candidate;
                    AccWriteResult w;
                    try { w = AccIssueSync.PatchIssueAsync(creds, c.AccIssueId, p.Patch).GetAwaiter().GetResult(); }
                    catch (Exception ex) { w = new AccWriteResult { Detail = ex.Message }; }

                    if (!w.Ok)
                    {
                        failed++;
                        if (w.Ambiguous) ambiguous++;
                        log.Add(new[] { c.IssueId, c.AccIssueId, "FAILED", w.Detail });
                        if (w.Status == AccFetchStatus.AuthFailed && w.HttpStatus != 403) break;   // sign-in gone
                        continue;
                    }

                    AccIssuePush.ApplyPushed(p, DateTime.Now);
                    batch.MarkModified();
                    pushed++;
                    log.Add(new[] { c.IssueId, c.AccIssueId, "pushed", p.Patch.ToString(Newtonsoft.Json.Formatting.None) });
                    SafeAudit(doc, c, p);

                    if (!string.IsNullOrWhiteSpace(p.CommentText))
                    {
                        AccWriteResult cw;
                        try { cw = AccIssueSync.AddCommentAsync(creds, c.AccIssueId, p.CommentText).GetAwaiter().GetResult(); }
                        catch (Exception ex) { cw = new AccWriteResult { Detail = ex.Message }; }
                        if (cw.Ok)
                        {
                            AccIssuePush.MarkCommentsPushed(c.Row);
                            commented++;
                        }
                        else
                        {
                            // The PATCH stands; the comments stay unsent and are offered again
                            // with the next change to this issue.
                            commentFailed++;
                            log.Add(new[] { c.IssueId, c.AccIssueId, "comment FAILED", cw.Detail });
                        }
                    }
                }

                try { batch.Commit(); }
                catch (Exception ex)
                {
                    StingLog.Error("ACC_PushIssueChanges: register write failed after pushing", ex);
                    Say(interactive, $"{pushed} change(s) reached ACC, but the STING register could not be updated " +
                                     $"({ex.Message}). The next Import Issues run will see ACC agreeing with STING and " +
                                     "record it — nothing is lost, but do not push again before importing.");
                    return Result.Failed;
                }

                foreach (var p in plans)
                {
                    foreach (var cf in p.Conflicts)
                        log.Add(new[] { cf.IssueId, cf.AccIssueId, "CONFLICT", $"{cf.Field}: STING '{cf.LocalValue}' / ACC '{cf.AccValue}' / base '{cf.BaseValue}'" });
                    foreach (var n in p.NotPushed)
                        log.Add(new[] { p.Candidate.IssueId, p.Candidate.AccIssueId, "not pushed", n });
                }
                foreach (var r in readFailures) log.Add(new[] { r, "", "read FAILED", "" });
                string csv = WriteCsv(doc, log);

                var sb = new StringBuilder();
                sb.AppendLine($"Sent to ACC:        {pushed}");
                if (commented > 0 || commentFailed > 0)
                    sb.AppendLine($"Comments posted:    {commented}" + (commentFailed > 0 ? $"  ({commentFailed} FAILED — offered again next time)" : ""));
                if (failed > 0)
                    sb.AppendLine($"FAILED:             {failed}  (register unchanged for these; offered again next run)" +
                                  (ambiguous > 0 ? $" — {ambiguous} got no clear answer and MAY have been applied: check them in ACC" : ""));
                sb.AppendLine();
                sb.AppendLine(preview);
                foreach (var l in log.Where(x => x[2].Contains("FAILED")).Take(5))
                    sb.AppendLine($"  {l[0]}: {l[3]}");
                if (csv != null) { sb.AppendLine(); sb.AppendLine("CSV: " + csv); }

                if (interactive) new TaskDialog(Title) { MainInstruction = $"{pushed} change(s) sent to ACC", MainContent = sb.ToString() }.Show();
                else StingLog.Info($"{Title}: {sb}");
                StingLog.Info($"ACC_PushIssueChanges: candidates={candidates.Count} ready={ready.Count} pushed={pushed} " +
                              $"failed={failed} ambiguous={ambiguous} comments={commented}/{commentFailed} " +
                              $"conflicts={plans.Sum(p => p.Conflicts.Count)} readFailures={readFailures.Count}");
                return failed > 0 || readFailures.Count > 0 ? Result.Failed : Result.Succeeded;
            }
        }

        private static string Describe(List<AccPushPlanItem> plans, List<string> readFailures, int deferred)
        {
            var sb = new StringBuilder();
            var ready = plans.Where(p => p.HasWrite).ToList();
            sb.AppendLine($"Ready to send:      {ready.Count}");
            foreach (var p in ready.Take(10))
            {
                var parts = new List<string>();
                if (p.AccStatus != null) parts.Add("status → " + p.AccStatus);
                if (p.PushFields.Contains("assigned_to"))
                    parts.Add("assignee → " + ((string)p.Patch["assignedTo"] ?? "(none)"));
                if (p.CommentText != null) parts.Add("+ comment");
                sb.AppendLine($"  {p.Candidate.IssueId} (ACC {p.Candidate.AccIssueId}): {string.Join(", ", parts)}");
            }
            if (ready.Count > 10) sb.AppendLine($"  … and {ready.Count - 10} more");
            int conflicts = plans.Sum(p => p.Conflicts.Count);
            sb.AppendLine($"Conflicts (skipped):{conflicts,4}  (changed in both STING and ACC — reconcile, then import)");
            foreach (var c in plans.SelectMany(p => p.Conflicts).Take(5))
                sb.AppendLine($"  {c.IssueId} [{c.Field}] STING '{c.LocalValue}' / ACC '{c.AccValue}'");
            var notes = plans.SelectMany(p => p.NotPushed.Select(n => $"{p.Candidate.IssueId}: {n}")).ToList();
            if (notes.Count > 0)
            {
                sb.AppendLine($"Not sent:           {notes.Count}");
                foreach (var n in notes.Take(5)) sb.AppendLine("  " + n);
                if (notes.Count > 5) sb.AppendLine($"  … and {notes.Count - 5} more (see CSV)");
            }
            if (readFailures.Count > 0)
            {
                sb.AppendLine($"Could not read:     {readFailures.Count}");
                foreach (var r in readFailures.Take(3)) sb.AppendLine("  " + r);
            }
            if (deferred > 0) sb.AppendLine($"Deferred:           {deferred} (more than {MaxPerRun} in one run — run again)");
            return sb.ToString().TrimEnd();
        }

        private static void SafeAudit(Document doc, AccPushCandidate c, AccPushPlanItem p)
        {
            try
            {
                Planscape.Docs.Workflow.AuditLog.Append(doc, "issue.acc_pushed", c.IssueId, new JObject
                {
                    ["issue_id"] = c.IssueId,
                    ["acc_issue_id"] = c.AccIssueId,
                    ["patch"] = p.Patch.DeepClone(),
                    ["comment"] = p.CommentText != null,
                });
            }
            catch (Exception ex) { StingLog.Warn("ACC_PushIssueChanges audit: " + ex.Message); }
        }

        private static string WriteCsv(Document doc, List<string[]> rows)
        {
            try
            {
                var lines = new List<string> { "IssueId,AccIssueId,Action,Detail" };
                lines.AddRange(rows.Select(r => string.Join(",", r.Select(Csv))));
                string path = OutputLocationHelper.GetRoutedPath(doc, "Issue",
                    $"STING_ACC_IssuePush_{DateTime.Now:yyyyMMdd_HHmm}.csv");
                File.WriteAllLines(path, lines, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("ACC_PushIssueChanges CSV: " + ex.Message); return null; }
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        private static void Say(bool interactive, string text)
        {
            if (interactive) TaskDialog.Show(Title, text);
            else StingLog.Info($"{Title}: {text}");
        }
    }
}
