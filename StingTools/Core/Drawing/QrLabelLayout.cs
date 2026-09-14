using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    // ══════════════════════════════════════════════════════════════════════
    //  QrLabelLayout — where each QR label sits on a label sheet.
    //
    //  ROADMAP QR-12 asked for MaxRects bin-packing here. It would buy nothing:
    //  every label is the SAME SIZE, and for equal squares a uniform grid is
    //  already optimal — bin-packing exists to fit items of differing sizes.
    //  Running MaxRects over identical squares produces the same grid, slower, in
    //  code nobody can check by eye.
    //
    //  What the naive version actually got wrong was simpler and real: it started a
    //  new row whenever the cursor was above the bottom margin, so the LAST row
    //  could begin with less than a full cell of height left and run off the sheet.
    //  Labels there would be clipped on the plot — and a clipped QR is unreadable
    //  while still looking like a label someone can use.
    //
    //  So this computes the grid that FITS, and says how many cells that is before
    //  anything is drawn. Revit-free, so the arithmetic is unit-tested.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>One label's cell, in millimetres from the sheet origin.</summary>
    public readonly struct QrLabelCell
    {
        /// <summary>Bottom-left of the QR square itself.</summary>
        public readonly double XMm, YMm;
        /// <summary>Zero-based sheet this cell is on.</summary>
        public readonly int SheetIndex;

        public QrLabelCell(double x, double y, int sheetIndex)
        {
            XMm = x; YMm = y; SheetIndex = sheetIndex;
        }

        /// <summary>Where the tag caption goes: centred under the code.</summary>
        public double CaptionYMm(double captionGapMm) => YMm - captionGapMm;

        public override string ToString() => $"sheet {SheetIndex} ({XMm:F1},{YMm:F1})mm";
    }

    public sealed class QrLabelGrid
    {
        public int Columns { get; init; }
        public int Rows { get; init; }
        /// <summary>Cells on one sheet. Zero means the sheet is too small to carry
        /// even one label — a real answer the caller must handle, not a division
        /// to guard against later.</summary>
        public int PerSheet => Columns * Rows;
        /// <summary>Left edge of the first column, mm. The grid is centred in the
        /// usable width so an under-filled sheet does not look lopsided when a
        /// person trims it.</summary>
        public double OriginXMm { get; init; }
        /// <summary>Bottom edge of the TOP row, mm.</summary>
        public double TopRowYMm { get; init; }
        public double PitchXMm { get; init; }
        public double PitchYMm { get; init; }
    }

    public static class QrLabelLayout
    {
        /// <summary>Vertical room reserved under each code for its tag caption, mm.
        /// A QR alone cannot be matched to an asset by eye, which is exactly what a
        /// person does when the scan fails.</summary>
        public const double CaptionBandMm = 6.0;

        /// <summary>Work out the grid for one sheet.</summary>
        /// <param name="sheetWmm">Sheet width, mm.</param>
        /// <param name="sheetHmm">Sheet height, mm.</param>
        /// <param name="codeMm">Printed size of each QR, mm.</param>
        /// <param name="gutterMm">Gap between cells, mm — wide enough to cut along.</param>
        /// <param name="marginMm">Inset from the sheet edge, mm.</param>
        public static QrLabelGrid PlanGrid(double sheetWmm, double sheetHmm,
                                           double codeMm, double gutterMm, double marginMm)
        {
            double usableW = sheetWmm - 2 * marginMm;
            double usableH = sheetHmm - 2 * marginMm;

            double pitchX = codeMm + gutterMm;
            double pitchY = codeMm + CaptionBandMm + gutterMm;

            // Columns: n cells need n*code + (n-1)*gutter. Solve, then floor.
            int cols = usableW <= 0 ? 0 : (int)Math.Floor((usableW + gutterMm) / pitchX);
            int rows = usableH <= 0 ? 0 : (int)Math.Floor((usableH + gutterMm) / pitchY);
            if (cols < 0) cols = 0;
            if (rows < 0) rows = 0;

            // Centre the block horizontally in the usable width. Vertically it stays
            // top-aligned: a half-empty last sheet then has its blank space at the
            // BOTTOM, where it is trimmed off rather than cut through.
            double blockW = cols > 0 ? cols * codeMm + (cols - 1) * gutterMm : 0;
            double originX = marginMm + Math.Max(0, (usableW - blockW) / 2.0);
            double topRowY = sheetHmm - marginMm - codeMm;

            return new QrLabelGrid
            {
                Columns = cols,
                Rows = rows,
                OriginXMm = originX,
                TopRowYMm = topRowY,
                PitchXMm = pitchX,
                PitchYMm = pitchY,
            };
        }

        /// <summary>Lay out <paramref name="count"/> labels across as many sheets as
        /// it takes, row-major and top-down — the order a plotted sheet reads.
        ///
        /// Returns an EMPTY list when the grid holds nothing, rather than dividing by
        /// zero or quietly placing one label off the sheet. The caller reports that;
        /// it is a real outcome for a tiny sheet with a large code size.</summary>
        public static IReadOnlyList<QrLabelCell> Place(int count, QrLabelGrid grid)
        {
            var cells = new List<QrLabelCell>();
            if (count <= 0 || grid == null || grid.PerSheet <= 0) return cells;

            for (int i = 0; i < count; i++)
            {
                int sheet = i / grid.PerSheet;
                int onSheet = i % grid.PerSheet;
                int row = onSheet / grid.Columns;
                int col = onSheet % grid.Columns;

                cells.Add(new QrLabelCell(
                    grid.OriginXMm + col * grid.PitchXMm,
                    grid.TopRowYMm - row * grid.PitchYMm,
                    sheet));
            }
            return cells;
        }

        /// <summary>How many sheets <paramref name="count"/> labels will take.
        /// Zero when the grid holds nothing — NOT one, because "one sheet that can
        /// carry no labels" is not a thing to produce.</summary>
        public static int SheetCount(int count, QrLabelGrid grid)
        {
            if (count <= 0 || grid == null || grid.PerSheet <= 0) return 0;
            return (count + grid.PerSheet - 1) / grid.PerSheet;
        }
    }
}
