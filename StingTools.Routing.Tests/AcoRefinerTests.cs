// AcoRefinerTests — deterministic regression tests for the multi-
// objective cost function and AcoConfig defaults. Path-search tests
// stay out of scope for the pure-logic suite because they need the
// full VoxelGrid + obstacle pipeline (live separately in
// VoxelGridTests).

using System.Collections.Generic;
using StingTools.Core.Routing;
using Xunit;

namespace StingTools.Routing.Tests
{
    public class AcoRefinerTests
    {
        // ── AcoConfig defaults — guard rails against accidental tuning. ──

        [Fact]
        public void AcoConfig_DefaultSeedIs1234_ForReproducibility()
        {
            var cfg = new AcoConfig();
            Assert.Equal(1234, cfg.Seed);
        }

        [Fact]
        public void AcoConfig_DefaultWeightsMatchDocumented7Tuple()
        {
            var cfg = new AcoConfig();
            // Documented: length=1.0, bends=2.0, clearance=5.0, system=3.0,
            //             void=1.5, slope=2.5, thermal=1.0
            Assert.Equal(new double[] { 1.0, 2.0, 5.0, 3.0, 1.5, 2.5, 1.0 }, cfg.Weights);
        }

        [Fact]
        public void AcoConfig_DefaultsCheap_ForUnconfiguredCalls()
        {
            var cfg = new AcoConfig();
            Assert.Equal(0.0, cfg.CostPerMetre);
            Assert.Equal(0,   cfg.MaxBends);
            Assert.Null(cfg.ClearanceProbe);
        }

        [Fact]
        public void AcoConfig_HighEvaporationLow_MatchesPheromonePersistence()
        {
            var cfg = new AcoConfig();
            // 15% evaporation per iteration is the published ACO default —
            // strong enough to forget bad paths, gentle enough to retain
            // the seed signal across the 50-iteration window.
            Assert.Equal(0.15, cfg.Evaporation);
            Assert.Equal(50,   cfg.MaxIterations);
            Assert.Equal(16,   cfg.AntCount);
        }

        // ── MultiObjective: degenerate paths return ∞ ────────────────────

        [Fact]
        public void MultiObjective_NullPath_ReturnsInfinity()
        {
            Assert.Equal(double.PositiveInfinity, AcoRefiner.MultiObjective(null, new AcoConfig()));
        }

        [Fact]
        public void MultiObjective_SingleCellPath_ReturnsInfinity()
        {
            var path = new List<VoxelCell> { MakeCell(0, 0, 0) };
            Assert.Equal(double.PositiveInfinity, AcoRefiner.MultiObjective(path, new AcoConfig()));
        }

        // ── MultiObjective: term contributions ──────────────────────────

        [Fact]
        public void MultiObjective_StraightPath_ChargesLengthOnly()
        {
            // Three colinear cells along +X — no bends, no clearance probe.
            var path = new List<VoxelCell>
            {
                MakeCell(0, 0, 0),
                MakeCell(1, 0, 0),
                MakeCell(2, 0, 0),
            };
            var cfg = new AcoConfig();
            double cost = AcoRefiner.MultiObjective(path, cfg);
            // Two unit-cell hops with default w[0]=1.0 — bends term is 0.
            // Length is in feet; cells are 1 ft apart in MakeCell.
            Assert.True(cost > 0 && cost < 5,
                $"Straight 2-hop path should have small cost; got {cost}");
        }

        [Fact]
        public void MultiObjective_LRoute_ChargesBendOverHead()
        {
            // X-then-Y route — one bend.
            var straight = new List<VoxelCell>
            {
                MakeCell(0, 0, 0), MakeCell(1, 0, 0), MakeCell(2, 0, 0)
            };
            var lroute = new List<VoxelCell>
            {
                MakeCell(0, 0, 0), MakeCell(1, 0, 0), MakeCell(1, 1, 0)
            };
            var cfg = new AcoConfig();
            Assert.True(AcoRefiner.MultiObjective(lroute, cfg) >
                        AcoRefiner.MultiObjective(straight, cfg),
                "L-route should cost more than straight via the bend term.");
        }

        [Fact]
        public void MultiObjective_MaxBendsExponentialPenalty()
        {
            // Three 90° bends path → bends=3. With MaxBends=2, the path
            // pays 2^(bends - max) = 2 extra units in the bend term.
            var path = new List<VoxelCell>
            {
                MakeCell(0, 0, 0),
                MakeCell(1, 0, 0),
                MakeCell(1, 1, 0),  // bend 1
                MakeCell(2, 1, 0),  // bend 2
                MakeCell(2, 2, 0),  // bend 3
            };
            double withCap    = AcoRefiner.MultiObjective(path, new AcoConfig { MaxBends = 2 });
            double withoutCap = AcoRefiner.MultiObjective(path, new AcoConfig { MaxBends = 0 });
            Assert.True(withCap > withoutCap,
                $"MaxBends violation should increase cost. with={withCap} without={withoutCap}");
        }

        [Fact]
        public void MultiObjective_ClearanceProbe_NonZeroPenaltiesPath()
        {
            // Probe returns 1 metre violation for every cell — every cell
            // contributes 1² = 1 to the sum. A 3-cell path: 3 violations.
            var path = new List<VoxelCell>
            {
                MakeCell(0, 0, 0), MakeCell(1, 0, 0), MakeCell(2, 0, 0)
            };
            var cfg = new AcoConfig { ClearanceProbe = _ => 1.0 };
            double cost = AcoRefiner.MultiObjective(path, cfg);
            // Term 2 weight is 5.0 by default; with 3 violations × 1² × 5 = 15.
            Assert.True(cost >= 15.0,
                $"3 unit clearance violations × w[2]=5 should add ≥15; got {cost}");
        }

        [Fact]
        public void MultiObjective_CostPerMetre_ConvertsLengthToBudget()
        {
            // Length-only path × $/m should pull the cost up linearly.
            var path = new List<VoxelCell>
            {
                MakeCell(0, 0, 0), MakeCell(1, 0, 0), MakeCell(2, 0, 0)
            };
            var noCost   = new AcoConfig();
            var withCost = new AcoConfig { CostPerMetre = 100.0 };
            Assert.True(AcoRefiner.MultiObjective(path, withCost) >
                        AcoRefiner.MultiObjective(path, noCost),
                "Non-zero CostPerMetre must increase the length term.");
        }

        // ── Helpers ─────────────────────────────────────────────────────

        // VoxelCell takes integer indices + spatial mins; SideFt=1 makes
        // the cell a 1 ft × 1 ft × 1 ft cube anchored at the index. The
        // public API the cost function reads (Ix/Iy/Iz + cell-centre via
        // MinX+SideFt*0.5 etc.) all comes through cleanly.
        private static VoxelCell MakeCell(int ix, int iy, int iz)
            => new VoxelCell(ix, iy, iz, ix, iy, iz, 1.0);
    }
}
