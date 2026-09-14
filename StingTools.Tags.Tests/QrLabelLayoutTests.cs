using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// QR label-sheet layout.
    ///
    /// ROADMAP QR-12 asked for MaxRects bin-packing. It would buy nothing: every
    /// label is the same size, and for equal squares a uniform grid is already
    /// optimal. What the naive version got wrong was real and simpler — it started a
    /// row whenever the cursor was above the bottom margin, so the last row could
    /// begin with less than a cell of height left and run off the sheet. A clipped
    /// QR is unreadable while still looking like a label someone can use.
    ///
    /// These tests are about the sheet edge, the empty cases, and the ordering a
    /// person relies on when cutting the plot up.
    /// </summary>
    public class QrLabelLayoutTests
    {
        // A1 landscape, and the command's shipped constants.
        private const double W = 841, H = 594, Code = 30, Gutter = 12, Margin = 20;

        private static QrLabelGrid A1() => QrLabelLayout.PlanGrid(W, H, Code, Gutter, Margin);

        [Fact]
        public void Every_cell_lands_inside_the_printable_area()
        {
            // The defect this replaces: a label clipped by the sheet edge.
            var grid = A1();
            var cells = QrLabelLayout.Place(grid.PerSheet, grid);

            Assert.NotEmpty(cells);
            foreach (var c in cells)
            {
                Assert.True(c.XMm >= Margin - 0.001, $"{c} starts left of the margin");
                Assert.True(c.XMm + Code <= W - Margin + 0.001, $"{c} runs past the right margin");
                // The caption band sits UNDER the code, so the bottom bound has to
                // account for it or the tag text is the thing that gets clipped.
                Assert.True(c.YMm - QrLabelLayout.CaptionBandMm >= Margin - 0.001,
                    $"{c} leaves no room for its caption above the bottom margin");
                Assert.True(c.YMm + Code <= H - Margin + 0.001, $"{c} runs past the top margin");
            }
        }

        [Fact]
        public void The_grid_is_as_full_as_it_can_be()
        {
            // Guards against a lazy off-by-one that silently wastes a whole column or
            // row on every sheet — which on a 200-asset job is several extra plots.
            var grid = A1();
            double usableW = W - 2 * Margin;
            double usableH = H - 2 * Margin;

            // One more column would not fit.
            Assert.True((grid.Columns + 1) * Code + grid.Columns * Gutter > usableW + 0.001,
                $"{grid.Columns + 1} columns would have fitted");
            // Nor one more row.
            double rowH = Code + QrLabelLayout.CaptionBandMm;
            Assert.True((grid.Rows + 1) * rowH + grid.Rows * Gutter > usableH + 0.001,
                $"{grid.Rows + 1} rows would have fitted");
        }

        [Fact]
        public void Labels_read_left_to_right_then_top_to_bottom()
        {
            // The order someone cutting up a plot expects, and the order the tags were
            // sorted into. Getting it wrong makes a sorted sheet look shuffled.
            var grid = A1();
            var cells = QrLabelLayout.Place(grid.Columns * 2, grid);

            // First row: same Y, increasing X.
            for (int i = 1; i < grid.Columns; i++)
            {
                Assert.Equal(cells[0].YMm, cells[i].YMm, 3);
                Assert.True(cells[i].XMm > cells[i - 1].XMm);
            }
            // Second row starts back at the left, lower down.
            Assert.Equal(cells[0].XMm, cells[grid.Columns].XMm, 3);
            Assert.True(cells[grid.Columns].YMm < cells[0].YMm);
        }

        [Fact]
        public void Overflow_starts_a_new_sheet_rather_than_overlapping()
        {
            var grid = A1();
            var cells = QrLabelLayout.Place(grid.PerSheet + 1, grid);

            Assert.Equal(0, cells[grid.PerSheet - 1].SheetIndex);
            Assert.Equal(1, cells[grid.PerSheet].SheetIndex);
            // And the first cell of sheet 2 is back at the top-left.
            Assert.Equal(cells[0].XMm, cells[grid.PerSheet].XMm, 3);
            Assert.Equal(cells[0].YMm, cells[grid.PerSheet].YMm, 3);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(5, 1)]
        [InlineData(0, 0)]
        public void Sheet_count_matches_what_was_placed(int count, int minSheets)
        {
            var grid = A1();
            var cells = QrLabelLayout.Place(count, grid);

            Assert.Equal(count, cells.Count);
            Assert.Equal(QrLabelLayout.SheetCount(count, grid), cells.Count == 0 ? 0 : cells.Max(c => c.SheetIndex) + 1);
            Assert.True(QrLabelLayout.SheetCount(count, grid) >= minSheets);
        }

        [Fact]
        public void Nothing_to_place_produces_no_sheets()
        {
            // Zero, not one. "One sheet that carries no labels" is not a thing to plot.
            Assert.Equal(0, QrLabelLayout.SheetCount(0, A1()));
            Assert.Empty(QrLabelLayout.Place(0, A1()));
        }

        [Fact]
        public void A_sheet_too_small_for_one_label_holds_none_rather_than_one_off_the_edge()
        {
            // The caller checks PerSheet and refuses before creating anything. Returning
            // a grid of 1 here would put a label off the paper and look like it worked.
            var tiny = QrLabelLayout.PlanGrid(50, 50, Code, Gutter, Margin);

            Assert.Equal(0, tiny.PerSheet);
            Assert.Empty(QrLabelLayout.Place(10, tiny));
            Assert.Equal(0, QrLabelLayout.SheetCount(10, tiny));
        }

        [Fact]
        public void A_negative_usable_area_does_not_produce_negative_columns()
        {
            // Margins larger than the sheet. Absurd input, but a negative column count
            // would make Place() divide by a negative and scatter cells.
            var absurd = QrLabelLayout.PlanGrid(30, 30, Code, Gutter, Margin);

            Assert.True(absurd.Columns >= 0);
            Assert.True(absurd.Rows >= 0);
            Assert.Equal(0, absurd.PerSheet);
        }

        [Fact]
        public void The_block_is_centred_so_a_part_filled_sheet_is_not_lopsided()
        {
            var grid = A1();
            double blockW = grid.Columns * Code + (grid.Columns - 1) * Gutter;
            double leftGap = grid.OriginXMm;
            double rightGap = W - (grid.OriginXMm + blockW);

            Assert.Equal(leftGap, rightGap, 3);
        }

        [Theory]
        [InlineData(420, 297)]    // A3
        [InlineData(594, 420)]    // A2
        [InlineData(1189, 841)]   // A0
        [InlineData(297, 420)]    // A3 portrait
        public void Every_paper_size_lays_out_within_its_own_edges(double w, double h)
        {
            // The sizes the title-block catalogue actually ships. A grid that only
            // works on A1 would fail on exactly the small sheets where the arithmetic
            // is tightest.
            var grid = QrLabelLayout.PlanGrid(w, h, Code, Gutter, Margin);
            Assert.True(grid.PerSheet > 0, $"{w}x{h} should hold at least one {Code}mm label");

            foreach (var c in QrLabelLayout.Place(grid.PerSheet, grid))
            {
                Assert.True(c.XMm >= Margin - 0.001 && c.XMm + Code <= w - Margin + 0.001, $"{c} off {w}x{h}");
                Assert.True(c.YMm - QrLabelLayout.CaptionBandMm >= Margin - 0.001
                            && c.YMm + Code <= h - Margin + 0.001, $"{c} off {w}x{h}");
            }
        }
    }
}
