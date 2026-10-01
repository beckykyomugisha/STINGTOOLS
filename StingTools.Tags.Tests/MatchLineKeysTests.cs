using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-56: match-line keys moved from shared parameters (which Revit 2025 will not
    /// bind to 'Lines' — AllowsBoundParameters = False) to Extensible Storage. These pin
    /// which store wins and how a stamped key is parsed and compared.
    /// </summary>
    public class MatchLineKeysTests
    {
        private const string Pair = "3f2a:viewA-uid:viewB-uid";

        private static MatchLineKeys Es(string g, string r, string d)
            => new MatchLineKeys(g, r, d, MatchLineKeySource.ExtensibleStorage);
        private static MatchLineKeys Params(string g, string r, string d)
            => new MatchLineKeys(g, r, d, MatchLineKeySource.SharedParameters);

        [Fact]
        public void Storage_wins_when_present()
        {
            var k = MatchLineKeyRules.Resolve(Es(Pair, "A-101", "vertical"), Params("old:x:y", "A-999", "horizontal"));
            Assert.Equal(MatchLineKeySource.ExtensibleStorage, k.Source);
            Assert.Equal(Pair, k.PairGuid);
            Assert.Equal("A-101", k.Ref);
            Assert.Equal("vertical", k.Direction);
        }

        [Fact]
        public void Storage_wins_wholesale_an_empty_field_is_not_filled_from_parameters()
        {
            // The writer stores all three keys together; writeDirection off stores "".
            var k = MatchLineKeyRules.Resolve(Es(Pair, "A-101", ""), Params(Pair, "A-101", "dogleg"));
            Assert.Equal(MatchLineKeySource.ExtensibleStorage, k.Source);
            Assert.Equal("", k.Direction);
        }

        [Fact]
        public void Parameters_are_the_fallback_when_there_is_no_entity()
        {
            var k = MatchLineKeyRules.Resolve(null, Params(Pair, "A-102", "horizontal"));
            Assert.Equal(MatchLineKeySource.SharedParameters, k.Source);
            Assert.True(k.IsMatchLine);
            Assert.Equal("A-102", k.Ref);
        }

        [Fact]
        public void Parameters_are_the_fallback_when_the_entity_is_empty()
        {
            var k = MatchLineKeyRules.Resolve(Es("", "", ""), Params(Pair, "A-102", ""));
            Assert.Equal(MatchLineKeySource.SharedParameters, k.Source);
            Assert.Equal(Pair, k.PairGuid);
        }

        [Fact]
        public void Unbound_parameters_and_no_entity_mean_not_a_match_line()
        {
            // Unbound parameters read as null: never an error, just "no value".
            var k = MatchLineKeyRules.Resolve(null, Params(null, null, null));
            Assert.Equal(MatchLineKeySource.None, k.Source);
            Assert.False(k.IsMatchLine);
            Assert.True(k.IsEmpty);
            Assert.NotNull(k.Ref);
            Assert.Same(MatchLineKeys.Empty, MatchLineKeyRules.Resolve(null, null));
        }

        [Fact]
        public void A_ref_without_a_pair_key_is_not_a_findable_match_line()
        {
            var k = MatchLineKeyRules.Resolve(Es("", "A-101", "vertical"), null);
            Assert.Equal(MatchLineKeySource.ExtensibleStorage, k.Source);
            Assert.False(k.IsMatchLine);
        }

        [Fact]
        public void Values_are_trimmed_and_null_safe()
        {
            var k = Es("  " + Pair + " ", null, " vertical");
            Assert.Equal(Pair, k.PairGuid);
            Assert.Equal("", k.Ref);
            Assert.Equal("vertical", k.Direction);
        }

        [Theory]
        [InlineData(Pair + ":seg1", Pair)]
        [InlineData(Pair + ":SEG12", Pair)]
        [InlineData(Pair, Pair)]
        [InlineData(Pair + ":segX", Pair + ":segX")]   // not an integer: not a segment suffix
        [InlineData(":seg1", ":seg1")]                 // nothing before it: kept
        [InlineData("", "")]
        [InlineData(null, null)]
        public void BasePairKey_strips_only_a_trailing_segment_suffix(string stamped, string expected)
        {
            Assert.Equal(expected, MatchLineKeyRules.BasePairKey(stamped));
        }

        [Fact]
        public void Keys_expose_the_base_pair_key()
        {
            Assert.Equal(Pair, Es(Pair + ":seg2", "A-101", "dogleg").BasePairKey);
        }

        [Theory]
        [InlineData(Pair, "3f2a")]
        [InlineData("3f2a", "3f2a")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void ScopePairGuid_is_the_first_field(string key, string expected)
        {
            Assert.Equal(expected, MatchLineKeyRules.ScopePairGuid(key));
        }

        [Fact]
        public void SamePair_ignores_case_and_segment_suffix()
        {
            Assert.True(MatchLineKeyRules.SamePair(Pair + ":seg1", Pair.ToUpperInvariant()));
            Assert.False(MatchLineKeyRules.SamePair(Pair, "other:viewA-uid:viewB-uid"));
            Assert.False(MatchLineKeyRules.SamePair("", ""));
            Assert.False(MatchLineKeyRules.SamePair(null, Pair));
        }

        [Fact]
        public void RefsMatch_needs_both_opposite_sheets()
        {
            Assert.True(MatchLineKeyRules.RefsMatch(new[] { "A-102", "a-101" }, "A-101", "A-102"));
            Assert.False(MatchLineKeyRules.RefsMatch(new[] { "A-102", "A-102" }, "A-101", "A-102"));
            Assert.False(MatchLineKeyRules.RefsMatch(new List<string>(), "A-101", "A-102"));
            Assert.False(MatchLineKeyRules.RefsMatch(null, "A-101", "A-102"));
        }

        [Fact]
        public void RefsMatch_after_a_renumber_is_false_so_the_pair_is_restamped()
        {
            Assert.False(MatchLineKeyRules.RefsMatch(new[] { "A-102", "A-101" }, "A-201", "A-102"));
        }
    }
}
