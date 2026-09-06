using System.Collections.Generic;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MaterialCommodity.RateSource has been populated since the first export
    /// and written to no sheet, so 28,000 against cement printed identically
    /// whether it came from the user's supplier quote or from the shipped
    /// guess. Only one of those is safe to tender against.
    /// </summary>
    public class RateProvenanceLabelTests
    {
        private static MaterialCommodity C(double rate, string source, bool memo = false) =>
            new MaterialCommodity
            {
                CommodityKey = "cement", Description = "Cement", SupplierUnit = "Bags",
                OrderQuantity = 93, RateUGX = rate, RateSource = source, IsMemorandum = memo
            };

        [Fact]
        public void A_Project_Rate_Says_Project()
        {
            Assert.Equal(RateProvenanceLabel.Project, RateProvenanceLabel.For(C(31000, "project")));
        }

        [Fact]
        public void A_Baseline_Rate_Says_Indicative_Not_Baseline()
        {
            // The LITERAL, not the constant. "baseline" reads as authority and
            // the file it comes from says the opposite in its own header —
            // indicative Kampala prices, re-price before tender — so the word
            // on the sheet is the whole point of this column. Asserting the
            // constant would let somebody rename it back to "Baseline" without
            // a single test moving; that mutation was run and passed, which is
            // why this is written this way.
            Assert.Equal("Indicative", RateProvenanceLabel.For(C(28000, "baseline")));
            Assert.NotEqual("Baseline", RateProvenanceLabel.For(C(28000, "baseline")));
        }

        [Theory]
        [InlineData("project", "Project")]
        [InlineData("unpriced", "NOT PRICED")]
        public void The_Other_Labels_Are_Pinned_To_Their_Words_Too(string source, string expected)
        {
            Assert.Equal(expected, RateProvenanceLabel.For(C(source == "unpriced" ? 0 : 1, source)));
        }

        [Fact]
        public void An_Unpriced_Row_Says_So_Loudly()
        {
            Assert.Equal(RateProvenanceLabel.NotPriced, RateProvenanceLabel.For(C(0, "unpriced")));
        }

        [Fact]
        public void A_Memorandum_Gets_The_Same_Dash_Its_Amount_Cell_Gets()
        {
            // Labelling it NOT PRICED would read as a defect to fix — the
            // misreading that had four intermediate measures priced beside
            // their own constituents.
            Assert.Equal(RateProvenanceLabel.None, RateProvenanceLabel.For(C(0, "unpriced", memo: true)));
        }

        [Fact]
        public void A_Rate_With_No_Recorded_Source_Is_Not_Quietly_Called_Baseline()
        {
            Assert.Equal("?", RateProvenanceLabel.For(C(5000, "")));
        }

        [Fact]
        public void A_Null_Commodity_Does_Not_Throw()
        {
            Assert.Equal(RateProvenanceLabel.None, RateProvenanceLabel.For(null));
        }

        // ── the summary line ────────────────────────────────────────────────

        [Fact]
        public void The_Summary_Counts_Each_Provenance()
        {
            string s = RateProvenanceLabel.Summary(new List<MaterialCommodity>
            {
                C(31000, "project"), C(28000, "baseline"), C(0, "unpriced")
            });

            Assert.Contains("1 row(s) priced from THIS project's rates", s);
            Assert.Contains("1 from the shipped indicative figures", s);
            Assert.Contains("1 not priced", s);
        }

        [Fact]
        public void Indicative_Rows_Carry_The_Re_Price_Warning()
        {
            string s = RateProvenanceLabel.Summary(new[] { C(28000, "baseline") });

            Assert.Contains("must re-price before tender", s);
            Assert.Contains("Price Commodities", s);
        }

        [Fact]
        public void Unpriced_Rows_Say_The_Total_Is_An_Understatement()
        {
            // The grand total silently omits them. Saying "not priced" without
            // saying what that does to the total is the half-truth.
            string s = RateProvenanceLabel.Summary(new[] { C(0, "unpriced") });

            Assert.Contains("UNDER-statement", s);
        }

        [Fact]
        public void A_Fully_Project_Priced_Schedule_Carries_No_Warning_Tail()
        {
            string s = RateProvenanceLabel.Summary(new[] { C(31000, "project") });

            Assert.DoesNotContain("re-price before tender", s);
            Assert.DoesNotContain("UNDER-statement", s);
        }

        [Fact]
        public void Memoranda_Are_Not_Counted_In_Any_Bucket()
        {
            // They have no rate by design, so counting them as "not priced"
            // would report a problem that does not exist.
            string s = RateProvenanceLabel.Summary(new[] { C(31000, "project"), C(0, "x", memo: true) });

            Assert.Contains("1 row(s) priced from THIS project's rates", s);
            Assert.Contains("0 not priced", s);
        }

        [Fact]
        public void Nothing_To_Report_Returns_Null_Rather_Than_A_Line_Of_Zeroes()
        {
            Assert.Null(RateProvenanceLabel.Summary(new MaterialCommodity[0]));
            Assert.Null(RateProvenanceLabel.Summary(null));
            Assert.Null(RateProvenanceLabel.Summary(new[] { C(0, "x", memo: true) }));
        }

        [Fact]
        public void An_Unknown_Source_Is_Reported_As_Something_That_Should_Not_Happen()
        {
            string s = RateProvenanceLabel.Summary(new[] { C(5000, "") });

            Assert.Contains("should not", s);
        }
    }
}
