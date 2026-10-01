using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Plumbing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-120: the drainage riser drew true elevation at a pinned 1:50 and the supply
    /// schematic gave every element its own column, so large models overflowed the sheet.
    /// </summary>
    public class SchematicFitTests
    {
        private static readonly List<double> Levels = new List<double> { 0.0, 10.0, 22.0, 30.0 };

        [Fact]
        public void Storey_rows_have_a_fixed_pitch_whatever_the_storey_height()
        {
            Assert.Equal(0.0, SchematicFit.StoreyRow(Levels, 0.0, 10), 6);
            Assert.Equal(1.0, SchematicFit.StoreyRow(Levels, 10.0, 10), 6);
            Assert.Equal(2.0, SchematicFit.StoreyRow(Levels, 22.0, 10), 6);   // a 12 ft storey is still one row
            Assert.Equal(1.5, SchematicFit.StoreyRow(Levels, 16.0, 10), 6);   // half way up storey 1
            Assert.Equal(-0.5, SchematicFit.StoreyRow(Levels, -5.0, 10), 6);  // below: the first storey's height
            Assert.Equal(3.25, SchematicFit.StoreyRow(Levels, 32.0, 10), 6);  // above: the last storey's height
        }

        [Fact]
        public void Coincident_levels_do_not_divide_by_zero()
        {
            var lv = new List<double> { 0.0, 10.0, 10.0, 20.0 };
            Assert.Equal(1.5, SchematicFit.StoreyRow(lv, 15.0, 10), 6);
            Assert.Equal(0.5, SchematicFit.StoreyRow(new List<double> { 0.0 }, 5.0, 10), 6);
            Assert.Equal(0.5, SchematicFit.StoreyRow(new List<double>(), 5.0, 10), 6);
        }

        [Fact]
        public void The_smallest_scale_that_fits_the_slot_is_chosen()
        {
            // 20 storeys at 3000 mm: 60 mm a storey at 1:50 (1200 mm), 30 mm at 1:100 (600 mm).
            Func<int, (double, double)> ext = s => (300, 20 * Math.Max(3000.0 / s, 16));
            var fit = SchematicFit.ChooseScale(ext, 700, 680, 50);
            Assert.True(fit.Fits);
            Assert.Equal(100, fit.Scale);
            Assert.Null(fit.Problem());
            // Small model: the drawing type's own scale.
            Assert.Equal(50, SchematicFit.ChooseScale(s => (100, 100), 700, 680, 50).Scale);
        }

        [Fact]
        public void A_drawing_that_cannot_fit_is_reported_not_squeezed_past_readable()
        {
            // 60 storeys: the storey pitch stops at 16 mm (960 mm) and never fits 680 mm.
            Func<int, (double, double)> ext = s => (300, 60 * Math.Max(3000.0 / s, 16));
            var fit = SchematicFit.ChooseScale(ext, 700, 680, 50);
            Assert.False(fit.Fits);
            Assert.Equal(200, fit.Scale);   // the first scale at the least overflow — not 1:1000
            Assert.Contains("overflows", fit.Problem());
        }

        [Fact]
        public void An_unknown_slot_keeps_the_minimum_scale_and_says_so()
        {
            var fit = SchematicFit.ChooseScale(s => (5000, 5000), 0, 0, 50);
            Assert.Equal(50, fit.Scale);
            Assert.False(fit.SlotKnown);
            Assert.NotNull(fit.Problem());
        }

        [Fact]
        public void Text_size_is_estimated_from_its_longest_line()
        {
            Assert.Equal(5 * 2.5 * 0.7, SchematicFit.EstimateTextWidthMm("DN100\nAB", 2.5), 6);
            Assert.Equal(5 * 2.5 * 0.7 * 0.8, SchematicFit.EstimateTextWidthMm("DN100", 2.5, 0.8), 6);
            Assert.Equal(2 * 3.5 * 1.5, SchematicFit.EstimateTextHeightMm("a\nb", 3.5), 6);
            Assert.Equal(0, SchematicFit.EstimateTextWidthMm("", 2.5));
        }

        // meter 1 — p2 — f3 — p4 — f5 — p6 — tee 7 — p8 — fixture 9
        //                                        \— p10 — fixture 11
        private static readonly Dictionary<long, long[]> Net = new Dictionary<long, long[]>
        {
            [1] = new long[] { 2 }, [2] = new long[] { 1, 3 }, [3] = new long[] { 2, 4 }, [4] = new long[] { 3, 5 },
            [5] = new long[] { 4, 6 }, [6] = new long[] { 5, 7 }, [7] = new long[] { 6, 8, 10 },
            [8] = new long[] { 7, 9 }, [9] = new long[] { 8 }, [10] = new long[] { 7, 11 }, [11] = new long[] { 10 },
        };
        private static IEnumerable<long> N(long id) => Net.TryGetValue(id, out var a) ? a : new long[0];
        private static bool Keep(long id) => N(id).Count() != 2;

        [Fact]
        public void A_run_of_pass_through_pipe_collapses_to_one_segment()
        {
            var segs = SchematicFit.CollapseChains(1, N, Keep);
            Assert.Equal(3, segs.Count);
            var main = segs.Single(s => s.From == 1);
            Assert.Equal(7, main.To);
            Assert.Equal(new long[] { 2, 3, 4, 5, 6 }, main.Through);
            Assert.Contains(segs, s => s.From == 7 && s.To == 9 && s.Through.SequenceEqual(new long[] { 8 }));
            Assert.Contains(segs, s => s.From == 7 && s.To == 11);
        }

        [Fact]
        public void A_horizontal_run_takes_one_column_not_one_per_element()
        {
            // Every node on the same floor (row 0): the old layout gave each of the 11 its own column.
            var cells = SchematicFit.LayoutTree(1, SchematicFit.CollapseChains(1, N, Keep), id => 0);
            Assert.Equal(new long[] { 1, 7, 9, 11 }, cells.Keys.OrderBy(k => k));
            Assert.Equal(cells.Count, cells.Values.Distinct().Count());   // no shared cell
        }

        [Fact]
        public void A_loop_of_pass_through_nodes_does_not_hang()
        {
            // 1 — 2 (tee) — 3 — 4 — 5 — back to 2
            var ring = new Dictionary<long, long[]>
            {
                [1] = new long[] { 2 }, [2] = new long[] { 1, 3, 5 }, [3] = new long[] { 2, 4 },
                [4] = new long[] { 3, 5 }, [5] = new long[] { 4, 2 },
            };
            IEnumerable<long> R(long id) => ring.TryGetValue(id, out var a) ? a : new long[0];
            var segs = SchematicFit.CollapseChains(1, R, id => R(id).Count() != 2);
            Assert.Contains(segs, s => s.From == 1 && s.To == 2);
            Assert.All(segs, s => Assert.NotEqual(s.From, s.To));
        }
    }
}
