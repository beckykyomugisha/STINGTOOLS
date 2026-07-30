// DocumentRegisterMergeTests.cs — IM-13.
//
// The headline test is TrailingSpaceDocNumber_DedupsWithDeliverable: before the shared
// DocumentIdentity rule, a register row "PRJ-001 " and a deliverable "PRJ-001" landed in
// disjoint id namespaces and BuildUnified emitted the same document twice.

using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DocumentRegisterMergeTests
    {
        private static JObject RegisterRow(string docNumber, string title = "Register title") =>
            new JObject
            {
                ["doc_number"]   = docNumber,
                ["title"]        = title,
                ["type"]         = "Drawing",
                ["discipline"]   = "A",
                ["direction"]    = "OUT",
                ["file_format"]  = "PDF",
                ["created_by"]   = "registrar",
            };

        private static JObject DeliverableRow(string docNumber, string name = "Deliverable name") =>
            new JObject
            {
                ["DocNumber"]   = docNumber,
                ["Name"]        = name,
                ["Type"]        = "Drawing",
                ["Suitability"] = "S2",
                ["CDE"]         = "SHARED",
                ["Revision"]    = "P02",
                ["Status"]      = "Issued",
            };

        // ── IM-13 regression: the whole point of the change ──

        [Fact]
        public void TrailingSpaceDocNumber_DedupsWithDeliverable()
        {
            // Same document, one store carrying a stray trailing space.
            var deliverables = new[] { DocumentRegisterMerge.MapDeliverableRow(DeliverableRow("PRJ-001")) };
            var register     = new[] { DocumentRegisterMerge.MapRegisterRow(RegisterRow("PRJ-001 ")) };

            var merged = DocumentRegisterMerge.Merge(deliverables, register);

            Assert.Single(merged);
            Assert.Equal("PRJ-001", merged[0].Id);          // stored trimmed
            Assert.Equal("both", merged[0].Source);         // recognised as one document
            // Deliverable lifecycle fields win; register fills the gaps it alone carries.
            Assert.Equal("Deliverable name", merged[0].Title);
            Assert.Equal("S2", merged[0].Suitability);
            Assert.Equal("registrar", merged[0].CreatedBy);
        }

        [Theory]
        [InlineData("PRJ-001 ", "PRJ-001")]     // trailing space in the register
        [InlineData("PRJ-001", "PRJ-001 ")]     // trailing space in the deliverable
        [InlineData(" PRJ-001", "PRJ-001")]     // leading space
        [InlineData("  PRJ-001  ", " PRJ-001")] // both, unevenly
        [InlineData("PRJ-001\t", "PRJ-001")]    // tab
        public void WhitespaceVariants_AllCollapseToOneRow(string registerId, string deliverableId)
        {
            var merged = DocumentRegisterMerge.Merge(
                new[] { DocumentRegisterMerge.MapDeliverableRow(DeliverableRow(deliverableId)) },
                new[] { DocumentRegisterMerge.MapRegisterRow(RegisterRow(registerId)) });

            Assert.Single(merged);
            Assert.Equal("PRJ-001", merged[0].Id);
            Assert.Equal("both", merged[0].Source);
        }

        [Fact]
        public void HandBuiltEntries_AreAlsoNormalisedByMerge()
        {
            // Merge must not trust its callers to have trimmed already — otherwise the
            // disjoint-namespace bug can be reintroduced from any other call site.
            var merged = DocumentRegisterMerge.Merge(
                new[] { new RegisterEntry { Id = "PRJ-009 ", Source = "deliverable" } },
                new[] { new RegisterEntry { Id = "PRJ-009",  Source = "register" } });

            Assert.Single(merged);
            Assert.Equal("PRJ-009", merged[0].Id);
            Assert.Equal("both", merged[0].Source);
        }

        // ── Guard rails: dedup must not over-collapse ──

        [Fact]
        public void GenuinelyDifferentIds_StaySeparate()
        {
            var merged = DocumentRegisterMerge.Merge(
                new[] { DocumentRegisterMerge.MapDeliverableRow(DeliverableRow("PRJ-001")) },
                new[] { DocumentRegisterMerge.MapRegisterRow(RegisterRow("PRJ-002")) });

            Assert.Equal(2, merged.Count);
            Assert.Equal(new[] { "PRJ-001", "PRJ-002" }, merged.Select(m => m.Id).ToArray());
            Assert.All(merged, m => Assert.NotEqual("both", m.Source));
        }

        [Fact]
        public void InternalWhitespaceIsNotStripped()
        {
            // Trimming is edge-only: "PRJ 001" and "PRJ001" are different documents.
            var merged = DocumentRegisterMerge.Merge(
                new[] { DocumentRegisterMerge.MapDeliverableRow(DeliverableRow("PRJ 001")) },
                new[] { DocumentRegisterMerge.MapRegisterRow(RegisterRow("PRJ001")) });

            Assert.Equal(2, merged.Count);
        }

        [Fact]
        public void RowsWithNoId_AreKeptNotCollapsed()
        {
            // Two id-less rows are not "the same document" — they must both survive.
            var merged = DocumentRegisterMerge.Merge(
                new[] { DocumentRegisterMerge.MapDeliverableRow(DeliverableRow("   ")) },
                new[] { DocumentRegisterMerge.MapRegisterRow(RegisterRow("")) });

            Assert.Equal(2, merged.Count);
            Assert.All(merged, m => Assert.Equal("", m.Id));
        }

        [Fact]
        public void IdMatchingIsCaseInsensitive()
        {
            var merged = DocumentRegisterMerge.Merge(
                new[] { DocumentRegisterMerge.MapDeliverableRow(DeliverableRow("prj-001")) },
                new[] { DocumentRegisterMerge.MapRegisterRow(RegisterRow("PRJ-001 ")) });

            Assert.Single(merged);
            Assert.Equal("both", merged[0].Source);
        }

        [Fact]
        public void DeliverableFallsBackToCodeWhenDocNumberBlank()
        {
            var row = new JObject { ["DocNumber"] = "  ", ["Code"] = " DLV-007 ", ["Name"] = "x" };
            var entry = DocumentRegisterMerge.MapDeliverableRow(row);

            Assert.Equal("DLV-007", entry.Id);
        }

        [Fact]
        public void RegisterFallsThroughDocIdCandidates()
        {
            var row = new JObject { ["doc_number"] = "", ["doc_id"] = " D-42 ", ["title"] = "x" };
            var entry = DocumentRegisterMerge.MapRegisterRow(row);

            Assert.Equal("D-42", entry.Id);
        }

        [Fact]
        public void NumericDocNumber_IsStillResolved()
        {
            // A doc number stored as a JSON number used to cast to null via (string).
            var row = new JObject { ["doc_number"] = 1001, ["title"] = "x" };

            Assert.Equal("1001", DocumentRegisterMerge.MapRegisterRow(row).Id);
        }
    }

    public class DocumentIdentityTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("PRJ-001", "PRJ-001")]
        [InlineData("  PRJ-001  ", "PRJ-001")]
        [InlineData("\tPRJ-001\r\n", "PRJ-001")]
        public void Normalize_TrimsAndCollapsesBlank(string input, string expected)
            => Assert.Equal(expected, DocumentIdentity.Normalize(input));

        [Fact]
        public void FirstNonBlank_SkipsBlankCandidatesAndTrims()
        {
            var o = new JObject { ["a"] = "   ", ["b"] = " hit ", ["c"] = "miss" };

            Assert.Equal("hit", DocumentIdentity.FirstNonBlank(o, "a", "b", "c"));
        }

        [Fact]
        public void FirstNonBlank_ReturnsNullWhenNothingMatches()
        {
            var o = new JObject { ["a"] = "  " };

            Assert.Null(DocumentIdentity.FirstNonBlank(o, "a", "absent"));
            Assert.Null(DocumentIdentity.FirstNonBlank(null, "a"));
        }

        [Fact]
        public void FirstNonBlankValue_MirrorsTheJObjectRule()
        {
            Assert.Equal("hit", DocumentIdentity.FirstNonBlankValue(null, "  ", " hit ", "next"));
            Assert.Null(DocumentIdentity.FirstNonBlankValue("  ", null));
            Assert.Null(DocumentIdentity.FirstNonBlankValue());
        }

        [Fact]
        public void AllThreeResolversShareTheSameRule()
        {
            // The three stores use different candidate KEYS but must agree on the VALUE rule:
            // the same raw id must normalise identically whichever store it came from.
            var deliverable = new JObject { ["DocNumber"] = "PRJ-001 " };
            var register    = new JObject { ["doc_number"] = " PRJ-001" };
            var coord       = new JObject { ["document_id"] = "  PRJ-001  " };

            string a = DocumentIdentity.FirstNonBlank(deliverable, DocumentIdentity.DeliverableKeys);
            string b = DocumentIdentity.FirstNonBlank(register, DocumentIdentity.RegisterKeys);
            string c = DocumentIdentity.FirstNonBlank(coord, DocumentIdentity.CoordStoreKeys);

            Assert.Equal("PRJ-001", a);
            Assert.Equal(a, b);
            Assert.Equal(b, c);
        }
    }
}
