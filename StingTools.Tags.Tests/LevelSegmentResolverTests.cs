using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-40: a STING:: box's level segment resolves by level code, then name, then name without punctuation — never by "contains". The plugin calls the same file.</summary>
    public class LevelSegmentResolverTests
    {

        private static List<LevelRef> Levels() => new List<LevelRef>
        {
            new LevelRef { Id = 11, Name = "Level 1",  Code = "L01" },
            new LevelRef { Id = 12, Name = "Level 10", Code = "L10" },
            new LevelRef { Id = 13, Name = "Roof",     Code = "RF" },
        };

        [Fact]
        public void A_level_code_addresses_a_level_whose_name_has_a_space()
        {
            Assert.Equal(11L, LevelSegmentResolver.Resolve("L01", Levels(), out var note));
            Assert.Contains("code", note);
        }

        [Fact]
        public void A_name_without_its_space_still_names_the_level()
        {
            Assert.Equal(11L, LevelSegmentResolver.Resolve("Level_1", Levels(), out _));
            Assert.Equal(11L, LevelSegmentResolver.Resolve("Level1", Levels(), out _));
        }

        [Fact]
        public void An_exact_name_still_resolves()
            => Assert.Equal(13L, LevelSegmentResolver.Resolve("roof", Levels(), out _));

        [Fact]
        public void L1_never_lands_on_Level_10()
        {
            Assert.Null(LevelSegmentResolver.Resolve("L1", Levels(), out var note));
            Assert.False(string.IsNullOrEmpty(note));
        }

        [Fact]
        public void Two_levels_answering_the_same_segment_is_ambiguous_not_the_first()
        {
            var lv = new List<LevelRef>
            {
                new LevelRef { Id = 1, Name = "Level-1", Code = "A" },
                new LevelRef { Id = 2, Name = "Level 1", Code = "B" },
            };
            Assert.Null(LevelSegmentResolver.Resolve("Level1", lv, out var note));
            Assert.Contains("Level-1", note);
        }
    }
}
