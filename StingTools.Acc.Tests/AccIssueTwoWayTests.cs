// Two-way ACC issues: LocalAhead reporting in the import, the STING -> ACC push decisions,
// the incremental watermark, custom attribute resolution, the auto-import debounce, and the
// settings keys that govern them.
//
// Every "nothing was sent" / "nothing changed" assertion is paired with a non-empty case
// first, so a planner that silently did nothing cannot pass.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssueTwoWayTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 30, 9, 0, 0);
        private static readonly DateTime T1 = T0.AddHours(1);
        private static readonly DateTime T2 = T0.AddHours(2);

        private static AccIssueImportRecord Acc(string id, string title = "Duct clashes beam", string status = "open",
            string assignee = "user-1", string due = "")
            => new AccIssueImportRecord
            {
                Id = id, Title = title, Status = status, AssignedTo = assignee, Description = "L02 grid C4",
                DueDate = due, AssignedToType = "user",
            };

        private static AccIssueImportResult Merge(JArray rows, DateTime now, bool complete = true,
            Dictionary<string, AccOriginLink> origins = null, params AccIssueImportRecord[] pulled)
            => AccIssueImport.Merge(rows, pulled, origins, new JArrayAccIssueWriter(rows, now, "tester"), now, "tester", complete);

        private static JObject ByAcc(JArray rows, string accId)
            => rows.OfType<JObject>().Single(r => (string)r[AccIssueImport.AccIdField] == accId);

        private static AccIssue Current(string id, string status = "open", string assignee = "user-1",
            string title = "Duct clashes beam", List<string> permittedStatuses = null, List<string> permittedAttrs = null,
            string assigneeType = "user")
            => new AccIssue
            {
                Id = id, Title = title, Status = status, AssignedToUserId = assignee, AssignedToType = assigneeType,
                Description = "L02 grid C4", PermittedStatuses = permittedStatuses, PermittedAttributes = permittedAttrs,
            };

        // ── 1. LocalAhead ───────────────────────────────────────────────────

        [Fact]
        public void LocalAhead_ChangedInStingOnly_ReportedPerField_LocalKept_NotAConflict()
        {
            var rows = new JArray();
            Merge(rows, T0, pulled: Acc("a-1"));
            var row = ByAcc(rows, "a-1");
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed on site");
            row["assigned_to"] = "user-2";

            var r = Merge(rows, T2, pulled: Acc("a-1"));

            Assert.Empty(r.Conflicts);
            Assert.Equal(2, r.LocalAhead.Count);
            var st = r.LocalAhead.Single(l => l.Field == "status");
            Assert.Equal("CLOSED", st.LocalValue);
            Assert.Equal("OPEN", st.AccValue);
            Assert.Equal("OPEN", st.BaseValue);
            Assert.Equal("a-1", st.AccIssueId);
            Assert.Equal("CLOSED", IssueSchema.StatusOf(row));   // kept
            Assert.Equal("user-2", (string)row["assigned_to"]);
        }

        [Fact]
        public void LocalAhead_EmptyWhenNothingChangedLocally()
        {
            var rows = new JArray();
            Merge(rows, T0, pulled: Acc("a-1"));
            var r = Merge(rows, T1, pulled: Acc("a-1"));
            Assert.Single(rows);
            Assert.Empty(r.LocalAhead);
        }

        // ── 3. incremental + mapping ─────────────────────────────────────────

        [Fact]
        public void Incremental_IssueNotReturned_IsNotReportedMissing()
        {
            var rows = new JArray();
            Merge(rows, T0, pulled: new[] { Acc("a-1"), Acc("a-2") });

            var full = Merge(rows, T1, complete: true, pulled: Acc("a-2"));
            Assert.Single(full.MissingFromAcc);                        // the full read does see it

            var inc = Merge(rows, T2, complete: false, pulled: Acc("a-2", title: "renamed"));
            Assert.True(inc.Incremental);
            Assert.Empty(inc.MissingFromAcc);
            Assert.Single(inc.Updated);
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public void DueDate_IsImported_AndUpdatedFromAcc()
        {
            var rows = new JArray();
            Merge(rows, T0, pulled: Acc("a-1", due: "2026-10-15"));
            var row = ByAcc(rows, "a-1");
            Assert.Equal("2026-10-15", (string)row["date_due"]);

            Merge(rows, T1, pulled: Acc("a-1", due: "2026-10-22"));
            Assert.Equal("2026-10-22", (string)row["date_due"]);
        }

        [Fact]
        public void DueDate_RowImportedBeforeItWasOwned_NoSpuriousConflict()
        {
            // The shape the previous importer wrote: base without date_due, date_due "".
            var rows = new JArray();
            Merge(rows, T0, pulled: Acc("a-1"));
            var row = ByAcc(rows, "a-1");
            ((JObject)row[AccIssueImport.BaseField]).Remove("date_due");
            Assert.Equal("", (string)row["date_due"]);

            var r = Merge(rows, T1, pulled: Acc("a-1", due: "2026-11-01"));

            Assert.Empty(r.Conflicts);
            Assert.Equal("2026-11-01", (string)row["date_due"]);
        }

        [Fact]
        public void From_MapsDisplayIdDueDateUpdatedAtAndAttributes()
        {
            var a = new AccIssue
            {
                Id = "a-1", DisplayId = "123", DueDate = new DateTime(2026, 10, 15), AssignedToType = "company",
                UpdatedAt = new DateTime(2026, 9, 30, 8, 15, 0, DateTimeKind.Utc),
                CustomAttributes = { new AccCustomAttributeValue { AttributeDefinitionId = "def-1", Value = "11@A.rvt|22@B.rvt" } },
            };
            var r = AccIssueImportRecord.From(a);
            Assert.Equal("123", r.DisplayId);
            Assert.Equal("2026-10-15", r.DueDate);
            Assert.Equal("2026-09-30T08:15:00.000Z", r.UpdatedAt);
            Assert.Equal("company", r.AssignedToType);
            Assert.Equal("11@A.rvt|22@B.rvt", r.CustomAttributes["def-1"]);

            var rows = new JArray();
            Merge(rows, T0, pulled: r);
            var row = ByAcc(rows, "a-1");
            Assert.Equal("123", (string)row["acc_display_id"]);
            Assert.Equal("2026-09-30T08:15:00.000Z", (string)row["acc_updated_at"]);
            Assert.Equal("company", (string)row["acc_assigned_to_type"]);
        }

        [Fact]
        public void Origin_FromCustomAttribute_LinksLikeTheSidecar_SidecarWins()
        {
            var r1 = Acc("a-1"); r1.CustomAttributes["def-sig"] = "11@A.rvt|22@B.rvt";
            var r2 = Acc("a-2"); r2.CustomAttributes["def-sig"] = "from-attribute";
            var origins = new Dictionary<string, AccOriginLink>();
            AccOriginLink.AddSidecar(origins, AccIssueImport.ClashEscalationOrigin,
                new Dictionary<string, string> { ["from-sidecar"] = "a-2" });

            int added = AccOriginLink.AddFromAttribute(origins, AccIssueImport.ClashEscalationOrigin, new[] { r1, r2 }, "def-sig");

            Assert.Equal(1, added);
            Assert.Equal("11@A.rvt|22@B.rvt", origins["a-1"].Key);
            Assert.Equal("from-sidecar", origins["a-2"].Key);

            var rows = new JArray();
            Merge(rows, T0, origins: origins, pulled: r1);
            Assert.StartsWith("CLASH-", IssueSchema.IdOf(ByAcc(rows, "a-1")));
        }

        // ── 2. push planning ─────────────────────────────────────────────────

        private static (JArray rows, JObject row) Imported()
        {
            var rows = new JArray();
            Merge(rows, T0, pulled: Acc("a-1"));
            return (rows, ByAcc(rows, "a-1"));
        }

        [Fact]
        public void Push_Candidates_OnlyRowsChangedSinceBase()
        {
            var (rows, row) = Imported();
            Merge(rows, T0, pulled: Acc("a-2"));
            rows.Add(new JObject { ["issue_id"] = "RFI-0001", ["status"] = "CLOSED" });   // never imported
            Assert.Empty(AccIssuePush.FindCandidates(rows));

            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed");
            var c = Assert.Single(AccIssuePush.FindCandidates(rows));
            Assert.Equal("a-1", c.AccIssueId);
            Assert.Equal("status", Assert.Single(c.Changes).Field);
        }

        [Fact]
        public void Push_Plan_StatusRoundTrips_PatchAndComment()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed on site, photo in CDE");
            ((JArray)row["comments"]).Add("site confirmed");
            var c = AccIssuePush.FindCandidates(rows).Single();

            var p = AccIssuePush.Plan(c, Current("a-1", permittedStatuses: new List<string> { "open", "closed" }));

            Assert.True(p.HasWrite);
            Assert.Equal("closed", (string)p.Patch["status"]);
            Assert.Empty(p.Conflicts);
            Assert.Contains("fixed on site, photo in CDE", p.CommentText);
            Assert.Contains("site confirmed", p.CommentText);
        }

        [Fact]
        public void Push_Plan_AccChangedSinceBase_IsConflict_NothingSent()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed");
            var c = AccIssuePush.FindCandidates(rows).Single();

            var p = AccIssuePush.Plan(c, Current("a-1", status: "in_dispute"));

            Assert.False(p.HasWrite);
            var cf = Assert.Single(p.Conflicts);
            Assert.Equal("status", cf.Field);
            Assert.Equal("IN_PROGRESS", cf.AccValue);
        }

        [Fact]
        public void Push_Plan_StatusNotPermitted_Reported_NotSent()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(),
                Current("a-1", permittedStatuses: new List<string> { "open", "in_progress" }));
            Assert.False(p.HasWrite);
            Assert.Contains(p.NotPushed, n => n.Contains("does not permit"));
        }

        [Theory]
        [InlineData("OPEN", "open")]
        [InlineData("IN_PROGRESS", "in_progress")]
        [InlineData("RESPONDED", "completed")]
        [InlineData("CLOSED", "closed")]
        [InlineData("ACCEPTED", null)]
        [InlineData("RESOLVED", null)]
        [InlineData("VOID", null)]
        public void Push_ToAccStatus_OnlyRoundTrippingStatuses(string canonical, string acc)
            => Assert.Equal(acc, AccIssuePush.ToAccStatus(canonical));

        [Fact]
        public void Push_Plan_UnrepresentableStatus_Reported()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "VOID", "coord", T1, "duplicate");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.False(p.HasWrite);
            Assert.Contains(p.NotPushed, n => n.Contains("no ACC status maps back"));
        }

        private static AccProjectDirectory Members() => AccProjectDirectory.From(new List<AccProjectUser>
        {
            new AccProjectUser { Id = "f-1", AutodeskId = "user-1", Email = "one@kut.test", Name = "One", Status = "active",
                                 CompanyId = "co-1", CompanyName = "Planscape" },
            new AccProjectUser { Id = "f-2", AutodeskId = "user-2", Email = "two@kut.test", Name = "Two Person", Status = "active",
                                 CompanyId = "co-2", CompanyName = "Symbion" },
        });

        [Fact]
        public void Push_Plan_Assignee_UsesAccType_TitleNotPushed()
        {
            var (rows, row) = Imported();
            row["assigned_to"] = "user-2";
            row["title"] = "Local title";
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"), Members());

            Assert.Equal("user-2", (string)p.Patch["assignedTo"]);
            Assert.Equal("user", (string)p.Patch["assignedToType"]);
            Assert.Null(p.Patch["title"]);
            Assert.Contains(p.NotPushed, n => n.StartsWith("title"));
            Assert.Null(p.CommentText);   // no note, no comments
        }

        [Fact]
        public void Push_Plan_AssigneeEmail_ResolvedToTheMembersAutodeskId_AndRecordedAfterPush()
        {
            var (rows, row) = Imported();
            row["assigned_to"] = "TWO@kut.test";
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"), Members());

            Assert.True(p.HasWrite);
            Assert.Equal("user-2", (string)p.Patch["assignedTo"]);
            Assert.Equal("user", (string)p.Patch["assignedToType"]);

            AccIssuePush.ApplyPushed(p, T1);
            Assert.Equal("user-2", (string)row["assigned_to"]);
            Assert.Equal("Two Person", (string)row[AccIssueImport.AssignedNameField]);
            // The next import (ACC now holds user-2) sees agreement, not an ACC change.
            var r = Merge(rows, T2, pulled: Acc("a-1", assignee: "user-2"));
            Assert.Empty(r.LocalAhead);
            Assert.Empty(r.Conflicts);
        }

        [Fact]
        public void Push_Plan_AssigneeName_ResolvedLikeEscalation()
        {
            var (rows, row) = Imported();
            row["assigned_to"] = "Two Person";
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"), Members());
            Assert.Equal("user-2", (string)p.Patch["assignedTo"]);
        }

        [Fact]
        public void Push_Plan_UnresolvableAssignee_RefusedWithReason_StatusStillPushed()
        {
            var (rows, row) = Imported();
            row["assigned_to"] = "nobody@kut.test";
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"), Members());

            Assert.True(p.HasWrite);
            Assert.Equal("closed", (string)p.Patch["status"]);
            Assert.Null(p.Patch["assignedTo"]);
            Assert.DoesNotContain("assigned_to", p.PushFields);
            Assert.Contains(p.NotPushed, n => n.StartsWith("assignee 'nobody@kut.test'") && n.Contains("no project member"));
        }

        [Fact]
        public void Push_Plan_AssigneeEmail_MembersUnreadable_Refused_NotSentRaw()
        {
            var (rows, row) = Imported();
            row["assigned_to"] = "two@kut.test";
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"), null, "HTTP 403");
            Assert.False(p.HasWrite);
            Assert.Contains(p.NotPushed, n => n.Contains("member list could not be read") && n.Contains("HTTP 403"));
        }

        [Fact]
        public void Push_Plan_AssigneeEmail_WhenPreviousAssigneeWasACompany_IsAUser()
        {
            var (rows, row) = Imported();
            row["assigned_to"] = "two@kut.test";
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1", assigneeType: "company"), Members());
            Assert.Equal("user", (string)p.Patch["assignedToType"]);
            Assert.Equal("user-2", (string)p.Patch["assignedTo"]);
        }

        [Fact]
        public void Push_Plan_AssigneeNotEditable_Reported()
        {
            var (rows, row) = Imported();
            row["assigned_to"] = "user-2";
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(),
                Current("a-1", permittedAttrs: new List<string> { "title" }));
            Assert.False(p.HasWrite);
            Assert.Contains(p.NotPushed, n => n.Contains("assignee"));
        }

        [Fact]
        public void Push_ApplyPushed_NextImportSeesAgreement_NoLocalAhead()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed");
            ((JArray)row["comments"]).Add("one");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.True(p.HasWrite);

            AccIssuePush.ApplyPushed(p, T1);
            AccIssuePush.MarkCommentsPushed(row);

            Assert.Empty(AccIssuePush.FindCandidates(rows));
            var r = Merge(rows, T2, pulled: Acc("a-1", status: "closed"));
            Assert.Empty(r.LocalAhead);
            Assert.Empty(r.Conflicts);
            Assert.Equal("CLOSED", IssueSchema.StatusOf(row));
            // A later change does not resend the comment already sent.
            row["assigned_to"] = "user-3";
            var p2 = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1", status: "closed"));
            Assert.Null(p2.CommentText);
        }

        [Fact]
        public void Push_FailedWrite_LeavesRowUntouched_OfferedAgain()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed");
            string before = row.ToString();
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.True(p.HasWrite);
            // (no ApplyPushed: the PATCH failed)
            Assert.Equal(before, row.ToString());
            Assert.Single(AccIssuePush.FindCandidates(rows));
        }

        // ── E9: comments are pushed on their own, notes are not lost, keys not counts ──

        [Fact]
        public void Push_ACommentOnlyChange_IsACandidate_AndIsPosted()
        {
            var (rows, row) = Imported();
            Assert.Empty(AccIssuePush.FindCandidates(rows));
            ((JArray)row["comments"]).Add("photo uploaded to CDE");

            var c = Assert.Single(AccIssuePush.FindCandidates(rows));
            Assert.Empty(c.Changes);
            Assert.Equal(1, c.PendingComments);
            var p = AccIssuePush.Plan(c, Current("a-1"));
            Assert.False(p.HasWrite);
            Assert.True(p.HasComment);
            Assert.Contains("photo uploaded to CDE", p.CommentText);

            AccIssuePush.MarkCommentsPushed(row, p.CommentKeys);
            Assert.Empty(AccIssuePush.FindCandidates(rows));
        }

        [Fact]
        public void Push_StatusAlreadyAgreedInAcc_StillPostsTheNote()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "closed after site walk");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1", status: "closed"));
            Assert.Contains("status", p.AgreeFields);
            Assert.False(p.HasWrite);
            Assert.True(p.HasComment);
            Assert.Contains("closed after site walk", p.CommentText);
        }

        [Fact]
        public void Push_UnpushableStatus_PostsTheNoteOnce_NotEveryRun()
        {
            var (rows, row) = Imported();
            IssueSchema.ApplyStatus(row, "VOID", "coord", T1, "duplicate of #12");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.False(p.HasWrite);
            Assert.Contains("duplicate of #12", p.CommentText);

            AccIssuePush.MarkCommentsPushed(row, p.CommentKeys);
            // The VOID change stays a candidate (it cannot be pushed), but the note is not resent.
            var p2 = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.Null(p2.CommentText);
        }

        [Fact]
        public void Push_CommentsAreKeyedByContent_DeletingOneDoesNotHideANewOne()
        {
            var (rows, row) = Imported();
            var comments = (JArray)row["comments"];
            comments.Add("first");
            comments.Add("second");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            AccIssuePush.MarkCommentsPushed(row, p.CommentKeys);

            // With a count (2), removing "first" and adding "third" left the count satisfied and
            // "third" was never sent.
            comments.RemoveAt(0);
            comments.Add("third");
            var p2 = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.Contains("third", p2.CommentText);
            Assert.DoesNotContain("second", p2.CommentText);
        }

        [Fact]
        public void Push_ALegacyCommentCount_StillMarksThoseCommentsSent()
        {
            var (rows, row) = Imported();
            var comments = (JArray)row["comments"];
            comments.Add("old one");
            comments.Add("new one");
            row[AccIssueImport.CommentsPushedField] = 1;   // written before E9
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.Contains("new one", p.CommentText);
            Assert.DoesNotContain("old one", p.CommentText);
        }

        [Fact]
        public void Push_OnlyTheAcceptedKeysAreRecorded()
        {
            var (rows, row) = Imported();
            ((JArray)row["comments"]).Add("a");
            var p = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            ((JArray)row["comments"]).Add("b");   // added after planning, so not in this post
            AccIssuePush.MarkCommentsPushed(row, p.CommentKeys);
            var p2 = AccIssuePush.Plan(AccIssuePush.FindCandidates(rows).Single(), Current("a-1"));
            Assert.Contains("b", p2.CommentText);
            Assert.DoesNotContain("\na", p2.CommentText);
        }

        // ── 3. watermark ─────────────────────────────────────────────────────

        [Fact]
        public void State_FirstRunFull_ThenIncrementalWithOverlap_ProjectChangeForcesFull()
        {
            var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            var s = new AccIssueImportState();
            Assert.Null(s.SinceFor("b.p1", false, now, out var why0));
            Assert.Contains("no previous import", why0);

            s.RecordSuccess("b.p1", now, wasFull: true);
            var since = s.SinceFor("p1", false, now.AddHours(1), out var why1);   // b. form equals bare
            Assert.Equal(now - AccIssueImportState.Overlap, since);
            Assert.StartsWith("incremental", why1);

            Assert.Null(s.SinceFor("p2", false, now.AddHours(1), out var why2));
            Assert.Contains("project changed", why2);
            Assert.Null(s.SinceFor("p1", true, now.AddHours(1), out _));
            Assert.Null(s.SinceFor("p1", false, now.AddDays(8), out var why3));
            Assert.Contains("deletions", why3);
        }

        [Fact]
        public void State_SaveLoadRoundTrip_AndCorruptFileIsFullRead()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sting-accstate-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, AccIssueImportState.FileName);
            try
            {
                var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
                var s = new AccIssueImportState();
                s.RecordSuccess("p1", now, wasFull: true);
                Assert.True(s.Save(path, out var err), err);
                Assert.True(s.Save(path, out err), err);   // replace path

                var back = AccIssueImportState.Load(path, out var warn);
                Assert.Equal("", warn);
                Assert.Equal(now, back.LastSuccessUtc);
                Assert.Equal(now, back.LastFullUtc);
                Assert.NotNull(back.SinceFor("p1", false, now.AddMinutes(30), out _));

                File.WriteAllText(path, "{ not json");
                var bad = AccIssueImportState.Load(path, out warn);
                Assert.NotEqual("", warn);
                Assert.Null(bad.SinceFor("p1", false, now.AddMinutes(30), out _));
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        // ── 4. debounce ──────────────────────────────────────────────────────

        [Fact]
        public void Gate_BurstOfSignals_OneRunNowAndOneTrailing()
        {
            var g = new AccAutoImportGate();
            var t = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal(AccAutoImportDecision.RunNow, g.OnSignal(t, out _));
            Assert.Equal(AccAutoImportDecision.ScheduleTrailing, g.OnSignal(t.AddSeconds(10), out var delay));
            Assert.Equal(TimeSpan.FromSeconds(50), delay);
            Assert.Equal(AccAutoImportDecision.AlreadyScheduled, g.OnSignal(t.AddSeconds(20), out _));
            g.OnTrailingFired(t.AddSeconds(60));
            Assert.Equal(AccAutoImportDecision.ScheduleTrailing, g.OnSignal(t.AddSeconds(70), out _));
            var g2 = new AccAutoImportGate();
            g2.OnSignal(t, out _);
            Assert.Equal(AccAutoImportDecision.RunNow, g2.OnSignal(t.AddSeconds(61), out _));
        }

        // ── 5. custom attributes (pure) ──────────────────────────────────────

        [Fact]
        public void Fields_ResolveByTitle_UnknownAmbiguousAndListReported()
        {
            var defs = new List<AccIssueAttributeDefinition>
            {
                new AccIssueAttributeDefinition { Id = "d1", Title = "STING Signature", DataType = "text" },
                new AccIssueAttributeDefinition { Id = "d2", Title = "Score", DataType = "numeric" },
                new AccIssueAttributeDefinition { Id = "d3", Title = "Discipline", DataType = "list" },
                new AccIssueAttributeDefinition { Id = "d4", Title = "Dup", DataType = "text" },
                new AccIssueAttributeDefinition { Id = "d5", Title = "dup", DataType = "text" },
            };
            var map = new Dictionary<string, string>
            {
                ["clashSignature"] = "sting signature", ["triageScore"] = "Score",
                ["modelSet"] = "Discipline", ["clashId"] = "Dup",
            };
            var causes = new List<AccRootCause> { new AccRootCause { Id = "rc1", Title = "Coordination", CategoryTitle = "Design" } };

            var r = AccIssueFields.Resolve(map, defs, "coordination", causes);

            Assert.Equal("d1", r.DefinitionIdFor("clashSignature"));
            Assert.Equal("d2", r.DefinitionIdFor("triageScore"));
            Assert.Null(r.DefinitionIdFor("modelSet"));
            Assert.Null(r.DefinitionIdFor("clashId"));
            Assert.Equal("rc1", r.RootCauseId);
            Assert.Contains(r.Problems, p => p.Contains("list attribute"));
            Assert.Contains(r.Problems, p => p.Contains("ambiguous"));

            var problems = new List<string>();
            var values = AccIssueFields.Values(r, new Dictionary<string, string>
            { ["clashSignature"] = "sig", ["triageScore"] = "0.91" }, problems);
            Assert.Equal(2, values.Count);
            Assert.Equal(JTokenType.Float, values.Single(v => v.AttributeDefinitionId == "d2").Value.Type);

            var bad = AccIssueFields.Values(r, new Dictionary<string, string> { ["triageScore"] = "high" }, problems);
            Assert.Empty(bad);
            Assert.Contains(problems, p => p.Contains("not a number"));

            var none = AccIssueFields.Resolve(map, defs, "No Such Cause", causes);
            Assert.Equal("", none.RootCauseId);
            Assert.Contains(none.Problems, p => p.Contains("root cause"));
        }

        [Fact]
        public void IssueBody_CarriesCustomAttributesAndRootCause_OnlyWhenSet()
        {
            var plain = AccIssueSync.BuildIssueBody(new AccIssue { Title = "t" }, "sub");
            Assert.Null(plain["customAttributes"]);
            Assert.Null(plain["rootCauseId"]);

            var rich = AccIssueSync.BuildIssueBody(new AccIssue
            {
                Title = "t", RootCauseId = "rc1",
                CustomAttributes = { new AccCustomAttributeValue { AttributeDefinitionId = "d1", Value = "sig" } },
            }, "sub");
            Assert.Equal("rc1", (string)rich["rootCauseId"]);
            var ca = (JObject)((JArray)rich["customAttributes"]).Single();
            Assert.Equal("d1", (string)ca["attributeDefinitionId"]);
            Assert.Equal("sig", (string)ca["value"]);
        }

        // ── settings keys ────────────────────────────────────────────────────

        private static AccOperatingPolicy Policy(string json)
        {
            string dir = Path.Combine(Path.GetTempPath(), "sting-accpol2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, AccOperatingPolicy.FileName);
            File.WriteAllText(path, json);
            try { return AccOperatingPolicy.Load(path); }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Fact]
        public void Settings_NewKeys_Loaded()
        {
            var p = Policy("{ \"unattended\": true, \"pushIssueChanges\": true, \"autoImportIssues\": true, " +
                           "\"issueCustomAttributes\": { \"clashSignature\": \"STING Signature\" }, \"issueRootCause\": \"Coordination\" }");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.True(p.PushIssueChanges);
            Assert.True(p.AutoImportIssues);
            Assert.Equal("STING Signature", p.IssueCustomAttributes["clashSignature"]);
            Assert.Equal("Coordination", p.IssueRootCause);
            Assert.Equal(AccIssuePushMode.PushUnattended, p.IssuePushMode);
        }

        [Theory]
        [InlineData("{ \"pushIssueChanges\": \"yes\" }")]
        [InlineData("{ \"autoImportIssues\": 1 }")]
        [InlineData("{ \"issueRootCause\": 5 }")]
        [InlineData("{ \"issueCustomAttributes\": [\"a\"] }")]
        [InlineData("{ \"issueCustomAttributes\": { \"isoTagg\": \"x\" } }")]
        [InlineData("{ \"issueCustomAttributes\": { \"clashId\": 3 } }")]
        public void Settings_NewKeys_WrongTypeOrUnknownField_IsMalformed_EverythingOff(string json)
        {
            var p = Policy(json);
            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.False(p.PushIssueChanges);
            Assert.False(p.AutoImportIssues);
            Assert.Empty(p.IssueCustomAttributes);
        }

        [Fact]
        public void Settings_PushMode_Matrix()
        {
            Assert.Equal(AccIssuePushMode.PreviewAndConfirm, AccOperatingPolicy.Interactive().IssuePushMode);
            Assert.Equal(AccIssuePushMode.PreviewAndConfirm, Policy("{ \"pushIssueChanges\": true }").IssuePushMode);
            Assert.Equal(AccIssuePushMode.ReportOnly, Policy("{ \"unattended\": true }").IssuePushMode);
            Assert.Equal(AccIssuePushMode.PushUnattended, Policy("{ \"unattended\": true, \"pushIssueChanges\": true }").IssuePushMode);
            Assert.False(AccOperatingPolicy.Interactive().AutoImportIssues);
        }
    }
}
