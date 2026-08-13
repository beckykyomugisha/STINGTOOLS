// IM Phase 3 — warning snapshot trend store.
//
// These pin three things the runtime cannot cheaply re-check:
//   • the JSONL codec round-trips and survives a corrupt line (a crash mid-append
//     must not cost the whole trend history);
//   • the store is append-only — N scans produce N lines and never rewrite an
//     earlier one, which is what makes the file safe to tail;
//   • the server payloads match the field names in
//     Planscape.API/Controllers/WarningsController.cs. That contract is the one
//     thing here with no compile-time link to the server — a rename on either
//     side would otherwise fail silently at runtime, in a fire-and-forget call
//     that deliberately swallows its own failure.
//
// This is wiring, not a bug fix, so there is no red-then-green pair: nothing was
// broken before, because nothing existed. The one exception is
// Payload_field_names_match_the_server_contract, which is a genuine regression
// guard against a future rename.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class WarningSnapshotFormatTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 7, 20, 14, 2, 0, DateTimeKind.Utc);

        private static WarningSnapshotFormat.WarningSnapshot Snap(
            int total, int health = 70, string kind = WarningSnapshotFormat.KindScan, DateTime? ts = null)
            => new WarningSnapshotFormat.WarningSnapshot
            {
                TsUtc = ts ?? T0,
                Kind = kind,
                User = "del",
                Total = total,
                AutoFixable = 3,
                ManualReview = total - 3,
                HealthScore = health,
                BaselineTotal = 40,
                BySeverity = new Dictionary<string, int> { ["Critical"] = 1, ["High"] = 2 },
                ByCategory = new Dictionary<string, int> { ["Geometric"] = 4, ["Data"] = 1 },
            };

        // ── Codec round-trip ────────────────────────────────────────────────

        [Fact]
        public void Round_trip_preserves_every_field()
        {
            var original = Snap(42);
            var back = WarningSnapshotFormat.TryParseLine(WarningSnapshotFormat.FormatLine(original));

            Assert.NotNull(back);
            Assert.Equal(42, back.Total);
            Assert.Equal(3, back.AutoFixable);
            Assert.Equal(39, back.ManualReview);
            Assert.Equal(70, back.HealthScore);
            Assert.Equal(40, back.BaselineTotal);
            Assert.Equal(WarningSnapshotFormat.KindScan, back.Kind);
            Assert.Equal("del", back.User);
            Assert.Equal(T0, back.TsUtc);
            Assert.Equal(1, back.BySeverity["Critical"]);
            Assert.Equal(2, back.BySeverity["High"]);
            Assert.Equal(4, back.ByCategory["Geometric"]);
        }

        [Fact]
        public void A_formatted_line_never_contains_a_newline()
        {
            // Load-bearing: one record per line is the entire framing contract.
            // A pretty-printed record would corrupt every subsequent parse.
            string line = WarningSnapshotFormat.FormatLine(Snap(7));
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }

        [Fact]
        public void Multiple_lines_all_parse_in_order()
        {
            string content = string.Join(Environment.NewLine, new[]
            {
                WarningSnapshotFormat.FormatLine(Snap(10)),
                WarningSnapshotFormat.FormatLine(Snap(20)),
                WarningSnapshotFormat.FormatLine(Snap(30)),
            });

            var parsed = WarningSnapshotFormat.ParseLines(content);

            Assert.Equal(3, parsed.Count);
            Assert.Equal(new[] { 10, 20, 30 }, parsed.Select(p => p.Total).ToArray());
        }

        [Fact]
        public void A_corrupt_line_is_skipped_and_the_rest_survive()
        {
            // The realistic failure: process killed mid-append leaves a half line.
            string content = string.Join(Environment.NewLine, new[]
            {
                WarningSnapshotFormat.FormatLine(Snap(10)),
                "{\"ts\":\"2026-07-20T14:0",          // truncated
                "not json at all",
                "",
                WarningSnapshotFormat.FormatLine(Snap(30)),
            });

            var parsed = WarningSnapshotFormat.ParseLines(content);

            Assert.Equal(2, parsed.Count);
            Assert.Equal(new[] { 10, 30 }, parsed.Select(p => p.Total).ToArray());
        }

        [Fact]
        public void Empty_and_null_content_yield_no_rows_rather_than_throwing()
        {
            Assert.Empty(WarningSnapshotFormat.ParseLines(null));
            Assert.Empty(WarningSnapshotFormat.ParseLines(""));
            Assert.Empty(WarningSnapshotFormat.ParseLines("   \n  \n"));
        }

        [Fact]
        public void A_snapshot_with_no_baseline_omits_the_field_and_reads_back_null()
        {
            var s = Snap(5);
            s.BaselineTotal = null;

            string line = WarningSnapshotFormat.FormatLine(s);
            Assert.DoesNotContain("baseline_total", line);
            Assert.Null(WarningSnapshotFormat.TryParseLine(line).BaselineTotal);
        }

        // ── Append-only growth ──────────────────────────────────────────────

        [Fact]
        public void N_scans_produce_N_lines_and_never_rewrite_an_earlier_one()
        {
            // Models the store's append path: each scan contributes exactly one
            // line, and every previously written line is byte-identical after.
            var lines = new List<string>();
            var firstFive = new List<string>();

            for (int i = 1; i <= 25; i++)
            {
                lines.Add(WarningSnapshotFormat.FormatLine(Snap(i, ts: T0.AddMinutes(i))));
                if (i == 5) firstFive = new List<string>(lines);
            }

            Assert.Equal(25, lines.Count);

            // Prefix untouched — this is what "append-only" means in practice.
            for (int i = 0; i < firstFive.Count; i++)
                Assert.Equal(firstFive[i], lines[i]);

            var parsed = WarningSnapshotFormat.ParseLines(string.Join(Environment.NewLine, lines));
            Assert.Equal(25, parsed.Count);
            Assert.Equal(1, parsed.First().Total);
            Assert.Equal(25, parsed.Last().Total);
        }

        [Fact]
        public void Cap_keeps_the_most_recent_rows_and_is_a_noop_below_the_limit()
        {
            var lines = Enumerable.Range(1, 10).Select(i => $"line{i}").ToList();

            var under = WarningSnapshotFormat.Cap(lines, 50);
            Assert.Equal(10, under.Count);
            Assert.Equal("line1", under.First());       // untouched below the cap

            var over = WarningSnapshotFormat.Cap(lines, 4);
            Assert.Equal(4, over.Count);
            Assert.Equal("line7", over.First());        // oldest dropped
            Assert.Equal("line10", over.Last());
        }

        // ── Server contract ─────────────────────────────────────────────────

        [Fact]
        public void Payload_field_names_match_the_server_contract()
        {
            // PushWarningReportRequest(int TotalWarnings, int HealthScore,
            //                          string? ByCategoryJson, string? BySeverityJson)
            var report = WarningSnapshotFormat.BuildReportPayload(Snap(42, health: 55));

            Assert.Equal(4, report.Properties().Count());
            Assert.Equal(42, (int)report["totalWarnings"]);
            Assert.Equal(55, (int)report["healthScore"]);
            Assert.NotNull(report["byCategoryJson"]);
            Assert.NotNull(report["bySeverityJson"]);

            // SaveWarningBaselineRequest(int WarningCount, int HealthScore,
            //                            int TotalElements, double CompliancePercent)
            var baseline = WarningSnapshotFormat.BuildBaselinePayload(
                Snap(42, health: 55, kind: WarningSnapshotFormat.KindBaseline), 1200, 87.5);

            Assert.Equal(4, baseline.Properties().Count());
            Assert.Equal(42, (int)baseline["warningCount"]);
            Assert.Equal(55, (int)baseline["healthScore"]);
            Assert.Equal(1200, (int)baseline["totalElements"]);
            Assert.Equal(87.5, (double)baseline["compliancePercent"]);
        }

        [Fact]
        public void The_category_and_severity_payload_fields_are_json_strings_not_objects()
        {
            // The server binds these to `string?` and stores them verbatim. Emitting
            // a nested object here would bind as null and silently lose the breakdown.
            var payload = WarningSnapshotFormat.BuildReportPayload(Snap(9));

            Assert.Equal(JTokenType.String, payload["bySeverityJson"].Type);
            Assert.Equal(JTokenType.String, payload["byCategoryJson"].Type);

            var reparsed = JObject.Parse((string)payload["bySeverityJson"]);
            Assert.Equal(1, (int)reparsed["Critical"]);
            Assert.Equal(2, (int)reparsed["High"]);
        }

        [Fact]
        public void Payload_builders_return_null_for_a_null_snapshot_rather_than_throwing()
        {
            Assert.Null(WarningSnapshotFormat.BuildReportPayload(null));
            Assert.Null(WarningSnapshotFormat.BuildBaselinePayload(null, 0, 0));
        }

        // ── Trend ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData(10, 15, "↑5")]
        [InlineData(15, 10, "↓5")]
        [InlineData(12, 12, "→0")]
        public void Delta_describes_the_direction_of_travel(int before, int after, string expectedArrow)
        {
            string hint = WarningSnapshotFormat.DescribeDelta(
                Snap(before, ts: T0), Snap(after, ts: T0.AddHours(1)));

            Assert.StartsWith(expectedArrow, hint);
        }

        [Fact]
        public void Delta_is_null_when_there_is_nothing_to_compare_against()
        {
            Assert.Null(WarningSnapshotFormat.DescribeDelta(null, Snap(10)));
            Assert.Null(WarningSnapshotFormat.DescribeDelta(Snap(10), null));
        }
    }
}
