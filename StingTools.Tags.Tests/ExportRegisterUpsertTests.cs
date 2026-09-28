// DOCX-13: the document-register row rule, applied to an in-memory register so a
// batch of exports is one load and one save. These pin the rule the per-file path
// used, so batching changes how often the file is written and nothing else.

using System;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ExportRegisterUpsertTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 27, 10, 30, 0);

        private static ExportRegistration Pdf(string file, string number = null, string suit = "S3") => new ExportRegistration
        {
            FilePath = "P/01_SHARED/A_Architectural/" + file,
            DocType = "DR", Description = file, Suitability = suit,
            Revision = "P02", CdeStatus = "SHARED", DocNumber = number,
        };

        [Fact]
        public void A_batch_mints_consecutive_ids_across_both_id_spellings()
        {
            var reg = JArray.Parse(@"[{""doc_id"":""DOC-0007""},{""document_id"":""DOC-0003""}]");
            Assert.True(ExportRegisterUpsert.Apply(reg, Pdf("a.pdf"), Now, "u", out string id1, out bool n1));
            Assert.True(ExportRegisterUpsert.Apply(reg, Pdf("b.pdf"), Now, "u", out string id2, out bool n2));
            Assert.True(n1 && n2);
            Assert.Equal("DOC-0008", id1);
            Assert.Equal("DOC-0009", id2);
            Assert.Equal(4, reg.Count);
            var row = (JObject)reg[2];
            Assert.Equal("DOC-0008", (string)row["doc_id"]);
            Assert.Equal("DOC-0008", (string)row["document_id"]);
            Assert.Equal("PDF", (string)row["file_format"]);
            Assert.Equal("2026-09-27 10:30", (string)row["date_created"]);
        }

        [Fact]
        public void The_same_file_twice_in_one_batch_updates_rather_than_duplicates()
        {
            var reg = new JArray();
            ExportRegisterUpsert.Apply(reg, Pdf("a.pdf", suit: "S2"), Now, "u", out _, out _);
            ExportRegisterUpsert.Apply(reg, Pdf("a.pdf", suit: "S4"), Now, "u", out string id, out bool added);
            Assert.False(added);
            Assert.Single(reg);
            Assert.Equal("DOC-0001", id);
            Assert.Equal("S4", (string)reg[0]["suitability"]);
        }

        [Fact]
        public void A_deliverable_number_matches_before_the_file_name()
        {
            var reg = JArray.Parse(@"[{""document_id"":""DOC-0001"",""doc_number"":""SAH-PLNS-ZZ-01-DR-A-0001"",""file_name"":""old.pdf""}]");
            ExportRegisterUpsert.Apply(reg, Pdf("new.pdf", "SAH-PLNS-ZZ-01-DR-A-0001"), Now, "u", out string id, out bool added);
            Assert.False(added);
            Assert.Equal("DOC-0001", id);
            Assert.Equal("new.pdf", (string)reg[0]["file_name"]);
            Assert.Equal("2026-09-27 10:30", (string)reg[0]["date_modified"]);
        }

        [Fact]
        public void A_legacy_row_with_no_id_is_still_updated_and_reported_recorded()
        {
            var reg = JArray.Parse(@"[{""file_name"":""a.pdf""}]");
            Assert.True(ExportRegisterUpsert.Apply(reg, Pdf("a.pdf"), Now, "u", out string id, out bool added));
            Assert.Null(id);
            Assert.False(added);
            Assert.Equal("SHARED", (string)reg[0]["cde_status"]);
        }

        [Fact]
        public void Nothing_to_record_is_not_recorded()
        {
            var reg = new JArray();
            Assert.False(ExportRegisterUpsert.Apply(reg, new ExportRegistration(), Now, "u", out _, out _));
            Assert.Empty(reg);
        }

        [Fact]
        public void Missing_facts_take_the_documented_defaults()
        {
            var reg = new JArray();
            ExportRegisterUpsert.Apply(reg, new ExportRegistration { FilePath = "x.ifc", DocType = "M3" }, Now, "u", out _, out _);
            Assert.Equal("S0", (string)reg[0]["suitability"]);
            Assert.Equal("P01", (string)reg[0]["revision"]);
            Assert.Equal("WIP", (string)reg[0]["cde_status"]);
        }
    }
}
