using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-194..213 — the drawing producer's edge cases, decided Revit-free
    /// (ProductionEdgeDecisions). Each test names the defect it pins.
    /// </summary>
    public class ProductionEdgeDecisionsTests
    {
        // ── DTW-194 ───────────────────────────────────────────────────

        [Fact]
        public void A_run_that_cannot_write_the_counters_says_who_owns_them_and_that_nothing_was_guessed()
        {
            var line = ProductionEdgeDecisions.CountersBlockedLine("owned by Jane (Project Information)");
            Assert.Contains("owned by Jane", line);
            Assert.Contains("never numbered from a guess", line);
            Assert.StartsWith("Run stopped before any drawing was produced", line);
        }

        [Fact]
        public void A_sheet_that_cannot_be_numbered_is_reported_with_its_type_and_reason()
        {
            var line = ProductionEdgeDecisions.SheetNotNumberedLine("arch-plan-A1-1to100", "reload latest");
            Assert.Contains("'arch-plan-A1-1to100'", line);
            Assert.Contains("reload latest", line);
            Assert.Contains("no sheet was made", line);
        }

        [Fact]
        public void A_missing_reason_still_reads_as_a_reason()
            => Assert.Contains("reason unknown", ProductionEdgeDecisions.CountersBlockedLine(null));

        // ── DTW-197 ───────────────────────────────────────────────────

        [Fact]
        public void No_sheet_is_made_before_a_view_exists()
            => Assert.False(ProductionEdgeDecisions.CreateSheetNow(true, sheetKnown: false, sheetAttempted: false, viewProduced: false));

        [Fact]
        public void The_first_produced_view_makes_the_sheet_once()
        {
            Assert.True(ProductionEdgeDecisions.CreateSheetNow(true, false, false, true));
            Assert.False(ProductionEdgeDecisions.CreateSheetNow(true, false, sheetAttempted: true, viewProduced: true));
        }

        [Fact]
        public void An_existing_sheet_or_no_sheet_requested_makes_none()
        {
            Assert.False(ProductionEdgeDecisions.CreateSheetNow(true, sheetKnown: true, sheetAttempted: false, viewProduced: true));
            Assert.False(ProductionEdgeDecisions.CreateSheetNow(false, false, false, true));
        }

        [Theory]
        [InlineData(true, true, 0, 0, true)]    // made, nothing placed: removed
        [InlineData(true, true, 1, 0, false)]   // something placed: kept
        [InlineData(true, false, 0, 0, false)]  // views placed later by the caller: kept
        [InlineData(false, true, 0, 0, false)]  // an existing sheet is never removed
        public void An_empty_new_sheet_is_removed(bool created, bool place, int placed, int reused, bool expected)
            => Assert.Equal(expected, ProductionEdgeDecisions.DiscardNewSheet(created, place, placed, reused));

        // ── DTW-199 ───────────────────────────────────────────────────

        [Fact]
        public void A_view_moved_to_another_sheet_is_reported_as_kept_there()
        {
            var line = ProductionEdgeDecisions.KeptOnOtherSheetLine("Power - Level 1", "E-105", "E-101");
            Assert.Contains("kept on sheet E-105", line);
            Assert.Contains("not placed on E-101", line);
        }
    }
}
