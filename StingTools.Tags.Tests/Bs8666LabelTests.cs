// Tests for Bs8666Label - the reinforcement annotation appended to TAG7
// section A.
//
// The property that matters is PARTIAL INPUT. A model part-way through
// detailing has a mark and no count, or a count and no spacing. Emitting
// "21H16--" for that would look like data and be a lie about what is modelled,
// so every part is optional and the sentence degrades to what is actually known.

using System;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Bs8666LabelTests
    {
        [Fact]
        public void TheCompleteAnnotationReadsAsASentence()
        {
            var t = Bs8666Label.Compose("03", "21", "H16", "150");
            Assert.Equal(" reinforced as bar mark 03, 21 no. H16 at 150 mm centres", t.Plain);
            Assert.True(t.HasContent);
        }

        [Fact]
        public void NothingSuppliedMeansNothingAppended()
        {
            var t = Bs8666Label.Compose(null, "", "   ", null);
            Assert.False(t.HasContent);
            Assert.Equal("", t.Plain);
            Assert.Equal("", t.Marked);
        }

        [Theory]
        // mark only - the commonest partial state, early in detailing
        [InlineData("03", "", "", "", " reinforced as bar mark 03")]
        // count and size, no mark yet
        [InlineData("", "21", "H16", "", " reinforced as 21 no. H16")]
        // mark and spacing, no count
        [InlineData("03", "", "", "150", " reinforced as bar mark 03 at 150 mm centres")]
        // size alone
        [InlineData("", "", "H16", "", " reinforced as H16")]
        // count alone - no size yet
        [InlineData("", "21", "", "", " reinforced as 21 no.")]
        // spacing alone
        [InlineData("", "", "", "150", " reinforced as at 150 mm centres")]
        public void EveryPartIsOptional(string mark, string no, string ts, string spacing, string expected)
        {
            Assert.Equal(expected, Bs8666Label.Compose(mark, no, ts, spacing).Plain);
        }

        [Fact]
        public void WhitespaceIsTrimmedRatherThanPrinted()
        {
            Assert.Equal(" reinforced as bar mark 03", Bs8666Label.Compose("  03  ", " ", "", "").Plain);
        }

        [Fact]
        public void TheMarkedFormCarriesLabelAndValueMarkers()
        {
            var t = Bs8666Label.Compose("03", "21", "H16", "150");
            // The marked form is what the rich-display renderer reads; the plain
            // form must stay free of markers or it prints them on the drawing.
            Assert.Contains("«L»", t.Marked);
            Assert.Contains("«V»", t.Marked);
            Assert.DoesNotContain("«", t.Plain);
        }

        // ── type and size ────────────────────────────────────────────────────

        [Theory]
        [InlineData("H", "16", "H16")]
        [InlineData("B500B", "20", "B500B20")]
        [InlineData("", "16", "16 mm")]        // size with no grade is still worth printing
        [InlineData("H", "", "H")]
        [InlineData("", "", "")]
        public void TypeAndSizeJoinsTheTwoHalves(string type, string size, string expected)
        {
            Assert.Equal(expected, Bs8666Label.TypeAndSize(type, size));
        }

        [Fact]
        public void ATypeThatAlreadyContainsTheSizeIsNotDoubled()
        {
            // A project writing "H16" into the type field must not produce "H1616".
            Assert.Equal("H16", Bs8666Label.TypeAndSize("H16", "16"));
        }

        [Fact]
        public void NullsAreNotAnError()
        {
            Assert.Equal("", Bs8666Label.TypeAndSize(null, null));
            Assert.False(Bs8666Label.Compose(null, null, null, null).HasContent);
        }
    }
}
