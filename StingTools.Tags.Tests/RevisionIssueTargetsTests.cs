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
            var p = RevisionIssueTargets.Plan(new[] { "A-101" }, new[] { "X-1", "a-101" }, "M-200", Stamped, All, quiet: true);
            Assert.Equal(RevisionIssueSource.CloudsOrPicks, p.Source);
            Assert.Equal(new[] { "A-101", "X-1" }, p.SheetNumbers);
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
}
