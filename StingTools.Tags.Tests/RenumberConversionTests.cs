using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-211. Under the ISO policy "compacting gaps" rewrote every profile-era sheet —
    /// issued ones included — to an ISO number, and the preview called it compaction.
    /// </summary>
    public class RenumberConversionTests
    {

        private static SheetNumberEngine.RenumberItem Item(string id, string number, int? seq, bool shapeChange, bool issued)
            => new SheetNumberEngine.RenumberItem
            {
                Id = id, CurrentNumber = number, Bucket = "iso|x", CurrentSeq = seq,
                ShapeChange = shapeChange, Issued = issued,
                NumberFor = s => "PRJ-ORG-01-00-DR-A-" + s.ToString("D4"),
            };

        [Fact]
        public void A_profile_era_sheet_is_listed_as_a_conversion_not_a_gap()
        {
            var items = new List<SheetNumberEngine.RenumberItem>
            {
                Item("1", "PRJ-ORG-01-00-DR-A-0001", 1, false, false),
                Item("2", "A-Level1-003", 3, true, false),
            };
            var plan = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber), SheetNumberPolicyKind.Iso);
            Assert.Single(plan.Moves);
            Assert.Single(plan.Conversions);
            Assert.Equal("A-Level1-003", plan.Conversions[0].From);
            Assert.Equal("PRJ-ORG-01-00-DR-A-0002", plan.Conversions[0].To);
        }

        [Fact]
        public void An_issued_profile_era_sheet_is_kept_unless_the_caller_opts_in()
        {
            var items = new List<SheetNumberEngine.RenumberItem>
            {
                Item("1", "PRJ-ORG-01-00-DR-A-0001", 1, false, false),
                Item("2", "A-Level1-003", 3, true, true),
            };
            var kept = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber), SheetNumberPolicyKind.Iso);
            Assert.Empty(kept.Moves);
            Assert.Single(kept.IssuedKept);
            Assert.Contains("A-Level1-003", kept.IssuedKept[0]);

            var converted = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber),
                SheetNumberPolicyKind.Iso, convertIssued: true);
            Assert.Single(converted.Conversions);
            Assert.Empty(converted.IssuedKept);
        }

        [Fact]
        public void An_issued_sheet_already_in_shape_is_still_compacted()
        {
            var items = new List<SheetNumberEngine.RenumberItem>
            {
                Item("1", "PRJ-ORG-01-00-DR-A-0002", 2, false, true),
            };
            var plan = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber), SheetNumberPolicyKind.Iso);
            Assert.Single(plan.Moves);
            Assert.Empty(plan.Conversions);
            Assert.Empty(plan.IssuedKept);
        }
    }
}
