// The form segment of an issued identifier. It printed LG — legend — on a site
// plan, which is the same failure shape as the discipline census: a rule that
// asks "is X present" where the question is "what is this sheet".

using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SheetFormResolverTests
    {
        private static Dictionary<string, int> Census(int dr = 0, int sh = 0, int m3 = 0, int lg = 0)
        {
            var c = new Dictionary<string, int>();
            if (dr > 0) c[SheetFormResolver.Drawing] = dr;
            if (sh > 0) c[SheetFormResolver.Schedule] = sh;
            if (m3 > 0) c[SheetFormResolver.Model3D] = m3;
            if (lg > 0) c[SheetFormResolver.Legend] = lg;
            return c;
        }

        [Fact]
        public void TheSheetThatShippedTheBugIsADrawing()
        {
            // "SITE PLAN, LOCATION PLAN, SEPTIC TANK DETAILS, BOUNDARY WALL DETAILS"
            // — one plan plus three drafting views. Drafting views used to count as
            // legends, and ANY legend won, so this was issued as ...-LG-...
            Assert.Equal("DR", SheetFormResolver.Resolve(
                "SITE PLAN, LOCATION PLAN, SEPTIC TANK DETAILS, BOUNDARY WALL DETAILS",
                Census(dr: 4)));
        }

        [Fact]
        public void OneLegendBesideManyPlansDoesNotMakeALegendSheet()
        {
            Assert.Equal("DR", SheetFormResolver.FromCensus(Census(dr: 8, lg: 1)));
        }

        [Fact]
        public void OneScheduleBesideManyPlansDoesNotMakeAScheduleSheet()
        {
            Assert.Equal("DR", SheetFormResolver.FromCensus(Census(dr: 5, sh: 1)));
        }

        [Theory]
        [InlineData(0, 6, 0, 0, "SH")]
        [InlineData(0, 0, 3, 0, "M3")]
        [InlineData(0, 0, 0, 4, "LG")]
        public void ASheetThatIsWhollyOneThingTakesThatForm(
            int dr, int sh, int m3, int lg, string expected)
            => Assert.Equal(expected, SheetFormResolver.FromCensus(Census(dr, sh, m3, lg)));

        [Fact]
        public void AMajorityIsNeededToOverrideDrawing()
        {
            // Exactly half is not a majority: a mixed sheet is a drawing, which is
            // what it looks like to anyone holding it.
            Assert.Equal("DR", SheetFormResolver.FromCensus(Census(dr: 3, sh: 3)));
            Assert.Equal("SH", SheetFormResolver.FromCensus(Census(dr: 2, sh: 5)));
        }

        [Theory]
        [InlineData("DOOR SCHEDULE", "SH")]
        [InlineData("WINDOW SCHEDULES", "SH")]
        [InlineData("SYMBOLS AND ABBREVIATIONS", "LG")]
        [InlineData("DRAINAGE LEGEND", "LG")]
        [InlineData("AXONOMETRIC VIEW", "M3")]
        public void TheTitleDecidesWhenItSaysSomething(string title, string expected)
            => Assert.Equal(expected, SheetFormResolver.FromTitle(title));

        [Fact]
        public void TheTitleBeatsTheCensus()
        {
            // A schedule sheet whose schedule is placed as a drafting view still says
            // SCHEDULE on it, and that is a statement of intent — the same precedence
            // the discipline resolver uses, for the same reason.
            Assert.Equal("SH", SheetFormResolver.Resolve("DOOR SCHEDULE", Census(dr: 3)));
        }

        [Theory]
        [InlineData("LEGENDARY BUILDING PLAN")]   // LEGEND is not a word here
        [InlineData("GROUND FLOOR PLAN")]
        public void SubstringsAndOrdinaryTitlesSayNothing(string title)
            => Assert.Null(SheetFormResolver.FromTitle(title));

        [Fact]
        public void AnEmptySheetIsADrawing()
        {
            Assert.Equal("DR", SheetFormResolver.Resolve("UNTITLED", null));
            Assert.Null(SheetFormResolver.FromCensus(new Dictionary<string, int>()));
        }
    }
}
