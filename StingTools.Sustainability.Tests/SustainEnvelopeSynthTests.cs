using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Gap fix #1 — synthesise a representative envelope from floor area so the
    // energy estimate isn't fabric-blind.
    public class SustainEnvelopeSynthTests
    {
        [Fact]
        public void ZeroArea_ProducesNoSegments()
        {
            var segs = SustainEnvelopeSynth.FromFloorArea(0, 3, true, new EnvelopeSynthInputs());
            Assert.Empty(segs);
        }

        [Fact]
        public void TopLevel_AddsWallsWindowsAndRoof()
        {
            var segs = SustainEnvelopeSynth.FromFloorArea(100, 3, isTopLevel: true, new EnvelopeSynthInputs());
            Assert.Equal(4, segs.Count(s => s.Kind == SegmentKind.ExteriorWall));
            Assert.Equal(4, segs.Count(s => s.Kind == SegmentKind.Window));
            Assert.Single(segs.Where(s => s.Kind == SegmentKind.Roof));
        }

        [Fact]
        public void MidFloor_HasNoRoofSegment()
        {
            var segs = SustainEnvelopeSynth.FromFloorArea(100, 3, isTopLevel: false, new EnvelopeSynthInputs());
            Assert.Empty(segs.Where(s => s.Kind == SegmentKind.Roof));
        }

        [Fact]
        public void RoofArea_EqualsFloorArea()
        {
            var segs = SustainEnvelopeSynth.FromFloorArea(250, 3, true, new EnvelopeSynthInputs());
            var roof = segs.Single(s => s.Kind == SegmentKind.Roof);
            Assert.Equal(250, roof.AreaM2, 3);
        }

        [Fact]
        public void WallAndWindowAreas_FollowPerimeterAndWwr()
        {
            // area 100 → perimeter = 3.5×√100 = 35 m; height 3 → gross wall = 105 m².
            // WWR 0.3 → windows 31.5, net wall 73.5; each split over 4 faces.
            var inp = new EnvelopeSynthInputs { Wwr = 0.30, PerimeterFactor = 3.5 };
            var segs = SustainEnvelopeSynth.FromFloorArea(100, 3, false, inp);
            double wall = segs.Where(s => s.Kind == SegmentKind.ExteriorWall).Sum(s => s.AreaM2);
            double win  = segs.Where(s => s.Kind == SegmentKind.Window).Sum(s => s.AreaM2);
            Assert.Equal(73.5, wall, 1);
            Assert.Equal(31.5, win, 1);
        }

        [Fact]
        public void UsesConstructionProfileUValues()
        {
            var inp = new EnvelopeSynthInputs { WallUvalue = 0.18, WindowUvalue = 1.1, RoofUvalue = 0.13 };
            var segs = SustainEnvelopeSynth.FromFloorArea(100, 3, true, inp);
            Assert.All(segs.Where(s => s.Kind == SegmentKind.ExteriorWall), s => Assert.Equal(0.18, s.UvalueWm2K, 3));
            Assert.All(segs.Where(s => s.Kind == SegmentKind.Window),       s => Assert.Equal(1.1,  s.UvalueWm2K, 3));
            Assert.Equal(0.13, segs.Single(s => s.Kind == SegmentKind.Roof).UvalueWm2K, 3);
        }

        [Fact]
        public void WindowsSpanFourOrientations()
        {
            var segs = SustainEnvelopeSynth.FromFloorArea(100, 3, false, new EnvelopeSynthInputs());
            var orientations = segs.Where(s => s.Kind == SegmentKind.Window)
                                   .Select(s => s.OrientationDeg).OrderBy(d => d).ToArray();
            Assert.Equal(new double[] { 0, 90, 180, 270 }, orientations);
        }

        [Fact]
        public void ZeroWwr_ProducesNoWindows()
        {
            var segs = SustainEnvelopeSynth.FromFloorArea(100, 3, false, new EnvelopeSynthInputs { Wwr = 0 });
            Assert.Empty(segs.Where(s => s.Kind == SegmentKind.Window));
            Assert.Equal(4, segs.Count(s => s.Kind == SegmentKind.ExteriorWall));
        }
    }
}
