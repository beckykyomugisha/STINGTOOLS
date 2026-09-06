using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// The rate editor exists because the instruction it replaces cannot be
    /// followed. "Add a row keyed 'RD_Breeze Block 01_Panel — Concrete'"
    /// requires typing a non-ASCII em dash into a file matched by exact
    /// OrdinalIgnoreCase lookup; a hyphen misses and the row stays unpriced
    /// with no error at all. 21 of the 25 keys the first real export asked for
    /// are like that.
    ///
    /// So the tests here are mostly about what the editor REFUSES to do —
    /// every one of them a way it could quietly lose a price.
    /// </summary>
    public class RateEditorSeedTests
    {
        private static MaterialScheduleDocument Doc(params MaterialCommodity[] commodities)
        {
            var d = new MaterialScheduleDocument();
            d.Stages.Add(new StageSection
            {
                StageId = "superstructure",
                Commodities = commodities.ToList()
            });
            return d;
        }

        private static MaterialCommodity C(string key, double qty = 1, double rate = 0,
                                          string source = "unpriced", bool memo = false) =>
            new MaterialCommodity
            {
                CommodityKey = key, Description = key, SupplierUnit = "No.",
                OrderQuantity = qty, RateUGX = rate, RateSource = source, IsMemorandum = memo
            };

        // ── seeding ─────────────────────────────────────────────────────────

        [Fact]
        public void The_Key_Comes_From_The_Schedule_So_Nobody_Types_An_Em_Dash()
        {
            const string awkward = "RD_Breeze Block 01_Panel — Concrete";
            var seed = RateEditorSeed.Build(Doc(C(awkward)));

            Assert.Equal(awkward, seed.Rows.Single().CommodityKey);
        }

        [Fact]
        public void A_Memorandum_Is_Never_Offered_A_Rate()
        {
            // Its AmountUGX is hard-zero by design. A rate cell here would look
            // like it did nothing, and pricing blockwork area beside the blocks
            // derived from it pays for the same wall twice.
            var seed = RateEditorSeed.Build(Doc(C("blockwork-area", memo: true), C("blocks")));

            Assert.Equal("blocks", seed.Rows.Single().CommodityKey);
            Assert.Equal(1, seed.MemorandaExcluded);
            Assert.Contains("twice", seed.Summary());
        }

        [Fact]
        public void Priced_Rows_Are_Offered_Too_Because_A_Baseline_Rate_Is_Indicative()
        {
            var seed = RateEditorSeed.Build(Doc(C("cement", rate: 28000, source: "baseline")));

            var row = seed.Rows.Single();
            Assert.False(row.IsUnpriced);
            Assert.Equal("baseline", row.CurrentSource);
        }

        [Fact]
        public void Unpriced_Rows_Sort_First_Then_By_Quantity()
        {
            var seed = RateEditorSeed.Build(Doc(
                C("cement", qty: 90, rate: 28000, source: "baseline"),
                C("small-unpriced", qty: 1),
                C("big-unpriced", qty: 610)));

            Assert.Equal(new[] { "big-unpriced", "small-unpriced", "cement" },
                         seed.Rows.Select(r => r.CommodityKey).ToArray());
        }

        [Fact]
        public void One_Commodity_Appearing_In_Two_Stages_Is_One_Row()
        {
            // They share a rate; two rows would let a user set two.
            var d = Doc(C("cement", rate: 28000));
            d.Stages.Add(new StageSection { StageId = "finishes", Commodities = { C("cement", rate: 28000) } });

            Assert.Single(RateEditorSeed.Build(d).Rows);
        }

        [Fact]
        public void An_Empty_Schedule_Says_So_Rather_Than_Opening_A_Blank_Grid()
        {
            Assert.Contains("Nothing to price", RateEditorSeed.Build(Doc()).Summary());
            Assert.Contains("Nothing to price", RateEditorSeed.Build(null).Summary());
        }

        // ── merge ───────────────────────────────────────────────────────────

        private static RateEditRow Edit(string key, double? newRate, string unit = "No.") =>
            new RateEditRow { CommodityKey = key, SupplierUnit = unit, NewRateUGX = newRate };

        [Fact]
        public void A_Blank_Cell_Leaves_An_Existing_Price_Alone()
        {
            // The failure this prevents: opening the editor, changing one row,
            // and silently deleting every other price by saving.
            var existing = new[] { new CommodityRate { CommodityKey = "cement", RateUGX = 30000 } };

            var merged = RateEditorSeed.Merge(existing, new[] { Edit("cement", null) },
                                              out int added, out int changed, out int untouched);

            Assert.Equal(30000, merged.Single().RateUGX);
            Assert.Equal(0, added);
            Assert.Equal(0, changed);
            Assert.Equal(1, untouched);
        }

        [Fact]
        public void A_Zero_Is_Not_Written_Because_The_Resolver_Would_Ignore_It()
        {
            // Resolve() only takes a project row when RateUGX > 0, so a zero row
            // is a file that looks like a decision and behaves like an absence.
            var merged = RateEditorSeed.Merge(null, new[] { Edit("roof", 0) },
                                              out int added, out _, out int untouched);

            Assert.Empty(merged);
            Assert.Equal(0, added);
            Assert.Equal(1, untouched);
        }

        [Fact]
        public void A_New_Rate_Is_Added()
        {
            var merged = RateEditorSeed.Merge(null, new[] { Edit("Generic - 225mm", 45000) },
                                              out int added, out _, out _);

            Assert.Equal(45000, merged.Single().RateUGX);
            Assert.Equal("project", merged.Single().Source);
            Assert.Equal(1, added);
        }

        [Fact]
        public void An_Existing_Rate_Is_Changed_Not_Duplicated()
        {
            var existing = new[] { new CommodityRate { CommodityKey = "cement", RateUGX = 28000 } };

            var merged = RateEditorSeed.Merge(existing, new[] { Edit("cement", 31000) },
                                              out int added, out int changed, out _);

            Assert.Equal(31000, merged.Single().RateUGX);
            Assert.Equal(0, added);
            Assert.Equal(1, changed);
        }

        [Fact]
        public void Retyping_The_Same_Number_Is_Not_A_Change()
        {
            var existing = new[] { new CommodityRate { CommodityKey = "cement", RateUGX = 28000 } };

            RateEditorSeed.Merge(existing, new[] { Edit("cement", 28000) },
                                 out _, out int changed, out int untouched);

            Assert.Equal(0, changed);
            Assert.Equal(1, untouched);
        }

        [Fact]
        public void A_Rate_For_Something_Not_In_Todays_Model_Is_Kept()
        {
            // Models change between exports. Dropping rates for what this one
            // does not contain would discard pricing work with nothing said.
            var existing = new[] { new CommodityRate { CommodityKey = "priced-last-month", RateUGX = 9000 } };

            var merged = RateEditorSeed.Merge(existing, new[] { Edit("cement", 28000) },
                                              out _, out _, out _);

            Assert.Equal(2, merged.Count);
            Assert.Contains(merged, r => r.CommodityKey == "priced-last-month" && r.RateUGX == 9000);
        }

        [Fact]
        public void The_Description_Of_A_Hand_Edited_Row_Survives_A_Rewrite()
        {
            var existing = new[] { new CommodityRate
                { CommodityKey = "cement", RateUGX = 28000, Description = "Hima 42.5N, quoted 3 Sep" } };

            var merged = RateEditorSeed.Merge(existing, new[] { Edit("cement", 31000) },
                                              out _, out _, out _);

            Assert.Equal("Hima 42.5N, quoted 3 Sep", merged.Single().Description);
        }

        // ── validation ──────────────────────────────────────────────────────

        [Fact]
        public void A_Duplicate_Key_Is_Reported_Because_The_Last_One_Would_Win_Silently()
        {
            var problems = RateEditorSeed.Validate(new[] { Edit("cement", 1), Edit("cement", 2) });

            Assert.Contains(problems, p => p.Contains("more than once"));
        }

        [Fact]
        public void A_Negative_Rate_Is_Reported()
        {
            Assert.Contains(RateEditorSeed.Validate(new[] { Edit("cement", -5) }),
                            p => p.Contains("negative"));
        }

        [Fact]
        public void A_Clean_Set_Reports_Nothing()
        {
            Assert.Empty(RateEditorSeed.Validate(new[] { Edit("cement", 28000), Edit("sand", 1400000) }));
        }
    }
}
