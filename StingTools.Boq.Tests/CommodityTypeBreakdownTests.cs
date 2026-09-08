using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// "We need the number of sheets/tiles for each type of roof."
    ///
    /// The schedule aggregates by commodity because that is what you BUY:
    /// three shingle roofs become one pile of bundles, and ordering three
    /// separate piles would be wrong. But aggregation destroys the three other
    /// things a quantity gets used for — checking a number against the model,
    /// phasing a delivery, and dividing work between subcontractors. "80
    /// bundles" cannot be traced back to a roof, and a wrong roof cannot be
    /// found inside it.
    ///
    /// So the breakdown is ADDITIVE: the order line is untouched and this says
    /// what went into it.
    /// </summary>
    public class CommodityTypeBreakdownTests
    {
        private static MaterialCommodity Commodity(string key, double order, string unit = "Bundles") =>
            new MaterialCommodity
            {
                CommodityKey = key, Description = key, SupplierUnit = unit, OrderQuantity = order
            };

        private static Dictionary<string, Dictionary<string, double>> Source(
            string key, params (string type, double qty)[] parts) =>
            new Dictionary<string, Dictionary<string, double>>
            {
                [key] = parts.ToDictionary(p => p.type, p => p.qty)
            };

        [Fact]
        public void An_Order_Is_Split_By_Each_Types_Measured_Share()
        {
            // 856 m² of roof over three types, ordered as 304 bundles.
            var b = CommodityTypeBreakdown.Build(
                new[] { Commodity("roof-shingle", 304) },
                Source("roof-shingle",
                       ("Generic - 225mm", 610.61),
                       ("Generic - 225mm 2", 224.44),
                       ("Generic - 225mm 3", 21.23))).Single();

            Assert.Equal(3, b.Parts.Count);
            // Largest first — the order somebody checking a roof works in.
            Assert.Equal("Generic - 225mm", b.Parts[0].TypeName);
            Assert.Equal(304, b.Parts.Sum(p => p.OrderQuantity), 1);
        }

        [Fact]
        public void The_Parts_Never_Exceed_The_Order_Line()
        {
            // THE reason this apportions rather than re-converts. Converting
            // each type separately and rounding each UP to whole bundles would
            // order more than the schedule says, and two documents in one
            // workbook disagreeing about a total is worse than no breakdown.
            var b = CommodityTypeBreakdown.Build(
                new[] { Commodity("roof-shingle", 304) },
                Source("roof-shingle", ("A", 100), ("B", 100), ("C", 100))).Single();

            Assert.True(b.Parts.Sum(p => p.OrderQuantity) <= 304.01,
                        "the split must never order more than the line it splits");
        }

        [Fact]
        public void Shares_Are_Fractions_Of_The_MEASURED_Total()
        {
            var b = CommodityTypeBreakdown.Build(
                new[] { Commodity("x", 100) },
                Source("x", ("A", 75), ("B", 25))).Single();

            Assert.Equal(0.75, b.Parts[0].Share, 4);
            Assert.Equal(0.25, b.Parts[1].Share, 4);
        }

        [Fact]
        public void A_Single_Type_Commodity_Is_Marked_As_Adding_Nothing()
        {
            var b = CommodityTypeBreakdown.Build(
                new[] { Commodity("x", 100) }, Source("x", ("Only", 50))).Single();

            Assert.True(b.IsSingleType);
        }

        [Fact]
        public void A_Memorandum_Is_Never_Broken_Down()
        {
            // It carries no purchasable quantity; splitting it would give the
            // double-count a per-type shape.
            var memo = Commodity("blockwork-area", 174);
            memo.IsMemorandum = true;

            Assert.Empty(CommodityTypeBreakdown.Build(
                new[] { memo }, Source("blockwork-area", ("A", 100), ("B", 74))));
        }

        [Fact]
        public void A_Commodity_With_No_Recorded_Source_Is_OMITTED_Not_Invented()
        {
            // Showing it whole under a made-up type name would be a breakdown
            // that invents its own source.
            Assert.Empty(CommodityTypeBreakdown.Build(
                new[] { Commodity("x", 100) },
                new Dictionary<string, Dictionary<string, double>>()));
        }

        [Fact]
        public void A_Commodity_Appearing_In_Two_Stages_Is_Broken_Down_Once()
        {
            var c = Commodity("cement", 93);
            Assert.Single(CommodityTypeBreakdown.Build(
                new[] { c, Commodity("cement", 93) },
                Source("cement", ("Wall", 60), ("Slab", 33))));
        }

        [Fact]
        public void Zero_And_Negative_Parts_Are_Dropped()
        {
            var b = CommodityTypeBreakdown.Build(
                new[] { Commodity("x", 100) },
                Source("x", ("Real", 50), ("Zero", 0), ("Negative", -5))).Single();

            Assert.Single(b.Parts);
            Assert.Equal("Real", b.Parts[0].TypeName);
        }

        // ── the note ────────────────────────────────────────────────────────

        [Fact]
        public void The_Note_Says_The_Order_Line_Is_Unchanged()
        {
            var bs = CommodityTypeBreakdown.Build(
                new[] { Commodity("x", 100) }, Source("x", ("A", 60), ("B", 40)));

            string s = CommodityTypeBreakdown.Summary(bs);

            Assert.Contains("what you BUY and is unchanged", s);
            Assert.Contains("APPORTIONED by measured share, not re-converted", s);
        }

        [Fact]
        public void Nothing_Split_Says_The_Sheet_Would_Repeat_The_Schedule()
        {
            var bs = CommodityTypeBreakdown.Build(
                new[] { Commodity("x", 100) }, Source("x", ("Only", 50)));

            Assert.Contains("adds nothing this time", CommodityTypeBreakdown.Summary(bs));
        }

        [Fact]
        public void No_Commodities_At_All_Gets_No_Note()
        {
            Assert.Null(CommodityTypeBreakdown.Summary(new List<CommodityBreakdown>()));
            Assert.Null(CommodityTypeBreakdown.Summary(null));
        }

        // ── the wiring, not the shape ───────────────────────────────────────

        [Fact]
        public void The_Aggregator_Records_Which_Type_Contributed_What()
        {
            // Setting SourceByType by hand in the tests above proves the split
            // arithmetic and nothing about whether aggregation ever FILLS it —
            // the same gap a mutation found in #833.
            var inputs = new AggregatorInputs
            {
                Constituents =
                {
                    new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag",
                                           Quantity = 60, TypeName = "Blockwork 200" },
                    new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag",
                                           Quantity = 33, TypeName = "Brickwork 230" },
                },
                Units = new SupplierUnitTable
                {
                    Rules =
                    {
                        new SupplierUnitRule
                        {
                            CommodityKey = "cement", SupplierUnit = "Bags", SourceUnit = "bag",
                            SourceUnitsPerSupplierUnit = 1, RoundUpToWhole = true,
                            MatchKinds = { "mortar_cement" }
                        }
                    }
                },
                StageDefs = { new StageDefinition { StageId = "s", Title = "S", Order = 1 } },
                DefaultStageId = "s"
            };

            var doc = CommodityAggregator.Build(inputs);

            Assert.True(doc.SourceByType.ContainsKey("cement"));
            Assert.Equal(60, doc.SourceByType["cement"]["Blockwork 200"]);
            Assert.Equal(33, doc.SourceByType["cement"]["Brickwork 230"]);
        }
    }
}
