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
    }
}
