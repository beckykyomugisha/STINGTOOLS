using System.Collections.Generic;
using StingTools.Core.Mep;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-122: "Skip levels with nothing modelled" read only the host for non-MEP plans,
    /// so a federated MEP host with the architecture linked skipped every architectural,
    /// structural and coordination plan. A linked model's occupied levels now count on
    /// the host level at or below them.
    /// </summary>
    public class LinkedModelLevelsTests
    {
        private static readonly List<(long, double)> Host = new List<(long, double)> { (10, 0), (20, 12), (30, 24) };

        [Fact]
        public void Linked_levels_map_onto_the_host_levels_at_or_below_them()
        {
            var set = LinkedModelLevels.HostLevels(new[] { 0.0, 12.0, 13.5 }, Host);
            Assert.Equal(new HashSet<long> { 10, 20 }, set);
        }

        [Fact]
        public void A_federated_mep_host_counts_the_linked_architecture()
        {
            // Host holds MEP only; the linked architecture is on ground and first.
            var linked = LinkedModelLevels.HostLevels(new[] { 0.0, 12.0 }, Host);
            Assert.True(LinkedModelLevels.HasModel(false, 10, linked));
            Assert.True(LinkedModelLevels.HasModel(false, 20, linked));
            Assert.False(LinkedModelLevels.HasModel(false, 30, linked));   // a truly empty level is still skipped
        }

        [Fact]
        public void The_host_alone_still_decides_without_links()
        {
            Assert.True(LinkedModelLevels.HasModel(true, 30, null));
            Assert.False(LinkedModelLevels.HasModel(false, 30, new HashSet<long>()));
        }

        [Fact]
        public void No_host_levels_maps_nothing()
            => Assert.Empty(LinkedModelLevels.HostLevels(new[] { 0.0 }, new List<(long, double)>()));
    }
}
