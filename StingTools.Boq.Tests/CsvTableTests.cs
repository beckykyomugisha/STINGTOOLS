using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>DSCH-2: readers find CSV columns by name, so a moved column cannot misread.</summary>
    public class CsvTableTests
    {
        private static CsvTable T(params string[] lines) => CsvTable.Parse(lines, CommodityRateResolver.SplitCsvLine);

        [Fact]
        public void Skips_comments_and_blank_lines_before_and_after_the_header()
        {
            var t = T("# banner", "", "﻿Name, Rate ,Unit", "# note", "Walls,85,m2");
            Assert.Equal(3, t.HeaderLine);
            Assert.Single(t.Rows);
            Assert.Equal("85", t.Rows[0]["rate"]);
            Assert.Equal(5, t.Rows[0].Line);
        }

        [Fact]
        public void An_inserted_column_moves_nothing()
        {
            var a = T("Name,Rate,Unit", "Walls,85,m2");
            var b = T("Name,PROD,Rate,Unit", "Walls,WL,85,m2");
            Assert.Equal(a.Rows[0]["Rate"], b.Rows[0]["Rate"]);
            Assert.Equal(a.Rows[0]["Unit"], b.Rows[0]["Unit"]);
        }

        [Fact]
        public void Missing_column_or_short_row_reads_empty_and_is_reportable()
        {
            var t = T("Name,Rate,Unit", "Walls,85");
            Assert.Equal("", t.Rows[0]["Unit"]);
            Assert.Equal("", t.Rows[0]["Nope"]);
            Assert.Equal(-1, t.Col("Nope"));
            Assert.Equal(new[] { "Nope" }, t.Missing("Name", "Nope"));
        }

        [Fact]
        public void Every_pinned_single_table_shipped_csv_has_a_header_that_names_its_columns()
        {
            // The registry pins each single-table CSV's header; CsvTable must find
            // every declared column by name in the file as it is in the repository.
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "tools", "data_schemas.json")))
                root = Path.GetDirectoryName(root);
            Assert.NotNull(root);
            var reg = JObject.Parse(File.ReadAllText(Path.Combine(root!, "tools", "data_schemas.json")));
            int checkedFiles = 0;
            foreach (var p in ((JObject)reg["schemas"]!).Properties())
            {
                // headerInComment files (RESOLVED_BINDINGS) have no header row to look
                // up by name: their readers stay positional by design.
                if ((string?)p.Value["format"] != "csv-table" || p.Value["headerInComment"]?.Value<bool>() == true)
                    continue;
                string path = Path.Combine(root!, p.Name.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"{p.Name}: registered but missing");
                var t = CsvTable.Parse(File.ReadAllLines(path), CommodityRateResolver.SplitCsvLine);
                foreach (var c in p.Value["columns"]!)
                    if (c["optional"]?.Value<bool>() != true)
                        Assert.True(t.Has((string)c["name"]!), $"{p.Name}: column {c["name"]} not found by name");
                checkedFiles++;
            }
            // Guard against the test silently checking nothing.
            Assert.True(checkedFiles >= 50, $"only {checkedFiles} pinned CSVs were checked");
        }
    }
}
