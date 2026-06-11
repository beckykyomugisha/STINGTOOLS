using System.Collections.Generic;
using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the Revit-free device-coordination geometry: AABB overlap / planar
    /// clearance, per-room mounting-height outliers, and the swing-side sign test.
    /// </summary>
    public class DeviceCoordinationTests
    {
        private static Aabb Box(double x, double y, double z, double half = 50)
            => new Aabb
            {
                MinX = x - half, MaxX = x + half,
                MinY = y - half, MaxY = y + half,
                MinZ = z - half, MaxZ = z + half,
            };

        // ── AABB overlap / gap ──────────────────────────────────────────
        [Fact]
        public void OverlapsXY_true_when_boxes_intersect_in_plan()
        {
            Assert.True(Box(0, 0, 1000).OverlapsXY(Box(40, 0, 9999)));   // Z ignored in plan
            Assert.False(Box(0, 0, 1000).OverlapsXY(Box(200, 0, 1000)));
        }

        [Fact]
        public void PlanarGapMm_zero_on_overlap_else_edge_distance()
        {
            Assert.Equal(0, Box(0, 0, 0).PlanarGapMm(Box(40, 0, 0)));     // overlapping (halves 50 each)
            // centres 300 apart, each half 50 → edge gap 200 in X
            Assert.Equal(200, Box(0, 0, 0).PlanarGapMm(Box(300, 0, 0)), 3);
        }

        [Fact]
        public void ClearanceViolation_flags_too_close()
        {
            Assert.True(DeviceCoordination.ClearanceViolation(Box(0, 0, 0), Box(200, 0, 0), 300));  // gap 100 < 300
            Assert.False(DeviceCoordination.ClearanceViolation(Box(0, 0, 0), Box(500, 0, 0), 300)); // gap 400 ≥ 300
        }

        // ── Mounting-height outliers ────────────────────────────────────
        [Fact]
        public void MountingHeightOutliers_flags_deviation_from_median()
        {
            var zs = new List<double> { 1200, 1205, 1198, 1400 };   // median ~1201.5, last is +198
            var outliers = DeviceCoordination.MountingHeightOutliers(zs, 50);
            Assert.Single(outliers);
            Assert.Equal(3, outliers[0]);
        }

        [Fact]
        public void MountingHeightOutliers_empty_for_consistent_or_single()
        {
            Assert.Empty(DeviceCoordination.MountingHeightOutliers(new List<double> { 1200, 1210, 1190 }, 50));
            Assert.Empty(DeviceCoordination.MountingHeightOutliers(new List<double> { 1200 }, 50));
        }

        [Theory]
        [InlineData(new double[] { 1, 2, 3 }, 2)]
        [InlineData(new double[] { 1, 2, 3, 4 }, 2.5)]
        public void Median_handles_odd_and_even(double[] xs, double expected)
            => Assert.Equal(expected, DeviceCoordination.Median(xs));

        // ── Swing-side sign test ────────────────────────────────────────
        [Fact]
        public void OnSwingSide_uses_dot_product_sign()
        {
            // door at origin, swings toward +X. A device at +X is on the swing side.
            Assert.True(DeviceCoordination.OnSwingSide(0, 0, 1, 0, 500, 0));
            Assert.False(DeviceCoordination.OnSwingSide(0, 0, 1, 0, -500, 0));
            // device perpendicular to swing dir → not on swing side (dot = 0)
            Assert.False(DeviceCoordination.OnSwingSide(0, 0, 1, 0, 0, 500));
        }
    }
}
