// ACC_ImportIssues — the merge of a complete ACC Issues pull into the STING register.
//
// Every test asserts on a NON-EMPTY register first where it matters: a merge that silently
// did nothing would pass a "nothing was deleted" test against an empty array.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssueImportTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 30, 9, 0, 0);
        private static readonly DateTime T1 = T0.AddHours(1);
        private static readonly DateTime T2 = T0.AddHours(2);

        private static AccIssueImportRecord Acc(string id, string title = "Duct clashes beam",
            string status = "open", string assignee = "user-1", string desc = "L02 grid C4")
            => new AccIssueImportRecord
            {
                Id = id, Title = title, Status = status, AssignedTo = assignee, Description = desc,
                IssueTypeId = "type-1", LocationDescription = "Level 02",
            };

        private static AccIssueImportResult Run(JArray rows, DateTime now, params AccIssueImportRecord[] pulled)
            => Run(rows, now, null, pulled);

        private static AccIssueImportResult Run(JArray rows, DateTime now,
            Dictionary<string, AccOriginLink> origins, params AccIssueImportRecord[] pulled)
            => AccIssueImport.Merge(rows, pulled, origins, new JArrayAccIssueWriter(rows, now, "tester"), now, "tester");

        private static JObject ByAcc(JArray rows, string accId)
            => rows.OfType<JObject>().Single(r => (string)r[AccIssueImport.AccIdField] == accId);

        // ── create ────────────────────────────────────────────────────────────

        [Fact]
        public void Create_NewAccIssue_BecomesCanonicalAccRow()
        {
            var rows = new JArray();
            var r = Run(rows, T0, Acc("a-1"), Acc("a-2", status: "in_review"));

            Assert.Equal(2, r.Created.Count);
            Assert.Equal(2, rows.Count);
            var row = ByAcc(rows, "a-1");
            Assert.Equal("ACC-0001", IssueSchema.IdOf(row));
            Assert.Equal("acc", IssueSchema.SourceOf(row));
            Assert.Equal("Duct clashes beam", (string)row["title"]);
            Assert.Equal("user-1", (string)row["assigned_to"]);
            Assert.Equal("OPEN", IssueSchema.StatusOf(row));
            Assert.Equal("IN_PROGRESS", IssueSchema.StatusOf(ByAcc(rows, "a-2")));
            // No invented SLA due date on an issue whose due date belongs to ACC.
            Assert.Equal("", (string)row["date_due"]);
            // The register's shared predicate sees them — the whole point of the import.
            Assert.Equal(2, IssueSchema.OpenCount(rows));
        }

        [Fact]
        public void Create_MintsAfterExistingIds_NeverCollides()
        {
            var rows = new JArray(new JObject { ["issue_id"] = "ACC-0007", ["status"] = "OPEN", ["title"] = "manual" });
            Run(rows, T0, Acc("a-1"));
            Assert.Equal("ACC-0008", IssueSchema.IdOf(ByAcc(rows, "a-1")));
        }

        // ── status mapping ────────────────────────────────────────────────────

        [Theory]
        [InlineData("draft", "OPEN", true)]
        [InlineData("open", "OPEN", true)]
        [InlineData("pending", "IN_PROGRESS", true)]
        [InlineData("in_progress", "IN_PROGRESS", true)]
        [InlineData("in_review", "IN_PROGRESS", true)]
        [InlineData("in_dispute", "IN_PROGRESS", true)]
        // ACC "completed" = the assignee answered, the creator has not accepted. STILL OPEN.
        [InlineData("completed", "RESPONDED", true)]
        // A rejected completion goes back to the assignee — open, not void.
        [InlineData("not_approved", "OPEN", true)]
        [InlineData("closed", "CLOSED", false)]
        public void Status_AccVocabulary_MapsToCanonical(string acc, string canonical, bool open)
        {
            Assert.Equal(canonical, IssueStatusNormalizer.CanonicalAcc(acc));
            Assert.Equal(open, IssueStatusNormalizer.IsOpen(IssueStatusNormalizer.CanonicalAcc(acc)));
        }

        [Fact]
        public void Status_GenericCompleted_IsUnchangedForNonAccWriters()
        {
            // ACC's meaning must not leak into the generic table other writers rely on.
            Assert.Equal(IssueStatusKind.Closed, IssueStatusNormalizer.Normalize("completed"));
            Assert.Equal(IssueStatusKind.Void, IssueStatusNormalizer.Normalize("rejected"));
        }

        [Fact]
        public void Status_UnknownAccSpelling_FallsBackToGenericAndFailsSafeOpen()
        {
            Assert.True(IssueStatusNormalizer.IsOpen(IssueStatusNormalizer.CanonicalAcc("some_new_state")));
        }

        // ── update ────────────────────────────────────────────────────────────

        [Fact]
        public void Update_AccChangedTitleStatusAssignee_AppliedWithHistory()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"));
            var r = Run(rows, T1, Acc("a-1", title: "Duct clashes beam (rev)", status: "completed", assignee: "user-2"));

            Assert.Single(r.Updated);
            Assert.Equal(1, r.StatusChanges);
            Assert.Empty(r.Conflicts);
            var row = ByAcc(rows, "a-1");
            Assert.Equal("Duct clashes beam (rev)", (string)row["title"]);
            Assert.Equal("user-2", (string)row["assigned_to"]);
            Assert.Equal("RESPONDED", IssueSchema.StatusOf(row));
            var hist = (JArray)row["status_history"];
            Assert.Equal("RESPONDED", (string)hist.Last["to"]);
            Assert.Single(rows);
        }

        [Fact]
        public void Update_ClosedInAcc_ClosesTheSameRow_NotANewOne()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"));
            Run(rows, T1, Acc("a-1", status: "closed"));
            Assert.Single(rows);
            Assert.Equal("CLOSED", IssueSchema.StatusOf(rows[0] as JObject));
            Assert.Equal(0, IssueSchema.OpenCount(rows));

            // And re-opened in ACC later, the closed row is found again (any status).
            Run(rows, T2, Acc("a-1", status: "open"));
            Assert.Single(rows);
            Assert.Equal("OPEN", IssueSchema.StatusOf(rows[0] as JObject));
        }

        [Fact]
        public void Update_LocalEditOnly_IsKept_NoConflict()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"));
            var row = ByAcc(rows, "a-1");
            row["title"] = "Coordinator's clearer title";
            IssueSchema.ApplyStatus(row, "IN_PROGRESS", "coord", T1, "picked up");

            var r = Run(rows, T2, Acc("a-1"));   // ACC unchanged

            Assert.Empty(r.Conflicts);
            Assert.Equal("Coordinator's clearer title", (string)row["title"]);
            Assert.Equal("IN_PROGRESS", IssueSchema.StatusOf(row));
        }

        [Fact]
        public void Update_PreservesStingOnlyFields()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"));
            var row = ByAcc(rows, "a-1");
            ((JArray)row["comments"]).Add("site confirmed");
            row["priority"] = "HIGH";

            Run(rows, T1, Acc("a-1", title: "new"));
            Assert.Equal("site confirmed", (string)((JArray)row["comments"])[0]);
            Assert.Equal("HIGH", (string)row["priority"]);
        }

        // ── conflict ──────────────────────────────────────────────────────────

        [Fact]
        public void Conflict_BothSidesChanged_LocalKept_Reported_AndRecordedOnRow()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"));
            var row = ByAcc(rows, "a-1");
            row["title"] = "STING title";

            var r = Run(rows, T1, Acc("a-1", title: "ACC title"));

            var c = Assert.Single(r.Conflicts);
            Assert.Equal("title", c.Field);
            Assert.Equal("STING title", c.LocalValue);
            Assert.Equal("ACC title", c.AccValue);
            Assert.Equal("Duct clashes beam", c.BaseValue);
            Assert.Equal("STING title", (string)row["title"]);   // not overwritten silently
            Assert.Equal("title", (string)((JArray)row[AccIssueImport.ConflictsField])[0]["field"]);

            // Reported again until somebody reconciles it — the base did not move.
            var again = Run(rows, T2, Acc("a-1", title: "ACC title"));
            Assert.Single(again.Conflicts);
        }

        [Fact]
        public void Conflict_ResolvedByMakingThemAgree_ClearsIt()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"));
            var row = ByAcc(rows, "a-1");
            row["title"] = "STING title";
            Run(rows, T1, Acc("a-1", title: "ACC title"));
            Assert.NotNull(row[AccIssueImport.ConflictsField]);

            row["title"] = "ACC title";   // coordinator accepts ACC's wording
            var r = Run(rows, T2, Acc("a-1", title: "ACC title"));
            Assert.Empty(r.Conflicts);
            Assert.Null(row[AccIssueImport.ConflictsField]);
        }

        [Fact]
        public void Conflict_StatusClosedLocally_ReopenedInAcc_IsAConflictNotAReopen()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1", status: "open"));
            var row = ByAcc(rows, "a-1");
            IssueSchema.ApplyStatus(row, "CLOSED", "coord", T1, "fixed on site");

            var r = Run(rows, T2, Acc("a-1", status: "in_dispute"));
            var c = Assert.Single(r.Conflicts);
            Assert.Equal("status", c.Field);
            Assert.Equal("CLOSED", IssueSchema.StatusOf(row));
        }

        // ── never delete ──────────────────────────────────────────────────────

        [Fact]
        public void NoDelete_AccIssueGone_RowKeptAndReported_OtherRowsUntouched()
        {
            var manual = new JObject { ["issue_id"] = "RFI-0001", ["status"] = "OPEN", ["title"] = "manual", ["source"] = "manual" };
            var rows = new JArray(manual);
            Run(rows, T0, Acc("a-1"), Acc("a-2"));
            Assert.Equal(3, rows.Count);
            string manualBefore = manual.ToString();

            var r = Run(rows, T1, Acc("a-2"));   // a-1 deleted in ACC; empty-ish pull

            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { IssueSchema.IdOf(ByAcc(rows, "a-1")) }, r.MissingFromAcc);
            Assert.Equal(manualBefore, manual.ToString());

            var none = Run(rows, T2);   // a genuinely empty container
            Assert.Equal(3, rows.Count);
            Assert.Equal(2, none.MissingFromAcc.Count);
        }

        // ── idempotency ───────────────────────────────────────────────────────

        [Fact]
        public void Idempotent_SecondIdenticalImport_ChangesNothing()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"), Acc("a-2", status: "completed"));
            string before = rows.ToString();

            var r = Run(rows, T1, Acc("a-1"), Acc("a-2", status: "completed"));

            Assert.False(r.AnyChange);
            Assert.Equal(2, r.Unchanged);
            Assert.Equal(0, r.StatusChanges);
            Assert.Equal(before, rows.ToString());
        }

        [Fact]
        public void Idempotent_EquivalentAccStatus_IsNotAStatusChange()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1", status: "in_review"));
            var r = Run(rows, T1, Acc("a-1", status: "pending"));   // both IN_PROGRESS
            Assert.Equal(0, r.StatusChanges);
            Assert.Equal("pending", (string)ByAcc(rows, "a-1")["acc_status"]);
        }

        [Fact]
        public void DuplicateOrBlankIdsInPull_AreSkipped_NotDuplicated()
        {
            var rows = new JArray();
            var r = Run(rows, T0, Acc("a-1"), Acc("a-1"), Acc(""));
            Assert.Single(rows);
            Assert.Equal(2, r.Skipped);
        }

        // ── round trip ────────────────────────────────────────────────────────

        [Fact]
        public void RoundTrip_EscalatedClash_MarkedAsStingOrigin_OneRow()
        {
            var origins = new Dictionary<string, AccOriginLink>();
            AccOriginLink.AddSidecar(origins, AccIssueImport.ClashEscalationOrigin,
                new Dictionary<string, string> { ["11@A.rvt|22@B.rvt"] = "a-9" });
            var rows = new JArray();

            Run(rows, T0, origins, Acc("a-9", title: "Clash [Hard]"));
            Run(rows, T1, origins, Acc("a-9", title: "Clash [Hard]"));

            var row = Assert.Single(rows.OfType<JObject>());
            Assert.Equal(AccIssueImport.ClashEscalationOrigin, (string)row[AccIssueImport.OriginField]);
            Assert.Equal("11@A.rvt|22@B.rvt", (string)row[AccIssueImport.OriginKeyField]);
            Assert.StartsWith("CLASH-", IssueSchema.IdOf(row));
        }

        [Fact]
        public void RoundTrip_ExistingStingRowWithSameKey_IsLinked_NotDuplicated()
        {
            var sting = new JObject
            {
                ["issue_id"] = "CLASH-0003", ["status"] = "OPEN", ["title"] = "Clash [Hard]",
                ["description"] = "L02 grid C4", ["assigned_to"] = "user-1",
                ["source"] = "clash", ["source_hash"] = "11@A.rvt|22@B.rvt",
            };
            var rows = new JArray(sting);
            var origins = new Dictionary<string, AccOriginLink>();
            AccOriginLink.AddSidecar(origins, AccIssueImport.ClashEscalationOrigin,
                new Dictionary<string, string> { ["11@A.rvt|22@B.rvt"] = "a-9" });

            var r = Run(rows, T0, origins, Acc("a-9", title: "Clash [Hard]", status: "completed"));

            Assert.Single(rows);
            Assert.Single(r.Linked);
            Assert.Empty(r.Created);
            Assert.Equal("a-9", (string)sting[AccIssueImport.AccIdField]);
            Assert.Equal("CLASH-0003", IssueSchema.IdOf(sting));
            // Never imported before, so there is no base: ACC's status differs from STING's
            // and nothing says which is newer — reported, not applied.
            Assert.Contains(r.Conflicts, c => c.Field == "status");
            Assert.Equal("OPEN", IssueSchema.StatusOf(sting));

            // Next import finds it by ACC id; still one row.
            Run(rows, T1, origins, Acc("a-9", title: "Clash [Hard]", status: "completed"));
            Assert.Single(rows);
        }

        // ── E8: the incremental watermark is ACC's clock, not this PC's ───────────

        private static readonly DateTime AccNow = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Watermark_IsTheServersDate_WhenSent()
        {
            var m = AccIssueImportState.AccWatermark(AccNow, new DateTime?[] { AccNow.AddDays(-3) }, null, out string basis);
            Assert.Equal(AccNow, m);
            Assert.Contains("Date", basis);
        }

        [Fact]
        public void Watermark_FallsBackToTheNewestUpdatedAt_NeverMovingBackwards()
        {
            var newest = AccNow.AddMinutes(-20);
            Assert.Equal(newest, AccIssueImportState.AccWatermark(null,
                new DateTime?[] { AccNow.AddDays(-2), newest, null }, AccNow.AddHours(-1), out _));
            // A window that returned only older issues keeps the current watermark.
            Assert.Equal(AccNow, AccIssueImportState.AccWatermark(null,
                new DateTime?[] { AccNow.AddDays(-2) }, AccNow, out string kept));
            Assert.Contains("kept", kept);
        }

        [Fact]
        public void Watermark_WithNoAccTime_IsNotAdvanced_TheWorkstationClockIsNeverUsed()
        {
            var s = new AccIssueImportState();
            s.RecordSuccess("p1", AccNow.AddHours(-1), wasFull: true);
            var m = AccIssueImportState.AccWatermark(null, new DateTime?[0], s.LastSuccessUtc, out string basis);
            Assert.Null(m);
            Assert.Contains("not advanced", basis);
            Assert.False(s.RecordSuccess("p1", m, wasFull: false, fullReadAtUtc: AccNow, skew: null));
            Assert.Equal(AccNow.AddHours(-1), s.LastSuccessUtc);
        }

        [Fact]
        public void APcRunningFast_DoesNotOpenAGap_TheNextWindowStartsAtAccsTime()
        {
            // This PC is 10 minutes ahead of ACC. Before E8 the watermark was the PC's start
            // (AccNow + 10 min), so the next window (minus 5 min overlap) skipped 5 minutes.
            DateTime pcStart = AccNow.AddMinutes(10);
            var s = new AccIssueImportState();
            var mark = AccIssueImportState.AccWatermark(AccNow, new DateTime?[0], null, out _);
            TimeSpan skew = AccNow - pcStart;
            Assert.True(s.RecordSuccess("p1", mark, wasFull: true, fullReadAtUtc: AccNow, skew: skew));
            var since = s.SinceFor("p1", false, pcStart.AddHours(1), out _);
            Assert.Equal(AccNow - AccIssueImportState.Overlap, since);
            Assert.Equal(skew, s.LastSkew);
        }

        [Fact]
        public void APcRunningSlow_IsNotForcedIntoAFullReadByTheFutureCheck()
        {
            // PC is 10 minutes BEHIND ACC: the ACC watermark looks "in the future" locally.
            var s = new AccIssueImportState();
            s.RecordSuccess("p1", AccNow, wasFull: true, fullReadAtUtc: AccNow, skew: TimeSpan.FromMinutes(10));
            var since = s.SinceFor("p1", false, AccNow.AddMinutes(-9), out string why);
            Assert.NotNull(since);
            Assert.StartsWith("incremental", why);
        }

        [Fact]
        public void TheMeasuredSkew_SurvivesSaveAndLoad()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting-skew-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var s = new AccIssueImportState();
                s.RecordSuccess("p1", AccNow, wasFull: true, fullReadAtUtc: AccNow, skew: TimeSpan.FromSeconds(-42));
                Assert.True(s.Save(path, out string err), err);
                var back = AccIssueImportState.Load(path, out _);
                Assert.Equal(TimeSpan.FromSeconds(-42), back.LastSkew);
                Assert.Equal(AccNow, back.LastSuccessUtc);
            }
            finally { try { System.IO.File.Delete(path); } catch (System.IO.IOException) { } }
        }

        // ── E6: who raised it and when come from ACC; no invented priority ────────

        private static AccIssueImportRecord Raised(string id, string createdAt, string by = "acc-user-7", string byName = "")
        {
            var a = Acc(id);
            a.CreatedAt = createdAt;
            a.CreatedBy = by;
            a.CreatedByName = byName;
            return a;
        }

        [Fact]
        public void ParseIssue_KeepsCreatedAtNull_WhenAccOmitsIt_AndReadsItWhenGiven()
        {
            var none = AccIssueSync.ParseIssue(JObject.Parse("{\"id\":\"x\",\"title\":\"t\"}"));
            Assert.Null(none.CreatedAt);
            Assert.Equal("", none.CreatedBy);
            Assert.Equal("", AccIssueImportRecord.From(none).CreatedAt);

            var given = AccIssueSync.ParseIssue(JObject.Parse(
                "{\"id\":\"x\",\"title\":\"t\",\"createdAt\":\"2026-08-15T07:30:00.000Z\",\"createdBy\":\"ABC123\"}"));
            Assert.Equal(new DateTime(2026, 8, 15, 7, 30, 0, DateTimeKind.Utc), given.CreatedAt);
            Assert.Equal("ABC123", given.CreatedBy);
            Assert.Equal("2026-08-15T07:30:00.000Z", AccIssueImportRecord.From(given).CreatedAt);
        }

        [Fact]
        public void AnImportedRow_CarriesAccsCreationDateAndRaiser_NotTheImportersNow()
        {
            var rows = new JArray();
            Run(rows, T0, Raised("a-1", "2026-08-15T07:30:00.000Z", byName: "Jane Engineer"));
            var row = ByAcc(rows, "a-1");
            var created = DateTime.Parse((string)row["created_date"], null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.Equal(new DateTime(2026, 8, 15, 7, 30, 0, DateTimeKind.Utc), created.ToUniversalTime());
            Assert.StartsWith("2026-08-15", (string)row["date_raised"]);
            Assert.Equal("Jane Engineer", (string)row["raised_by"]);
            Assert.Equal("acc-user-7", (string)row[AccIssueImport.CreatedByField]);
            Assert.NotEqual("tester", (string)row["raised_by"]);
        }

        [Fact]
        public void WithNoCreatorName_TheRaiserIsTheAccId_AndWithNoDate_TheDateIsBlank()
        {
            var rows = new JArray();
            Run(rows, T0, Raised("a-1", "", byName: ""));
            var row = ByAcc(rows, "a-1");
            Assert.Equal("acc-user-7", (string)row["raised_by"]);
            Assert.Equal("", (string)row["created_date"]);   // never the time of the pull
            Assert.Equal("", (string)row["date_raised"]);
        }

        [Fact]
        public void AnImportedRow_HasNoInventedPriority_AndSoNoSla()
        {
            var rows = new JArray();
            Run(rows, T0, Raised("a-1", "2026-08-15T07:30:00.000Z"));
            var row = ByAcc(rows, "a-1");
            Assert.Equal("", (string)row["priority"]);
            Assert.True((bool)row[AccIssueImport.PriorityDefaultedField]);
            Assert.Null(IssueSchema.SlaPriority(row));
        }

        [Fact]
        public void SlaPriority_IsTheStatedPriority_OrNull_NeverAssumedMedium()
        {
            Assert.Equal("HIGH", IssueSchema.SlaPriority(new JObject { ["priority"] = "high" }));
            Assert.Null(IssueSchema.SlaPriority(new JObject()));
            Assert.Null(IssueSchema.SlaPriority(new JObject { ["priority"] = "  " }));
            Assert.Null(IssueSchema.SlaPriority(new JObject { ["priority"] = "MEDIUM", ["priority_defaulted"] = true }));
        }

        [Fact]
        public void ARowAnEarlierImportStampedWithImportTime_IsCorrectedOnce()
        {
            var rows = new JArray();
            Run(rows, T0, Acc("a-1"));                        // as before E6: ACC gave no createdAt
            var row = ByAcc(rows, "a-1");
            row.Remove(AccIssueImport.CreatedAtField);         // a row written before E6 has no marker
            row["created_date"] = T0.ToString("o");
            row["raised_by"] = "tester";

            Run(rows, T1, Raised("a-1", "2026-08-15T07:30:00.000Z", byName: "Jane Engineer"));
            Assert.StartsWith("2026-08-15", (string)row["date_raised"]);
            Assert.Equal("Jane Engineer", (string)row["raised_by"]);
            Assert.Equal("2026-08-15T07:30:00.000Z", (string)row[AccIssueImport.CreatedAtField]);
        }

        [Fact]
        public void AStingRaisedRow_LinkedToItsAccIssue_KeepsItsOwnCreation()
        {
            var sting = new JObject
            {
                ["issue_id"] = "CLASH-0003", ["status"] = "OPEN", ["title"] = "Clash [Hard]",
                ["description"] = "L02 grid C4", ["assigned_to"] = "user-1",
                ["source"] = "clash", ["source_hash"] = "11@A.rvt|22@B.rvt",
                ["created_date"] = "2026-07-01T10:00:00.0000000Z", ["raised_by"] = "coordinator",
            };
            var rows = new JArray(sting);
            var origins = new Dictionary<string, AccOriginLink>();
            AccOriginLink.AddSidecar(origins, AccIssueImport.ClashEscalationOrigin,
                new Dictionary<string, string> { ["11@A.rvt|22@B.rvt"] = "a-9" });
            Run(rows, T0, origins, Raised("a-9", "2026-07-02T09:00:00.000Z"));
            Assert.Equal("2026-07-01T10:00:00.0000000Z", (string)sting["created_date"]);
            Assert.Equal("coordinator", (string)sting["raised_by"]);
        }
    }
}
