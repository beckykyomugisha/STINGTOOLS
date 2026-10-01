using System;
using System.IO;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-74. The Setup wizard's "two building sections per scope box" made unstamped
    /// sections with ViewSection.CreateSection, so no drawing-type command saw them and a
    /// re-run made more. It now produces both through DrawingProducer: the long-axis cut
    /// the producer already takes from a box (DTW-52, <see cref="SectionFromBox.Frame"/>)
    /// and the perpendicular one (<see cref="SectionFromBox.CrossFrame"/>).
    /// </summary>
    public class ScopeBoxTwoSectionsTests
    {
        [Fact]
        public void Cross_cut_is_perpendicular_to_the_long_cut_through_the_same_centre()
        {
            foreach (var angle in new[] { 0.0, Math.PI / 6, -Math.PI / 3 })
            foreach (var (w, d) in new[] { (40.0, 10.0), (10.0, 40.0) })
            {
                var a = SectionFromBox.Frame(12, -7, w, d, angle);
                var b = SectionFromBox.CrossFrame(12, -7, w, d, angle);
                Assert.Equal(a.OriginX, b.OriginX, 6); Assert.Equal(a.OriginY, b.OriginY, 6);
                Assert.Equal(0, a.DirX * b.DirX + a.DirY * b.DirY, 6);        // perpendicular
                Assert.Equal(1, b.DirX * b.DirX + b.DirY * b.DirY, 6);        // unit
                Assert.Equal(a.Depth, b.HalfWidth, 6);                         // short side / 2
                Assert.Equal(a.HalfWidth, b.Depth, 6);                         // long side / 2
            }
        }

        [Fact]
        public void Cross_cut_of_a_wide_box_runs_across_it()
        {
            var b = SectionFromBox.CrossFrame(0, 0, 40, 10, 0);
            Assert.Equal(0, b.DirX, 6); Assert.Equal(1, Math.Abs(b.DirY), 6);
            Assert.Equal(5, b.HalfWidth, 6);
            Assert.Equal(20, b.Depth, 6);
        }

        [Fact]
        public void A_box_with_no_extent_gives_no_cross_cut()
        {
            Assert.Null(SectionFromBox.CrossFrame(0, 0, 0, 10, 0));
            Assert.Null(SectionFromBox.CrossFrame(0, 0, 10, 0, 0));
        }

        [Fact]
        public void Wizard_produces_scope_box_sections_through_the_producer()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Temp", "ProjectSetupCommand.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "repo root");
            var src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Temp", "ProjectSetupCommand.cs"));
            Assert.DoesNotContain("ViewSection.CreateSection", src);
            Assert.Contains("ProduceScopeBoxSections(", src);
            Assert.Contains("DrawingProducer.BuildCrossSectionBoxFromScopeBox(", src);
        }
    }
}
