using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-200. Restore matched recorded "to" numbers only. After Ctrl+Z of a renumber
    /// that shifted numbers along (A→B, B→C), sheet 2 carries B again — so it was
    /// "restored" onto A, which sheet 1 already holds again.
    /// </summary>
    public class SheetRestorePlannerTests
    {
        private static KeyValuePair<string, string> C(string from, string to) => new KeyValuePair<string, string>(from, to);

        [Fact]
        public void A_recorded_renumber_still_in_place_is_restored()
        {
            var plan = SheetRestorePlanner.Build(new[] { C("A-001", "X-0001"), C("A-002", "X-0002") },
                new Dictionary<string, string> { { "X-0001", "1" }, { "X-0002", "2" } });
            Assert.Equal(2, plan.Moves.Count);
            Assert.Empty(plan.Skipped);
            Assert.Equal("A-001", plan.Moves.Single(m => m.SheetKey == "1").Restore);
        }

        [Fact]
        public void A_shift_still_in_place_restores_through_the_moving_sheets()
        {
            // s1: A -> B, s2: B -> C. Live: s1 = B, s2 = C. B is freed by s1 moving.
            var plan = SheetRestorePlanner.Build(new[] { C("A", "B"), C("B", "C") },
                new Dictionary<string, string> { { "B", "s1" }, { "C", "s2" } });
            Assert.Equal(2, plan.Moves.Count);
            Assert.Equal(0, plan.LooksUndone);
        }

        [Fact]
        public void After_undo_nothing_collides_and_the_undo_is_named()
        {
            // The same renumber, undone: s1 = A, s2 = B. The record still lists both.
            var plan = SheetRestorePlanner.Build(new[] { C("A", "B"), C("B", "C") },
                new Dictionary<string, string> { { "A", "s1" }, { "B", "s2" } });
            Assert.Empty(plan.Moves);
            Assert.Equal(1, plan.LooksUndone);
            Assert.Contains(plan.Skipped, s => s.Contains("Ctrl+Z"));
            Assert.Equal(2, plan.Skipped.Count);
        }

        [Fact]
        public void A_target_taken_by_an_unrelated_sheet_is_skipped_with_its_reason()
        {
            var plan = SheetRestorePlanner.Build(new[] { C("A-001", "X-0001"), C("A-002", "X-0002") },
                new Dictionary<string, string> { { "X-0001", "1" }, { "X-0002", "2" }, { "A-002", "9" } });
            Assert.Single(plan.Moves);
            Assert.Equal("1", plan.Moves[0].SheetKey);
            Assert.Single(plan.Skipped);
        }

        [Fact]
        public void Two_records_restoring_to_one_number_restore_only_the_first()
        {
            var plan = SheetRestorePlanner.Build(new[] { C("A", "X"), C("A", "Y") },
                new Dictionary<string, string> { { "X", "1" }, { "Y", "2" } });
            Assert.Single(plan.Moves);
            Assert.Equal("1", plan.Moves[0].SheetKey);
        }
    }
}
