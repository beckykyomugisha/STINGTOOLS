// Covers runner §2 case 5 — filter naming is stable and round-trips.
//
// This matters more than it looks: the "STING VIS - " prefix is the contract that lets
// Vis_PurgeFilters and Vis_ResetAll find and delete STING's filters without touching a
// user's own. If naming drifts, cleanup silently stops working.

using StingTools.Core.Visibility;
using Xunit;

namespace StingTools.Visibility.Tests
{
    public class VisibilityFilterNamingTests
    {
        [Theory]
        [InlineData("ZONE", "Z02")]
        [InlineData("LOC", "BLD1")]
        [InlineData("DISC", "M")]
        [InlineData("PROD", "AHU")]
        [InlineData("SYS", "HVAC")]
        public void TokenFilterName_RoundTrips(string token, string value)
        {
            string name = VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Token, token, value);

            VisibilityRuleKind kind;
            string parsedToken, parsedValue;
            Assert.True(VisibilityRuleMatcher.TryParseFilterName(name, out kind, out parsedToken, out parsedValue));

            Assert.Equal(VisibilityRuleKind.Token, kind);
            Assert.Equal(token, parsedToken);
            Assert.Equal(value, parsedValue);
            Assert.Equal(name, VisibilityRuleMatcher.FilterName(kind, parsedToken, parsedValue));
        }

        [Theory]
        [InlineData("Ducts")]
        [InlineData("Pipe Fittings")]
        [InlineData("Mechanical Equipment")]
        public void CategoryFilterName_RoundTrips(string categoryName)
        {
            string name = VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Category, null, categoryName);

            VisibilityRuleKind kind;
            string parsedToken, parsedValue;
            Assert.True(VisibilityRuleMatcher.TryParseFilterName(name, out kind, out parsedToken, out parsedValue));

            Assert.Equal(VisibilityRuleKind.Category, kind);
            Assert.Null(parsedToken);
            Assert.Equal(categoryName, parsedValue);
            Assert.Equal(name, VisibilityRuleMatcher.FilterName(kind, parsedToken, parsedValue));
        }

        [Fact]
        public void UnsetValue_RoundTrips()
        {
            string name = VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Token, "ZONE", VisibilityTokens.Unset);

            VisibilityRuleKind kind;
            string token, value;
            Assert.True(VisibilityRuleMatcher.TryParseFilterName(name, out kind, out token, out value));
            Assert.Equal(VisibilityTokens.Unset, value);
        }

        [Fact]
        public void BlankValue_NormalisesToTheUnsetSentinel()
        {
            Assert.Equal(
                VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Token, "ZONE", VisibilityTokens.Unset),
                VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Token, "ZONE", null));
        }

        [Fact]
        public void EveryNameCarriesThePurgePrefix()
        {
            Assert.StartsWith("STING VIS - ", VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Token, "ZONE", "Z01"));
            Assert.StartsWith("STING VIS - ", VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Category, null, "Ducts"));
            Assert.Equal("STING VIS - ", VisibilityRuleMatcher.FilterPrefix);
        }

        [Theory]
        [InlineData("STING VIS - ZONE=Z01", true)]
        [InlineData("STING VIS - Cat Ducts", true)]
        [InlineData("sting vis - ZONE=Z01", true)]   // recognition is case-insensitive
        [InlineData("Interior Walls", false)]
        [InlineData("STING - Stale Elements", false)] // the StaleFlag filter must NOT be purged
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsStingVisibilityFilter_OnlyClaimsOurOwn(string name, bool expected)
        {
            Assert.Equal(expected, VisibilityRuleMatcher.IsStingVisibilityFilter(name));
        }

        [Fact]
        public void ValueContainingEquals_StillRecoversTheTokenKey()
        {
            // Split is on the FIRST '=', so the token key is always clean.
            string name = VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Token, "SYS", "A=B");

            VisibilityRuleKind kind;
            string token, value;
            Assert.True(VisibilityRuleMatcher.TryParseFilterName(name, out kind, out token, out value));
            Assert.Equal("SYS", token);
            Assert.Equal("A=B", value);
        }

        [Fact]
        public void ForeignFilterName_DoesNotParse()
        {
            VisibilityRuleKind kind;
            string token, value;
            Assert.False(VisibilityRuleMatcher.TryParseFilterName("Some Other Filter", out kind, out token, out value));
        }
    }
}
