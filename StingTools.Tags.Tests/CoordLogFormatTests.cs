using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The coordination log had a writer/reader contract split: JSONL was written to
    /// coord_log.jsonl (and, by a second writer, into coord_log.json), while every
    /// reader whole-file-deserialised coord_log.json into a List. That threw on the
    /// second entry and the exception was swallowed, so the BCC timeline rendered
    /// empty on every project that actually had a log.
    ///
    /// These pin the JSONL contract itself. The reader is exercised directly rather
    /// than through Revit, which is what makes the guarantee testable at all.
    /// </summary>
    public class CoordLogFormatTests
    {
        private static JObject Entry(string action, string detail = "d") => new JObject
        {
            ["Timestamp"] = "2026-07-20 10:00:00",
            ["User"] = "tester",
            ["Action"] = action,
            ["Category"] = "Test",
            ["Detail"] = detail,
            ["Impact"] = "LOW",
        };

        [Fact]
        public void MultipleEntries_AllParse()
        {
            // The exact failure: more than one entry. A single-entry log parsed fine
            // under the old reader, which is why this went unnoticed.
            string content =
                CoordLogFormat.FormatLine(Entry("first")) + "\n" +
                CoordLogFormat.FormatLine(Entry("second")) + "\n" +
                CoordLogFormat.FormatLine(Entry("third")) + "\n";

            List<JObject> rows = CoordLogFormat.ParseLines(content);

            Assert.Equal(3, rows.Count);
            Assert.Equal("first", (string)rows[0]["Action"]);
            Assert.Equal("third", (string)rows[2]["Action"]);
        }

        [Fact]
        public void FormatLine_NeverSpansLines()
        {
            // One object per line is the whole contract — an indented write would
            // silently corrupt every subsequent read.
            string line = CoordLogFormat.FormatLine(Entry("x", "multi\nword"));

            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }

        [Fact]
        public void RoundTrip_PreservesFields()
        {
            var rows = CoordLogFormat.ParseLines(CoordLogFormat.FormatLine(Entry("act", "the detail")));

            Assert.Single(rows);
            Assert.Equal("act", (string)rows[0]["Action"]);
            Assert.Equal("the detail", (string)rows[0]["Detail"]);
            Assert.Equal("tester", (string)rows[0]["User"]);
        }

        [Fact]
        public void CorruptLine_IsSkipped_RestSurvive()
        {
            // A half-written line (crash mid-append) must cost that entry only. The
            // old whole-file parse discarded the entire history instead.
            string content =
                CoordLogFormat.FormatLine(Entry("good1")) + "\n" +
                "{\"Action\":\"truncated\"" + "\n" +
                CoordLogFormat.FormatLine(Entry("good2")) + "\n";

            var rows = CoordLogFormat.ParseLines(content);

            Assert.Equal(2, rows.Count);
            Assert.Equal("good1", (string)rows[0]["Action"]);
            Assert.Equal("good2", (string)rows[1]["Action"]);
        }

        [Fact]
        public void BlankAndWhitespaceLines_Ignored()
        {
            string content =
                CoordLogFormat.FormatLine(Entry("a")) + "\n" +
                "\n   \n" +
                CoordLogFormat.FormatLine(Entry("b")) + "\n";

            Assert.Equal(2, CoordLogFormat.ParseLines(content).Count);
        }

        [Fact]
        public void LegacyJsonArrayFile_StillReads()
        {
            // Logs written before the contract was unified are whole-file arrays.
            // Refusing them would silently drop a project's coordination history.
            string legacy = new JArray { Entry("old1"), Entry("old2") }.ToString();

            var rows = CoordLogFormat.ParseLines(legacy);

            Assert.Equal(2, rows.Count);
            Assert.Equal("old1", (string)rows[0]["Action"]);
        }

        [Fact]
        public void EmptyOrNull_YieldsNoRows()
        {
            Assert.Empty(CoordLogFormat.ParseLines(null));
            Assert.Empty(CoordLogFormat.ParseLines(""));
            Assert.Empty(CoordLogFormat.ParseLines("   \n  \n"));
        }

        [Fact]
        public void Cap_KeepsMostRecent()
        {
            var lines = new List<string> { "1", "2", "3", "4", "5" };

            var capped = CoordLogFormat.Cap(lines, 3);

            Assert.Equal(new[] { "3", "4", "5" }, capped);
        }

        [Fact]
        public void Cap_UnderLimit_ReturnsAll()
        {
            var lines = new List<string> { "1", "2" };

            Assert.Equal(2, CoordLogFormat.Cap(lines, 10).Count);
        }
    }
}
