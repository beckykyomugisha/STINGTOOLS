// A14 — the ACC clash triage CSV must not depend on the machine's culture.

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccClashCsvTests
    {
        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("en-GB")]
        public void Numbers_are_invariant_so_a_row_keeps_its_column_count(string culture)
        {
            var before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            try
            {
                var scored = new List<ScoredClash> { new ScoredClash { ClashId = "c1", Score = 0.875, Category = "HIGH", Rationale = "deep" } };
                var byId = new Dictionary<string, AccClashRecord>
                {
                    ["c1"] = new AccClashRecord { Id = "c1", LeftObjectId = 1234567, RightObjectId = 42, Status = "open",
                                                  LeftDocument = "A.rvt", RightDocument = "M.rvt" },
                };
                var rows = AccClashCsv.Rows(scored, byId);
                Assert.Equal(2, rows.Count);
                Assert.StartsWith("0.875,", rows[1]);
                Assert.Contains(",1234567,42,", rows[1]);
                // Header has 10 columns; with every text field quoted and no comma in them,
                // the unquoted separators must also number exactly 9.
                int separators = 0; bool inQuotes = false;
                foreach (char ch in rows[1]) { if (ch == '"') inQuotes = !inQuotes; else if (ch == ',' && !inQuotes) separators++; }
                Assert.Equal(9, separators);
            }
            finally { Thread.CurrentThread.CurrentCulture = before; }
        }

        // E10 — CSV formula injection: a cell a spreadsheet would run as a formula is text.

        [Theory]
        [InlineData("=HYPERLINK(\"http://x\",\"click\")")]
        [InlineData("+SUM(A1:A9)")]
        [InlineData("-2+3")]
        [InlineData("@cmd")]
        [InlineData("\tlead tab")]
        [InlineData("\rlead cr")]
        public void A_formula_lead_is_neutralised_and_quoted(string value)
        {
            string cell = AccCsv.Cell(value);
            Assert.StartsWith("\"'", cell);
            Assert.EndsWith("\"", cell);
        }

        [Theory]
        [InlineData("Duct clashes beam", "\"Duct clashes beam\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("", "\"\"")]
        [InlineData(null, "\"\"")]
        public void Ordinary_text_is_only_quoted(string value, string expected)
            => Assert.Equal(expected, AccCsv.Cell(value));

        [Fact]
        public void The_clash_csv_guards_an_acc_document_name()
        {
            var scored = new List<ScoredClash> { new ScoredClash { ClashId = "c1", Score = 0.5, Category = "HIGH", Rationale = "=1+1" } };
            var byId = new Dictionary<string, AccClashRecord>
            {
                ["c1"] = new AccClashRecord { Id = "c1", LeftDocument = "=cmd|' /C calc'!A0", RightDocument = "M.rvt" },
            };
            var row = AccClashCsv.Rows(scored, byId)[1];
            Assert.Contains("\"'=cmd|' /C calc'!A0\"", row);
            Assert.EndsWith("\"'=1+1\"", row);
        }

        [Fact]
        public void Federated_compliance_uses_the_same_guard()
            => Assert.Equal("'=x", AccFederatedCompliance.Csv("=x"));
    }
}

namespace StingTools.Acc.Tests
{
    // P9: two ACC commands wrote ACC-sourced text (location paths, barcodes, project values)
    // through a local quote-only helper, so a value starting = + - @ opened as a formula.
    public class AccCsvGuardCoverageTests
    {
        [Theory]
        [InlineData("AccCheckLocationsCommand.cs")]
        [InlineData("AccSyncProjectInfoCommand.cs")]
        public void ACC_command_csv_cells_go_through_the_formula_guard(string file)
        {
            string path = AccAttributeNamesTests.FindRepoFile("StingTools", "Clash", file);
            Assert.NotNull(path);
            string src = System.IO.File.ReadAllText(path);
            Assert.Contains("AccCsv.Cell(", src);
            Assert.DoesNotContain("(s ?? \"\").Replace(\"\\\"\", \"\\\"\\\"\")", src);
        }
    }
}
