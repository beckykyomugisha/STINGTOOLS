// The MIDP CSV reader against the real KUT template.
//
// Before: headers were compared exactly, so the KUT template's "Planned Date" (with a
// space) matched nothing and EVERY row was skipped; "Deliverable" was taken as the code so
// titles were blank; a missing suitability became "S2", a code nobody chose.

using System;
using System.IO;
using System.Linq;
using StingTools.Core.Delivery;
using Xunit;

namespace StingTools.Cost.Tests
{
    public class MidpCsvTests
    {
        private static string[] KutTemplate()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "GUIDES", "KUT_MIDP_TEMPLATE.csv");
                if (File.Exists(p)) return File.ReadAllLines(p);
            }
            throw new FileNotFoundException("GUIDES/KUT_MIDP_TEMPLATE.csv not found above the test output folder");
        }

        [Fact]
        public void TheKutTemplate_WithAMobilisationDate_ParsesEveryRow_WithTitlesAndTidps()
        {
            var lines = KutTemplate();
            int dataRows = lines.Skip(1).Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith(","));
            var rows = MidpCsv.Parse(lines, out int skipped, out int relOnly, new DateTime(2026, 10, 1));

            Assert.Equal(0, relOnly);
            Assert.True(rows.Count > 0, "no rows parsed from the KUT template");
            Assert.Equal(dataRows, rows.Count + skipped);
            var bep = rows.Single(r => r.Code == "Z-000");
            Assert.Equal("BIM Execution Plan (BEP)", bep.Title);          // "Deliverable" is the title, not the code
            Assert.Equal("A1", bep.RequiredSuitability);
            Assert.Equal("TIDP-Z", bep.TidpRef);
            Assert.Equal(new DateTime(2026, 10, 1), bep.PlannedDate);    // M0 = mobilisation
        }

        [Fact]
        public void WithoutAMobilisationDate_RelativeRowsAreCounted_NotDatedByGuess()
        {
            var rows = MidpCsv.Parse(KutTemplate(), out int skipped, out int relOnly, null);
            Assert.True(relOnly > 0);
            Assert.DoesNotContain(rows, r => r.PlannedRelMonth.Length > 0 && r.PlannedDate == default);
        }

        [Fact]
        public void HeadersMatchWhateverTheSpacingAndCase_AndNoSuitabilityIsInvented()
        {
            var rows = MidpCsv.Parse(new[]
            {
                "REF, deliverable ,PLANNED_DATE,Actual-Date,Rag",
                "A-101,Ground floor plan,2026-11-02,2026-11-05,G",
            }, out int skipped, out _, null);
            var r = Assert.Single(rows);
            Assert.Equal("A-101", r.Code);
            Assert.Equal("Ground floor plan", r.Title);
            Assert.Equal(new DateTime(2026, 11, 2), r.PlannedDate);
            Assert.Equal(new DateTime(2026, 11, 5), r.PlanActualDate);
            Assert.Equal("", r.RequiredSuitability);                      // not "S2"
            Assert.Equal("G", r.Rag);
        }
    }
}
