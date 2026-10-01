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

        // ── E5: dd/MM is the UK / Uganda order; nothing is re-read the other way ──

        [Theory]
        [InlineData("05/03/2027", 2027, 3, 5)]    // 5 March, not 3 May
        [InlineData("12/01/2027", 2027, 1, 12)]   // 12 January, not 1 December
        [InlineData("01/12/2027", 2027, 12, 1)]
        [InlineData("2027-03-05", 2027, 3, 5)]    // ISO first
        [InlineData("05-Mar-27", 2027, 3, 5)]
        [InlineData("5 March 2027", 2027, 3, 5)]
        public void DayFirst_IsTheDefault_AndIsoAlwaysWins(string raw, int y, int m, int d)
        {
            var rows = MidpCsv.Parse(new[] { "Ref,Title,Planned Date", $"A-1,Plan,{raw}" }, out int skipped, out _, null);
            Assert.Equal(0, skipped);
            Assert.Equal(new DateTime(y, m, d), Assert.Single(rows).PlannedDate);
        }

        [Fact]
        public void AMonthFirstDate_UnderDayFirst_IsRefusedWithAReason_NotSwapped()
        {
            var r = MidpCsv.ParseDetailed(new[] { "Ref,Title,Planned Date", "A-1,Plan,03/25/2027", "A-2,Plan,2027-03-25" }, null);
            Assert.Single(r.Rows);
            Assert.Equal(1, r.BadDate);
            Assert.Equal(1, r.Skipped);
            Assert.Contains("A-1", r.DateProblems.Single());
            Assert.Contains("dd/MM/yyyy", r.DateProblems.Single());
        }

        [Fact]
        public void AConfiguredMonthFirstOrder_ReadsMonthFirst_AndRefusesADayFirstDate()
        {
            var r = MidpCsv.ParseDetailed(new[] { "Ref,Planned Date", "A-1,03/05/2027", "A-2,25/03/2027" }, null, MidpDateOrder.MonthFirst);
            Assert.Equal(new DateTime(2027, 3, 5), Assert.Single(r.Rows).PlannedDate);
            Assert.Equal(1, r.BadDate);
        }

        [Theory]
        [InlineData("", MidpDateOrder.DayFirst)]
        [InlineData("dmy", MidpDateOrder.DayFirst)]
        [InlineData("dd/MM/yyyy", MidpDateOrder.DayFirst)]
        [InlineData("MDY", MidpDateOrder.MonthFirst)]
        [InlineData("MM/dd/yyyy", MidpDateOrder.MonthFirst)]
        public void TheDateOrderSetting_IsParsed(string raw, MidpDateOrder expected)
        {
            Assert.Equal(expected, MidpCsv.ParseOrder(raw));
        }

        [Fact]
        public void AnUnknownDateOrderSetting_IsNull_NotAGuess()
        {
            Assert.Null(MidpCsv.ParseOrder("ymd-ish"));
        }

        [Fact]
        public void IsoTimestamps_FromDeliverablesJson_StillRead()
        {
            Assert.True(MidpCsv.TryParseDate("2026-10-01T09:30:00", out var dt));
            Assert.Equal(new DateTime(2026, 10, 1, 9, 30, 0), dt);
        }

        // ── E7: a file with no code column is refused, empty codes are counted ──

        [Fact]
        public void NoCodeColumn_RefusesTheFile_AndNamesTheColumn()
        {
            var r = MidpCsv.ParseDetailed(new[] { "Title,Planned Date", "Plan,2027-01-01" }, null);
            Assert.True(r.Refused);
            Assert.Empty(r.Rows);
            Assert.Contains(r.MissingColumns, c => c.StartsWith("code"));
            Assert.Contains("refused", r.Describe());
        }

        [Fact]
        public void NoDateColumnAtAll_RefusesTheFile()
        {
            var r = MidpCsv.ParseDetailed(new[] { "Ref,Title", "A-1,Plan" }, null);
            Assert.True(r.Refused);
            Assert.Contains(r.MissingColumns, c => c.StartsWith("planned date"));
        }

        [Fact]
        public void EmptyCodeRows_AreCountedAsSkipped()
        {
            var rows = MidpCsv.Parse(new[] { "Ref,Title,Planned Date", ",Orphan,2027-01-01", "A-1,Plan,2027-01-01" },
                out int skipped, out _, null);
            Assert.Single(rows);
            Assert.Equal(1, skipped);
        }

        [Theory]
        [InlineData("Information Container ID")]
        [InlineData("information_container_id")]
        [InlineData("Container ID")]
        [InlineData("Document ID")]
        public void InformationContainerId_IsACodeHeader(string header)
        {
            var rows = MidpCsv.Parse(new[] { $"{header},Information Container Name,Planned Date", "KUT-X-0001,Plan,2027-01-01" },
                out _, out _, null);
            var r = Assert.Single(rows);
            Assert.Equal("KUT-X-0001", r.Code);
            Assert.Equal("Plan", r.Title);
        }
    }
}
