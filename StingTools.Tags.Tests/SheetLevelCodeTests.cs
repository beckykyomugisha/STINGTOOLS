using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-129: the ISO sheet number took a project-declared level code (DTW-105) while
    /// Tag Sheets' SHT_TAG_1 level stamp still took the elevation-derived one, so the two
    /// disagreed on any level with a declared code. Both now read one map and one lookup.
    /// </summary>
    public class SheetLevelCodeTests
    {
        private static List<StoreyDatum> Stack() => new List<StoreyDatum>
        {
            new StoreyDatum { Name = "Basement", ElevationMm = -3500 },
            new StoreyDatum { Name = "Ground",   ElevationMm = 0 },
            new StoreyDatum { Name = "Level 1",  ElevationMm = 4000 },
            new StoreyDatum { Name = "Podium",   ElevationMm = 8000 },
        };

        // What DrawingProducer.BuildIsoLevelMap builds: the stack with the declared codes over it.
        private static Dictionary<string, string> Map(IDictionary<string, string> declared)
            => IsoLevelCode.BuildMap(Stack(), declared);

        [Fact]
        public void A_declared_level_gets_the_same_code_in_the_stamp_and_the_number()
        {
            var map = Map(new Dictionary<string, string> { ["Podium"] = "P1" });
            string number = SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, "Podium", map);
            string stamp  = SheetLevelCode.ForSheet(new[] { "Podium" }, map);
            Assert.Equal(Iso19650DocumentCode.NormaliseLevel("P1"), number);
            Assert.Equal(number, stamp);
        }

        [Fact]
        public void The_declared_code_replaces_the_elevation_code_the_stamp_used_to_take()
        {
            var elevationOnly = IsoLevelCode.BuildMap(Stack());
            var declared = Map(new Dictionary<string, string> { ["Podium"] = "P1" });
            Assert.NotEqual(elevationOnly["Podium"], SheetLevelCode.ForSheet(new[] { "Podium" }, declared));
        }

        [Theory]
        [InlineData("Basement")]
        [InlineData("Ground")]
        [InlineData("Level 1")]
        [InlineData("Podium")]
        public void Every_level_agrees_between_stamp_and_number(string level)
        {
            var map = Map(new Dictionary<string, string> { ["Level 1"] = "M1" });
            Assert.Equal(SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, level, map),
                         SheetLevelCode.ForSheet(new[] { level }, map));
        }

        [Fact]
        public void Several_levels_on_one_sheet_are_ZZ()
            => Assert.Equal(IsoLevelCode.Multiple, SheetLevelCode.ForSheet(new[] { "Ground", "Level 1" }, Map(null)));

        [Fact]
        public void Two_views_of_one_level_are_that_level()
            => Assert.Equal(Map(null)["Ground"], SheetLevelCode.ForSheet(new[] { "Ground", "Ground" }, Map(null)));

        [Fact]
        public void No_levelled_view_is_XX()
        {
            Assert.Equal(IsoLevelCode.NotApplicable, SheetLevelCode.ForSheet(new string[] { null, null }, Map(null)));
            Assert.Equal(IsoLevelCode.NotApplicable, SheetLevelCode.ForSheet(new string[0], Map(null)));
        }

        [Fact]
        public void A_level_missing_from_the_map_falls_back_to_its_name_as_the_number_does()
        {
            string number = SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, "Ground", null);
            Assert.Equal(number, SheetLevelCode.ForSheet(new[] { "Ground" }, null));
        }
    }
}
