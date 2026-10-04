using StingTools.Core;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ELEC-32: a tagging batch Revit rolled back must be taken back out of the counts
    /// (AutoTag, Tag New Only, Batch Tag, Tag Format Migration, Resolve All Issues).
    /// </summary>
    public class TaggingStatsSnapshotTests
    {
        private static TaggingStats Seeded()
        {
            var s = new TaggingStats();
            s.RecordTagged("Doors", "A", "ARC", "L01");
            s.RecordTagged("Ducts", "M", "HVAC", "L02");
            s.RecordSkipped("Walls");
            s.RecordCollision("M-BLD1-Z01-L02-HVAC-SUP-DCT-0001", 2);
            s.RecordWarning("first batch warning");
            s.RecordTagCompleteness(complete: false, anyFallback: true, "A-BLD1-Z01-L01-ARC-ACC-DR-0001", 11);
            return s;
        }

        [Fact]
        public void Restore_takes_a_rolled_back_batch_back_out_of_every_count_and_list()
        {
            var stats = Seeded();
            string before = stats.BuildReport();
            int tagged = stats.TotalTagged, skipped = stats.TotalSkipped, collisions = stats.TotalCollisions,
                depth = stats.MaxCollisionDepth, incomplete = stats.IncompleteTagCount;

            var snap = TaggingStatsSnapshot.Take(stats);
            // The batch that will be rolled back:
            stats.RecordTagged("Doors", "A", "ARC", "L01");
            stats.RecordTagged("Pipes", "P", "DCW", "L03");
            stats.RecordOverwritten("Ducts", "M", "HVAC", "L02");
            stats.RecordCollision("P-BLD1-Z01-L03-DCW-DCW-PIP-0009", 5);
            stats.RecordSkipped("Floors");
            stats.RecordWarning("rolled-back batch warning");
            stats.RecordTokenRefusal("no LOC", 99);
            Assert.NotEqual(before, stats.BuildReport());

            snap.Restore();

            Assert.Equal(tagged, stats.TotalTagged);
            Assert.Equal(skipped, stats.TotalSkipped);
            Assert.Equal(0, stats.TotalOverwritten);
            Assert.Equal(collisions, stats.TotalCollisions);
            Assert.Equal(depth, stats.MaxCollisionDepth);
            Assert.Equal(incomplete, stats.IncompleteTagCount);
            Assert.Equal(0, stats.RefusedTagCount);
            Assert.Equal(before, stats.BuildReport());   // per-category / per-discipline breakdowns too
        }

        [Fact]
        public void Recording_after_a_restore_still_works()
        {
            var stats = Seeded();
            var snap = TaggingStatsSnapshot.Take(stats);
            stats.RecordTagged("Pipes", "P", "DCW", "L03");
            snap.Restore();
            stats.RecordTagged("Pipes", "P", "DCW", "L03");   // collections are the same live instances
            Assert.Equal(3, stats.TotalTagged);
        }
    }
}
