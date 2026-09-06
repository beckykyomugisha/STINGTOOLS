using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the second real export listed the intermediate measure AND
    /// the things bought from it, side by side, each with an empty rate cell and
    /// an R3 saying "has no rate. It will total zero in a priced schedule":
    ///
    ///     Blockwork wall     175 m2   (no rate)
    ///     Hollow blocks 8"   2,292 No. @ 2,500
    ///
    /// 175 m2 x any rate is the same wall, paid for twice. These pin the marking
    /// that makes the second payment unrepresentable rather than discouraged.
    /// </summary>
    public class IntermediateMeasureTests
    {
        private static readonly List<IntermediateMeasureRule> Rules = new List<IntermediateMeasureRule>
        {
            new IntermediateMeasureRule { Kind = "blockwork", Children = { "block_units" }, Note = "bought as blocks" },
            new IntermediateMeasureRule { Kind = "mortar", Children = { "mortar_cement", "mortar_sand" } },
        };

        private static MaterialScheduleDocument Doc(params MaterialCommodity[] rows)
        {
            var d = new MaterialScheduleDocument();
            d.Stages.Add(new StageSection { StageId = "superstructure", Title = "SUPERSTRUCTURE" });
            d.Stages[0].Commodities.AddRange(rows);
            return d;
        }

        [Fact]
        public void An_Intermediate_Whose_Children_Are_Present_Is_Marked()
        {
            var doc = Doc(
                new MaterialCommodity { CommodityKey = "Blockwork wall", SourceKind = "blockwork", OrderQuantity = 174.6 },
                new MaterialCommodity { CommodityKey = "block", SourceKind = "block_units", OrderQuantity = 2292, RateUGX = 2500 });

            IntermediateMeasureMarker.Apply(doc, Rules);

            Assert.True(doc.Stages[0].Commodities[0].IsMemorandum);
            Assert.False(doc.Stages[0].Commodities[1].IsMemorandum);
        }

        [Fact]
        public void A_Marked_Row_Can_Never_Carry_Money()
        {
            var doc = Doc(
                new MaterialCommodity { CommodityKey = "Blockwork wall", SourceKind = "blockwork", OrderQuantity = 174.6 },
                new MaterialCommodity { CommodityKey = "block", SourceKind = "block_units", OrderQuantity = 2292, RateUGX = 2500 });
            IntermediateMeasureMarker.Apply(doc, Rules);

            // Even if something later resolves a rate onto it — a project rate
            // card keyed by description would — the amount stays zero.
            doc.Stages[0].Commodities[0].RateUGX = 35000;

            Assert.Equal(0, doc.Stages[0].Commodities[0].AmountUGX);
            Assert.Equal(5730000, doc.Stages[0].SubTotalUGX);
        }

        [Fact]
        public void An_Intermediate_With_No_Children_Present_Stays_Priceable()
        {
            // A project whose data yields no block count must keep its blockwork
            // area priceable. Turning a double-count into an OMISSION is worse:
            // a duplicated line is arguable, a missing one is invisible.
            var doc = Doc(new MaterialCommodity
            {
                CommodityKey = "Blockwork wall", SourceKind = "blockwork", OrderQuantity = 174.6
            });

            IntermediateMeasureMarker.Apply(doc, Rules);

            Assert.False(doc.Stages[0].Commodities[0].IsMemorandum);
        }

        [Fact]
        public void Children_Count_Document_Wide_Not_Section_Wide()
        {
            // A wall's plaster routes to FINISHES while its blockwork stays in
            // the frame, so a per-section check would leave half the
            // intermediates priceable.
            var doc = new MaterialScheduleDocument();
            doc.Stages.Add(new StageSection { StageId = "superstructure" });
            doc.Stages.Add(new StageSection { StageId = "finishes" });
            doc.Stages[0].Commodities.Add(new MaterialCommodity { SourceKind = "mortar", OrderQuantity = 11.66 });
            doc.Stages[1].Commodities.Add(new MaterialCommodity { SourceKind = "mortar_cement", OrderQuantity = 93 });

            IntermediateMeasureMarker.Apply(doc, Rules);

            Assert.True(doc.Stages[0].Commodities[0].IsMemorandum);
        }

        [Fact]
        public void R3_Does_Not_Flag_A_Memorandum_Row()
        {
            // R3 is what invited the double-count: it told the reader the row was
            // missing a rate. A memorandum row is unpriced by design.
            var doc = Doc(
                new MaterialCommodity { CommodityKey = "Blockwork wall", SourceKind = "blockwork", OrderQuantity = 174.6 },
                new MaterialCommodity { CommodityKey = "block", SourceKind = "block_units", OrderQuantity = 2292, RateUGX = 2500 });
            IntermediateMeasureMarker.Apply(doc, Rules);

            var rec = Reconciler.Check(doc);

            Assert.DoesNotContain(rec.Issues, i => i.Code == "R3" && i.CommodityKey == "Blockwork wall");
        }

        [Fact]
        public void R3_Still_Flags_A_Genuinely_Unpriced_Row()
        {
            var doc = Doc(new MaterialCommodity { CommodityKey = "roof-sheet", OrderQuantity = 610 });

            var rec = Reconciler.Check(doc);

            Assert.Contains(rec.Issues, i => i.Code == "R3" && i.CommodityKey == "roof-sheet");
        }
    }
}
