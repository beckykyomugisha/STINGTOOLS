// ══════════════════════════════════════════════════════════════════════════
//  PanelSlotRulesTests.cs — ELEC-15 / ELEC-16.
//
//  Batch Assign fell back to a 42-way board and the door diagram to 24 when a
//  panel family reported no slot count — two different invented sizes for the
//  same unknown. The shared rule returns null instead, and the callers report.
//  The multi-pole slot step comes from the schedule's numbering, not a
//  hard-coded 2.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PanelSlotRulesTests
    {
        [Theory]
        [InlineData(42, 30, 42)]     // single-pole breakers wins
        [InlineData(null, 30, 30)]   // then max circuits
        [InlineData(0, 24, 24)]      // a zero is "not reported", not a size
        [InlineData(-1, 18, 18)]
        public void Slot_count_prefers_single_pole_breakers_then_circuits(int? spb, int? circuits, int expected)
        {
            Assert.Equal(expected, PanelSlotRules.ChooseSlotCount(spb, circuits));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(0, 0)]
        [InlineData(null, 0)]
        public void Unknown_slot_count_is_null_never_invented(int? spb, int? circuits)
        {
            Assert.Null(PanelSlotRules.ChooseSlotCount(spb, circuits));
        }

        [Theory]
        [InlineData(SlotNumbering.TwoColumnsAcross, false, 2, true)]
        [InlineData(SlotNumbering.TwoColumnsDown,   false, 1, true)]
        [InlineData(SlotNumbering.OneColumn,        false, 1, true)]
        [InlineData(SlotNumbering.TwoColumnsAcross, true,  1, true)]   // switchboard: always consecutive
        [InlineData(SlotNumbering.Unknown,          true,  1, true)]
        [InlineData(SlotNumbering.Unknown,          false, 2, false)]  // assumed, and flagged as such
        public void Step_follows_schedule_numbering(object n, bool switchboard, int step, bool known)
        {
            Assert.Equal(step, PanelSlotRules.StepFor((SlotNumbering)n, switchboard, out bool k));
            Assert.Equal(known, k);
        }

        [Fact]
        public void Compaction_moves_a_breaker_into_the_lowest_gap()
        {
            // Slots 1 and 2 free after a deletion; single-pole breaker at 5.
            var occupied = new HashSet<int> { 3, 4 };
            Assert.Equal(1, PanelSlotRules.LowestFreeStart(occupied, 5, 1, 2, 42));
        }

        [Fact]
        public void Compaction_needs_every_pole_free_at_the_step()
        {
            // Across numbering: a 3-pole at 9 needs s, s+2, s+4. Slot 3 is taken,
            // so start 1 fails; start 2 needs 2, 4, 6 — all free.
            var occupied = new HashSet<int> { 3 };
            Assert.Equal(2, PanelSlotRules.LowestFreeStart(occupied, 9, 3, 2, 42));
        }

        [Fact]
        public void Compaction_respects_consecutive_step()
        {
            var occupied = new HashSet<int> { 2 };
            // 2-pole, step 1: start 1 needs 1,2 (2 taken) -> start 3 needs 3,4.
            Assert.Equal(3, PanelSlotRules.LowestFreeStart(occupied, 6, 2, 1, 42));
        }

        [Fact]
        public void Already_compact_panel_does_not_move()
        {
            var occupied = new HashSet<int> { 1, 2, 3 };
            Assert.Null(PanelSlotRules.LowestFreeStart(occupied, 4, 1, 2, 42));
            Assert.Null(PanelSlotRules.LowestFreeStart(occupied, 1, 1, 2, 42));
        }

        [Fact]
        public void Compaction_never_places_beyond_the_slot_count()
        {
            // Only slot 1 is free below 3, but a 2-pole across needs 1 and 3 — 3 is itself.
            // With a 2-slot board nothing fits.
            var occupied = new HashSet<int> { 2 };
            Assert.Null(PanelSlotRules.LowestFreeStart(occupied, 3, 2, 2, 2));
        }
    }
}
