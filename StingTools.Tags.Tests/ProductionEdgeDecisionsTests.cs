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
    }
}
