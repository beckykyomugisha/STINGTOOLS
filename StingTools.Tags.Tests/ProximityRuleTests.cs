using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>TAGACC-21: executable coverage for the TAGACC-11 proximity rules.</summary>
    public class ProximityRuleTests
    {
        [Theory]
        [InlineData(10, 10, 30.0, true)]    // same level: height ignored
        [InlineData(10, 11, 0.0, false)]    // different levels: never, even at the same height
        [InlineData(0, 11, 2.0, true)]      // one has no level: within 5 ft
        [InlineData(10, 0, -4.9, true)]
        [InlineData(0, 0, 5.0, true)]       // boundary
        [InlineData(0, 0, 5.1, false)]      // floor above
        [InlineData(-1, 0, -12.0, false)]   // invalid id treated as no level
        public void Same_floor(long el, long cand, double dz, bool expected)
            => Assert.Equal(expected, ProximityRule.SameFloor(el, cand, dz));

        [Theory]
        [InlineData("Room", true)]
        [InlineData("TYPE_OVERRIDE", true)]
        [InlineData("Workset", true)]
        [InlineData("ScopeBox", true)]       // STING-LOC:: box is detection
        [InlineData("ProjectInfo", false)]   // one value for the whole model
        [InlineData("Proximity", false)]     // a copy of a copy
        [InlineData("Default", false)]
        [InlineData("", true)]               // tagged before sources were recorded
        [InlineData(null, true)]
        [InlineData("Banana", false)]        // unknown source: not detection
        public void Loc_copyable(string source, bool expected)
            => Assert.Equal(expected, ProximityRule.LocIsCopyable(source));

        [Theory]
        [InlineData("Room", true)]
        [InlineData("ScopeBox", true)]       // STING-ZONE:: box
        [InlineData("TYPE_OVERRIDE", true)]
        [InlineData("Proximity", false)]
        [InlineData("Default", false)]
        [InlineData("", true)]
        public void Zone_copyable(string source, bool expected)
            => Assert.Equal(expected, ProximityRule.ZoneIsCopyable(source));

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        [InlineData(7, false)]
        public void Sys_copyable(int layer, bool expected)
            => Assert.Equal(expected, ProximityRule.SysIsCopyable(layer));

        /// <summary>
        /// Proximity and the Token Confidence Audit read one vocabulary: a source the audit
        /// calls High is copyable, one it calls Medium or Low is not (blank aside).
        /// </summary>
        [Theory]
        [InlineData("TYPE_OVERRIDE")]
        [InlineData("Room")]
        [InlineData("Workset")]
        [InlineData("ScopeBox")]
        [InlineData("ProjectInfo")]
        [InlineData("Proximity")]
        [InlineData("Default")]
        public void Copyable_means_high_confidence(string source)
        {
            Assert.Equal(TokenConfidenceBands.ClassifyLoc(source).Band == ConfidenceBand.High, ProximityRule.LocIsCopyable(source));
            Assert.Equal(TokenConfidenceBands.ClassifyZone(source).Band == ConfidenceBand.High, ProximityRule.ZoneIsCopyable(source));
        }
    }
}
