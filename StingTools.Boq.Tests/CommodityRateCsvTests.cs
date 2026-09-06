using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// The CSV round trip. Until the rate editor there was no writer at all, so
    /// the parser's naive Split(',') had never been asked to read back anything
    /// it produced — and a Description is free text a quantity surveyor writes,
    /// where commas are ordinary.
    /// </summary>
    public class CommodityRateCsvTests
    {
        private static List<CommodityRate> RoundTrip(params CommodityRate[] rows)
        {
            var lines = CommodityRateResolver.WriteCsv(rows, "test");
            return CommodityRateResolver.ParseCsv(lines, out _);
        }

        [Fact]
        public void A_Description_With_A_Comma_Survives_The_Round_Trip()
        {
            // The bug this closes shifts COLUMNS: the rate is read from the
            // wrong field and either refuses to parse (the row disappears into
            // `skipped`) or parses as a different number entirely.
            var back = RoundTrip(new CommodityRate
            {
                CommodityKey = "cement", SupplierUnit = "Bags", RateUGX = 28000,
                Description = "Hima OPC 42.5N, 50 kg bag, quoted 3 Sep"
            });

            Assert.Equal("Hima OPC 42.5N, 50 kg bag, quoted 3 Sep", back.Single().Description);
            Assert.Equal(28000, back.Single().RateUGX);
        }

        [Fact]
        public void An_Em_Dash_Key_Survives_The_Round_Trip()
        {
            // The whole reason the editor exists: this key cannot be typed
            // reliably, so it must survive being written and read back.
            const string key = "RD_Breeze Block 01_Panel — Concrete";
            var back = RoundTrip(new CommodityRate { CommodityKey = key, RateUGX = 12000 });

            Assert.Equal(key, back.Single().CommodityKey);
        }

        [Fact]
        public void A_Quote_In_A_Description_Survives()
        {
            var back = RoundTrip(new CommodityRate
            {
                CommodityKey = "block", SupplierUnit = "No.", RateUGX = 2500,
                Description = "8\" hollow block"
            });

            Assert.Equal("8\" hollow block", back.Single().Description);
        }

        [Fact]
        public void The_Written_File_Is_Read_Back_By_The_Same_Parser_The_Builder_Uses()
        {
            var back = RoundTrip(
                new CommodityRate { CommodityKey = "cement", SupplierUnit = "Bags", RateUGX = 28000 },
                new CommodityRate { CommodityKey = "sand", SupplierUnit = "Trips", RateUGX = 1400000 });

            Assert.Equal(2, back.Count);
            Assert.Equal(1400000, back.Single(r => r.CommodityKey == "sand").RateUGX);
        }

        [Fact]
        public void The_Header_Says_The_File_Wins_Over_The_Baseline()
        {
            // Somebody opening this in Notepad should be able to tell what it is
            // and that hand-editing is allowed.
            var lines = CommodityRateResolver.WriteCsv(
                new[] { new CommodityRate { CommodityKey = "cement", RateUGX = 1 } }, null);

            Assert.Contains(lines, l => l.Contains("WIN over the corporate baseline"));
            Assert.Contains(lines, l => l.Contains("Safe to edit by hand"));
            Assert.Contains("CommodityKey,SupplierUnit,RateUGX,Description", lines);
        }

        [Fact]
        public void A_Header_Note_Is_Written_As_Comments_So_It_Never_Parses_As_A_Row()
        {
            var lines = CommodityRateResolver.WriteCsv(
                new[] { new CommodityRate { CommodityKey = "cement", RateUGX = 1 } },
                "Written by Sting on 2026-09-06\nfrom the 20:14 export");

            Assert.Contains(lines, l => l.StartsWith("# Written by Sting"));
            Assert.Single(CommodityRateResolver.ParseCsv(lines, out _));
        }

        [Fact]
        public void A_Keyless_Row_Is_Never_Written()
        {
            var lines = CommodityRateResolver.WriteCsv(
                new[] { new CommodityRate { CommodityKey = "  ", RateUGX = 5 } }, null);

            Assert.Empty(CommodityRateResolver.ParseCsv(lines, out _));
        }

        [Theory]
        [InlineData("a,b", new[] { "a", "b" })]
        [InlineData("\"a,b\",c", new[] { "a,b", "c" })]
        [InlineData("\"say \"\"hi\"\"\",c", new[] { "say \"hi\"", "c" })]
        [InlineData("a,,c", new[] { "a", "", "c" })]
        [InlineData("", new[] { "" })]
        public void The_Splitter_Honours_Quotes(string line, string[] expected)
        {
            Assert.Equal(expected, CommodityRateResolver.SplitCsvLine(line));
        }

        [Fact]
        public void The_Shipped_Baseline_Still_Parses_After_The_Splitter_Change()
        {
            // The corporate file is read by every export. A parser change that
            // broke it would be invisible until an export came back empty.
            string path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data", "STING_COMMODITY_RATES.csv");
            var rows = CommodityRateResolver.ParseCsv(System.IO.File.ReadAllLines(path), out var skipped);

            Assert.Empty(skipped);
            Assert.True(rows.Count > 20, $"only {rows.Count} rates parsed");
            Assert.All(rows, r => Assert.True(r.RateUGX > 0, $"{r.CommodityKey} has no rate"));
        }
    }
}
