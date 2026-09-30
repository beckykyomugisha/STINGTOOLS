using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Plumbing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The drainage and supply schematics used to invent content: stacks from the top
    /// quarter of nodes by DFU, a "DN{stack/2} VENT" on every stack, indicative branches,
    /// a 1.25 % slope, floors from Z / 3000 mm, model-mm glyphs on a 1:50 view, and a
    /// supply layout that stacked nodes on one cell. These are the rules that replace them.
    /// </summary>
    public class SchematicLayoutMathTests
    {
        // ── paper scale ───────────────────────────────────────────────────
        [Fact]
        public void Paper_millimetres_are_multiplied_by_the_view_scale()
        {
            // 3 mm on paper at 1:50 is 150 mm of model = 0.4921 ft.
            Assert.Equal(150.0 / 304.8, SchematicLayoutMath.PaperMmToModelFt(3, 50), 6);
            Assert.Equal(3.0 / 304.8, SchematicLayoutMath.PaperMmToModelFt(3, 1), 6);
            Assert.Equal(3.0 / 304.8, SchematicLayoutMath.PaperMmToModelFt(3, 0), 6);  // bad scale → 1:1, never 0
        }

        // ── text type choice ──────────────────────────────────────────────
        [Fact]
        public void Closest_text_height_is_chosen_not_the_first()
        {
            var heights = new List<double> { 10.0, 3.5, 2.4, double.NaN };
            Assert.Equal(2, SchematicLayoutMath.ClosestIndex(heights, 2.5));
            Assert.Equal(1, SchematicLayoutMath.ClosestIndex(heights, 3.4));
            Assert.Equal(-1, SchematicLayoutMath.ClosestIndex(new List<double>(), 2.5));
            Assert.Equal(-1, SchematicLayoutMath.ClosestIndex(new List<double> { double.NaN }, 2.5));
        }

        // ── system filter ─────────────────────────────────────────────────
        [Fact]
        public void System_filter_matches_the_whole_name_only()
        {
            Assert.True(SchematicLayoutMath.SystemNameMatches("Sanitary 1", "sanitary 1"));
            Assert.True(SchematicLayoutMath.SystemNameMatches(" Sanitary 1 ", "Sanitary 1"));
            Assert.False(SchematicLayoutMath.SystemNameMatches("Sanitary 10", "Sanitary 1"));
            Assert.False(SchematicLayoutMath.SystemNameMatches(null, "Sanitary 1"));
            Assert.True(SchematicLayoutMath.SystemNameMatches("anything", ""));
        }

        // ── levels ────────────────────────────────────────────────────────
        private static readonly List<double> Levels = new List<double> { 0.0, 10.0, 20.0, 30.0 };

        [Fact]
        public void Level_at_or_below_an_elevation()
        {
            Assert.Equal(1, SchematicLayoutMath.IndexAtOrBelow(Levels, 15.0, 0.1));
            Assert.Equal(2, SchematicLayoutMath.IndexAtOrBelow(Levels, 19.95, 0.1));   // within tolerance
            Assert.Equal(3, SchematicLayoutMath.IndexAtOrBelow(Levels, 99.0, 0.1));
            Assert.Equal(-1, SchematicLayoutMath.IndexAtOrBelow(Levels, -5.0, 0.1));   // below every level: none, not invented
        }

        [Fact]
        public void Levels_a_stack_passes_come_from_the_document_not_a_storey_height()
        {
            // A stack from 3 ft to 25 ft passes the ground level (at/below its foot), 10 and 20.
            Assert.Equal(new[] { 0, 1, 2 }, SchematicLayoutMath.LevelsSpanning(Levels, 3.0, 25.0, 0.1));
            // A short stack between two levels still names the one it stands on.
            Assert.Equal(new[] { 1 }, SchematicLayoutMath.LevelsSpanning(Levels, 12.0, 18.0, 0.1));
            // No levels: nothing, never a synthetic "L01".
            Assert.Empty(SchematicLayoutMath.LevelsSpanning(new List<double>(), 0, 30, 0.1));
            // Wholly below the lowest level.
            Assert.Empty(SchematicLayoutMath.LevelsSpanning(Levels, -20.0, -10.0, 0.1));
        }

        // ── stack grouping ────────────────────────────────────────────────
        [Fact]
        public void Per_storey_stack_segments_at_one_plan_position_are_one_stack()
        {
            var segs = new List<(long, double, double)>
            {
                (1, 0.0, 0.0), (2, 0.1, 0.0), (3, 20.0, 5.0), (4, 0.0, 0.2)
            };
            var groups = SchematicLayoutMath.ClusterByPlanPosition(segs, 0.5);
            Assert.Equal(2, groups.Count);
            Assert.Equal(new long[] { 1, 2, 4 }, groups[0]);
            Assert.Equal(new long[] { 3 }, groups[1]);
        }

        // ── vents ─────────────────────────────────────────────────────────
        // Graph: stack 1 — fitting 10 — vent 20 (DN 75)
        //        stack 1 — fitting 11 — drain 30 — vent 40 (a branch vent, off the branch)
        private static readonly Dictionary<long, long[]> Adj = new Dictionary<long, long[]>
        {
            [1]  = new long[] { 10, 11 },
            [10] = new long[] { 1, 20 },
            [20] = new long[] { 10 },
            [11] = new long[] { 1, 30 },
            [30] = new long[] { 11, 40, 50 },
            [40] = new long[] { 30 },
            [50] = new long[] { 30 },
        };
        private static readonly Dictionary<long, SchematicPipeKind> Kinds = new Dictionary<long, SchematicPipeKind>
        {
            [1] = SchematicPipeKind.DrainPipe, [10] = SchematicPipeKind.Fitting, [20] = SchematicPipeKind.VentPipe,
            [11] = SchematicPipeKind.Fitting, [30] = SchematicPipeKind.DrainPipe,
            [40] = SchematicPipeKind.VentPipe, [50] = SchematicPipeKind.Fixture,
        };
        private static IEnumerable<long> N(long id) => Adj.TryGetValue(id, out var a) ? a : new long[0];
        private static SchematicPipeKind K(long id) => Kinds.TryGetValue(id, out var k) ? k : SchematicPipeKind.Other;

        [Fact]
        public void A_vent_is_drawn_only_where_a_vent_pipe_connects_with_its_real_DN()
        {
            var hit = SchematicGraphRules.FindStackVent(new long[] { 1 }, N, K, id => id == 20 ? 75 : 50);
            Assert.NotNull(hit);
            Assert.Equal(new long[] { 20 }, hit.VentIds);   // the branch vent 40 is behind a drain pipe: not the stack's
            Assert.Equal(75, hit.DnMm);
        }

        [Fact]
        public void A_stack_with_no_vent_pipe_gets_no_vent()
        {
            var noVent = new Dictionary<long, SchematicPipeKind>(Kinds) { [20] = SchematicPipeKind.DrainPipe };
            var hit = SchematicGraphRules.FindStackVent(new long[] { 1 }, N,
                id => noVent.TryGetValue(id, out var k) ? k : SchematicPipeKind.Other, id => 100);
            Assert.Null(hit);
        }

        [Fact]
        public void Branch_fixture_count_is_what_the_branch_actually_serves()
        {
            // From drain 30 with the stack and its junction blocked: fixture 50 only.
            Assert.Equal(1, SchematicGraphRules.CountFixtures(30, new HashSet<long> { 1, 11 }, N, K));
            // A branch reaching nothing counts nothing (no "indicative" fixtures).
            Assert.Equal(0, SchematicGraphRules.CountFixtures(20, new HashSet<long> { 1, 10 }, N, K));
        }

        // ── layout cells ──────────────────────────────────────────────────
        [Fact]
        public void Two_nodes_never_share_a_cell()
        {
            var grid = new SchematicCellGrid();
            Assert.Equal(0, grid.Claim(3, 0));
            Assert.Equal(1, grid.Claim(3, 0));    // second claim on (3,0) shifts right
            Assert.Equal(-1, grid.Claim(3, 0));   // then left
            Assert.Equal(0, grid.Claim(4, 0));    // another row is independent
            var claimed = new[] { grid.IsOccupied(3, 0), grid.IsOccupied(3, 1), grid.IsOccupied(3, -1) };
            Assert.All(claimed, Assert.True);
        }

        // ── supply source + pressure honesty ──────────────────────────────
        [Fact]
        public void A_modelled_meter_is_the_source_before_equipment_and_a_plain_node_is_none()
        {
            Assert.True(SchematicLayoutMath.SupplySourceRank("MTR", false) < SchematicLayoutMath.SupplySourceRank("TK", true));
            Assert.True(SchematicLayoutMath.SupplySourceRank("PMP", true) < SchematicLayoutMath.SupplySourceRank(null, true));
            Assert.Equal(int.MaxValue, SchematicLayoutMath.SupplySourceRank("FX", false));
            Assert.Equal(int.MaxValue, SchematicLayoutMath.SupplySourceRank(null, false));
        }

        [Fact]
        public void Pressure_is_shown_only_when_configured_and_flagged_when_the_source_is_assumed()
        {
            Assert.Null(SchematicLayoutMath.PressureLabel(250, inletPressureConfigured: false, sourceAssumed: false));
            Assert.Equal("250 kPa", SchematicLayoutMath.PressureLabel(250, true, false));
            Assert.Equal("250 kPa (indicative)", SchematicLayoutMath.PressureLabel(250, true, true));
            Assert.Null(SchematicLayoutMath.PressureLabel(0, true, false));
        }

        [Fact]
        public void A_missing_slope_is_shown_as_unknown_never_a_default()
        {
            Assert.Equal("slope ?", SchematicLayoutMath.SlopeLabel(null));
            Assert.Equal("× 2.5%", SchematicLayoutMath.SlopeLabel(2.5));
            Assert.Equal("× 0%", SchematicLayoutMath.SlopeLabel(0));
        }
    }
}
