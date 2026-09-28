using System.Collections.Generic;
using System.Linq;
using StingTools.IfcResults;
using Xunit;
using R = StingTools.IfcResults.IfcSpaceMatcher.RoomKey;
using S = StingTools.IfcResults.IfcSpaceMatcher.SpaceKey;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ELEC-11 — IFC results import. Matching an IFC GlobalId against a Revit
    /// UniqueId could never succeed, and a second space for the same room silently
    /// overwrote the first.
    /// </summary>
    public class IfcSpaceMatcherTests
    {
        private static List<R> Rooms() => new List<R>
        {
            new R { Id = "1", Number = "101", Name = "Office", IfcGuids = { "2O2Fr$t4X7Zf8NOew3FLOH" } },
            new R { Id = "2", Number = "102", Name = "Meeting" },
            new R { Id = "3", Number = "103", Name = "Store" },
            new R { Id = "4", Number = "104", Name = "Store" },
        };

        [Fact]
        public void MatchesByGlobalIdFirst()
        {
            var r = IfcSpaceMatcher.MatchAll(Rooms(), new[] { new S { GlobalId = "2O2Fr$t4X7Zf8NOew3FLOH", Name = "999" } });
            var m = Assert.Single(r.Matches);
            Assert.Equal("1", m.RoomId);
            Assert.Equal("GlobalId", m.Via);
        }

        [Fact]
        public void GlobalIdIsCaseSensitive()
        {
            var r = IfcSpaceMatcher.MatchAll(Rooms(), new[] { new S { GlobalId = "2o2fr$t4x7zf8noew3floh" } });
            Assert.Empty(r.Matches);
            Assert.Single(r.Unmatched);
        }

        [Fact]
        public void FallsBackToRoomNumberThenName()
        {
            var r = IfcSpaceMatcher.MatchAll(Rooms(), new[]
            {
                new S { GlobalId = "x", Name = "102", LongName = "Whatever" },
                new S { GlobalId = "y", Name = "Z", LongName = "Office" },
            });
            Assert.Equal(new[] { ("2", "number"), ("1", "name") },
                r.Matches.Select(m => (m.RoomId, m.Via)).ToArray());
        }

        [Fact]
        public void SharedNameIsAmbiguousNotAGuess()
        {
            var r = IfcSpaceMatcher.MatchAll(Rooms(), new[] { new S { Name = "Store" } });
            Assert.Empty(r.Matches);
            Assert.Single(r.Ambiguous);
        }

        [Fact]
        public void SecondSpaceForSameRoomIsReportedNotLastWins()
        {
            var r = IfcSpaceMatcher.MatchAll(Rooms(), new[]
            {
                new S { Name = "101", LongName = "Office" },
                new S { Name = "101", LongName = "Office (copy)" },
            });
            var m = Assert.Single(r.Matches);
            Assert.Equal(0, m.SpaceIndex);
            Assert.Single(r.Duplicates);
        }
    }
}
