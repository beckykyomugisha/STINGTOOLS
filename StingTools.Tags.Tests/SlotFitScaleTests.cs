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

        [Fact]
        public void The_slot_hint_is_a_floor()
        {
            int s = SlotFitScale.Decide(30, typeScale: 50, currentScale: 50, scaleHint: 100, out bool c);
            Assert.Equal(100, s);
            Assert.True(c);
        }
    }
}
