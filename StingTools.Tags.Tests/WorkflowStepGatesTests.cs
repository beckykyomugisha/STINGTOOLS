// ══════════════════════════════════════════════════════════════════════════
//  WorkflowStepGatesTests.cs — gates the Complete Guide documented and no
//  code read (requiresPhase, requiresMinElements, requiresIssueCount,
//  onWeekday, afterTime, beforeTime), and minElementCount, which was tested
//  only when the step also had a `condition` string.
//
//  The shipped-preset test is the one that would have caught the
//  minElementCount defect: it loads the real JSON, so a step that sets the
//  key and no condition is asserted to be gated.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class WorkflowStepGatesTests
    {
        // Wednesday 30 September 2026, 09:15.
        private static readonly DateTime Wed0915 = new DateTime(2026, 9, 30, 9, 15, 0);

        private static WorkflowStepGates.Facts Facts(
            int elements = 1000, int tagged = 500, int issues = 0, string phase = "DE", DateTime? now = null)
            => new WorkflowStepGates.Facts
            {
                ElementCount = () => elements,
                TaggedCount = () => tagged,
                OpenIssueCount = () => issues,
                ProjectPhase = () => phase,
                Now = now ?? Wed0915,
            };

        [Fact]
        public void A_step_with_no_gates_runs()
        {
            Assert.Null(WorkflowStepGates.SkipReason(new WorkflowStep { CommandTag = "X" }, Facts()));
        }

        [Fact]
        public void A_count_the_step_does_not_ask_for_is_never_computed()
        {
            var f = Facts();
            f.ElementCount = () => throw new InvalidOperationException("scanned");
            f.TaggedCount = () => throw new InvalidOperationException("scanned");
            f.OpenIssueCount = () => throw new InvalidOperationException("scanned");
            Assert.Null(WorkflowStepGates.SkipReason(new WorkflowStep { OnWeekday = "Wed" }, f));
        }

        // ── minElementCount: now gated without a condition string ───────────

        [Theory]
        [InlineData(49, true)]
        [InlineData(50, false)]
        public void MinElementCount_gates_a_step_that_has_no_condition(int elements, bool skipped)
        {
            var step = new WorkflowStep { MinElementCount = 50 };
            Assert.Null(step.Condition);
            Assert.Equal(skipped, WorkflowStepGates.SkipReason(step, Facts(elements: elements)) != null);
        }

        [Fact]
        public void MaxElementCount_skips_a_larger_model()
        {
            string r = WorkflowStepGates.SkipReason(new WorkflowStep { MaxElementCount = 100 }, Facts(elements: 101));
            Assert.Contains("101 elements > max 100", r);
        }

        [Fact]
        public void Every_shipped_step_with_minElementCount_is_gated()
        {
            string data = Path.Combine(RepoRoot(), "StingTools", "Data");
            int found = 0;
            foreach (string file in Directory.GetFiles(data, "WORKFLOW_*.json"))
            {
                var preset = JsonConvert.DeserializeObject<WorkflowPreset>(File.ReadAllText(file));
                foreach (var step in preset?.Steps ?? Enumerable.Empty<WorkflowStep>())
                {
                    if (!step.MinElementCount.HasValue) continue;
                    found++;
                    Assert.NotNull(WorkflowStepGates.SkipReason(step, Facts(elements: step.MinElementCount.Value - 1)));
                }
            }
            Assert.True(found >= 2, $"expected the MorningHealthCheck and WeeklyDataDrop steps, found {found}");
        }

        // ── requiresMinElements / requiresIssueCount ────────────────────────

        [Theory]
        [InlineData(9, true)]
        [InlineData(10, false)]
        public void RequiresMinElements_counts_tagged_elements(int tagged, bool skipped)
        {
            var step = new WorkflowStep { RequiresMinElements = 10 };
            Assert.Equal(skipped, WorkflowStepGates.SkipReason(step, Facts(elements: 5000, tagged: tagged)) != null);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        public void RequiresIssueCount_needs_that_many_open_issues(int open, bool skipped)
        {
            var step = new WorkflowStep { RequiresIssueCount = 3 };
            Assert.Equal(skipped, WorkflowStepGates.SkipReason(step, Facts(issues: open)) != null);
        }

        // ── requiresPhase ───────────────────────────────────────────────────

        [Theory]
        [InlineData("DE", "DE", false)]
        [InlineData("de", "DE", false)]
        [InlineData("DE, CO", "CO", false)]
        [InlineData("CO", "DE", true)]
        public void RequiresPhase_matches_the_project_phase(string required, string projectPhase, bool skipped)
        {
            var step = new WorkflowStep { RequiresPhase = required };
            Assert.Equal(skipped, WorkflowStepGates.SkipReason(step, Facts(phase: projectPhase)) != null);
        }

        [Fact]
        public void RequiresPhase_skips_when_the_project_has_no_phase()
        {
            string r = WorkflowStepGates.SkipReason(new WorkflowStep { RequiresPhase = "DE" }, Facts(phase: ""));
            Assert.Contains("PRJ_ORG_PHASE_TXT", r);
        }

        // ── onWeekday ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("Wed", false)]
        [InlineData("wednesday", false)]
        [InlineData("Mon,Wed;Fri", false)]
        [InlineData("Mon,Fri", true)]
        public void OnWeekday_runs_only_on_listed_days(string days, bool skipped)
        {
            var step = new WorkflowStep { OnWeekday = days };
            Assert.Equal(skipped, WorkflowStepGates.SkipReason(step, Facts()) != null);
        }

        [Theory]
        [InlineData("Wedn")]
        [InlineData("We")]
        [InlineData("Funday")]
        public void An_unreadable_day_skips_and_names_the_value(string days)
        {
            string r = WorkflowStepGates.SkipReason(new WorkflowStep { OnWeekday = days }, Facts());
            Assert.NotNull(r);
            Assert.Contains(days, r);
        }

        // ── afterTime / beforeTime ──────────────────────────────────────────

        [Theory]
        [InlineData("09:00", null, false)]
        [InlineData("09:15", null, false)]
        [InlineData("09:16", null, true)]
        [InlineData(null, "09:16", false)]
        [InlineData(null, "09:15", true)]
        [InlineData("08:00", "17:00", false)]
        [InlineData("10:00", "17:00", true)]
        [InlineData("22:00", "06:00", true)]
        [InlineData("22:00", "10:00", false)]
        public void Time_window_is_after_inclusive_before_exclusive(string after, string before, bool skipped)
        {
            var step = new WorkflowStep { AfterTime = after, BeforeTime = before };
            Assert.Equal(skipped, WorkflowStepGates.SkipReason(step, Facts()) != null);
        }

        [Fact]
        public void An_overnight_window_runs_after_midnight()
        {
            var step = new WorkflowStep { AfterTime = "22:00", BeforeTime = "06:00" };
            Assert.Null(WorkflowStepGates.SkipReason(step, Facts(now: new DateTime(2026, 9, 30, 2, 0, 0))));
            Assert.Null(WorkflowStepGates.SkipReason(step, Facts(now: new DateTime(2026, 9, 30, 23, 0, 0))));
        }

        [Theory]
        [InlineData("9am")]
        [InlineData("25:00")]
        [InlineData("09.30")]
        public void An_unreadable_time_skips_and_names_the_value(string after)
        {
            string r = WorkflowStepGates.SkipReason(new WorkflowStep { AfterTime = after }, Facts());
            Assert.NotNull(r);
            Assert.Contains(after, r);
        }

        // ── JSON binding: the keys the guide documents are the keys bound ───

        [Fact]
        public void The_documented_keys_bind_from_preset_json()
        {
            const string json = @"{""commandTag"":""X"",""requiresPhase"":""DE"",""requiresMinElements"":5,
                ""requiresIssueCount"":2,""onWeekday"":""Mon"",""afterTime"":""07:00"",""beforeTime"":""18:00""}";
            var s = JsonConvert.DeserializeObject<WorkflowStep>(json);
            Assert.Equal("DE", s.RequiresPhase);
            Assert.Equal(5, s.RequiresMinElements);
            Assert.Equal(2, s.RequiresIssueCount);
            Assert.Equal("Mon", s.OnWeekday);
            Assert.Equal("07:00", s.AfterTime);
            Assert.Equal("18:00", s.BeforeTime);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
