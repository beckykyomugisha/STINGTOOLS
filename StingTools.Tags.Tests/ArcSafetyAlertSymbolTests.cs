using System;
using System.Linq;
using StingTools.Commands.Electrical.ArcFlash;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-32 — the ANSI Z535 safety-alert triangle on the arc-flash label header is drawn
    /// as filled regions from ArcSafetyAlertSymbol. These pin the geometry: sized to the
    /// header, inside it, apex up, equilateral, exclamation mark inside the triangle, the
    /// signal word clear of it, every polygon counter-clockwise (what a filled region needs).
    /// </summary>
    public class ArcSafetyAlertSymbolTests
    {
        private const double Header = 12, Left = 3, Gap = 2;
        private static ArcSafetyAlertSymbol Sym() => ArcSafetyAlertSymbol.Compute(Left, Header, Gap);

        [Fact]
        public void Triangle_is_equilateral_apex_up_and_sized_to_the_header()
        {
            var t = Sym().Triangle;
            double Side(int i) => Math.Sqrt(Math.Pow(t[i].X - t[(i + 1) % 3].X, 2) + Math.Pow(t[i].Y - t[(i + 1) % 3].Y, 2));
            Assert.Equal(Side(0), Side(1), 9);
            Assert.Equal(Side(1), Side(2), 9);
            double height = t.Max(p => p.Y) - t.Min(p => p.Y);
            Assert.Equal(Header * ArcSafetyAlertSymbol.HeightFraction, height, 9);
            // apex is the single highest point, centred over the base
            var apex = t.OrderByDescending(p => p.Y).First();
            Assert.Equal((t[0].X + t[1].X) / 2, apex.X, 9);
        }

        [Fact]
        public void Symbol_sits_inside_the_header_strip_at_its_left()
        {
            var s = Sym();
            var all = s.Triangle.Concat(s.Bar).ToList();
            Assert.All(all, p => Assert.InRange(p.Y, -Header, 0));
            Assert.Equal(Left, s.Triangle.Min(p => p.X), 9);
            // vertically centred
            Assert.Equal(-Header / 2, (s.Triangle.Max(p => p.Y) + s.Triangle.Min(p => p.Y)) / 2, 9);
        }

        [Fact]
        public void Exclamation_mark_lies_inside_the_triangle()
        {
            var s = Sym();
            Assert.All(s.Bar, p => Assert.True(s.InsideTriangle(p), $"bar corner {p.X:0.##},{p.Y:0.##} outside"));
            var c = s.DotCentre; double r = s.DotRadius;
            foreach (var d in new[] { (r, 0.0), (-r, 0.0), (0.0, r), (0.0, -r) })
                Assert.True(s.InsideTriangle(new ArcPt(c.X + d.Item1, c.Y + d.Item2)), "dot leaves the triangle");
            // the dot is below the bar, with a gap
            Assert.True(c.Y + r < s.Bar.Min(p => p.Y));
        }

        [Fact]
        public void Signal_word_starts_clear_of_the_triangle()
        {
            var s = Sym();
            Assert.Equal(s.Triangle.Max(p => p.X) + Gap, s.TextStartX, 9);
        }

        [Fact]
        public void Polygons_are_counter_clockwise()
        {
            var s = Sym();
            Assert.True(ArcSafetyAlertSymbol.SignedArea2(s.Triangle) > 0);
            Assert.True(ArcSafetyAlertSymbol.SignedArea2(s.Bar) > 0);
        }

        [Fact]
        public void Scales_with_the_header()
        {
            var a = ArcSafetyAlertSymbol.Compute(0, 12, 0);
            var b = ArcSafetyAlertSymbol.Compute(0, 24, 0);
            Assert.Equal(2 * a.DotRadius, b.DotRadius, 9);
            Assert.Equal(2 * a.TextStartX, b.TextStartX, 9);
            Assert.Throws<ArgumentOutOfRangeException>(() => ArcSafetyAlertSymbol.Compute(0, 0, 0));
        }
    }
}
