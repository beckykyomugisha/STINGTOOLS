using System;
using System.IO;
using System.Linq;
using StingTools.BOQ.Rates;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-34 — STING_DEFAULT_COST_RATES.csv says three different things a 0 used
    /// to say at once: a benchmark rate, NOT MEASURED (never a bill item), or — by
    /// having no row — "measurable, no benchmark" (unpriced, a tender query).
    /// </summary>
    public class DefaultCostRatesCsvTests
    {
        private const string Header = "Category,RatePerUnit_USD,Unit,Description";

        private static DefaultCostRatesTable Parse(params string[] rows)
            => DefaultCostRatesCsv.Parse(new[] { Header }.Concat(rows), CommodityRateResolver.SplitCsvLine);

        private static DefaultCostRatesTable Shipped()
            => DefaultCostRatesCsv.Parse(
                File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", "STING_DEFAULT_COST_RATES.csv")),
                CommodityRateResolver.SplitCsvLine);

        [Fact]
        public void A_Positive_Number_Is_A_Rate()
        {
            var t = Parse("Walls,85,m²,Walls");
            Assert.Equal(85, t.Rates["Walls"].ratePerUnit);
            Assert.Empty(t.NotMeasured);
            Assert.Empty(t.Problems);
        }

        [Theory]
        [InlineData("NOT MEASURED")]
        [InlineData("not measured")]
        [InlineData(" Not Measured ")]
        public void Not_Measured_Is_A_Declaration_Not_A_Rate(string cell)
        {
            var t = Parse($"Rooms,{cell},n/a,Rooms");
            Assert.Contains("Rooms", t.NotMeasured);
            Assert.False(t.Rates.ContainsKey("Rooms"));
            Assert.Empty(t.Problems);
        }

        [Fact]
        public void A_Bare_Zero_Is_Refused_And_Reported()
        {
            var t = Parse("Site,0,each,Site elements");
            Assert.False(t.Rates.ContainsKey("Site"));
            Assert.DoesNotContain("Site", t.NotMeasured);
            Assert.Single(t.Problems);
            Assert.Contains("NOT MEASURED", t.Problems[0]);
        }

        [Fact]
        public void An_Unreadable_Rate_Is_Reported()
        {
            var t = Parse("Walls,eighty,m²,Walls");
            Assert.Empty(t.Rates);
            Assert.Single(t.Problems);
        }

        [Fact]
        public void A_Duplicate_Category_Keeps_The_First_Row()
        {
            var t = Parse("Walls,85,m²,a", "Walls,NOT MEASURED,n/a,b");
            Assert.Equal(85, t.Rates["Walls"].ratePerUnit);
            Assert.Empty(t.NotMeasured);
            Assert.Single(t.Problems);
        }

        [Fact]
        public void A_Missing_Rate_Column_Reads_Nothing()
        {
            var t = DefaultCostRatesCsv.Parse(new[] { "Category,Unit", "Walls,m²" }, CommodityRateResolver.SplitCsvLine);
            Assert.Contains("RatePerUnit_USD", t.MissingColumns);
            Assert.Empty(t.Rates);
            Assert.Empty(t.NotMeasured);
        }

        // ── The shipped file: the QS decision per row ──

        [Fact]
        public void Shipped_File_Has_No_Problems()
            => Assert.Empty(Shipped().Problems);

        [Theory]
        [InlineData("Rooms")] [InlineData("Areas")] [InlineData("Spaces")] [InlineData("Zones")] [InlineData("HVAC Zones")]
        [InlineData("Entourage")] [InlineData("Parts")] [InlineData("Assemblies")] [InlineData("Mass")]
        [InlineData("Detail Items")] [InlineData("Model Groups")] [InlineData("Materials")] [InlineData("Profiles")]
        [InlineData("RVT Links")] [InlineData("Toposolid Links")] [InlineData("Property Lines")]
        [InlineData("Property Line Segments")]
        [InlineData("Analytical Duct Segments")] [InlineData("Analytical Pipe Segments")] [InlineData("Analytical Members")]
        [InlineData("Analytical Nodes")] [InlineData("Analytical Links")] [InlineData("Analytical Openings")]
        [InlineData("Analytical Panels")]
        [InlineData("Area Based Loads")] [InlineData("Area Loads")] [InlineData("Line Loads")] [InlineData("Point Loads")]
        [InlineData("Internal Area Loads")] [InlineData("Internal Line Loads")] [InlineData("Internal Point Loads")]
        public void Shipped_Non_Work_Categories_Are_Not_Measured(string category)
            => Assert.Contains(category, Shipped().NotMeasured);

        [Fact]
        public void Shipped_Site_Is_Measurable_But_Has_No_Benchmark()
        {
            // Site components (bollards, benches, bins) are work: they reach the bill
            // unpriced and are flagged at risk, not excluded and not NIL.
            var t = Shipped();
            Assert.DoesNotContain("Site", t.NotMeasured);
            Assert.False(t.Rates.ContainsKey("Site"));
        }

        [Fact]
        public void Shipped_Not_Measured_Count_Is_Pinned()
            => Assert.Equal(31, Shipped().NotMeasured.Count);

        [Fact]
        public void Shipped_Card_Has_No_Row_For_A_Not_Measured_Category()
        {
            // One owner of "this category is never billed": the benchmark file. The
            // corporate rate card (cost_rates_5d.csv) must not also price, NIL or INCL
            // such a category — a row there could never be reached, and would read as
            // a second decision that disagrees with the first.
            var notMeasured = Shipped().NotMeasured;
            var card = CostRateCsv.Parse(
                File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", "cost_rates_5d.csv")),
                CommodityRateResolver.SplitCsvLine);
            var clash = card.Rows.Where(r => notMeasured.Contains(r.Category))
                .Select(r => $"line {r.LineNumber}: {r.Category}").ToList();
            Assert.True(clash.Count == 0,
                "cost_rates_5d.csv rates a NOT MEASURED category: " + string.Join("; ", clash));
            Assert.NotEmpty(card.Rows);   // not vacuous
        }

        [Fact]
        public void Shipped_Measured_Categories_Still_Price()
        {
            var t = Shipped();
            Assert.Equal(85, t.Rates["Walls"].ratePerUnit);
            Assert.Equal(60, t.Rates["Toposolid"].ratePerUnit);
            Assert.All(t.Rates.Values, r => Assert.True(r.ratePerUnit > 0));
        }
    }
}
