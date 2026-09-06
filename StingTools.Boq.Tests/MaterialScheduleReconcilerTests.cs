using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the invariants that cannot be made structural. Fixtures are the
    /// PATMAC MALL sample's own defects:
    ///   D1 duplicate section letters
    ///   D2 summary order not matching body order
    ///   D3 Amount != Quantity x Rate  (now impossible — AmountUGX is derived)
    ///   D4 the same commodity priced two ways
    /// </summary>
    public class MaterialScheduleReconcilerTests
    {
        private static MaterialCommodity Sand(double rate) => new MaterialCommodity
        {
            CommodityKey = "sand", Description = "Sand",
            SupplierUnit = "Trips (Sino Truck)",
            NetQuantity = 2, OrderQuantity = 2, RateUGX = rate
        };

        private static MaterialScheduleDocument TwoStages(double rateA, double rateB)
        {
            var doc = new MaterialScheduleDocument();
            var a = new StageSection { StageId = "substructure", Title = "SUB-STRUCTURE" };
            a.Commodities.Add(Sand(rateA));
            var b = new StageSection { StageId = "superstructure", Title = "SUPERSTRUCTURE" };
            b.Commodities.Add(Sand(rateB));
            doc.Stages.Add(a);
            doc.Stages.Add(b);
            StageMapper.AssignLetters(doc.Stages);
            return doc;
        }

        [Fact]
        public void D4_The_Same_Commodity_At_Two_Rates_Is_Flagged()
        {
            var doc = TwoStages(1_500_000, 1_400_000);   // the exact PATMAC discrepancy

            var rec = Reconciler.Check(doc);

            var issue = Assert.Single(rec.Issues, i => i.Code == "R1");
            Assert.Equal("sand", issue.CommodityKey);
            Assert.Contains("1,500,000", issue.Message);
            Assert.Contains("1,400,000", issue.Message);
        }

        [Fact]
        public void One_Consistent_Rate_Is_Clean()
        {
            var rec = Reconciler.Check(TwoStages(1_400_000, 1_400_000));
            Assert.True(rec.IsClean, string.Join(" | ", rec.Issues.Select(i => i.Message)));
        }

        [Fact]
        public void D1_Duplicate_Section_Letters_Are_Flagged()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[1].Letter = doc.Stages[0].Letter;   // force the PATMAC defect

            var rec = Reconciler.Check(doc);

            Assert.Contains(rec.Issues, i => i.Code == "R2");
        }

        [Fact]
        public void D2_The_Summary_Always_Matches_The_Body()
        {
            var doc = TwoStages(1_400_000, 1_400_000);

            var summary = doc.Summary.ToList();

            Assert.Equal(doc.Stages.Count, summary.Count);
            Assert.Equal(doc.Stages.Select(s => s.Letter), summary.Select(s => s.Letter));
            Assert.Equal(doc.WorksSubtotalUGX, summary.Sum(s => s.SubTotalUGX));
        }

        [Fact]
        public void An_Unpriced_Commodity_Is_Flagged_When_Prices_Are_Shown()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].RateUGX = 0;
            doc.Options.ShowPrices = true;

            var rec = Reconciler.Check(doc);

            Assert.Contains(rec.Issues, i => i.Code == "R3");
        }

        [Fact]
        public void The_Unpriced_Message_Never_Names_The_BIM_COORD_ALIAS()
        {
            // It used to say "add a row keyed 'X' to _BIM_COORD/commodity_rates.csv".
            // _BIM_COORD is the ALIAS; the live folder is _data/coord. A reader
            // following that literally opened a stale legacy folder, found
            // nothing, and concluded the file was missing — which it was, because
            // until the rate editor nothing in the codebase ever wrote it.
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].RateUGX = 0;
            doc.Options.ShowPrices = true;

            var r3 = Reconciler.Check(doc).Issues.Single(i => i.Code == "R3");

            Assert.DoesNotContain("_BIM_COORD", r3.Message);
            Assert.DoesNotContain("commodity_rates.csv", r3.Message);
        }

        [Fact]
        public void The_Unpriced_Message_Names_The_Action_And_Says_The_Total_Is_Short()
        {
            // The key is the row's display name, so most of them carry a
            // non-ASCII em dash an exact lookup will miss if it is retyped.
            // Price Commodities seeds it, so nothing has to be typed.
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].RateUGX = 0;
            doc.Options.ShowPrices = true;

            var r3 = Reconciler.Check(doc).Issues.Single(i => i.Code == "R3");

            Assert.Contains("Price Commodities", r3.Message);
            Assert.Contains("nothing has to be retyped", r3.Message);
            Assert.Contains("under-statement", r3.Message);
        }

        [Fact]
        public void An_Unpriced_Commodity_Is_Not_Flagged_When_Prices_Are_Hidden()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].RateUGX = 0;
            doc.Options.ShowPrices = false;

            var rec = Reconciler.Check(doc);

            Assert.DoesNotContain(rec.Issues, i => i.Code == "R3");
        }

        [Fact]
        public void An_Order_Quantity_Below_The_Net_Quantity_Is_Flagged()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].OrderQuantity = 1;   // net is 2
            doc.Stages[0].Commodities[0].NetQuantity = 2;

            var rec = Reconciler.Check(doc);

            Assert.Contains(rec.Issues, i => i.Code == "R4");
        }
    }
}
