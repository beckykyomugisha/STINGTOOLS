using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DT-R11 (Drawing Self-Test f, Revit 2025): a schedule placed into a slot measured
    /// its top-left at (248, 400) mm against a slot top-left of (250, 400) — the
    /// insertion point is not the visible corner. Placement now measures and corrects.
    /// </summary>
    public class ScheduleSlotAlignmentTests
    {
        private const double MmPerFt = 304.8;
        private static double Ft(double mm) => mm / MmPerFt;

        [Fact]
        public void Self_test_case_moves_the_schedule_2mm_right()
        {
            var c = ScheduleSlotAlignment.Compute(Ft(250), Ft(400), Ft(248), Ft(400));
            Assert.True(c.Needed);
            Assert.False(c.Refused);
            Assert.Equal(2.0, c.Dx * MmPerFt, 6);
            Assert.Equal(0.0, c.Dy);
            // After the move the corner is on the slot.
            Assert.True(ScheduleSlotAlignment.IsAligned(Ft(250), Ft(400), Ft(248) + c.Dx, Ft(400) + c.Dy));
        }

        [Fact]
        public void Both_axes_are_corrected()
        {
            var c = ScheduleSlotAlignment.Compute(Ft(250), Ft(400), Ft(247), Ft(401.5));
            Assert.True(c.Needed);
            Assert.Equal(3.0, c.Dx * MmPerFt, 6);
            Assert.Equal(-1.5, c.Dy * MmPerFt, 6);
        }

        [Fact]
        public void Rounding_is_not_a_misplacement()
        {
            var c = ScheduleSlotAlignment.Compute(Ft(250), Ft(400), Ft(250.05), Ft(399.95));
            Assert.False(c.Needed);
            Assert.False(c.Refused);
        }

        [Fact]
        public void An_implausible_offset_is_refused_not_applied()
        {
            var c = ScheduleSlotAlignment.Compute(Ft(250), Ft(400), Ft(100), Ft(400));
            Assert.False(c.Needed);
            Assert.True(c.Refused);
        }

        [Fact]
        public void A_bad_measurement_is_refused()
            => Assert.True(ScheduleSlotAlignment.Compute(Ft(250), Ft(400), double.NaN, Ft(400)).Refused);
    }
}
