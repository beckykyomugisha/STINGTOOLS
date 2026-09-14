using System;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The sheet-QR placement decision.
    ///
    /// This is the half of `SheetQrStamper` that can be wrong without Revit, and the
    /// only half that has been wrong so far: the first cut resolved the slot from
    /// fractional coords against the drawable rect and landed at y = -29 mm on A0 and
    /// y = -5 mm on A3 portrait — off the paper, invisible on screen AND absent from
    /// the plot, with nothing reporting a thing.
    ///
    /// Those two exact numbers are pinned below. The rest of the stamper (creating an
    /// ImageType, placing an ImageInstance, writing the parameter) needs a Document and
    /// is ROADMAP QR-1 — a manual Revit check, honestly listed as such.
    /// </summary>
    public class SheetQrPlacementTests
    {
        // A1 landscape: 841 x 594 mm. The title block IS the sheet.
        private static readonly QrRect A1 = new QrRect(0, 0, 841, 594);
        // The A1 slot as shipped in STING_TITLE_BLOCKS.json.
        private static readonly QrRect A1Slot = new QrRect(794, 10, 820, 36);

        [Fact]
        public void A_valid_slot_is_used_as_declared()
        {
            var plan = SheetQrPlacement.Plan(A1Slot, A1);

            Assert.Equal(QrPlacementSource.Slot, plan.Source);
            Assert.Equal(807, plan.CentreXMm, 3);
            Assert.Equal(23, plan.CentreYMm, 3);
            Assert.Equal(26, plan.SizeMm, 3);
            Assert.Null(plan.Rejection);
        }

        [Fact]
        public void The_A0_regression_falls_back_instead_of_going_off_the_paper()
        {
            // The exact defect. fracAnchor [0.969136, -0.234043] against A0 landscape's
            // drawable (x10 y135 w1169 h701) resolved to (1143, -29) mm. A negative Y is
            // off the sheet: nothing renders, nothing prints, nothing complains.
            var offPaper = new QrRect(1143, -29, 1180, 10);
            var a0 = new QrRect(0, 0, 1189, 841);

            var plan = SheetQrPlacement.Plan(offPaper, a0);

            Assert.Equal(QrPlacementSource.CornerFallback, plan.Source);
            Assert.NotNull(plan.Rejection);
            Assert.Contains("outside the title block", plan.Rejection);
            // And the fallback itself must be on the paper.
            Assert.True(a0.Contains(plan.CentreXMm, plan.CentreYMm));
        }

        [Fact]
        public void The_A3_portrait_regression_falls_back_too()
        {
            // Same defect, smaller margin: (278, -5) mm on A3 portrait. A near-miss is
            // the dangerous case — it is easy to eyeball as "roughly right".
            var offPaper = new QrRect(269, -5, 278, 14);
            var a3p = new QrRect(0, 0, 297, 420);

            var plan = SheetQrPlacement.Plan(offPaper, a3p);

            Assert.Equal(QrPlacementSource.CornerFallback, plan.Source);
            Assert.True(a3p.Contains(plan.CentreXMm, plan.CentreYMm));
        }

        [Fact]
        public void A_slot_too_small_to_scan_is_refused_not_honoured()
        {
            // Honouring a 6 mm slot yields a mark no camera resolves: it looks like a
            // working stamp on screen and fails in the one place it matters.
            var tiny = new QrRect(800, 10, 806, 16);

            var plan = SheetQrPlacement.Plan(tiny, A1);

            Assert.Equal(QrPlacementSource.CornerFallback, plan.Source);
            Assert.Contains("scannable", plan.Rejection);
            Assert.True(plan.SizeMm >= SheetQrPlacement.MinScannableMm);
        }

        [Theory]
        [InlineData(12.0)]   // exactly at the floor — must be accepted
        [InlineData(12.5)]
        [InlineData(40.0)]
        public void A_slot_at_or_above_the_floor_is_accepted(double size)
        {
            var slot = new QrRect(700, 10, 700 + size, 10 + size);
            Assert.Equal(QrPlacementSource.Slot, SheetQrPlacement.Plan(slot, A1).Source);
        }

        [Fact]
        public void A_non_square_slot_is_fitted_to_its_short_side()
        {
            // A QR is square. Using the long side would overhang the slot.
            var wide = new QrRect(700, 10, 760, 35);   // 60 x 25
            var plan = SheetQrPlacement.Plan(wide, A1);

            Assert.Equal(QrPlacementSource.Slot, plan.Source);
            Assert.Equal(25, plan.SizeMm, 3);
        }

        [Fact]
        public void An_empty_slot_is_refused()
        {
            var plan = SheetQrPlacement.Plan(new QrRect(700, 10, 700, 10), A1);
            Assert.NotEqual(QrPlacementSource.Slot, plan.Source);
        }

        [Fact]
        public void No_slot_falls_back_to_the_top_right_of_the_title_block()
        {
            // Top-right, NOT bottom-right: every STING title block puts its project-info
            // block along the bottom strip, and the families that reach this branch are
            // exactly the ones with no slot declared, so a blind bottom-right fallback
            // would land on real content.
            var plan = SheetQrPlacement.Plan(null, A1);

            Assert.Equal(QrPlacementSource.CornerFallback, plan.Source);
            Assert.True(plan.CentreXMm > A1.CentreX, "fallback should be on the right half");
            Assert.True(plan.CentreYMm > A1.CentreY, "fallback should be on the top half");
            Assert.True(A1.Contains(plan.CentreXMm, plan.CentreYMm));
        }

        [Fact]
        public void The_fallback_never_overhangs_a_small_title_block()
        {
            // A 30 x 30 mm block cannot carry a 20 mm stamp with 8 mm margins. Shrink to
            // fit rather than hang it over the edge.
            var tiny = new QrRect(0, 0, 30, 30);
            var plan = SheetQrPlacement.Plan(null, tiny);

            Assert.True(plan.CentreXMm - plan.SizeMm / 2 >= tiny.X0 - 1e-6,
                $"stamp starts at {plan.CentreXMm - plan.SizeMm / 2}, left of the block");
            Assert.True(plan.CentreXMm + plan.SizeMm / 2 <= tiny.X1 + 1e-6,
                $"stamp ends at {plan.CentreXMm + plan.SizeMm / 2}, right of the block");
            Assert.True(plan.CentreYMm + plan.SizeMm / 2 <= tiny.Y1 + 1e-6);
        }

        [Fact]
        public void With_neither_a_slot_nor_a_title_block_it_says_so()
        {
            var plan = SheetQrPlacement.Plan(null, null);

            Assert.Equal(QrPlacementSource.LastResort, plan.Source);
            Assert.NotNull(plan.Rejection);
            // Never (0,0): a stamp under the border reads as "no QR was produced", which
            // is the silent-no-op failure this codebase specialises in.
            Assert.True(plan.CentreXMm > 0 && plan.CentreYMm > 0);
        }

        [Fact]
        public void A_slot_outside_an_unknown_title_block_is_still_honoured()
        {
            // With no title-block extent there is nothing to check the slot against.
            // "Cannot tell" must not become "reject" — that would silently move every
            // correctly-declared stamp to a corner whenever Revit withheld a bbox.
            var slot = new QrRect(794, 10, 820, 36);
            var plan = SheetQrPlacement.Plan(slot, null);

            Assert.Equal(QrPlacementSource.Slot, plan.Source);
            Assert.Equal(807, plan.CentreXMm, 3);
        }

        [Fact]
        public void An_inverted_rect_is_normalised_rather_than_silently_wrong()
        {
            var inverted = new QrRect(820, 36, 794, 10);
            Assert.Equal(794, inverted.X0, 3);
            Assert.Equal(820, inverted.X1, 3);
            Assert.Equal(26, inverted.Width, 3);
            Assert.Equal(QrPlacementSource.Slot, SheetQrPlacement.Plan(inverted, A1).Source);
        }

        [Fact]
        public void Every_shipped_slot_size_clears_the_scannable_floor()
        {
            // The spec ships 15, 18, 22 and 26 mm slots. If someone lowers one below the
            // floor, the planner silently stops using it — this makes that visible here
            // rather than as a missing stamp on a plot.
            foreach (var mm in new[] { 15.0, 18.0, 22.0, 26.0 })
                Assert.True(mm >= SheetQrPlacement.MinScannableMm,
                    $"a shipped slot size of {mm} mm is under the {SheetQrPlacement.MinScannableMm} mm floor");
        }
    }
}
