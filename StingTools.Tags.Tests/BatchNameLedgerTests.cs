using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-45: a sheet number or view name taken by an item that rolled back is free again for the rest of the batch. The plugin calls the same file.</summary>
    public class BatchNameLedgerTests
    {

        [Fact]
        public void A_number_whose_sheet_rolled_back_is_free_again()
        {
            var ledger = new BatchNameLedger(new[] { "A-100" }, StringComparer.OrdinalIgnoreCase);
            ledger.Record("A-101", 555);
            var alive = new HashSet<long>();          // sheet 555 was rolled back
            Assert.False(ledger.Contains("A-101", id => alive.Contains(id)));
            Assert.True(ledger.Contains("A-100", id => alive.Contains(id)));   // primed: never released
        }

        [Fact]
        public void Heal_releases_only_the_dead_under_a_prefix()
        {
            var ledger = new BatchNameLedger(Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            ledger.Record("A-101", 1); ledger.Record("A-101-A", 2); ledger.Record("B-101", 3);
            int n = ledger.Heal("A-101", id => id == 2);
            Assert.Equal(1, n);
            Assert.DoesNotContain("A-101", ledger.Names);
            Assert.Contains("A-101-A", ledger.Names);
            Assert.Contains("B-101", ledger.Names);
        }
    }
}
