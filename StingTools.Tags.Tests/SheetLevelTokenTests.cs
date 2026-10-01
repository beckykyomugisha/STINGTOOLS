using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-198. Sheet NAMES went through SafeShort, the sheet-NUMBER shaping: spaces
    /// stripped and eight characters kept, so "Ground Floor" printed "GroundFl" on the
    /// sheet name, and in numbers "Basement 1" and "Basement 2" both became "Basement".
    /// </summary>
    public class SheetLevelTokenTests
    {
        [Fact]
        public void The_old_number_shaping_is_what_truncated_names()
        {
            // The defect, pinned: this is what a name pattern used to get.
            Assert.Equal("GroundFl", SheetNumberEngine.SafeShort("Ground Floor"));
            Assert.Equal(SheetNumberEngine.SafeShort("Basement 1"), SheetNumberEngine.SafeShort("Basement 2"));
        }

        [Fact]
        public void Name_pattern_keeps_the_full_level_name()
            => Assert.Equal("Power Layout - Ground Floor",
                SheetNumberEngine.ApplyNamePattern("Power Layout - {lvl}", "E", "Ground Floor", "", "", "", "Plan", 1, null));

        [Fact]
        public void Name_pattern_keeps_a_full_mark_and_sequence()
            => Assert.Equal("Plant Room North Wing - 003",
                SheetNumberEngine.ApplyNamePattern("{mark} - {seq:D3}", "M", "Level 1", "", "Plant Room North Wing", "", "Plan", 3, null));

        [Theory]
        [InlineData("Level 1: East", "Level 1 East")]
        [InlineData("Roof {Plant}", "Roof Plant")]
        [InlineData("  Mezz  [A] ", "Mezz A")]
        [InlineData("B1;B2|B3", "B1 B2 B3")]
        public void Name_safe_removes_only_what_Revit_refuses(string raw, string expected)
            => Assert.Equal(expected, SheetNumberEngine.SheetNameSafe(raw));

        [Fact]
        public void Name_safe_empty_is_the_visible_placeholder()
            => Assert.Equal("XX", SheetNumberEngine.SheetNameSafe("  "));

        [Fact]
        public void Name_pattern_takes_extras_like_the_number_does()
            => Assert.Equal("PRJ Level 2",
                SheetNumberEngine.ApplyNamePattern("{project} {lvl}", "A", "Level 2", "", "", "", "Plan", 1,
                    new Dictionary<string, string> { { "project", "PRJ" } }));

        [Fact]
        public void Number_level_token_prefers_the_level_code()
        {
            Assert.Equal("B1", SheetNumberEngine.NumberLevelToken("B1", "Basement 1"));
            Assert.Equal("B2", SheetNumberEngine.NumberLevelToken("B2", "Basement 2"));
        }

        [Fact]
        public void Without_a_code_the_short_level_keeps_its_trailing_number()
        {
            Assert.Equal("Basemen1", SheetNumberEngine.NumberLevelToken(null, "Basement 1"));
            Assert.Equal("Basemen2", SheetNumberEngine.NumberLevelToken("", "Basement 2"));
            Assert.NotEqual(SheetNumberEngine.ShortLevel("Level 10"), SheetNumberEngine.ShortLevel("Level 1"));
            Assert.Equal("GroundFl", SheetNumberEngine.ShortLevel("Ground Floor"));
            Assert.Equal("Level1", SheetNumberEngine.ShortLevel("Level 1"));
            Assert.Equal("XX", SheetNumberEngine.ShortLevel(""));
        }

        [Fact]
        public void Number_level_token_is_number_safe()
            => Assert.Equal("L01", SheetNumberEngine.NumberLevelToken("L 0:1", "Level 1"));
    }
}
