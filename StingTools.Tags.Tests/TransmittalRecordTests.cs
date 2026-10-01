// IM-17: three writers, three shapes of transmittals.json row, one reader. And a
// status the reader does not know is shown as written, never turned into DRAFT.

using System;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TransmittalRecordTests
    {
        private static readonly JObject FromCreateTransmittal = JObject.Parse(@"{
            ""transmittal_id"":""TX-0004"",""date_issued"":""2026-09-20"",""date_prepared"":""2026-09-20"",
            ""to_organization"":""Client"",""suitability_code"":""S3"",
            ""document_ids"":[""DOC-0001"",""DOC-0002""],""status"":""ISSUED"",""from_organization"":""jd""}");

        private static readonly JObject FromQuickTransmittal = JObject.Parse(@"{
            ""transmittal_id"":""TX-0005"",""date"":""2026-09-21"",""recipient"":""A; B"",
            ""suitability"":""S2"",""documents"":[""a.pdf""],""status"":""SENT"",""created_by"":""kk""}");

        private static readonly JObject FromAutoTransmittal = JObject.Parse(@"{
            ""id"":""TX-0006"",""issue_date"":""2026-09-22T10:00:00Z"",
            ""recipients"":[""QS"",""PM""],""documents"":[""x.pdf"",""y.pdf"",""z.pdf""],
            ""status"":""AUTO_GENERATED"",""issued_by"":""svc""}");

        [Fact]
        public void Every_writer_shape_reads_its_id_date_recipient_and_documents()
        {
            Assert.Equal("TX-0004", TransmittalRecord.Id(FromCreateTransmittal));
            Assert.Equal("2026-09-20", TransmittalRecord.Date(FromCreateTransmittal));
            Assert.Equal("Client", TransmittalRecord.Recipient(FromCreateTransmittal));
            Assert.Equal(2, TransmittalRecord.Documents(FromCreateTransmittal).Count);
            Assert.Equal("S3", TransmittalRecord.Suitability(FromCreateTransmittal));
            Assert.Equal("jd", TransmittalRecord.CreatedBy(FromCreateTransmittal));

            Assert.Equal("TX-0005", TransmittalRecord.Id(FromQuickTransmittal));
            Assert.Equal("2026-09-21", TransmittalRecord.Date(FromQuickTransmittal));
            Assert.Equal("A; B", TransmittalRecord.Recipient(FromQuickTransmittal));
            Assert.Single(TransmittalRecord.Documents(FromQuickTransmittal));
            Assert.Equal("S2", TransmittalRecord.Suitability(FromQuickTransmittal));

            Assert.Equal("TX-0006", TransmittalRecord.Id(FromAutoTransmittal));
            Assert.StartsWith("2026-09-22", TransmittalRecord.Date(FromAutoTransmittal));
            Assert.Equal("QS; PM", TransmittalRecord.Recipient(FromAutoTransmittal));
            Assert.Equal(3, TransmittalRecord.Documents(FromAutoTransmittal).Count);
        }

        [Theory]
        [InlineData("ISSUED", "ISSUED")]          // was coerced to DRAFT
        [InlineData("prepared", "PREPARED")]
        [InlineData("AUTO_GENERATED", "AUTO_GENERATED")]
        [InlineData("SOMETHING_ELSE", "SOMETHING_ELSE")]   // shown as written, not DRAFT
        [InlineData("", "DRAFT")]
        [InlineData(null, "DRAFT")]
        public void A_status_is_shown_as_written(string raw, string expected)
            => Assert.Equal(expected, TransmittalStatus.Normalise(raw));

        [Fact]
        public void Every_status_a_writer_produces_is_in_the_vocabulary()
        {
            foreach (var s in new[] { "DRAFT", "SENT", "ISSUED", "PREPARED", "AUTO_GENERATED" })
                Assert.True(TransmittalStatus.IsKnown(s), s);
        }

        [Fact]
        public void A_prepared_row_has_no_issue_date_but_still_lists_under_when_it_was_prepared()
        {
            var row = JObject.Parse(@"{""transmittal_id"":""TX-0007"",""date_issued"":"""",""date_prepared"":""2026-09-25"",""status"":""PREPARED""}");
            Assert.Equal("", TransmittalRecord.IssueDate(row));
            Assert.Equal("2026-09-25", TransmittalRecord.Date(row));
        }

        [Fact]
        public void Uploading_marks_a_prepared_row_sent_once_and_never_rewinds_a_later_state()
        {
            var rows = new JArray(
                JObject.Parse(@"{""transmittal_id"":""TX-0007"",""date_issued"":"""",""status"":""PREPARED""}"),
                JObject.Parse(@"{""transmittal_id"":""TX-0008"",""date_issued"":""2026-09-01"",""status"":""ACKNOWLEDGED""}"));
            var now = new DateTime(2026, 9, 27, 9, 0, 0);

            var row = TransmittalRecord.MarkSent(rows, "TX-0007", now, "me", "uploaded");
            Assert.NotNull(row);
            Assert.Equal("SENT", (string)row["status"]);
            Assert.Equal("2026-09-27", (string)row["date_issued"]);
            var h = (JArray)row["status_history"];
            Assert.Equal("PREPARED", (string)h[0]["from"]);
            Assert.Equal("SENT", (string)h[0]["to"]);

            Assert.Null(TransmittalRecord.MarkSent(rows, "TX-0007", now, "me", "again"));   // already SENT
            Assert.Null(TransmittalRecord.MarkSent(rows, "TX-0008", now, "me", "x"));       // further along
            Assert.Equal("2026-09-01", (string)rows[1]["date_issued"]);
            Assert.Null(TransmittalRecord.MarkSent(rows, "TX-9999", now, "me", "x"));
        }

        // P1: an upload whose transmittal was not marked SENT used to say nothing - MarkSent
        // returned null whether the row was already SENT (fine), missing, or in another state.
        [Fact]
        public void Why_a_transmittal_was_not_marked_sent_is_said_and_already_sent_is_fine()
        {
            var rows = Newtonsoft.Json.Linq.JArray.Parse(@"[{""transmittal_id"":""TR-1"",""status"":""SENT""},{""transmittal_id"":""TR-2"",""status"":""VOID""}]");
            Assert.Null(StingTools.BIMManager.TransmittalRecord.WhyNotMarkedSent(rows, "TR-1"));
            Assert.Contains("VOID", StingTools.BIMManager.TransmittalRecord.WhyNotMarkedSent(rows, "TR-2"));
            Assert.Contains("not in transmittals.json", StingTools.BIMManager.TransmittalRecord.WhyNotMarkedSent(rows, "TR-9"));
        }
    }
}
