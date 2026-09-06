using System.Collections.Generic;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the money in a material schedule is DERIVED, never stored, so
    /// the PATMAC defect "Amount != Quantity x Rate" (D3) cannot be represented.
    /// </summary>
    public class MaterialScheduleModelTests
    {
        private static MaterialCommodity Cement(double orderQty, double rate) => new MaterialCommodity
        {
            CommodityKey = "cement",
            Description = "Cement (OPC 42.5N)",
            SupplierUnit = "Bags",
            NetQuantity = orderQty,
            OrderQuantity = orderQty,
            RateUGX = rate
        };

        [Fact]
        public void Commodity_Amount_Is_Order_Quantity_Times_Rate()
        {
            var c = Cement(350, 28000);
            Assert.Equal(9_800_000, c.AmountUGX);
        }

        [Fact]
        public void Stage_SubTotal_Sums_Commodities_Labour_And_Provisional_Sums()
        {
            var stage = new StageSection { StageId = "substructure", Title = "SUB-STRUCTURE" };
            stage.Commodities.Add(Cement(350, 28000));                       //  9,800,000
            stage.Labour.Add(new LabourLine { AmountUGX = 7_546_800 });      //  7,546,800
            stage.ProvisionalSums.Add(new ProvisionalSumLine { AmountUGX = 1_000_000 });

            Assert.Equal(18_346_800, stage.SubTotalUGX);
        }

        [Fact]
        public void Document_Applies_Contingency_To_The_Works_Subtotal()
        {
            var doc = new MaterialScheduleDocument();
            doc.Options.ContingencyPct = 5.0;
            var stage = new StageSection { StageId = "s", Title = "S" };
            stage.Commodities.Add(Cement(100, 10_000));                      // 1,000,000
            doc.Stages.Add(stage);

            Assert.Equal(1_000_000, doc.WorksSubtotalUGX);
            Assert.Equal(50_000, doc.ContingencyUGX);
            Assert.Equal(1_050_000, doc.GrandTotalUGX);
        }
    }
}
