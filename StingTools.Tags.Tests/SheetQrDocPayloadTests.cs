using System;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>The rich ISO 19650 document payload: what it encodes, what it reads
    /// back, and — the part no other test covers — whether it still FITS.
    ///
    /// A payload that grows past the printed cell does not fail loudly. It prints, it
    /// looks like a QR, and it stops scanning on site. Nothing in Revit or CI notices.
    /// So the module budget is pinned here, at the only point where adding a field is
    /// cheap to reconsider: the moment someone adds one.</summary>
    public class SheetQrDocPayloadTests
    {
        // The identifier Iso19650DocumentCode assembles into SHT_TAG_1_TXT:
        // Project-Originator-Volume-Level-Type-Role-Number. NO revision -- that is
        // a carried fact, because a document's identity does not change when it is
        // revised.
        private const string DocId = "PROJECTN-PLNS-ZZ-02-DR-A-0001";

        private static StingQrFormat.DocFacts FullFacts() => new StingQrFormat.DocFacts
        {
            Suitability  = "S4",
            CdeState     = "PUB",
            IssueDate    = "20260914",
            Zone         = "Z01",
            SheetOfTotal = "25.100",
            Lod          = "350",
            PaperSize    = "A1",
            Scale        = "1.100",
            Initials     = "DRW.CHK.APR",
            Signature    = "7F3K9A2B4C6D",
            Revision     = "P02",
        };

        // ── What it builds ──────────────────────────────────────────────────

        [Fact]
        public void The_document_link_is_keyed_by_the_full_iso_identifier()
        {
            var url = StingQrFormat.BuildDocUrl(DocId, FullFacts());
            Assert.Contains("/D/" + DocId, url);
        }

        [Fact]
        public void An_identifier_is_required()
        {
            // A document link with no document identifies nothing. Refuse rather than
            // emit a URL whose only content is the host.
            Assert.Throws<ArgumentException>(() => StingQrFormat.BuildDocUrl(null));
            Assert.Throws<ArgumentException>(() => StingQrFormat.BuildDocUrl("   "));
        }

        [Fact]
        public void Trailing_absent_facts_are_dropped_not_padded()
        {
            var url = StingQrFormat.BuildDocUrl(DocId,
                new StingQrFormat.DocFacts { Suitability = "S4" });

            Assert.EndsWith("/S4", url);
            Assert.DoesNotContain("/-", url);   // no run of empty segments
        }

        [Fact]
        public void An_interior_absent_fact_holds_its_place()
        {
            // If a gap collapsed, every field after it would shift one position and
            // read as its neighbour — a silent wrong answer, the worst kind.
            var url = StingQrFormat.BuildDocUrl(DocId,
                new StingQrFormat.DocFacts { Suitability = "S4", IssueDate = "20260914" });

            Assert.Contains("/S4/-/20260914", url);
        }

        // ── What it reads back ──────────────────────────────────────────────

        [Fact]
        public void A_built_document_link_parses_back_to_every_fact()
        {
            var facts = FullFacts();
            var back = StingQrFormat.Parse(StingQrFormat.BuildDocUrl(DocId, facts));

            Assert.NotNull(back);
            Assert.Equal(StingQrKind.Document, back.Kind);
            Assert.Equal(DocId, back.DocId);
            Assert.Equal(facts.Suitability,  back.Facts.Suitability);
            Assert.Equal(facts.CdeState,     back.Facts.CdeState);
            Assert.Equal(facts.IssueDate,    back.Facts.IssueDate);
            Assert.Equal(facts.Zone,         back.Facts.Zone);
            Assert.Equal(facts.SheetOfTotal, back.Facts.SheetOfTotal);
            Assert.Equal(facts.Lod,          back.Facts.Lod);
            Assert.Equal(facts.PaperSize,    back.Facts.PaperSize);
            Assert.Equal(facts.Scale,        back.Facts.Scale);
            Assert.Equal(facts.Initials,     back.Facts.Initials);
            Assert.Equal(facts.Signature,    back.Facts.Signature);
            Assert.Equal(facts.Revision,     back.Facts.Revision);
        }

        [Fact]
        public void The_project_comes_from_the_identifier_and_the_revision_does_not()
        {
            var back = StingQrFormat.Parse(StingQrFormat.BuildDocUrl(DocId, FullFacts()));

            Assert.Equal("PROJECTN", back.ProjectCode);
            // The revision is a CARRIED FACT now. Reading it off the end of the
            // identifier would report "0001" -- the number -- as the revision.
            Assert.Equal("P02", back.Revision);
            Assert.Equal("P02", back.Facts.Revision);
            Assert.DoesNotContain("P02", back.DocId);
        }

        [Fact]
        public void A_code_with_no_revision_fact_reports_no_revision()
        {
            var back = StingQrFormat.Parse(StingQrFormat.BuildDocUrl(DocId,
                new StingQrFormat.DocFacts { Suitability = "S4" }));

            Assert.Null(back.Revision);
            Assert.Equal("0001", back.DocId.Split('-')[6]);   // the number, not a revision
        }

        [Fact]
        public void An_absent_interior_fact_reads_back_as_null_not_a_dash()
        {
            var back = StingQrFormat.Parse(StingQrFormat.BuildDocUrl(DocId,
                new StingQrFormat.DocFacts { Suitability = "S4", IssueDate = "20260914" }));

            Assert.Equal("S4", back.Facts.Suitability);
            Assert.Null(back.Facts.CdeState);          // the placeholder, not the value
            Assert.Equal("20260914", back.Facts.IssueDate);
        }

        [Fact]
        public void A_document_link_is_not_mistaken_for_a_sheet_or_element_link()
        {
            var doc   = StingQrFormat.Parse(StingQrFormat.BuildDocUrl(DocId, FullFacts()));
            var sheet = StingQrFormat.Parse(StingQrFormat.BuildSheetUrl("PROJECTN", "A-L1-001"));

            Assert.Equal(StingQrKind.Document, doc.Kind);
            Assert.Equal(StingQrKind.Sheet, sheet.Kind);
            Assert.Null(sheet.DocId);
            Assert.Null(sheet.Facts);
        }

        // ── Whether it fits ─────────────────────────────────────────────────

        /// <summary>QR alphanumeric mode covers 0-9 A-Z space and $ % * + - . / :
        /// — and NOT '?', '&amp;' or '='. One query character drops the whole payload
        /// into byte mode and costs ~40% of the symbol's capacity. Measured on
        /// ZXing 0.16.9 at ECC Q: the same facts took 53 modules as a query string
        /// and 49 as path segments, while carrying eleven MORE characters.
        ///
        /// This is why the format is positional. If it is ever "tidied" into
        /// ?key=value, this test is the thing that objects.</summary>
        [Fact]
        public void The_payload_stays_inside_qr_alphanumeric_mode()
        {
            var url = StingQrFormat.BuildDocUrl(DocId, FullFacts());

            const string Allowed = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
            foreach (char c in url)
                Assert.True(Allowed.IndexOf(c) >= 0,
                    $"'{c}' is outside QR alphanumeric mode, which forces the whole " +
                    "payload into byte mode and shrinks the printable cell budget.");
        }

        /// <summary>The budget. 31 mm is the cell the title block now reserves, and
        /// 0.50 mm per module is the floor for a phone camera on a plotted, folded
        /// drawing. Alphanumeric mode at ECC Q fits ~140 characters in that.
        ///
        /// If a new field pushes past this, the fix is NOT to raise the number — it is
        /// to drop a field, shorten the host, or widen the printed cell deliberately.</summary>
        [Fact]
        public void The_full_payload_fits_the_31mm_cell()
        {
            var url = StingQrFormat.BuildDocUrl(DocId, FullFacts());
            int budget = StingQrFormat.MaxCharsForCell(31.0);

            Assert.True(url.Length <= budget,
                $"Payload is {url.Length} chars; a 31 mm cell at 0.50 mm/module holds " +
                $"about {budget} in alphanumeric mode at ECC Q. It will print and look " +
                $"fine, and stop scanning on site.\n{url}");
        }

        /// <summary>The whole rich payload does NOT fit a 24 mm cell, and that is the
        /// point: older sheets and other title blocks still carry one. It must shed
        /// fields rather than print something too dense to read.</summary>
        [Theory]
        [InlineData(40.0)]
        [InlineData(31.0)]
        [InlineData(28.0)]
        [InlineData(24.0)]
        [InlineData(20.0)]
        public void A_smaller_cell_gets_a_shorter_payload_never_a_denser_one(double cellMm)
        {
            var url = StingQrFormat.BuildDocUrlWithin(DocId, FullFacts(), cellMm);

            // null is a legitimate answer: below ~28 mm not even the bare identifier
            // fits, and the caller falls back to the short sheet link. What must never
            // happen is a payload that exceeds the cell's budget.
            if (url == null) return;

            Assert.True(url.Length <= StingQrFormat.MaxCharsForCell(cellMm),
                $"{cellMm} mm cell got a {url.Length}-char payload, over its " +
                $"{StingQrFormat.MaxCharsForCell(cellMm)}-char budget.\n{url}");
            Assert.Contains(DocId, url);   // the identifier is never what gets dropped
        }

        /// <summary>The floor, measured rather than assumed. The host is 30 characters
        /// and a typical ISO 19650 identifier 35, so the bare document link is 65 — it
        /// needs a 24 mm cell just to exist, and carries almost no facts there. 31 mm is
        /// the first size at which the full issue record fits, which is the whole
        /// argument for widening the title-block cell.</summary>
        [Fact]
        public void The_document_link_needs_24mm_to_exist_and_31mm_to_be_worth_it()
        {
            Assert.Null(StingQrFormat.BuildDocUrlWithin(DocId, FullFacts(), 20.0));

            var at24 = StingQrFormat.BuildDocUrlWithin(DocId, FullFacts(), 24.0);
            var at31 = StingQrFormat.BuildDocUrlWithin(DocId, FullFacts(), 31.0);

            Assert.NotNull(at24);
            Assert.NotNull(at31);

            // 24 mm buys the identifier and little else; 31 mm buys the record.
            Assert.True(at31.Length > at24.Length + 30,
                $"31 mm should carry substantially more than 24 mm.\n24: {at24}\n31: {at31}");
            Assert.Contains("DRW.CHK.APR", at31);
            Assert.DoesNotContain("DRW.CHK.APR", at24);
        }

        [Fact]
        public void Shedding_keeps_the_facts_that_answer_is_this_sheet_current()
        {
            // A 28 mm cell cannot hold everything. Suitability and CDE state are the
            // LAST to go, because they are what lets an offline scan say "this print is
            // superseded" — the one thing worth carrying in the code rather than behind
            // it. The signature goes first.
            var url = StingQrFormat.BuildDocUrlWithin(DocId, FullFacts(), 28.0);

            Assert.NotNull(url);
            Assert.Contains("/S4", url);
            Assert.DoesNotContain("7F3K9A2B4C6D", url);
        }

        [Fact]
        public void A_cell_too_small_for_any_document_link_returns_null()
        {
            // The caller then falls back to the short sheet link rather than printing
            // a code dense enough to be decorative.
            Assert.Null(StingQrFormat.BuildDocUrlWithin(DocId, FullFacts(), 12.0));
        }
    }
}
