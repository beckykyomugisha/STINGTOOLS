using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Pasting a column out of a supplier's spreadsheet is the reason the rate
    /// grid beats a form. Typing 58 numbers is the work the grid was meant to
    /// remove, not preserve.
    ///
    /// The tests are mostly about alignment, because a paste that silently
    /// shifts by one row puts every rate below the shift on the wrong
    /// commodity — and each of those then looks like a deliberate figure.
    /// </summary>
    public class RatePasteParserTests
    {
        [Fact]
        public void A_Plain_Column_Parses_In_Order()
        {
            var r = RatePasteParser.Parse("28000\r\n45000\r\n1400000");

            Assert.Equal(new double?[] { 28000, 45000, 1400000 },
                         r.Cells.Select(c => c.Value).ToArray());
        }

        [Fact]
        public void Excels_Trailing_Newline_Does_Not_Become_A_Row()
        {
            // Copying a 3-cell range puts "a\r\nb\r\nc\r\n" on the clipboard.
            var r = RatePasteParser.Parse("28000\r\n45000\r\n1400000\r\n");

            Assert.Equal(3, r.Cells.Count);
        }

        [Fact]
        public void An_INTERIOR_Blank_Keeps_Its_Place()
        {
            // The alignment rule. Dropping the gap would move 1400000 up onto
            // whatever commodity the blank belonged to, and nothing on screen
            // would say so.
            var r = RatePasteParser.Parse("28000\r\n\r\n1400000");

            Assert.Equal(3, r.Cells.Count);
            Assert.Null(r.Cells[1].Value);
            Assert.False(r.Cells[1].Unparseable);
            Assert.Equal(1400000, r.Cells[2].Value);
        }

        [Fact]
        public void A_Line_With_No_Number_Is_Rejected_Not_Zeroed()
        {
            // A pasted header row, or a "TBC". Treating either as zero prices
            // the commodity at nothing and totals it as free.
            var r = RatePasteParser.Parse("Rate\r\n28000\r\nTBC");

            Assert.Equal(1, r.ValueCount);
            Assert.Equal(2, r.Rejected.Count);
            Assert.Contains("Rate", r.Rejected);
            Assert.Contains("TBC", r.Rejected);
        }

        [Fact]
        public void A_Rejected_Line_Still_Consumes_Its_Row()
        {
            // Same alignment rule as a blank: three lines in, three cells out.
            var r = RatePasteParser.Parse("28000\r\nTBC\r\n45000");

            Assert.Equal(3, r.Cells.Count);
            Assert.True(r.Cells[1].Unparseable);
            Assert.Equal(45000, r.Cells[2].Value);
        }

        [Theory]
        [InlineData("28,000", 28000)]
        [InlineData("UGX 28000", 28000)]
        [InlineData("UGX 1,400,000", 1400000)]
        [InlineData(" 28000 ", 28000)]
        [InlineData("28000.50", 28000.50)]
        [InlineData("$45", 45)]
        public void Currency_And_Separators_Are_Decoration(string text, double expected)
        {
            Assert.Equal(expected, RatePasteParser.ParseNumber(text));
        }

        [Theory]
        [InlineData("28k")]
        [InlineData("TBC")]
        [InlineData("n/a")]
        [InlineData("")]
        [InlineData("   ")]
        public void Anything_That_Is_Not_A_Number_Stays_Not_A_Number(string text)
        {
            // "28k" must NOT read as 28. Stripping decoration is not the same
            // as guessing at an abbreviation.
            Assert.Null(RatePasteParser.ParseNumber(text));
        }

        [Fact]
        public void A_Parenthesised_Negative_Stays_Negative()
        {
            // Accounting notation. Making it positive would turn a credit into
            // a price; it is rejected downstream as negative, which is visible.
            Assert.Equal(-500, RatePasteParser.ParseNumber("(500)"));
        }

        [Fact]
        public void A_Two_Column_Paste_Takes_The_RATE_Not_The_Description()
        {
            var r = RatePasteParser.Parse("Cement\t28000\r\nSand\t1400000");

            Assert.Equal(new double?[] { 28000, 1400000 }, r.Cells.Select(c => c.Value).ToArray());
        }

        [Fact]
        public void A_Quantity_Column_Before_The_Rate_Does_Not_Win()
        {
            // The case that actually discriminates first-field from last-field.
            // Copying Qty + Rate out of a working sheet is ordinary, and both
            // fields are numeric — "first numeric wins" would price cement at
            // 93 and nothing on screen would look wrong.
            var r = RatePasteParser.Parse("93\t28000\r\n2\t1400000");

            Assert.Equal(new double?[] { 28000, 1400000 }, r.Cells.Select(c => c.Value).ToArray());
        }

        [Fact]
        public void A_Three_Column_Paste_Still_Takes_The_Rightmost_Number()
        {
            var r = RatePasteParser.Parse("Cement\tBags\t93\t28000");

            Assert.Equal(28000, r.Cells.Single().Value);
        }

        [Fact]
        public void A_Description_Containing_Digits_Does_Not_Win_Over_The_Rate()
        {
            // "8\" hollow block" has a number in it. Last-numeric-field is what
            // stops it being read as the price.
            var r = RatePasteParser.Parse("8 inch hollow block\t2500");

            Assert.Equal(2500, r.Cells.Single().Value);
        }

        [Fact]
        public void Empty_Clipboard_Produces_Nothing_And_Says_Nothing()
        {
            Assert.Empty(RatePasteParser.Parse("").Cells);
            Assert.Empty(RatePasteParser.Parse(null).Cells);
            Assert.Null(RatePasteParser.Parse("").Summary(0, 0));
        }

        // ── the report ──────────────────────────────────────────────────────

        [Fact]
        public void The_Summary_Names_What_Was_Not_Applied()
        {
            var r = RatePasteParser.Parse("28000\r\nTBC\r\n\r\n45000");

            string s = r.Summary(applied: 2, ranOutOfRows: 0);

            Assert.Contains("Pasted 2 rate(s)", s);
            Assert.Contains("1 blank line(s) left their row unchanged", s);
            Assert.Contains("'TBC'", s);
            Assert.Contains("not a rate of zero", s);
        }

        [Fact]
        public void Running_Off_The_Bottom_Of_The_Grid_Is_Reported()
        {
            // Pasting 20 values starting 5 rows from the end must not silently
            // drop 15 of them.
            var r = RatePasteParser.Parse("1\r\n2\r\n3");

            Assert.Contains("had no row left to land on", r.Summary(applied: 1, ranOutOfRows: 2));
        }

        [Fact]
        public void A_Clean_Paste_Reports_Only_The_Count()
        {
            var r = RatePasteParser.Parse("28000\r\n45000");

            string s = r.Summary(applied: 2, ranOutOfRows: 0);

            Assert.Equal("Pasted 2 rate(s).", s);
        }
    }
}
