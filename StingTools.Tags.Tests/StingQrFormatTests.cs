using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// QR payload contract — plugin half.
    ///
    /// Reads tools/qr_payload_corpus.json, the SAME file the mobile suite reads
    /// (Planscape/tests/qrParser.contract.test.mjs). The two parsers disagreed for
    /// the entire life of the feature — the plugin emitted `sting://asset/…` and
    /// the scanner accepted only `planscape://` or a bare UUID — so every code the
    /// plugin produced was rejected by our own app at the parse step. Nothing
    /// failed loudly: a rejected payload is indistinguishable from a bad scan.
    ///
    /// A shared corpus turns that class of disagreement into a red test.
    /// </summary>
    public class StingQrFormatTests
    {
        private static JObject LoadCorpus()
        {
            // Walk up from the test binary to the repo root. Fails LOUDLY if the
            // corpus is missing — a contract test that silently finds no cases and
            // reports green is worse than no test at all.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "qr_payload_corpus.json");
                if (File.Exists(candidate)) return JObject.Parse(File.ReadAllText(candidate));
                dir = dir.Parent;
            }
            throw new FileNotFoundException(
                "tools/qr_payload_corpus.json not found walking up from " + AppContext.BaseDirectory);
        }

        public static IEnumerable<object[]> Corpus()
        {
            var cases = (JArray)LoadCorpus()["cases"];
            foreach (var c in cases)
                yield return new object[] { (string)c["name"], c };
        }

        [Fact]
        public void Corpus_is_not_empty()
        {
            var cases = (JArray)LoadCorpus()["cases"];
            Assert.True(cases.Count >= 10, $"corpus shrank to {cases.Count} cases — was it truncated?");
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void Parse_matches_the_shared_corpus(string name, JToken c)
        {
            string raw = (string)c["raw"];
            var expect = c["expect"];
            bool ok = (bool)expect["ok"];

            var got = StingQrFormat.Parse(raw);

            if (!ok)
            {
                // Rejection is the REQUIREMENT here, not an absence of behaviour.
                // Returning a guess for these is the defect.
                Assert.True(got == null,
                    $"{name}: must be rejected, got tag='{got?.Tag}' uid='{got?.UniqueId}'");
                return;
            }

            Assert.True(got != null, $"{name}: must resolve, got null");
            Assert.Equal(raw.Trim(), got.Raw);
            Assert.Equal((string)expect["projectCode"], got.ProjectCode);

            if ((string)expect["kind"] == "sheet")
            {
                Assert.Equal(StingQrKind.Sheet, got.Kind);
                Assert.Equal((string)expect["sheetNumber"], got.SheetNumber);
                Assert.Equal((string)expect["revision"], got.Revision);
                // A sheet payload must NOT masquerade as an element. An element
                // search on a sheet number returns nothing, which reads as "not in
                // this model" — a wrong answer rather than an empty one.
                Assert.Null(got.Tag);
                return;
            }

            Assert.Equal(StingQrKind.Element, got.Kind);
            Assert.Equal((string)expect["tag"], got.Tag);
            Assert.Equal((string)expect["uniqueId"], got.UniqueId);
        }

        [Theory]
        [InlineData("KUT", "M-101", null)]
        [InlineData("KUT", "M-101", "P03")]
        [InlineData("KUT", "M-101/A", "C01")]
        public void Sheet_url_round_trips(string code, string number, string rev)
        {
            var back = StingQrFormat.Parse(StingQrFormat.BuildSheetUrl(code, number, rev));
            Assert.NotNull(back);
            Assert.Equal(StingQrKind.Sheet, back.Kind);
            Assert.Equal(code, back.ProjectCode);
            Assert.Equal(number, back.SheetNumber);
            Assert.Equal(rev, back.Revision);
        }

        [Fact]
        public void Sheet_and_element_urls_do_not_collide()
        {
            // The two differ only by one path segment (/s/ vs /e/), so prove a sheet
            // URL never parses as an element and vice versa.
            var sheet = StingQrFormat.Parse(StingQrFormat.BuildSheetUrl("KUT", "M-101"));
            var element = StingQrFormat.Parse(StingQrFormat.BuildElementUrl("KUT", "M-101"));

            Assert.Equal(StingQrKind.Sheet, sheet.Kind);
            Assert.Equal(StingQrKind.Element, element.Kind);
            Assert.Null(sheet.Tag);
            Assert.Null(element.SheetNumber);
        }

        [Fact]
        public void Build_sheet_rejects_an_empty_number()
            => Assert.Throws<ArgumentException>(() => StingQrFormat.BuildSheetUrl("KUT", ""));

        [Theory]
        [InlineData("KUT", "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003", null)]
        [InlineData("KUT", "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003", "3f2504e0-4f89-11d3-9a0c-0305e82c3301-000a1b2c")]
        [InlineData("PRJ A", "E-BLD1-Z01-L01-LV/HV-PWR-DB-0007", null)]
        [InlineData("KUT", "tag with spaces", null)]
        public void Build_then_parse_round_trips(string code, string tag, string uid)
        {
            var url = StingQrFormat.BuildElementUrl(code, tag, uid);
            var back = StingQrFormat.Parse(url);

            Assert.NotNull(back);
            Assert.Equal(code, back.ProjectCode);
            Assert.Equal(tag, back.Tag);
            Assert.Equal(uid, back.UniqueId);
        }

        [Fact]
        public void Build_emits_https_not_a_custom_scheme()
        {
            // The whole point of the 2026-09 change: a stock phone camera must be
            // able to open it. A custom scheme shows an unopenable string to anyone
            // without the app — which is exactly the site operative a printed QR is
            // for. Pin it so a future "tidy up the URL" cannot quietly undo it.
            var url = StingQrFormat.BuildElementUrl("KUT", "M-0001");
            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.DoesNotContain("sting://", url);
        }

        [Fact]
        public void Build_rejects_an_empty_tag()
        {
            // A code with no tag identifies nothing. Minting one would put an
            // unresolvable QR on a printed drawing, where it cannot be recalled.
            Assert.Throws<ArgumentException>(() => StingQrFormat.BuildElementUrl("KUT", ""));
            Assert.Throws<ArgumentException>(() => StingQrFormat.BuildElementUrl("KUT", null));
        }

        [Fact]
        public void Missing_project_code_falls_back_rather_than_throwing()
        {
            var url = StingQrFormat.BuildElementUrl(null, "M-0001");
            Assert.Equal("PRJ", StingQrFormat.Parse(url).ProjectCode);
        }

        [Theory]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301-000a1b2c", true)]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301-zzzzzzzz", false)]
        [InlineData("M-BLD1-Z01-L02-HVAC-SUP-AHU-0003", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void LooksLikeUniqueId_distinguishes_a_uid_from_a_tag(string s, bool expected)
            => Assert.Equal(expected, StingQrFormat.LooksLikeUniqueId(s));
    }
}
