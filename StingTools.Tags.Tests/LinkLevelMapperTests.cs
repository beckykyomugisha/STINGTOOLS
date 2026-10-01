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

        private const double Mm = 1.0 / 304.8;

        // DTW-115: an MEP model's SSL level sits 50-150 mm below the architect's FFL. At-or-below
        // put it on the storey beneath, so its plant was counted on the wrong floor.
        [Theory]
        [InlineData(-50)]
        [InlineData(-150)]
        [InlineData(-299)]
        [InlineData(80)]
        public void A_link_level_just_off_a_host_level_maps_to_that_level(double offsetMm)
        {
            var host = new List<(long, double)> { (1, 0), (2, 3600 * Mm), (3, 7200 * Mm) };
            Assert.Equal(2L, LinkLevelMapper.HostLevelFor(3600 * Mm + offsetMm * Mm, host));
        }

        [Fact]
        public void Outside_the_band_the_level_at_or_below_still_wins()
        {
            var host = new List<(long, double)> { (1, 0), (2, 3600 * Mm), (3, 7200 * Mm) };
            Assert.Equal(1L, LinkLevelMapper.HostLevelFor(3600 * Mm - 400 * Mm, host));   // a mezzanine below L2
            Assert.Equal(2L, LinkLevelMapper.HostLevelFor(3600 * Mm + 1200 * Mm, host));
        }

        [Fact]
        public void The_band_never_exceeds_half_the_local_storey()
        {
            // Levels 400 mm apart (a plinth and a ground floor): a link level 190 mm under the
            // upper one is within half that storey (200 mm) and maps up; 210 mm under is not.
            var host = new List<(long, double)> { (1, 0), (2, 400 * Mm) };
            Assert.Equal(2L, LinkLevelMapper.HostLevelFor(400 * Mm - 190 * Mm, host));
            Assert.Equal(1L, LinkLevelMapper.HostLevelFor(400 * Mm - 210 * Mm, host));
        }
    }
}
