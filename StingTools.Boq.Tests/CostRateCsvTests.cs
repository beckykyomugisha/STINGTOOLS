using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.BOQ.Rates;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-1. cost_rates_5d.csv gained a PROD column at index 1 (D6, #844). Three
    /// readers indexed it by position; one was patched with a second positional
    /// branch, the 5D Cost Trace was not, and it read MAT_DISCIPLINE ("A") as the USD
    /// rate — every row failed to parse and the command said "No cost rates found".
    /// These tests run the one parser every reader now calls (CostRateCsv) against
    /// the shipped file, and against the same file with a column inserted.
    /// </summary>
    public class CostRateCsvTests
    {
        private static string DataPath(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static string[] ShippedLines() => File.ReadAllLines(DataPath("cost_rates_5d.csv"));

        private static CostRateCsv.Result Parse(IEnumerable<string> lines) =>
            CostRateCsv.Parse(lines, CommodityRateResolver.SplitCsvLine);

        [Fact]
        public void Every_Shipped_Row_Parses_With_Both_Rates()
        {
            var lines = ShippedLines();
            var res = Parse(lines);
            int dataRows = lines.Skip(1).Count(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"));

            Assert.Empty(res.Problems);
            Assert.Empty(res.Layout.UnknownColumns);
            Assert.True(dataRows > 40, $"expected the shipped card, found {dataRows} rows");
            Assert.Equal(dataRows, res.Rows.Count);
            Assert.All(res.Rows, r =>
            {
                Assert.NotNull(r.RateUsd);
                Assert.NotNull(r.RateUgx);
                Assert.False(string.IsNullOrEmpty(r.Unit), $"line {r.LineNumber}: no unit");
            });
            // The value the positional 5D trace lost: Walls is 85 USD, not unparseable "A".
            Assert.Equal(85.0, res.Rows.First(r => r.Category == "Walls").RateUsd);
        }

        [Fact]
        public void An_Inserted_Column_Moves_Nothing()
        {
            // The D6 defect, replayed: insert a column at index 1 of every line. A
            // reader that indexes by position reads every later column off by one.
            var original = ShippedLines();
            var shifted = original.Select((l, i) =>
            {
                if (string.IsNullOrWhiteSpace(l) || l.TrimStart().StartsWith("#")) return l;
                int comma = l.IndexOf(',');
                return l.Substring(0, comma) + (i == 0 ? ",NEW_COLUMN," : ",x,") + l.Substring(comma + 1);
            }).ToArray();

            var a = CostRateCsv.ToUgxRateTable(Parse(original));
            var shiftedResult = Parse(shifted);
            var b = CostRateCsv.ToUgxRateTable(shiftedResult);

            Assert.Equal(a.Count, b.Count);
            foreach (var kv in a)
                Assert.Equal(kv.Value, b[kv.Key]);
            // ...and the unknown column is reported, not silently used.
            Assert.Equal(new[] { "NEW_COLUMN" }, shiftedResult.Layout.UnknownColumns);
        }

        [Fact]
        public void Disc_Prod_Keys_Keep_Same_Prod_Different_Discipline_Apart()
        {
            var table = CostRateCsv.ToUgxRateTable(Parse(ShippedLines()));
            // ATU (M) and the lightning air terminal (E) share PROD GRL at different rates.
            Assert.NotEqual(table["M|GRL"].rate, table["E|GRL"].rate);
            // Both PROD spellings the plugin writes for a foundation price it.
            Assert.Equal(table["S|FDN"], table["S|FND"]);
        }

        [Fact]
        public void The_Shipped_Card_Has_No_Duplicate_Product_Key()
        {
            var dupes = new List<string>();
            CostRateCsv.ToUgxRateTable(Parse(ShippedLines()), dupes);
            // Category repeats are deliberate (first row wins, CA-1); a DISC|PROD or
            // MAT_CODE repeat means a row can never be priced by its own key.
            Assert.DoesNotContain(dupes, k => k.Contains('|'));
        }

        [Fact]
        public void The_Parser_Knows_Exactly_The_Columns_The_Registry_Declares()
        {
            // One source of truth: tools/data_schemas.json. If a column is added to
            // the schema the parser must learn it, and vice versa.
            var reg = JObject.Parse(File.ReadAllText(DataPath("data_schemas.json")));
            var cols = reg["schemas"]!["StingTools/Data/cost_rates_5d.csv"]!["columns"]!
                .Select(c => (string)c["name"]!).ToArray();

            Assert.Equal(cols, CostRateCsvLayout.KnownColumns);
            var header = CommodityRateResolver.SplitCsvLine(ShippedLines()[0]).Select(h => h.Trim());
            Assert.Equal(cols, header);
        }

        [Fact]
        public void A_Legacy_Three_Column_Card_Still_Prices()
        {
            var res = Parse(new[] { "Category,Rate,Unit", "Walls,315000,m2" });
            Assert.True(res.Layout.IsLegacyRateCard);
            Assert.Equal(315000.0, CostRateCsv.ToUgxRateTable(res)["Walls"].rate);
        }

        [Fact]
        public void A_Header_Without_A_Rate_Or_Unit_Is_Refused_With_The_Columns_Named()
        {
            var missing = CostRateCsvLayout.FromHeader(new[] { "Category", "MAT_CODE", "Description" })
                .MissingRequired();
            Assert.Contains("Unit", missing);
            Assert.Contains(missing, m => m.Contains("Unit_Rate_UGX"));
        }

        [Fact]
        public void Header_Lookup_Tolerates_Case_Bom_And_Whitespace()
        {
            var l = CostRateCsvLayout.FromHeader(new[] { "﻿category", " unit_rate_ugx ", "UNIT" });
            Assert.Equal(0, l.Category);
            Assert.Equal(1, l.RateUgx);
            Assert.Equal(2, l.Unit);
            Assert.Empty(l.MissingRequired());
        }

        [Fact]
        public void A_Row_Without_A_Number_Is_Reported_Not_Dropped_Silently()
        {
            var res = Parse(new[] { "Category,Unit_Rate_UGX,Unit", "Walls,abc,m2", "Doors,10,each" });
            Assert.Single(res.Rows);
            Assert.Contains(res.Problems, p => p.StartsWith("line 2"));
        }
    }
}
