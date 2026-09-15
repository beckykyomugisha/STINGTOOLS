// The two rules a sheet number and a CSV lookup now go through. Both replaced
// hard-coded behaviour, and both have a failure mode that produces a
// plausible-looking wrong answer rather than an error.

using System;
using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SheetNumberPatternTests
    {
        [Theory]
        [InlineData("{disc}-{seq:D3}", "A", "01", 1, "A-001")]
        [InlineData("{disc}-{seq:D3}", "A", "01", 42, "A-042")]
        [InlineData("{disc}-{lvl}-{seq:D3}", "A", "01", 7, "A-01-007")]
        [InlineData("{proj}-{disc}-{seq:D4}", "M", "", 3, "SAH-M-0003")]
        [InlineData("{disc}{seq:D2}", "E", "", 9, "E09")]
        [InlineData("{seq}", "A", "", 5, "005")]
        public void PatternsBuildTheNumber(string pattern, string disc, string lvl,
                                           int seq, string expected)
            => Assert.Equal(expected, SheetDisciplineResolver.FormatNumber(
                pattern, disc, lvl, "SAH", "PLNS", seq));

        [Fact]
        public void AnEmptyTokenTakesItsSeparatorWithIt()
        {
            // "A--001" reached a live PDF filename once. A project with no level code
            // must get A-001, not a double dash where the level would have been.
            Assert.Equal("A-001", SheetDisciplineResolver.FormatNumber(
                "{disc}-{lvl}-{seq:D3}", "A", "", "SAH", "PLNS", 1));
            Assert.Equal("A-001", SheetDisciplineResolver.FormatNumber(
                "{disc}-{lvl}-{seq:D3}", "A", null, "SAH", "PLNS", 1));
        }

        [Fact]
        public void AnUnknownTokenSurvivesRatherThanVanishing()
        {
            // A typo that disappears produces a number nobody can explain; one that
            // survives is obvious the moment the preview is read.
            Assert.Contains("{nonsense}", SheetDisciplineResolver.FormatNumber(
                "{disc}-{nonsense}-{seq:D3}", "A", "01", "SAH", "PLNS", 1));
        }

        [Fact]
        public void AnEmptyPatternFallsBackRatherThanProducingAnEmptyNumber()
        {
            Assert.Equal("A-001", SheetDisciplineResolver.FormatNumber(null, "A", "", "", "", 1));
            Assert.Equal("A-001", SheetDisciplineResolver.FormatNumber("   ", "A", "", "", "", 1));
        }

        // ── The vocabulary translation ───────────────────────────────────
        [Theory]
        [InlineData("A", "ARCH")]
        [InlineData("S", "STR")]
        [InlineData("M", "MEP")]
        [InlineData("E", "ELE")]
        [InlineData("P", "PLM")]
        [InlineData("FP", "FP")]
        [InlineData("COORD", "COORD")]
        [InlineData("ARCH", "ARCH")]   // already a column name
        public void ShortCodesTranslateToCsvColumns(string disc, string expected)
            => Assert.Equal(expected, SheetDisciplineResolver.ToCsvColumn(disc));

        [Fact]
        public void ADisciplineWithNoColumnIsGeneralNotSomebodyElses()
        {
            // Folding Civil into Structural or Interiors into Architectural would put
            // one trade's title-block defaults onto another trade's drawings, and
            // nothing would report it. GEN means "the project-wide defaults", which
            // is exactly true.
            Assert.Equal("GEN", SheetDisciplineResolver.ToCsvColumn("C"));
            Assert.Equal("GEN", SheetDisciplineResolver.ToCsvColumn("L"));
            Assert.Equal("GEN", SheetDisciplineResolver.ToCsvColumn("I"));
        }

        [Fact]
        public void AColumnTheProjectAddedIsUsed()
        {
            // TITLE_BLOCK.csv's header was always read dynamically while the lookup
            // tested a hard-coded array, so an added column could never be reached.
            var header = new List<string> { "ARCH", "STR", "MEP", "CIVIL", "GEN" };
            Assert.Equal("CIVIL", SheetDisciplineResolver.ToCsvColumn("CIVIL", header));
            Assert.Equal("ARCH", SheetDisciplineResolver.ToCsvColumn("A", header));
        }

        [Fact]
        public void AColumnTheProjectRemovedFallsBackInsteadOfNaming()
        {
            var header = new List<string> { "ARCH", "GEN" };
            Assert.Equal("GEN", SheetDisciplineResolver.ToCsvColumn("M", header));
        }

        [Fact]
        public void NothingResolvesToGeneral()
        {
            Assert.Equal("GEN", SheetDisciplineResolver.ToCsvColumn(null));
            Assert.Equal("GEN", SheetDisciplineResolver.ToCsvColumn("  "));
        }
    }
}
