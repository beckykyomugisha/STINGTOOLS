using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-52: a Section rule produced for a scope box is cut from that box, and section types can be served by area boxes. The plugin calls the same file.</summary>
    public class SectionFromBoxTests
    {

        [Fact]
        public void The_section_cuts_through_the_centre_along_the_long_side()
        {
            var f = SectionFromBox.Frame(100, 50, 40, 10, 0);
            Assert.Equal(100, f.OriginX, 6); Assert.Equal(50, f.OriginY, 6);
            Assert.Equal(1, f.DirX, 6); Assert.Equal(0, f.DirY, 6);
            Assert.Equal(20, f.HalfWidth, 6);
            Assert.Equal(5, f.Depth, 6);
        }

        [Fact]
        public void A_deep_turned_box_is_cut_across_its_measured_axis()
        {
            var f = SectionFromBox.Frame(0, 0, 10, 40, Math.PI / 6);
            Assert.Equal(-Math.Sin(Math.PI / 6), f.DirX, 6);
            Assert.Equal(Math.Cos(Math.PI / 6), f.DirY, 6);
            Assert.Equal(20, f.HalfWidth, 6);
            Assert.Equal(5, f.Depth, 6);
        }

        [Fact]
        public void The_band_is_the_storey_clipped_to_the_box_or_the_box_without_a_level()
        {
            Assert.Equal((0.0, 30.0), SectionFromBox.Band(0, 30, null, null, 3, 13));
            var (b, t) = SectionFromBox.Band(-5, 100, 10, 23, 3, 13);
            Assert.Equal(7, b, 6); Assert.Equal(23, t, 6);
        }

        [Fact]
        public void A_section_type_cropped_by_scope_box_is_an_area_candidate()
        {
            var dt = new DrawingType
            {
                Id = "mep-section", Purpose = "Section", Scale = 50, PaperSize = "A1", Orientation = "Landscape",
                Crop = new DrawingCropStrategy { Kind = "ScopeBoxOrBbox" },
                Slots = new List<DrawingSlot> { new DrawingSlot { ViewType = "Section", NormW = 0.9, NormH = 0.8, Required = true } },
            };
            Assert.True(ScopeBoxSizing.IsAreaCandidate(dt, out var why), why);
            var dr = new Dictionary<string, (double W, double H)> { { "A1|Landscape", (821, 474) } };
            Assert.True(ScopeBoxSizing.TryMaxExtent(dt, dr, 0.9, out var w, out var d, out why), why);
            Assert.Equal(w, d, 6);
        }
    }
}
