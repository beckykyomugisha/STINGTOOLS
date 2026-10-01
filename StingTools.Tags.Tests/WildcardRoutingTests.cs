using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Routing requests callers really make, walked with the runtime matcher
    /// over the shipped table (DrawingDispatcher.Resolve runs the same walk).
    /// </summary>
    public class WildcardRoutingTests
    {
        private static string Route(string disc, string phase, string docType)
        {
            var lib = DrawingCatalogueFixture.Shipped();
            var ids = lib.DrawingTypes.Select(t => t.Id).ToHashSet();
            return DrawingRoutingMatcher.FirstMatchId(lib.Routing, disc, phase, docType,
                levelCode: null, projectCode: null, extra: _ => true, typeExists: ids.Contains);
        }

        // DTW-67: DocAutomationExtCommands asks for a grid section and the four
        // building elevations with discipline "*". No rule matched, so neither
        // ever got a drawing type and both fell back to a hand-named template.
        [Theory]
        [InlineData(DrawingPurpose.Section,   "arch-section-A1-1to50")]
        [InlineData(DrawingPurpose.Elevation, "arch-elev-A1-1to100")]
        public void A_discipline_agnostic_section_or_elevation_resolves(string docType, string expected)
            => Assert.Equal(expected, Route("*", "*", docType));

        [Fact]
        public void Discipline_specific_rules_still_win_over_the_wildcard()
        {
            Assert.Equal("struct-section-A1-1to50", Route("S", "*", DrawingPurpose.Section));
            Assert.Equal("mep-section-A1-1to50", Route("M", "*", DrawingPurpose.Section));
            Assert.Equal("pres-exterior-elev-A1", Route("*", "PRESENTATION", DrawingPurpose.Elevation));
        }
    }
}
