using System;

namespace StingTools.Core.Drawing
{
    // ══════════════════════════════════════════════════════════════════════
    //  SheetQrPlacement — WHERE the sheet QR goes, decided without Revit.
    //
    //  WHY THIS IS SPLIT OUT
    //  ---------------------
    //  `SheetQrStamper` needs a Document, a ViewSheet and a title-block instance, so
    //  nothing about it can be exercised outside Revit — and the one bug this feature
    //  has already produced lived exactly here: fractional slot coords resolved to
    //  y = -29 mm on A0 and -5 mm on A3 portrait, off the paper, where the stamp is
    //  invisible on screen AND absent from the plot with nothing reporting it.
    //
    //  That is arithmetic, not Revit. Splitting the decision from the API call makes
    //  the half that actually goes wrong testable, which is the compute/present split
    //  CLAUDE.md asks for (P1 #4), proven on one more feature.
    //
    //  Everything here is in MILLIMETRES. The caller converts to and from Revit feet
    //  at the boundary, in one place, so a unit slip has one place to live.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Where a placement came from. Callers log this: "placed at the slot"
    /// and "placed in a corner because the slot was nonsense" must never read alike.</summary>
    public enum QrPlacementSource
    {
        /// <summary>The family's `qr-code` slot, used as declared.</summary>
        Slot,
        /// <summary>The slot was unusable, so the title block's corner was used.</summary>
        CornerFallback,
        /// <summary>Neither a slot nor a title-block rect was available.</summary>
        LastResort
    }

    /// <summary>An axis-aligned rectangle in sheet millimetres.</summary>
    public readonly struct QrRect
    {
        public readonly double X0, Y0, X1, Y1;

        public QrRect(double x0, double y0, double x1, double y1)
        {
            // Normalise so callers cannot hand us an inverted rect and get silence.
            X0 = Math.Min(x0, x1); X1 = Math.Max(x0, x1);
            Y0 = Math.Min(y0, y1); Y1 = Math.Max(y0, y1);
        }

        public double Width => X1 - X0;
        public double Height => Y1 - Y0;
        public double CentreX => (X0 + X1) / 2.0;
        public double CentreY => (Y0 + Y1) / 2.0;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool Contains(double x, double y, double tol = 1e-6)
            => x >= X0 - tol && x <= X1 + tol && y >= Y0 - tol && y <= Y1 + tol;

        public override string ToString() => $"({X0:F1},{Y0:F1})-({X1:F1},{Y1:F1})mm";
    }

    /// <summary>The decision: where, how big, from what, and why.</summary>
    public sealed class QrPlacementPlan
    {
        public double CentreXMm { get; }
        public double CentreYMm { get; }
        public double SizeMm { get; }
        public QrPlacementSource Source { get; }

        /// <summary>Null when the slot was used as declared. Otherwise states, in one
        /// sentence, what was wrong with it — this is what reaches the operator, so
        /// "the slot resolves off the paper" beats "fallback used".</summary>
        public string Rejection { get; }

        public QrPlacementPlan(double cx, double cy, double size, QrPlacementSource source, string rejection = null)
        {
            CentreXMm = cx; CentreYMm = cy; SizeMm = size; Source = source; Rejection = rejection;
        }
    }

    public static class SheetQrPlacement
    {
        /// <summary>Printed size when nothing else decides it, mm. 20 mm at ~25 modules
        /// gives a 0.8 mm module — comfortably above the ~0.5 mm floor for a phone
        /// camera at arm's length on paper.</summary>
        public const double DefaultSizeMm = 20.0;

        /// <summary>Inset from the title-block corner for the fallback, mm.</summary>
        public const double FallbackMarginMm = 8.0;

        /// <summary>Below this a QR stops being scannable on a print and becomes
        /// decoration, so a slot smaller than this is refused rather than honoured.</summary>
        public const double MinScannableMm = 12.0;

        /// <summary>Decide where the stamp goes.</summary>
        /// <param name="slot">The family's `qr-code` slot, or null when it has none.</param>
        /// <param name="titleBlock">The title block's footprint on the sheet, or null
        /// when Revit could not give one.</param>
        public static QrPlacementPlan Plan(QrRect? slot, QrRect? titleBlock)
        {
            string rejection = null;

            if (slot.HasValue)
            {
                var s = slot.Value;
                double fit = Math.Min(s.Width, s.Height);

                if (s.IsEmpty)
                {
                    rejection = $"the qr-code slot is empty {s}";
                }
                else if (fit < MinScannableMm)
                {
                    // Honouring a 4 mm slot produces a mark nothing can read, which looks
                    // like a working stamp on screen and fails in the one place it matters.
                    rejection = $"the qr-code slot is {fit:F1} mm, under the {MinScannableMm:F0} mm "
                              + "a printed code needs to stay scannable";
                }
                else if (titleBlock.HasValue && !titleBlock.Value.Contains(s.CentreX, s.CentreY))
                {
                    // The A0 / A3-portrait defect. A slot outside the title block is worse
                    // than no slot: off-paper is invisible on screen and absent from the plot.
                    rejection = $"the qr-code slot centres at ({s.CentreX:F0}, {s.CentreY:F0}) mm, "
                              + $"outside the title block {titleBlock.Value}";
                }
                else
                {
                    return new QrPlacementPlan(s.CentreX, s.CentreY, fit, QrPlacementSource.Slot);
                }
            }

            if (titleBlock.HasValue && !titleBlock.Value.IsEmpty)
            {
                var tb = titleBlock.Value;
                double size = DefaultSizeMm;

                // Never propose a stamp bigger than the block it sits on. On a small or
                // odd title block, shrink to fit rather than hang it over the edge.
                double room = Math.Min(tb.Width, tb.Height) - 2 * FallbackMarginMm;
                if (room > 0 && room < size) size = room;

                // Top-right, not bottom-right: every STING title block puts its
                // project-info block along the bottom strip, so a blind bottom-right
                // fallback would land on real content on exactly the families that
                // reach this branch (the ones with no slot declared).
                double cx = tb.X1 - FallbackMarginMm - size / 2.0;
                double cy = tb.Y1 - FallbackMarginMm - size / 2.0;
                return new QrPlacementPlan(cx, cy, size, QrPlacementSource.CornerFallback, rejection);
            }

            // Nothing to anchor to. Offset from the origin so the stamp is at least
            // visible and obviously misplaced, rather than invisibly at (0,0) under
            // the border where it reads as "no QR was produced".
            return new QrPlacementPlan(
                FallbackMarginMm + DefaultSizeMm / 2.0,
                FallbackMarginMm + DefaultSizeMm / 2.0,
                DefaultSizeMm,
                QrPlacementSource.LastResort,
                rejection ?? "no qr-code slot and no title-block extent");
        }
    }
}
