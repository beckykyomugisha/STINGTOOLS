using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// STING-ZONE::&lt;code&gt; — the ZONE twin of STING-LOC::&lt;code&gt;. The code is
    /// written into the ZONE token and so into the tag, so it takes the grammar's
    /// segment rule (A-Z 0-9 . _ -): no spaces, no second "::".
    /// </summary>
    public class ScopeBoxZoneNameTests
    {
        [Theory]
        [InlineData("STING-ZONE::Z01")]
        [InlineData("sting-zone::z02")]
        [InlineData("STING-ZONE::")]
        [InlineData("STING-ZONE::Wing A")]
        public void ZonePrefixClassifiesAsZone(string name)
            => Assert.Equal(ScopeBoxKind.Zone, ScopeBoxNames.Classify(name));

        [Fact]
        public void OtherPrefixesAreNotZones()
        {
            Assert.Equal(ScopeBoxKind.Building, ScopeBoxNames.Classify("STING-LOC::BLD1"));
            Assert.Equal(ScopeBoxKind.Area, ScopeBoxNames.Classify("STING-AREA::A01"));
            Assert.Equal(ScopeBoxKind.DrawingType, ScopeBoxNames.Classify("STING::arch-plan-A1-1to100"));
            Assert.Equal(ScopeBoxKind.Plain, ScopeBoxNames.Classify("ZONE Z01"));
        }

        [Theory]
        [InlineData("STING-ZONE::Z01", "Z01")]
        [InlineData("STING-ZONE::WING-A", "WING-A")]
        [InlineData("STING-ZONE::North_1.2", "North_1.2")]
        [InlineData("sting-zone::z03", "z03")]
        [InlineData("STING-ZONE:: Z04 ", "Z04")]   // trimmed, as the LOC index trims
        public void ValidZoneNamesParse(string name, string expected)
        {
            Assert.True(ScopeBoxNames.TryParseZone(name, out var zone, out var reason), reason);
            Assert.Equal(expected, zone);
            Assert.Null(reason);
        }

        [Theory]
        [InlineData("STING-ZONE::")]
        [InlineData("STING-ZONE::   ")]
        [InlineData("STING-ZONE::Wing A")]
        [InlineData("STING-ZONE::Z01::L02")]
        [InlineData("STING-ZONE::Z/01")]
        [InlineData("STING-ZONE::Zöne")]
        public void MalformedZoneNamesAreRejectedWithAReason(string name)
        {
            Assert.False(ScopeBoxNames.TryParseZone(name, out var zone, out var reason));
            Assert.Null(zone);
            Assert.Equal(ScopeBoxNames.ZonePatternReason, reason);
        }

        [Theory]
        [InlineData("STING-LOC::BLD1")]
        [InlineData("Scope Box 1")]
        [InlineData("")]
        [InlineData(null)]
        public void NonZoneNamesAreNotZonesAndCarryNoReason(string name)
        {
            Assert.False(ScopeBoxNames.TryParseZone(name, out var zone, out var reason));
            Assert.Null(zone);
            Assert.Null(reason);
        }

        [Fact]
        public void ComposeZoneRoundTrips()
        {
            Assert.Equal("STING-ZONE::Z01", ScopeBoxNames.ComposeZone("Z01"));
            Assert.True(ScopeBoxNames.TryParseZone(ScopeBoxNames.ComposeZone("WING-B"), out var z, out _));
            Assert.Equal("WING-B", z);
            Assert.Equal(string.Empty, ScopeBoxNames.ComposeZone("Wing B"));
            Assert.Equal(string.Empty, ScopeBoxNames.ComposeZone(""));
        }

        /// <summary>Every kind is reachable from a name — a new kind added to the enum
        /// without a prefix in Classify fails here instead of reading as Plain.</summary>
        [Fact]
        public void EveryKindIsProducedByClassify()
        {
            var samples = new[]
            {
                "Scope Box 1", "STING::x", "STING-LOC::B1", "STING-AREA::A1", "STING-SEED::10x10", "STING-ZONE::Z1",
            };
            var produced = new HashSet<ScopeBoxKind>(samples.Select(ScopeBoxNames.Classify));
            foreach (ScopeBoxKind k in Enum.GetValues(typeof(ScopeBoxKind)))
                Assert.Contains(k, produced);
        }
    }
}
