using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing.Dimensioning;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-84: the MEP run chain clustered pipes by pipe-to-pipe adjacency, but
    /// pipes meet through fittings — no run ever had two pipes, so the chain placed
    /// nothing. It also referenced the pipes' own centrelines (parallel to its
    /// witness line, which Revit rejects). These pin the Revit-free planning: runs
    /// walk through fittings but not through equipment, a chain is one line of
    /// collinear straights, and coincident stops collapse to one.
    /// </summary>
    public class MepRunPlanningTests
    {
        // pipe 1 — elbow 100 — pipe 2 — tee 101 — pipe 3 ; tee 101 — pipe 4 — AHU 200 — pipe 5
        private static readonly Dictionary<long, long[]> Graph = new Dictionary<long, long[]>
        {
            [1] = new long[] { 100 },
            [100] = new long[] { 1, 2 },
            [2] = new long[] { 100, 101 },
            [101] = new long[] { 2, 3, 4 },
            [3] = new long[] { 101 },
            [4] = new long[] { 101, 200 },
            [200] = new long[] { 4, 5 },
            [5] = new long[] { 200 },
        };

        private static IEnumerable<long> Nb(long id) => Graph.TryGetValue(id, out var n) ? n : new long[0];
        private static bool IsFitting(long id) => id >= 100 && id < 200;

        [Fact]
        public void Pipes_joined_by_fittings_form_one_run()
        {
            var runs = MepRunPlanning.Cluster(new long[] { 1, 2, 3, 4, 5 }, Nb, IsFitting);
            var main = runs.Single(r => r.Curves.Contains(1));
            Assert.Equal(new long[] { 1, 2, 3, 4 }, main.Curves);
            Assert.Equal(new long[] { 100, 101 }, main.Joints);
        }

        [Fact]
        public void A_run_does_not_leak_through_equipment()
        {
            var runs = MepRunPlanning.Cluster(new long[] { 1, 2, 3, 4, 5 }, Nb, IsFitting);
            Assert.Equal(2, runs.Count);
            Assert.Equal(new long[] { 5 }, runs.Single(r => r.Curves.Contains(5)).Curves);
        }

        [Fact]
        public void Collinear_straights_share_a_chain_parallel_offset_ones_do_not()
        {
            var segs = new List<MepSeg>
            {
                new MepSeg(1, 0, 0, 10, 0),
                new MepSeg(2, 10.5, 0, 20, 0),     // same line, past a coupling
                new MepSeg(3, 30, 0, 20.5, 0),     // same line, drawn backwards
                new MepSeg(4, 0, 3, 10, 3),        // parallel, 3 ft over
                new MepSeg(5, 20, 0, 20, 10),      // branch at right angles
                new MepSeg(6, 5, 5, 5, 5),         // riser seen end-on
            };
            var groups = MepRunPlanning.CollinearGroups(segs);
            Assert.Equal(3, groups.Count);
            Assert.Contains(groups, g => g.OrderBy(x => x).SequenceEqual(new long[] { 1, 2, 3 }));
            Assert.Contains(groups, g => g.SequenceEqual(new long[] { 4 }));
            Assert.Contains(groups, g => g.SequenceEqual(new long[] { 5 }));
        }

        [Fact]
        public void Collinear_grouping_holds_on_a_rotated_run()
        {
            // Two pieces of one 30-degree line, and a parallel one beside it.
            double c = System.Math.Cos(System.Math.PI / 6), s = System.Math.Sin(System.Math.PI / 6);
            var segs = new List<MepSeg>
            {
                new MepSeg(1, 0, 0, 10 * c, 10 * s),
                new MepSeg(2, 11 * c, 11 * s, 20 * c, 20 * s),
                new MepSeg(3, -2 * s, 2 * c, 10 * c - 2 * s, 10 * s + 2 * c),
            };
            var groups = MepRunPlanning.CollinearGroups(segs);
            Assert.Equal(2, groups.Count);
            Assert.Contains(groups, g => g.SequenceEqual(new long[] { 1, 2 }));
        }

        [Fact]
        public void Stops_at_one_position_merge_and_the_preferred_one_is_kept()
        {
            // 0: fitting centre at 10.000, 1: pipe end at 10.001 (same point), 2: free end at 0,
            // 3: fitting centre at 20.
            var kept = MepRunPlanning.ChainStops(new[] { 10.0, 10.001, 0.0, 20.0 });
            Assert.Equal(new[] { 2, 0, 3 }, kept);
        }

        [Fact]
        public void A_line_with_one_position_has_one_stop()
        {
            Assert.Single(MepRunPlanning.ChainStops(new[] { 4.0, 4.0, 4.0 }));
            Assert.Empty(MepRunPlanning.ChainStops(new double[0]));
        }

        // ── DTW-102: a linked line whose references Revit will not carry through the link ──

        [Fact]
        public void A_linked_line_that_loses_its_stops_to_link_references_is_a_fallback_not_too_short()
        {
            Assert.Equal(MepLineOutcome.Dimension, MepRunPlanning.LineOutcome(positions: 3, keptWithRefs: 3));
            // A position without a link reference would leave a fitting out of the chain:
            // a chain that silently skips a fitting is not placed.
            Assert.Equal(MepLineOutcome.LinkReferencesRefused, MepRunPlanning.LineOutcome(3, 2));
            // Enough points on the line, too few once the link refused references.
            Assert.Equal(MepLineOutcome.LinkReferencesRefused, MepRunPlanning.LineOutcome(3, 1));
            Assert.Equal(MepLineOutcome.LinkReferencesRefused, MepRunPlanning.LineOutcome(2, 0));
            // Too few points to begin with: the geometry's fault, not the link's.
            Assert.Equal(MepLineOutcome.TooShort, MepRunPlanning.LineOutcome(1, 1));
            Assert.Equal(MepLineOutcome.TooShort, MepRunPlanning.LineOutcome(0, 0));
        }

        [Fact]
        public void The_linked_run_report_counts_every_line_not_dimensioned()
        {
            var t = new LinkedMepTally();
            Assert.Null(t.Warning("MEP chain dim"));          // nothing linked: nothing said
            t.Placed(); t.Placed();
            Assert.Null(t.Warning("MEP chain dim"));          // all placed: the count is logged, not warned
            t.RefRefused("Link 'M': pipe 12 end reference");
            t.DimRefused("Link 'M': NewDimension: invalid reference");
            t.RefRefused("Link 'M': pipe 13");
            var w = t.Warning("MEP chain dim");
            Assert.Contains("2 linked", w);
            Assert.Contains("3 linked", w);
            Assert.Contains("pipe 12 end reference", w);
            Assert.Equal(2, t.DimensionedCount);
            Assert.Equal(3, t.NotDimensionedCount);
        }
    }
}
