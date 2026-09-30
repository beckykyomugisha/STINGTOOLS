using System;
using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-79. Title-block heal (DrawingTokenContext.BuildForExistingSheet) put the
    /// level NAME in {lvl}; production puts the ISO level code there when the number
    /// pattern is ISO-shaped (DTW-43). A healed ISO sheet's level cell then read
    /// "Level 1" beside a number that says "00".
    /// </summary>
    public class ExistingSheetLevelTokenTests
    {
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Level 1", "00" }, { "Level 2", "01" }, { "Mezzanine", "M1" }, { "Basement", "B1" },
            };

        private const string ProfilePattern = "A-{lvl}-{seq:D3}";

        [Fact]
        public void Iso_pattern_turns_the_context_level_name_into_the_iso_code()
            => Assert.Equal("00", SheetNumberPolicy.ExistingSheetLevelToken(SheetNumberPolicy.IsoPattern, "Level 1", true, Map));

        [Fact]
        public void Iso_pattern_matches_what_production_puts_in_lvl()
        {
            // Production (DrawingProducer.BuildTokenDict + DTW-43) uses exactly
            // LevelToken(IsoPattern, name, map) for an ISO-shaped pattern.
            foreach (var name in new[] { "Level 1", "Level 2", "Mezzanine", "Basement", "Unmapped Level 7" })
                Assert.Equal(SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, name, Map),
                             SheetNumberPolicy.ExistingSheetLevelToken(SheetNumberPolicy.IsoPattern, name, true, Map));
        }

        [Fact]
        public void Iso_pattern_without_a_map_still_gives_a_code_not_the_name()
            => Assert.Equal("07", SheetNumberPolicy.ExistingSheetLevelToken(SheetNumberPolicy.IsoPattern, "Level 7", true, null));

        [Fact]
        public void Profile_pattern_keeps_the_level_name()
            => Assert.Equal("Level 1", SheetNumberPolicy.ExistingSheetLevelToken(ProfilePattern, "Level 1", true, Map));

        [Theory]
        [InlineData("M1")]
        [InlineData("ZZ")]
        [InlineData("00")]
        public void Segment_stamp_value_is_kept_as_written(string stamp)
            => Assert.Equal(stamp, SheetNumberPolicy.ExistingSheetLevelToken(SheetNumberPolicy.IsoPattern, stamp, false, Map));

        [Fact]
        public void Unknown_level_stays_unknown()
            => Assert.Null(SheetNumberPolicy.ExistingSheetLevelToken(SheetNumberPolicy.IsoPattern, null, true, Map));

        [Fact]
        public void Existing_sheet_tokens_say_whether_the_level_is_a_name()
        {
            var fromContext = ExistingSheetTokens.Resolve("Level 1::-1::", "00", null, null);
            Assert.Equal("Level 1", fromContext.Level);
            Assert.True(fromContext.LevelIsName);

            var fromStamp = ExistingSheetTokens.Resolve(null, "M1", null, null);
            Assert.Equal("M1", fromStamp.Level);
            Assert.False(fromStamp.LevelIsName);
        }

        [Fact]
        public void Heal_builds_lvl_through_the_policy_rule()
        {
            var src = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "StingTools", "Core", "Drawing", "DrawingTokenContext.cs"));
            Assert.Contains("SheetNumberPolicy.ExistingSheetLevelToken", src);
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StingTools", "Core", "Drawing", "DrawingTokenContext.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "repo root");
            return dir.FullName;
        }
    }
}
