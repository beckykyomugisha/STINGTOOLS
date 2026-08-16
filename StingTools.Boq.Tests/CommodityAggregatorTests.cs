using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — many constituent rows collapse into one purchasable commodity
    /// per stage, converted to supplier units and priced from the commodity list.
    /// </summary>
    public class CommodityAggregatorTests
    {
        private static SupplierUnitTable Units()
        {
            var t = new SupplierUnitTable();
            t.Rules.Add(new SupplierUnitRule
            {
                CommodityKey = "cement", Description = "Cement (OPC 42.5N)",
                SupplierUnit = "Bags", SourceUnit = "bag",
                SourceUnitsPerSupplierUnit = 1.0, RoundUpToWhole = true,
                DefaultWastagePct = 0,
                MatchKinds = { "mortar_cement", "plaster_cement" }
            });
            t.Rules.Add(new SupplierUnitRule
            {
                CommodityKey = "sand", Description = "Sand",
                SupplierUnit = "Trips (Sino Truck)", SourceUnit = "m3",
                SourceUnitsPerSupplierUnit = 12.0, RoundUpToWhole = true,
                DefaultWastagePct = 0,
                MatchKinds = { "mortar_sand", "plaster_sand" }
            });
            return t;
        }

        private static List<StageDefinition> Stages() => new List<StageDefinition>
        {
            new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE", Order = 10,
                                  ConstituentKinds = { "mortar_cement", "mortar_sand" } },
            new StageDefinition { StageId = "finishes", Title = "FINISHES", Order = 20,
                                  ConstituentKinds = { "plaster_cement", "plaster_sand" } }
        };

        private static CommodityRateResolver Rates() => new CommodityRateResolver(
            new List<CommodityRate>
            {
                new CommodityRate { CommodityKey = "cement", RateUGX = 28000 },
                new CommodityRate { CommodityKey = "sand",   RateUGX = 1400000 }
            }, null);

        private static AggregatorInputs Inputs(params ConstituentInput[] rows) => new AggregatorInputs
        {
            Constituents = rows.ToList(),
            Units = Units(),
            StageDefs = Stages(),
            DefaultStageId = "superstructure",
            Rates = Rates()
        };

        [Fact]
        public void Rows_Of_The_Same_Commodity_In_The_Same_Stage_Merge_Into_One_Line()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag", Quantity = 120, TraceRef = "W1" },
                new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag", Quantity = 230, TraceRef = "W2" }));

            var stage = doc.Stages.Single(s => s.StageId == "superstructure");
            var cement = stage.Commodities.Single(c => c.CommodityKey == "cement");

            Assert.Equal(350, cement.OrderQuantity);
            Assert.Equal(9_800_000, cement.AmountUGX);
            Assert.Equal(new[] { "W1", "W2" }, cement.TraceRefs.ToArray());
        }

        [Fact]
        public void The_Same_Commodity_In_Two_Stages_Stays_Two_Lines()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_cement",  Unit = "bag", Quantity = 100 },
                new ConstituentInput { ConstituentKind = "plaster_cement", Unit = "bag", Quantity = 600 }));

            Assert.Equal(100, doc.Stages.Single(s => s.StageId == "superstructure")
                                        .Commodities.Single().OrderQuantity);
            Assert.Equal(600, doc.Stages.Single(s => s.StageId == "finishes")
                                        .Commodities.Single().OrderQuantity);
        }

        [Fact]
        public void Quantities_Are_Summed_Before_Conversion_Not_After()
        {
            // 7 m3 + 4 m3 = 11 m3 → 11/12 = 0.92 → 1 trip.
            // Converting per row first would give ceil(0.58) + ceil(0.33) = 2.
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_sand", Unit = "m3", Quantity = 7 },
                new ConstituentInput { ConstituentKind = "mortar_sand", Unit = "m3", Quantity = 4 }));

            var sand = doc.Stages.Single(s => s.StageId == "superstructure")
                                 .Commodities.Single(c => c.CommodityKey == "sand");
            Assert.Equal(1, sand.OrderQuantity);
        }

        [Fact]
        public void Stages_Come_Back_In_Order_And_Lettered()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "plaster_cement", Unit = "bag", Quantity = 10 },
                new ConstituentInput { ConstituentKind = "mortar_cement",  Unit = "bag", Quantity = 10 }));

            Assert.Equal(new[] { "superstructure", "finishes" }, doc.Stages.Select(s => s.StageId).ToArray());
            Assert.Equal(new[] { "A", "B" }, doc.Stages.Select(s => s.Letter).ToArray());
        }

        [Fact]
        public void A_Constituent_With_No_Unit_Rule_Still_Appears_Unconverted()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "formwork", Category = "Walls",
                                       Description = "Formwork", Unit = "m2", Quantity = 45 }));

            var all = doc.Stages.SelectMany(s => s.Commodities).ToList();
            var fw = Assert.Single(all, c => c.Description == "Formwork");
            Assert.Equal(45, fw.OrderQuantity);
            Assert.Equal("m2", fw.SupplierUnit);
            Assert.True(fw.IsUnpriced);
        }

        [Fact]
        public void Empty_Stages_Are_Dropped_So_The_Summary_Has_No_Blank_Rows()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag", Quantity = 10 }));

            Assert.Single(doc.Stages);
            Assert.Equal("superstructure", doc.Stages[0].StageId);
        }
    }
}
