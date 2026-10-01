using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-118 / DTW-123 — the ids read back out of a context stamp, written by
    /// the same ProductionContextKey.Compose the producer uses.</summary>
    public class ProductionContextIdsTests
    {
        [Fact]
        public void Level_and_box_ids_round_trip()
        {
            string stamp = ProductionContextKey.Compose("Level 1", 312, null, "A01",
                "STING-AREA::A01::L01", "1f2c3d4e-0000-0000-0000-000000000000-0004a1");
            var ids = ProductionContextIds.Parse(stamp);
            Assert.Equal(312L, ids.LevelId);
            Assert.Equal("1f2c3d4e-0000-0000-0000-000000000000-0004a1", ids.BoxUniqueId);
            Assert.Null(ids.RoomId);
            Assert.Equal("Level 1", ProductionContextIds.LevelName(stamp));
        }

        [Fact]
        public void A_room_context_round_trips()
        {
            string stamp = ProductionContextKey.Compose(null, null, "4471", "Kitchen (0.12)", null, null);
            var ids = ProductionContextIds.Parse(stamp);
            Assert.Equal("4471", ids.RoomId);
            Assert.Null(ids.LevelId);
            Assert.Null(ProductionContextIds.LevelName(stamp));
        }

        [Fact]
        public void A_stamp_from_before_ids_has_none()
        {
            var ids = ProductionContextIds.Parse("Level 1::::A01::STING-AREA::A01");
            Assert.False(ids.Any);
            Assert.Equal("Level 1", ProductionContextIds.LevelName("Level 1::::A01::STING-AREA::A01"));
            Assert.False(ProductionContextIds.Parse(null).Any);
            Assert.False(ProductionContextIds.Parse("garbage").Any);
        }
    }
}
