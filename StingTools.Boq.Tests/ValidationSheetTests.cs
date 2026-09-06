using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using StingTools.BOQ.MaterialSchedule;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the export has to carry its own explanation.
    ///
    /// The tiling scan was added so that "no tiling appeared" stopped being
    /// ambiguous. It worked — and then the answer lived only in the post-export
    /// dialog, so the moment that closed, a workbook reviewed later could not
    /// say why it looked the way it did. Reviewing one an hour after the fact
    /// meant asking the user to re-run Revit for a line of text.
    ///
    /// These write a REAL workbook and read it back, so what is asserted is the
    /// deliverable, not the model behind it.
    /// </summary>
    public class ValidationSheetTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(),
            "matsched_validation_" + Guid.NewGuid().ToString("N") + ".xlsx");

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
        }

        private static MaterialScheduleDocument Doc()
        {
            var d = new MaterialScheduleDocument();
            var s = new StageSection { StageId = "finishes", Letter = "A", Title = "ELEMENT 05: FINISHES" };
            s.Commodities.Add(new MaterialCommodity
            {
                CommodityKey = "cement", Description = "Cement", SupplierUnit = "Bags",
                NetQuantity = 90, OrderQuantity = 93, RateUGX = 28000
            });
            d.Stages.Add(s);
            return d;
        }

        private IXLWorksheet WriteAndRead(MaterialScheduleDocument doc)
        {
            MaterialScheduleXlsxWriter.Write(doc, _path);
            return new XLWorkbook(_path).Worksheet("Validation");
        }

        private static string ColumnText(IXLWorksheet ws, int col)
            => string.Join("\n", ws.RowsUsed().Select(r => r.Cell(col).GetString()));

        [Fact]
        public void Export_Notes_Reach_The_Workbook()
        {
            var doc = Doc();
            doc.Warnings.Add("Tiling scan: 10 wall/floor type(s) inspected, 1 carry a finish layer, 0 name a tile material.");
            doc.Warnings.Add("Room finishes: 24 placed room(s) read, 0 name a tiled floor, 0 a tiled wall, 0 a skirting.");

            var ws = WriteAndRead(doc);
            string text = ColumnText(ws, 4);

            Assert.Contains("EXPORT NOTES", text);
            Assert.Contains("Tiling scan: 10 wall/floor type(s)", text);
            Assert.Contains("Room finishes: 24 placed room(s)", text);
        }

        [Fact]
        public void Notes_Come_Before_The_Issues_They_Explain()
        {
            var doc = Doc();
            doc.Warnings.Add("Compound take-off is disabled.");
            doc.Reconciliation.Issues.Add(new ReconciliationIssue
            {
                Code = "R3", StageId = "finishes", CommodityKey = "cement", Message = "has no rate"
            });

            var ws = WriteAndRead(doc);
            string text = ColumnText(ws, 4);

            Assert.True(text.IndexOf("EXPORT NOTES", StringComparison.Ordinal)
                      < text.IndexOf("has no rate", StringComparison.Ordinal),
                "notes must sit above the per-row issues they explain");
        }

        [Fact]
        public void A_Clean_Run_With_Notes_Still_Says_It_Is_Clean()
        {
            // The regression this guards: the "no reconciliation issues" line
            // used to be written at a HARDCODED row 4. Once notes push the issue
            // table down, that row belongs to a note — so the clean message
            // would have overwritten one, or been buried above the header.
            var doc = Doc();
            doc.Warnings.Add("Tiling scan: nothing to report.");

            var ws = WriteAndRead(doc);
            string text = ColumnText(ws, 1);

            Assert.Contains("No reconciliation issues", text);
            Assert.Contains("Tiling scan: nothing to report.", ColumnText(ws, 4));
        }

        [Fact]
        public void No_Notes_Leaves_The_Sheet_As_It_Was()
        {
            // A document with nothing to say must not gain an empty NOTES block.
            var doc = Doc();

            var ws = WriteAndRead(doc);

            Assert.DoesNotContain("EXPORT NOTES", ColumnText(ws, 4));
            Assert.Equal("Code", ws.Cell(3, 1).GetString());
        }

        [Fact]
        public void Blank_Notes_Are_Skipped_Not_Rendered_As_Empty_Rows()
        {
            var doc = Doc();
            doc.Warnings.Add("Real note.");
            doc.Warnings.Add("   ");
            doc.Warnings.Add(null);

            var ws = WriteAndRead(doc);

            Assert.Equal(1, ws.RowsUsed().Count(r => r.Cell(1).GetString() == "NOTE"));
        }

        [Fact]
        public void Every_Issue_Still_Reaches_The_Sheet_Alongside_Notes()
        {
            var doc = Doc();
            doc.Warnings.Add("A note.");
            for (int i = 0; i < 5; i++)
                doc.Reconciliation.Issues.Add(new ReconciliationIssue
                {
                    Code = "R3", StageId = "finishes", CommodityKey = "k" + i, Message = "issue " + i
                });

            var ws = WriteAndRead(doc);
            string text = ColumnText(ws, 4);

            for (int i = 0; i < 5; i++) Assert.Contains("issue " + i, text);
        }
    }
}
