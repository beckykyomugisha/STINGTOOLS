using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Board pickers (circuit wizard, Add Spare / Move Circuit). They listed the family TYPE
    /// name, so two boards of one type read alike and the first was always used.
    /// </summary>
    public class BoardUniqueLabelTests
    {
        [Fact]
        public void Distinct_panel_names_are_listed_as_they_are()
            => Assert.Equal(new[] { "DB-L1", "DB-L2" },
                BoardNaming.UniqueLabels(new List<(string, long)> { ("DB-L1", 11), ("DB-L2", 12) }));

        [Fact]
        public void Boards_that_read_alike_get_their_id_so_each_can_be_picked()
        {
            var labels = BoardNaming.UniqueLabels(new List<(string, long)> { ("DB 12-way", 11), ("db 12-way", 12), ("MSB", 3) });
            Assert.Equal(new[] { "DB 12-way (id 11)", "db 12-way (id 12)", "MSB" }, labels);
            Assert.Equal(labels.Count, new HashSet<string>(labels).Count);
        }

        [Fact]
        public void A_board_with_no_name_is_named_by_its_id()
            => Assert.Equal(new[] { "Board 7" }, BoardNaming.UniqueLabels(new List<(string, long)> { ("  ", 7) }));
    }
}
