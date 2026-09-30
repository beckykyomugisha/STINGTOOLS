// Issue Sheets for Revision marked the revision Issued whatever it had added it
// to. A first issue of STING-produced sheets (no clouds yet) added it to nothing
// and locked the revision. RevisionIssueTargets decides the sheets and says when
// there are none, so the command stops before Issued = true.

using System.Linq;
using StingTools.BIMManager;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class RevisionIssueTargetsTests
    {
        private static readonly string[] All = { "A-100", "A-101", "M-200", "X-1" };
        private static readonly string[] Stamped = { "A-100", "M-200" };

        [Fact]
        public void Clouds_and_picks_win_and_are_merged()
        {
            var p = RevisionIssueTargets.Plan(new[] { "A-101" }, new[] { "X-1", "a-101" }, "", Stamped, All, quiet: true);
            Assert.Equal(RevisionIssueSource.CloudsOrPicks, p.Source);
            Assert.Equal(new[] { "A-101", "X-1" }, p.SheetNumbers);
        }

        [Fact]
        public void A_step_param_wins_over_clouds_because_the_step_named_the_sheets()
        {
            // A preset step that says "sheets": "M-200" issues M-200 even when a
            // cloud for the revision sits on A-101 — the step is the instruction.
            var p = RevisionIssueTargets.Plan(new[] { "A-101" }, null, "M-200", Stamped, All, quiet: true);
            Assert.Equal(RevisionIssueSource.StepParam, p.Source);
            Assert.Equal(new[] { "M-200" }, p.SheetNumbers);
        }

        [Fact]
        public void First_issue_in_a_preset_targets_the_stamped_sheets_without_asking()
        {
            var p = RevisionIssueTargets.Plan(null, null, "", Stamped, All, quiet: true);
            Assert.Equal(RevisionIssueSource.StampedSheets, p.Source);
            Assert.Equal(Stamped, p.SheetNumbers);
            Assert.False(p.NeedsConfirmation);
        }

        [Fact]
        public void First_issue_by_a_person_offers_the_stamped_sheets()
        {
            var p = RevisionIssueTargets.Plan(null, null, "", Stamped, All, quiet: false);
            Assert.True(p.NeedsConfirmation);
            Assert.Equal(2, p.SheetNumbers.Count);
        }

        [Fact]
        public void A_step_param_is_an_instruction_and_is_never_widened()
        {
            var p = RevisionIssueTargets.Plan(null, null, "A-101; NOPE", Stamped, All, quiet: true);
            Assert.Equal(RevisionIssueSource.StepParam, p.Source);
            Assert.Equal(new[] { "A-101" }, p.SheetNumbers);
            Assert.Equal(new[] { "NOPE" }, p.Unknown);

            var none = RevisionIssueTargets.Plan(null, null, "NOPE", Stamped, All, quiet: true);
            Assert.True(none.IsEmpty);
            Assert.Equal(RevisionIssueSource.None, none.Source);
            Assert.Contains("NOPE", none.Reason);
        }

        [Fact]
        public void Nothing_to_issue_is_empty_with_a_reason()
        {
            var p = RevisionIssueTargets.Plan(new string[0], new string[0], null, new string[0], All, quiet: true);
            Assert.True(p.IsEmpty);
            Assert.Equal(RevisionIssueSource.None, p.Source);
            Assert.Contains("un-issued", p.Reason);
        }
    }

    // The RevisionIssue preset runs Create Revision and then Auto Revision Cloud;
    // the cloud step compared against the snapshot Create Revision had just taken,
    // so it always found "no changes". baseline = "previous" skips that snapshot.
    public class RevisionSnapshotBaselineTests
    {
        private static readonly string[] Files =
        {
            @"C:\p\_data\coord\revisions\snapshot_pre_rev_P02_20261001_101500.json",
            @"C:\p\_data\coord\revisions\snapshot_pre_rev_P01_20260901_090000.json",
        };

        [Fact]
        public void Label_is_parsed_from_the_file_name()
        {
            Assert.Equal("pre_rev_P02", RevisionSnapshotBaseline.LabelOf(Files[0]));
            Assert.Equal("odd", RevisionSnapshotBaseline.LabelOf("odd.json"));
        }

        [Fact]
        public void Standalone_uses_the_latest_snapshot()
        {
            Assert.Equal(0, RevisionSnapshotBaseline.Pick(Files, "", "P02", out _));
            Assert.Equal(0, RevisionSnapshotBaseline.Pick(Files, "latest", "P02", out _));
        }

        [Fact]
        public void Previous_skips_the_new_revisions_own_snapshot()
        {
            Assert.Equal(1, RevisionSnapshotBaseline.Pick(Files, "previous", "P02", out var why));
            Assert.Contains("pre_rev_P01", why);
        }

        [Fact]
        public void Previous_does_not_skip_a_snapshot_that_is_not_the_latest_revisions_own()
        {
            // The newest revision is P03 but no pre_rev_P03 exists (e.g. it was
            // auto-opened on issue): the newest snapshot is still the baseline.
            Assert.Equal(0, RevisionSnapshotBaseline.Pick(Files, "previous", "P03", out _));
        }

        [Fact]
        public void Previous_on_the_first_revision_has_no_baseline()
        {
            Assert.Equal(-1, RevisionSnapshotBaseline.Pick(new[] { Files[1] }, "previous", "P01", out var why));
            Assert.Contains("no earlier baseline", why);
        }

        [Fact]
        public void No_snapshots_and_unknown_modes_are_reported()
        {
            Assert.Equal(-1, RevisionSnapshotBaseline.Pick(new string[0], "", "P01", out var none));
            Assert.Contains("Create Revision", none);
            Assert.Equal(-1, RevisionSnapshotBaseline.Pick(Files, "prevous", "P02", out var bad));
            Assert.StartsWith("unknown baseline", bad);
        }
    }
}
