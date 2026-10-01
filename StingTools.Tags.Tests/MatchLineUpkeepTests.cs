using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-46 / DTW-47: stale match lines are redrawn and curves keyed to views that no longer pair are pruned. The plugin calls the same file.</summary>
    public class MatchLineUpkeepTests
    {

        [Fact]
        public void A_moved_boundary_does_not_match_its_old_lines()
        {
            var placed = new List<(double, double, double, double)> { (0, 0, 10, 0) };
            Assert.True(MatchLineUpkeep.SegmentsMatch(placed, new List<(double, double, double, double)> { (10, 0, 0, 0) }, 0.01));
            Assert.False(MatchLineUpkeep.SegmentsMatch(placed, new List<(double, double, double, double)> { (0, 2, 10, 2) }, 0.01));
        }

        [Fact]
        public void A_curve_keyed_to_a_view_that_no_longer_pairs_is_pruned()
        {
            var liveScope = new HashSet<string> { "PAIR" };
            var liveViews = new HashSet<string> { "PAIR:va:vb" };
            Assert.False(MatchLineUpkeep.ShouldPrune("PAIR:va:vb", liveScope, liveViews));
            Assert.False(MatchLineUpkeep.ShouldPrune("PAIR:va:vb:seg2", liveScope, liveViews));
            Assert.True(MatchLineUpkeep.ShouldPrune("PAIR:va:deleted", liveScope, liveViews));
            Assert.True(MatchLineUpkeep.ShouldPrune("GONE:va:vb", liveScope, liveViews));
        }

        [Fact]
        public void A_pair_that_failed_this_run_is_not_pruned()
            => Assert.False(MatchLineUpkeep.ShouldPrune("PAIR:va:x", new HashSet<string> { "PAIR" },
                   new HashSet<string>(), new HashSet<string> { "PAIR" }));
    }
}
