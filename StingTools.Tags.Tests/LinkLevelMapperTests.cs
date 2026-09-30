using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using StingTools.Core.Mep;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-49: MEP modelled in a link counts on the host level at or below it. The plugin calls the same file.</summary>
    public class LinkLevelMapperTests
    {

        [Fact]
        public void A_link_level_maps_to_the_host_level_at_or_below_it()
        {
            var host = new List<(long, double)> { (1, 0), (2, 12), (3, 24) };
            Assert.Equal(2L, LinkLevelMapper.HostLevelFor(12.001, host));
            Assert.Equal(2L, LinkLevelMapper.HostLevelFor(15, host));
            Assert.Equal(1L, LinkLevelMapper.HostLevelFor(-3, host));
            Assert.Null(LinkLevelMapper.HostLevelFor(5, new List<(long, double)>()));
        }
    }
}
