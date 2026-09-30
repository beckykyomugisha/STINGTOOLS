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
    }
}
