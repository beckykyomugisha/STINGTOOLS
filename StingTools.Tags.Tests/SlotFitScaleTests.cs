using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-150 — fit-to-slot only coarsens the drawing type's scale.</summary>
    public class SlotFitScaleTests
    {
        [Fact]
        public void A_small_plan_keeps_the_type_scale()
        {
            // Fits at 1:18 → the old code rounded up to 1:20 and replaced 1:100.
            int s = SlotFitScale.Decide(18, typeScale: 100, currentScale: 100, scaleHint: null, out bool c);
            Assert.Equal(100, s);
            Assert.False(c);
        }

        [Fact]
        public void A_view_that_does_not_fit_is_coarsened_and_reported()
        {
            int s = SlotFitScale.Decide(140, typeScale: 100, currentScale: 100, scaleHint: null, out bool c);
            Assert.Equal(200, s);
            Assert.True(c);
        }

        [Fact]
        public void The_type_scale_is_the_baseline_not_the_current_view_scale()
        {
            // The view arrives at 1:20, the type says 1:50, it fits at 1:30.
            int s = SlotFitScale.Decide(30, typeScale: 50, currentScale: 20, scaleHint: null, out bool c);
            Assert.Equal(50, s);
            Assert.False(c);
        }

        [Fact]
        public void No_type_scale_uses_the_current_view_scale()
        {
            int s = SlotFitScale.Decide(30, typeScale: 0, currentScale: 50, scaleHint: null, out bool c);
            Assert.Equal(50, s);
            Assert.False(c);
        }

        // ── DTW-157 ──────────────────────────────────────────────────────

        [Fact]
        public void Annotation_margin_pushes_a_borderline_fit_to_the_next_scale()
        {
            // The crop alone fits at 1:95 (→ 1:100); heads and title need 10 % more.
            double required = 95 * SlotFitScale.ClampMarginFactor(SlotFitScale.DefaultAnnotationMarginFactor);
            int s = SlotFitScale.Decide(required, typeScale: 100, currentScale: 100, scaleHint: null, out bool c);
            Assert.Equal(200, s);
            Assert.True(c);
        }

        [Theory]
        [InlineData(0.5, 1.0)]
        [InlineData(double.NaN, 1.0)]
        [InlineData(1.2, 1.2)]
        [InlineData(9.0, 2.0)]
        public void Margin_factor_is_clamped(double input, double expected)
            => Assert.Equal(expected, SlotFitScale.ClampMarginFactor(input));

        [Fact]
        public void Overflow_is_any_side_past_the_slot()
        {
            // slot centred (100,100), 50 x 40
            Assert.False(SlotFitScale.Overflows(76, 81, 124, 119, 100, 100, 50, 40, 0.1));
            Assert.True(SlotFitScale.Overflows(74, 81, 124, 119, 100, 100, 50, 40, 0.1));  // left
            Assert.True(SlotFitScale.Overflows(76, 81, 124, 121, 100, 100, 50, 40, 0.1));  // top
        }

        [Fact]
        public void The_slot_hint_is_a_floor()
        {
            int s = SlotFitScale.Decide(30, typeScale: 50, currentScale: 50, scaleHint: 100, out bool c);
            Assert.Equal(100, s);
            Assert.True(c);
        }
    }
}
