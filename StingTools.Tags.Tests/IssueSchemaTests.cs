// IssueSchemaTests.cs — ISO IM runner, Phase 2 (ROADMAP IM-4 / IM-5).
//
// Covers the three things the Phase 2 work claims to fix:
//   1. the issue_id / id / IssueId schema fork,
//   2. duplicate identifier minting,
//   3. status vocabulary routing through one predicate.
//
// Each duplicate-id test carries a "RED" counterpart that reproduces the OLD minting rule
// and asserts it collides, so the regression test is demonstrably testing something.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class IssueSchemaTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);

        /// <summary>The old rule: `{type}-{rows.Count + 1:D4}`, as three writers minted ids.</summary>
        private static string LegacyCountMint(JArray rows, string type) => $"{type}-{rows.Count + 1:D4}";

        // ── 1. Schema fork ────────────────────────────────────────────────

        [Theory]
        [InlineData("issue_id", "NCR-0001")]   // BIMManagerEngine.CreateIssue / BCF / RaiseIssue
        [InlineData("id",       "NCR-0002")]   // warnings + gap auto-escalation paths
        [InlineData("IssueId",  "NCR-0003")]   // LpsAutoIssueRaiser (PascalCase record)
        public void IdOf_reads_all_three_historical_spellings(string field, string value)
        {
            var row = new JObject { [field] = value };
            Assert.Equal(value, IssueSchema.IdOf(row));
        }

        [Fact]
        public void Migrate_hoists_a_legacy_id_row_onto_the_canonical_field()
        {
            // The exact shape WarningsEngineExt.AutoCreateIssuesFromWarnings wrote.
            var row = new JObject
            {
                ["id"] = "SI-0007",
                ["title"] = "[AUTO] Room not enclosed",
                ["status"] = "OPEN",
                ["auto_created"] = true,
            };

            Assert.True(IssueSchema.Migrate(row));

            // Visible to an issue_id-only reader...
            Assert.Equal("SI-0007", (string)row["issue_id"]);
            // ...and the duplicate mirror is gone, so the two halves cannot drift apart again.
            Assert.Null(row["id"]);
            // Provenance inferred from auto_created.
            Assert.Equal("warning", (string)row["source"]);
        }

        [Fact]
        public void Migrate_converts_the_PascalCase_LPS_record_wholesale()
        {
            var row = new JObject
            {
                ["IssueId"] = "LPS-AUTO-20260101-1",
                ["Title"] = "LPS compliance — down conductor spacing",
                ["Status"] = "OPEN",
                ["Priority"] = "HIGH",
                ["RaisedBy"] = "STING",
                ["RelatedCheck"] = "DownConductorSpacing",
                ["ElementIds"] = new JArray("101", "102"),
            };

            Assert.True(IssueSchema.Migrate(row));

            Assert.Equal("LPS-AUTO-20260101-1", (string)row["issue_id"]);
            Assert.Equal("LPS compliance — down conductor spacing", (string)row["title"]);
            Assert.Equal("HIGH", (string)row["priority"]);
            Assert.Equal("STING", (string)row["raised_by"]);
            Assert.Equal("DownConductorSpacing", (string)row["related_check"]);
            Assert.Equal(2, ((JArray)row["element_ids"]).Count);
            // PascalCase keys are gone — no reader has to know about them any more.
            Assert.Null(row["IssueId"]);
            Assert.Null(row["Title"]);
            Assert.Null(row["Status"]);
        }

        [Fact]
        public void Migrate_rescues_element_ids_written_as_a_comma_joined_string()
        {
            // WarningsEngine.CreateIssuesFromWarnings built its JSON by string concatenation,
            // so element_ids landed as a STRING. Every element lookup against it matched
            // nothing, silently.
            var row = new JObject { ["id"] = "NCR-0001", ["element_ids"] = "101,102,103" };

            IssueSchema.Migrate(row);

            var ids = Assert.IsType<JArray>(row["element_ids"]);
            Assert.Equal(new[] { "101", "102", "103" }, ids.Select(t => (string)t));
        }

        [Fact]
        public void Migrate_is_idempotent()
        {
            var row = new JObject { ["id"] = "SI-0001", ["status"] = "OPEN" };
            IssueSchema.Migrate(row);
            string first = row.ToString();

            Assert.False(IssueSchema.Migrate(row));   // nothing left to change
            Assert.Equal(first, row.ToString());
        }

        [Fact]
        public void A_forked_store_becomes_uniformly_addressable_after_migration()
        {
            // One register holding all three spellings — the state IM-4 describes.
            var store = new JArray
            {
                new JObject { ["issue_id"] = "RFI-0001", ["status"] = "OPEN" },
                new JObject { ["id"]       = "NCR-0001", ["status"] = "OPEN" },
                new JObject { ["IssueId"]  = "LPS-0001", ["Status"] = "OPEN" },
            };

            Assert.Equal(3, IssueSchema.MigrateAll(store));

            // An issue_id-only reader (BIMManagerCommands) now sees every row.
            var visible = store.OfType<JObject>().Select(r => (string)r["issue_id"]).ToList();
            Assert.Equal(new[] { "RFI-0001", "NCR-0001", "LPS-0001" }, visible);

            // And lookup by id works regardless of which writer created the row.
            Assert.NotNull(IssueSchema.FindById(store, "NCR-0001"));
            Assert.NotNull(IssueSchema.FindById(store, "LPS-0001"));
        }

        // ── 2. Duplicate identifiers (IM-5) ───────────────────────────────

        [Fact]
        public void RED_the_old_count_based_rule_collides_with_a_live_row()
        {
            // Minimal reproduction. A register holding ONE row numbered above its count is
            // all it takes: Count=1 → mints NCR-0002; then Count=2 → mints NCR-0003, which
            // the store already contains.
            var store = new JArray { new JObject { ["id"] = "NCR-0003", ["status"] = "OPEN" } };

            string first = LegacyCountMint(store, "NCR");
            store.Add(new JObject { ["id"] = first, ["status"] = "OPEN" });
            string second = LegacyCountMint(store, "NCR");
            store.Add(new JObject { ["id"] = second, ["status"] = "OPEN" });

            Assert.Equal("NCR-0002", first);
            Assert.Equal("NCR-0003", second);

            // Two rows now share one identifier — every lookup resolves to whichever is first.
            var ids = store.OfType<JObject>().Select(IssueSchema.IdOf).ToList();
            Assert.Equal(3, ids.Count);
            Assert.Equal(2, ids.Count(i => i == "NCR-0003"));
        }

        [Fact]
        public void GREEN_the_minter_never_reissues_a_live_identifier()
        {
            var store = new JArray { new JObject { ["id"] = "NCR-0003", ["status"] = "OPEN" } };
            var minter = new IssueIdMinter(store);

            string first = minter.Next("NCR");
            store.Add(new JObject { ["issue_id"] = first });
            string second = minter.Next("NCR");
            store.Add(new JObject { ["issue_id"] = second });

            Assert.Equal("NCR-0004", first);    // continues from the high-water mark, not the count
            Assert.Equal("NCR-0005", second);
            Assert.Equal(3, store.OfType<JObject>().Select(IssueSchema.IdOf).Distinct().Count());
        }

        [Fact]
        public void Minting_a_batch_yields_unique_ids()
        {
            // The regression test the runner asked for: many issues created in ONE batch.
            var store = new JArray();
            var minter = new IssueIdMinter(store);

            var minted = Enumerable.Range(0, 50).Select(_ => minter.Next("NCR")).ToList();

            Assert.Equal(50, minted.Distinct().Count());
            Assert.Equal("NCR-0001", minted.First());
            Assert.Equal("NCR-0050", minted.Last());
        }

        [Fact]
        public void Minting_a_mixed_type_batch_keeps_per_type_sequences_independent()
        {
            var minter = new IssueIdMinter(new JArray());

            var ncr = new List<string>();
            var si = new List<string>();
            for (int i = 0; i < 5; i++) { ncr.Add(minter.Next("NCR")); si.Add(minter.Next("SI")); }

            Assert.Equal(new[] { "NCR-0001", "NCR-0002", "NCR-0003", "NCR-0004", "NCR-0005" }, ncr);
            Assert.Equal(new[] { "SI-0001", "SI-0002", "SI-0003", "SI-0004", "SI-0005" }, si);
            Assert.Equal(10, ncr.Concat(si).Distinct().Count());
        }

        [Fact]
        public void Minting_sees_the_high_water_mark_across_all_id_spellings()
        {
            // GetNextIssueId used to scan "issue_id" ONLY, so a register whose highest NCR
            // came from an escalation path ("id") had an invisible high-water mark, and it
            // reissued an identifier that row already held.
            var store = new JArray
            {
                new JObject { ["issue_id"] = "NCR-0002" },
                new JObject { ["id"]       = "NCR-0009" },   // invisible to the old scan
                new JObject { ["IssueId"]  = "NCR-0004" },
            };

            Assert.Equal("NCR-0010", new IssueIdMinter(store).Next("NCR"));
        }

        [Fact]
        public void Minting_skips_a_taken_id_even_when_the_sequence_would_land_on_it()
        {
            // Gappy register: high-water is 3, but 2 is free and 4 is taken out of sequence.
            var store = new JArray
            {
                new JObject { ["issue_id"] = "SI-0001" },
                new JObject { ["issue_id"] = "SI-0003" },
            };
            var minter = new IssueIdMinter(store);

            Assert.Equal("SI-0004", minter.Next("SI"));
            Assert.Equal("SI-0005", minter.Next("SI"));
        }

        // ── 3. Status vocabulary ──────────────────────────────────────────

        [Theory]
        [InlineData("OPEN",        true)]
        [InlineData("Open",        true)]   // clash engine
        [InlineData("open",        true)]   // ACC
        [InlineData("New",         true)]   // server / mobile
        [InlineData("IN_PROGRESS", true)]
        [InlineData("CLOSED",      false)]
        [InlineData("Resolved",    false)]
        [InlineData("VOID",        false)]
        public void IsOpen_is_one_predicate_across_every_spelling(string status, bool expected)
        {
            Assert.Equal(expected, IssueSchema.IsOpen(new JObject { ["status"] = status }));
        }

        [Fact]
        public void IsOpen_reads_the_PascalCase_status_too()
        {
            // An LPS row that has not been migrated yet must still reach the gate.
            Assert.True(IssueSchema.IsOpen(new JObject { ["Status"] = "OPEN" }));
            Assert.False(IssueSchema.IsOpen(new JObject { ["Status"] = "CLOSED" }));
        }

        [Fact]
        public void An_absent_status_counts_as_open_so_the_gate_fails_safe()
        {
            Assert.True(IssueSchema.IsOpen(new JObject { ["issue_id"] = "RFI-0001" }));
        }

        [Fact]
        public void Migration_does_not_destroy_a_status_it_does_not_recognise()
        {
            // RESPONDED and ACCEPTED are written by UpdateIssueCommand and filtered on
            // exactly elsewhere. Canonicalising them would collapse both to "UNKNOWN" and
            // lose a distinction the workflow depends on.
            foreach (string status in new[] { "RESPONDED", "ACCEPTED" })
            {
                var row = new JObject { ["issue_id"] = "RFI-0001", ["status"] = status };
                IssueSchema.Migrate(row);
                Assert.Equal(status, (string)row["status"]);
                Assert.True(IssueSchema.IsOpen(row));   // not closed ⇒ still needs attention
            }
        }

        [Fact]
        public void OpenCount_counts_mixed_vocabularies_as_one()
        {
            var store = new JArray
            {
                new JObject { ["issue_id"] = "A", ["status"] = "OPEN" },
                new JObject { ["id"]       = "B", ["status"] = "Open" },
                new JObject { ["IssueId"]  = "C", ["Status"] = "open" },
                new JObject { ["issue_id"] = "D", ["status"] = "CLOSED" },
                new JObject { ["issue_id"] = "E", ["status"] = "Resolved" },
            };

            // The BCC KPI, the has_open_issues gate and any dashboard all call this.
            Assert.Equal(3, IssueSchema.OpenCount(store));
        }

        // ── 4. Creation + dedup ───────────────────────────────────────────

        [Fact]
        public void Create_emits_the_canonical_identifier_and_nothing_else()
        {
            var row = IssueSchema.Create(
                new IssueSpec { Type = "NCR", Title = "t", Source = IssueSource.Warning },
                "NCR-0001", T0, "tester");

            Assert.Equal("NCR-0001", (string)row["issue_id"]);
            Assert.Null(row["id"]);        // the fork is not recreated on write
            Assert.Null(row["IssueId"]);
            Assert.Equal("OPEN", (string)row["status"]);
            Assert.Equal("warning", (string)row["source"]);
        }

        [Fact]
        public void Create_sets_the_SLA_due_date_from_priority()
        {
            string Due(string priority) => (string)IssueSchema.Create(
                new IssueSpec { Priority = priority, Title = "t" }, "X-0001", T0, "u")["date_due"];

            Assert.Equal("2026-07-21", Due("CRITICAL"));   // +1
            Assert.Equal("2026-07-23", Due("HIGH"));       // +3
            Assert.Equal("2026-07-27", Due("MEDIUM"));     // +7
            Assert.Equal("2026-08-03", Due("LOW"));        // +14
        }

        [Fact]
        public void FindOpenByDedupKey_matches_only_a_still_open_issue_of_the_same_source()
        {
            var store = new JArray
            {
                new JObject { ["issue_id"] = "NCR-0001", ["status"] = "CLOSED",
                              ["source"] = "warning", ["source_hash"] = "cat:Room" },
                new JObject { ["issue_id"] = "NCR-0002", ["status"] = "OPEN",
                              ["source"] = "clash",   ["source_hash"] = "cat:Room" },
            };

            // Closed ⇒ no match, so a recurrence raises a fresh issue rather than being
            // suppressed forever by a resolved one.
            Assert.Null(IssueSchema.FindOpenByDedupKey(store, IssueSource.Warning, "cat:Room"));
            // Different provenance, same hash ⇒ not the same finding.
            Assert.Null(IssueSchema.FindOpenByDedupKey(store, IssueSource.Lps, "cat:Room"));
            // Matching source + hash + still open ⇒ match.
            Assert.NotNull(IssueSchema.FindOpenByDedupKey(store, IssueSource.Clash, "cat:Room"));
        }

        [Fact]
        public void ApplyStatus_records_history_and_stamps_the_close_date()
        {
            var row = IssueSchema.Create(new IssueSpec { Title = "t" }, "RFI-0001", T0, "author");

            Assert.True(IssueSchema.ApplyStatus(row, "Closed", "closer", T0.AddDays(1), "fixed"));

            Assert.Equal("CLOSED", (string)row["status"]);
            Assert.Equal("2026-07-21 09:00", (string)row["date_closed"]);
            var hist = Assert.IsType<JArray>(row["status_history"]);
            var entry = Assert.IsType<JObject>(hist.Single());
            Assert.Equal("OPEN", (string)entry["from"]);
            Assert.Equal("CLOSED", (string)entry["to"]);
            Assert.Equal("closer", (string)entry["by"]);

            // A no-op transition changes nothing and adds no history.
            Assert.False(IssueSchema.ApplyStatus(row, "CLOSED", "x", T0.AddDays(2), null));
            Assert.Single(hist);
        }

        // ── 5. IM-8: the unreserved-minter defect ─────────────────────────

        [Fact]
        public void RED_a_minter_rebuilt_per_call_repeats_itself_when_the_caller_batches()
        {
            // What the retired GetNextIssueId did: rebuild from the array on every call.
            // Correct ONLY because every caller happened to append in the same iteration.
            // A caller that batched creations before saving got the same id every time.
            string Rebuilt(JArray rows, string type) => new IssueIdMinter(rows).Next(type);

            var store = new JArray();
            var batched = new List<string>();
            for (int i = 0; i < 3; i++) batched.Add(Rebuilt(store, "BCF"));   // nothing appended yet

            Assert.Equal(new[] { "BCF-0001", "BCF-0001", "BCF-0001" }, batched);
            Assert.Single(batched.Distinct());
        }

        [Fact]
        public void GREEN_one_minter_held_across_a_batch_does_not_repeat()
        {
            var store = new JArray();
            var minter = new IssueIdMinter(store);          // held for the whole batch
            var batched = new List<string>();
            for (int i = 0; i < 3; i++) batched.Add(minter.Next("BCF"));

            Assert.Equal(new[] { "BCF-0001", "BCF-0002", "BCF-0003" }, batched);
            Assert.Equal(3, batched.Distinct().Count());
        }

        // ── 6. Adopt: importer-shaped records stay canonical ──────────────

        [Fact]
        public void An_adopted_record_is_migrated_so_an_importer_cannot_refork_the_schema()
        {
            // Adopt() is what the BCF importers use. If it accepted a row verbatim, an
            // importer emitting "id" would quietly reopen IM-4.
            var imported = new JObject
            {
                ["id"] = "BCF-0001",
                ["title"] = "Imported topic",
                ["status"] = "Active",
                ["bcf_guid"] = "abc-123",
            };

            IssueSchema.Migrate(imported);   // what Adopt does on the way in

            Assert.Equal("BCF-0001", (string)imported["issue_id"]);
            Assert.Null(imported["id"]);
            // "Active" is a known spelling -> IN_PROGRESS -> still open for the gate.
            Assert.True(IssueSchema.IsOpen(imported));
        }

        [Fact]
        public void Bcf_status_words_reach_the_open_predicate()
        {
            // BCF 2.1 speaks Active/Resolved/Closed. The gate could not read any of them
            // before the importers normalised on the way in.
            Assert.True(IssueStatusNormalizer.IsOpen("Active"));
            Assert.False(IssueStatusNormalizer.IsOpen("Resolved"));
            Assert.False(IssueStatusNormalizer.IsOpen("Closed"));
            Assert.Equal("IN_PROGRESS", IssueStatusNormalizer.Canonical("Active"));
        }

        [Fact]
        public void Create_stamps_the_dedup_key_so_a_reimport_is_suppressed()
        {
            var row = IssueSchema.Create(
                new IssueSpec { Type = "BCF", Title = "t", Source = IssueSource.Bcf,
                                SourceHash = "bcf:abc-123" },
                "BCF-0001", T0, "importer");

            Assert.Equal("bcf", (string)row["source"]);
            Assert.Equal("bcf:abc-123", (string)row["source_hash"]);

            var store = new JArray { row };
            Assert.NotNull(IssueSchema.FindOpenByDedupKey(store, IssueSource.Bcf, "bcf:abc-123"));
        }

        [Fact]
        public void FindByServerId_backs_the_pull_dedup()
        {
            var store = new JArray
            {
                new JObject { ["issue_id"] = "RFI-0001" },
                new JObject { ["issue_id"] = "RFI-0002", ["server_id"] = "3f2b8c10-0000-0000-0000-000000000001" },
            };

            Assert.Equal("RFI-0002",
                IssueSchema.IdOf(IssueSchema.FindByServerId(store, "3F2B8C10-0000-0000-0000-000000000001")));
            Assert.Null(IssueSchema.FindByServerId(store, "no-such-guid"));
        }
    }
}
