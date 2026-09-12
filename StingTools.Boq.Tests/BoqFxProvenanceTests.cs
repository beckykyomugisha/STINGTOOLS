using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // ASS_CST_FX_DATE_DT was written by three commands and read by none. A stamped value
    // nobody surfaces is indistinguishable from one that was never written — and "which
    // day's rate produced this number" is the question a QS is asked at valuation.
    //
    // The rule these tests pin is the part that could go quietly wrong: a fixing date must
    // appear ONLY where a conversion actually happened. Printing one against a rate that
    // was already in the document currency asserts a conversion that never occurred, which
    // is a confident wrong provenance — worse than a blank cell.
    public class BoqFxProvenanceTests
    {
        [Theory]
        [InlineData("USD")]
        [InlineData("GBP")]
        [InlineData("  EUR  ")]
        public void ANonEmptySourceCurrency_MeansTheRegistryConverted(string cur)
        {
            // The FX adapter leaves SourceCurrencyCode empty when source and target already
            // matched, so a non-empty value IS the record that a conversion ran.
            Assert.True(BoqFxProvenance.WasConverted(cur));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NoSourceCurrency_MeansNoConversion(string cur)
        {
            Assert.False(BoqFxProvenance.WasConverted(cur));
        }

        [Fact]
        public void AConvertedLine_ReportsTheStampedFixingDate()
        {
            Assert.Equal("2026-09-06", BoqFxProvenance.FxDateFor("USD", "2026-09-06"));
            Assert.Equal("2026-09-06", BoqFxProvenance.FxDateFor("USD", "  2026-09-06  "));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("2026-09-06")]
        public void AnUnconvertedLine_NeverReportsAFixingDate(string stamped)
        {
            // Even when the element carries a stamp — it belongs to some other rate basis,
            // and showing it here would claim this rate was converted when it was not.
            Assert.Equal("", BoqFxProvenance.FxDateFor("", stamped));
            Assert.Equal("", BoqFxProvenance.FxDateFor(null, stamped));
        }

        [Fact]
        public void AConvertedLineWithNoStamp_CarriesTheGapAsANote()
        {
            string note = BoqFxProvenance.MissingFixingDateNote("USD", null);
            Assert.NotNull(note);
            Assert.Contains("USD", note);
            Assert.Contains("no fixing date", note);
            // Blank and whitespace are the same gap as absent.
            Assert.NotNull(BoqFxProvenance.MissingFixingDateNote("USD", ""));
            Assert.NotNull(BoqFxProvenance.MissingFixingDateNote("USD", "   "));
        }

        [Fact]
        public void ALineThatNeedsNoNote_GetsNone()
        {
            // Converted and dated — nothing to say.
            Assert.Null(BoqFxProvenance.MissingFixingDateNote("USD", "2026-09-06"));
            // Never converted — a missing fixing date is not a gap, it is irrelevant, and
            // noting it on every UGX line would bury the real findings.
            Assert.Null(BoqFxProvenance.MissingFixingDateNote("", null));
            Assert.Null(BoqFxProvenance.MissingFixingDateNote(null, null));
            Assert.Null(BoqFxProvenance.MissingFixingDateNote("   ", ""));
        }

        [Fact]
        public void TheNoteNamesTheCurrency_TrimmedNotRaw()
        {
            // The note reaches a tender bill's Note column; leading whitespace from a
            // sloppy provider string should not reach it.
            Assert.Contains("converted from USD)", BoqFxProvenance.MissingFixingDateNote("  USD  ", null));
        }

        [Fact]
        public void TheTwoHalvesAgree_OnEveryCombination()
        {
            // A line either reports a date, or carries a note saying why it cannot —
            // never both, and never neither once a conversion has happened.
            foreach (string cur in new[] { null, "", "   ", "USD" })
            foreach (string stamp in new[] { null, "", "   ", "2026-09-06" })
            {
                string date = BoqFxProvenance.FxDateFor(cur, stamp);
                string note = BoqFxProvenance.MissingFixingDateNote(cur, stamp);
                bool hasDate = !string.IsNullOrEmpty(date);
                Assert.False(hasDate && note != null, $"both a date and a note for currency={cur ?? "<null>"} stamp={stamp ?? "<null>"}");
                if (BoqFxProvenance.WasConverted(cur))
                    Assert.True(hasDate || note != null, "a converted line reported neither a date nor a note");
                else
                    Assert.True(!hasDate && note == null, "an unconverted line reported FX provenance");
            }
        }
    }
}
