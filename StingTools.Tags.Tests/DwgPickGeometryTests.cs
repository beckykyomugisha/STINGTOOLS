using System;
using System.Collections.Generic;
using StingTools.Model;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// CAD-2 — the interactive DWG pickers measured a size and then ignored it
    /// (Pick Wall), or never measured at all (Pick Column / Pick Beam). These cover
    /// the Revit-free half: measuring picked lines, recognising a rectangle, and
    /// choosing a type by width.
    /// </summary>
    public class DwgPickGeometryTests
    {
        private const double Tol = 1e-9;

        // ── MeasureParallelPair ────────────────────────────────────────────

        [Fact]
        public void ParallelLinesGiveTheirPerpendicularGapAndAMidCentreline()
        {
            // Two 5000-long faces 200 apart.
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 5000, 0, 0, 200, 5000, 200);
            Assert.True(p.IsParallel);
            Assert.Equal(200, p.Gap, 6);
            Assert.Equal(0, p.StartX, 6); Assert.Equal(100, p.StartY, 6);
            Assert.Equal(5000, p.EndX, 6); Assert.Equal(100, p.EndY, 6);
        }

        [Fact]
        public void AntiParallelSecondLineIsMatchedEndToEnd()
        {
            // Second line drawn the other way: without the swap the centreline would
            // cross diagonally and come out ~0 long.
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 5000, 0, 5000, 200, 0, 200);
            Assert.True(p.IsParallel);
            Assert.Equal(200, p.Gap, 6);
            Assert.Equal(5000, p.Length, 6);
            Assert.Equal(100, p.StartY, 6);
            Assert.Equal(100, p.EndY, 6);
        }

        [Fact]
        public void GapIsPerpendicularNotEndpointDistance()
        {
            // Faces offset along their length: endpoint distance is ~1000, gap is 250.
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 4000, 0, 1000, 250, 5000, 250);
            Assert.Equal(250, p.Gap, 6);
        }

        [Fact]
        public void RotatedFacesMeasureTheSameGap()
        {
            double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
            // Rotate (0,0)-(3000,0) and (0,300)-(3000,300) by 30°.
            var p = DwgPickGeometry.MeasureParallelPair(
                0, 0, 3000 * c, 3000 * s,
                -300 * s, 300 * c, 3000 * c - 300 * s, 3000 * s + 300 * c);
            Assert.True(p.IsParallel);
            Assert.Equal(300, p.Gap, 6);
        }

        [Fact]
        public void NonParallelLinesAreRejected()
        {
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 1000, 0, 0, 200, 1000, 700);
            Assert.False(p.IsParallel);
        }

        [Fact]
        public void ZeroLengthLineIsNotAPair()
        {
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 0, 0, 0, 200, 1000, 200);
            Assert.False(p.IsParallel);
        }

        [Fact]
        public void UnequalFacesUseTheirOverlapAsTheCentreline()
        {
            // A runs 0..10, B only 0..4, 1 apart. Averaging endpoints gave a centreline
            // 0..7 — longer than B and shorter than A. The element exists only where
            // both faces do: 0..4.
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 10, 0, 0, 1, 4, 1);
            Assert.True(p.IsParallel);
            Assert.Equal(1, p.Gap, 9);
            Assert.Equal(0, p.StartX, 9); Assert.Equal(0.5, p.StartY, 9);
            Assert.Equal(4, p.EndX, 9);   Assert.Equal(0.5, p.EndY, 9);
            Assert.Equal(4, p.Length, 9);
        }

        [Fact]
        public void OffsetFacesUseTheOverlapNotEitherLine()
        {
            // A 0..4000, B 1000..5000: the shared stretch is 1000..4000.
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 4000, 0, 5000, 250, 1000, 250);
            Assert.True(p.IsParallel);
            Assert.Equal(1000, p.StartX, 6); Assert.Equal(125, p.StartY, 6);
            Assert.Equal(4000, p.EndX, 6);   Assert.Equal(125, p.EndY, 6);
        }

        [Fact]
        public void TenDegreesApartIsNotParallel()
        {
            // cos 10° = 0.985 — inside the old 0.95 threshold (~18°), which let a
            // skewed pick through and measured a nonsense thickness.
            double a = 10 * Math.PI / 180;
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 1000, 0, 0, 200, 1000 * Math.Cos(a), 200 + 1000 * Math.Sin(a));
            Assert.False(p.IsParallel);
        }

        [Fact]
        public void TwoDegreesApartIsStillParallel()
        {
            double a = 2 * Math.PI / 180;
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 1000, 0, 0, 200, 1000 * Math.Cos(a), 200 + 1000 * Math.Sin(a));
            Assert.True(p.IsParallel);
        }

        [Fact]
        public void ParallelLinesThatDoNotOverlapAreNotAPair()
        {
            // Collinear-ish faces end to end share no stretch — nothing between them.
            var p = DwgPickGeometry.MeasureParallelPair(0, 0, 10, 0, 20, 1, 30, 1);
            Assert.False(p.IsParallel);
        }

        // ── NearestIndex / IndexWithinTolerance ────────────────────────────

        [Fact]
        public void WidthWithinFiveMillimetresIsAMatch()
        {
            var widths = new List<double> { 100, 203, 300 };
            Assert.Equal(1, DwgPickGeometry.IndexWithinTolerance(widths, 200, 5));
        }

        [Fact]
        public void ToleranceBoundaryIsInclusive()
        {
            var widths = new List<double> { 205 };
            Assert.Equal(0, DwgPickGeometry.IndexWithinTolerance(widths, 200, 5));
        }

        [Fact]
        public void WidthOutsideToleranceIsNoMatchButStillHasANearest()
        {
            // 212 is the nearest to 200 but more than 5 off: no match → duplicate from it.
            var widths = new List<double> { 100, 212, 300 };
            Assert.Equal(-1, DwgPickGeometry.IndexWithinTolerance(widths, 200, 5));
            Assert.Equal(1, DwgPickGeometry.NearestIndex(widths, 200));
        }

        [Fact]
        public void NearestPicksTheClosestNotTheFirstWithinTolerance()
        {
            var widths = new List<double> { 196, 200.5, 204 };
            Assert.Equal(1, DwgPickGeometry.IndexWithinTolerance(widths, 200, 5));
        }

        [Fact]
        public void UnmeasurableTypesAreIgnored()
        {
            var widths = new List<double> { 0, double.NaN, -5, 450 };
            Assert.Equal(3, DwgPickGeometry.NearestIndex(widths, 10));
            Assert.Equal(-1, DwgPickGeometry.NearestIndex(new List<double>(), 10));
            Assert.Equal(-1, DwgPickGeometry.NearestIndex(null, 10));
        }

        // ── TryParseRectangle ──────────────────────────────────────────────

        [Fact]
        public void ClosedAxisAlignedRectangleGivesSizeAndCentre()
        {
            var xs = new List<double> { 0, 400, 400, 0, 0 };
            var ys = new List<double> { 0, 0, 600, 600, 0 };
            Assert.True(DwgPickGeometry.TryParseRectangle(xs, ys, out var r));
            Assert.Equal(400, r.Width, 6);
            Assert.Equal(600, r.Depth, 6);
            Assert.Equal(200, r.CenterX, 6);
            Assert.Equal(300, r.CenterY, 6);
            Assert.Equal(0, r.AngleRad, 9);
        }

        [Fact]
        public void RectangleWithMidEdgeVertexIsStillARectangle()
        {
            var xs = new List<double> { 0, 200, 400, 400, 0 };
            var ys = new List<double> { 0, 0, 0, 600, 600 };
            Assert.True(DwgPickGeometry.TryParseRectangle(xs, ys, out var r));
            Assert.Equal(400, r.Width, 6);
            Assert.Equal(600, r.Depth, 6);
        }

        [Fact]
        public void RotatedRectangleKeepsItsSizeAndReportsTheRotation()
        {
            double a = 20 * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            double[,] local = { { 0, 0 }, { 500, 0 }, { 500, 300 }, { 0, 300 } };
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i < 4; i++)
            {
                xs.Add(local[i, 0] * c - local[i, 1] * s);
                ys.Add(local[i, 0] * s + local[i, 1] * c);
            }
            Assert.True(DwgPickGeometry.TryParseRectangle(xs, ys, out var r));
            Assert.Equal(500, r.Width, 6);
            Assert.Equal(300, r.Depth, 6);
            Assert.Equal(a, r.AngleRad, 9);
        }

        [Fact]
        public void RectangleDrawnAtNinetyDegreesIsPlacedUnrotatedWithSizesSwapped()
        {
            // First edge runs up +Y for 400, then 600 along -X: a 600 wide (X) × 400 deep (Y) footprint.
            var xs = new List<double> { 0, 0, -600, -600 };
            var ys = new List<double> { 0, 400, 400, 0 };
            Assert.True(DwgPickGeometry.TryParseRectangle(xs, ys, out var r));
            Assert.Equal(0, r.AngleRad, 9);
            Assert.Equal(600, r.Width, 6);
            Assert.Equal(400, r.Depth, 6);
        }

        [Fact]
        public void ParallelogramIsNotARectangle()
        {
            var xs = new List<double> { 0, 400, 500, 100 };
            var ys = new List<double> { 0, 0, 300, 300 };
            Assert.False(DwgPickGeometry.TryParseRectangle(xs, ys, out _));
        }

        [Fact]
        public void TriangleAndPentagonAreNotRectangles()
        {
            Assert.False(DwgPickGeometry.TryParseRectangle(
                new List<double> { 0, 400, 200, 0 }, new List<double> { 0, 0, 300, 0 }, out _));
            Assert.False(DwgPickGeometry.TryParseRectangle(
                new List<double> { 0, 400, 500, 200, -100 }, new List<double> { 0, 0, 300, 500, 300 }, out _));
        }

        // ── RectFromParallelEdges / NormalizeOrientation ───────────────────

        [Fact]
        public void TwoOppositeEdgesGiveTheColumnFootprint()
        {
            // Edges along X, 450 long, 300 apart.
            var pair = DwgPickGeometry.MeasureParallelPair(0, 0, 450, 0, 0, 300, 450, 300);
            var r = DwgPickGeometry.RectFromParallelEdges(pair);
            Assert.Equal(450, r.Width, 6);
            Assert.Equal(300, r.Depth, 6);
            Assert.Equal(225, r.CenterX, 6);
            Assert.Equal(150, r.CenterY, 6);
            Assert.Equal(0, r.AngleRad, 9);
        }

        [Theory]
        [InlineData(0.0, 0.0, false)]
        [InlineData(90.0, 0.0, true)]
        [InlineData(-90.0, 0.0, true)]
        [InlineData(180.0, 0.0, false)]
        [InlineData(100.0, 10.0, true)]
        [InlineData(-45.0, 45.0, true)]
        [InlineData(45.0, 45.0, false)]
        public void OrientationIsFoldedIntoAQuarterTurn(double inDeg, double outDeg, bool swapped)
        {
            double w = 500, d = 300, a = inDeg * Math.PI / 180;
            DwgPickGeometry.NormalizeOrientation(ref w, ref d, ref a);
            Assert.Equal(outDeg, a * 180 / Math.PI, 6);
            Assert.Equal(swapped ? 300 : 500, w, 9);
            Assert.Equal(swapped ? 500 : 300, d, 9);
        }

        // ── Distances (used to pick import-local vs model coordinates) ────

        [Fact]
        public void DistanceToSegmentClampsToTheEnds()
        {
            Assert.Equal(5, DwgPickGeometry.DistanceToSegment2D(0, 0, 10, 0, 5, 5), 9);
            Assert.Equal(5, DwgPickGeometry.DistanceToSegment2D(0, 0, 10, 0, 13, 4), 9);
        }

        [Fact]
        public void DistanceToPolylineIsTheNearestSegment()
        {
            var xs = new List<double> { 0, 10, 10 };
            var ys = new List<double> { 0, 0, 10 };
            Assert.Equal(1, DwgPickGeometry.DistanceToPolyline2D(xs, ys, 11, 5), 9);
            Assert.Equal(double.MaxValue, DwgPickGeometry.DistanceToPolyline2D(new List<double>(), new List<double>(), 0, 0));
        }
    }
}
